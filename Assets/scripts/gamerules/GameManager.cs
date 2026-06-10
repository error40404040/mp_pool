using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;

public enum GameState { PlayerAiming, PlacingCueBall, BallsMoving, SelectingPocket, GameOver }
public enum Player { Player1, Player2 }
public enum BallGroup { None, Solids, Stripes }

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public GameState CurrentState { get; private set; }

    [Header("Ссылки")]
    public CueController cueController;
    public CameraController cameraController;
    public List<Rigidbody> allBalls;
    public List<Pocket> allPockets;
    public Transform cueBallStartPoint;
    public BallPlacer ballPlacer;

    [Header("Правила игры")]
    public Player currentPlayer;
    public BallGroup player1Target = BallGroup.None;
    public BallGroup player2Target = BallGroup.None;
    private bool isTableOpen = true;

    [Header("Заказ лузы")]
    public Pocket orderedPocket;

    private bool wasFoulCommitted = false;
    private bool wasPlayerBallPocketed = false;
    private bool isGameOver = false;
    private Player gameWinner = Player.Player1;
    private readonly List<int> player1PocketedBalls = new List<int>();
    private readonly List<int> player2PocketedBalls = new List<int>();

    private List<Vector3> lastFramePositions;
    private float stopTimer = 0f;
    private PlayerControls controls;
    private const float timeToConsiderStopped = 0.2f;

    private bool justOrderedPocket = false;
    private bool isApplyingNetworkState = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else Instance = this;
        lastFramePositions = new List<Vector3>();

        controls = new PlayerControls();
        controls.Gameplay.Pause.performed += ctx =>
        {
            if (UIManager.Instance != null) UIManager.Instance.TogglePause();
        };
    }

    private void OnEnable() { controls?.Gameplay.Enable(); }
    private void OnDisable() { controls?.Gameplay.Disable(); }

    private void Start()
    {
        currentPlayer = Player.Player1;
        isTableOpen = true;
        isGameOver = false;
        player1Target = BallGroup.None;
        player2Target = BallGroup.None;
        player1PocketedBalls.Clear();
        player2PocketedBalls.Clear();
        orderedPocket = null;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateActivePlayerUI(currentPlayer);
        }

        ChangeState(GameState.PlayerAiming);
    }

    private void Update()
    {
        if (CurrentState == GameState.BallsMoving && HasLocalRulesAuthority())
        {
            CheckIfBallsHaveStopped();
        }
        else if (CurrentState == GameState.SelectingPocket)
        {
            HandlePocketSelection();
        }
    }

    private void HandlePocketSelection()
    {
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Camera cam = cameraController.GetComponent<Camera>();
            Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
            RaycastHit hit;

            if (Physics.Raycast(ray, out hit, 100f))
            {
                Pocket clickedPocket = hit.collider.GetComponentInParent<Pocket>();
                if (clickedPocket != null)
                {
                    SetTargetPocket(clickedPocket);
                }
            }
        }
    }


    private void CheckIfBallsHaveStopped()
    {
        if (!HasLocalRulesAuthority())
        {
            return;
        }

        bool areBallsMoving = false;
        for (int i = 0; i < allBalls.Count; i++)
        {
            if (allBalls[i] == null) continue;
            if (i >= lastFramePositions.Count) lastFramePositions.Add(Vector3.zero);
            if (Vector3.Distance(allBalls[i].position, lastFramePositions[i]) > 0.0001f)
            {
                areBallsMoving = true;
                break;
            }
        }

        if (areBallsMoving) stopTimer = 0f;
        else stopTimer += Time.deltaTime;

        while (lastFramePositions.Count > allBalls.Count) lastFramePositions.RemoveAt(lastFramePositions.Count - 1);
        while (lastFramePositions.Count < allBalls.Count) lastFramePositions.Add(Vector3.zero);
        for (int i = 0; i < allBalls.Count; i++)
        {
            if (allBalls[i] != null) lastFramePositions[i] = allBalls[i].position;
        }

        if (stopTimer > timeToConsiderStopped) ResolveTurn();
    }

    public void ChangeState(GameState newState)
    {
        if (CurrentState == newState && newState != GameState.PlayerAiming) return;

        if (newState == GameState.PlayerAiming && !isApplyingNetworkState)
        {
            if (!justOrderedPocket)
            {
                if (HasPocketedAllBalls(currentPlayer))
                {
                    newState = GameState.SelectingPocket;
                }
            }
        }

        if (CurrentState == newState) return;
        CurrentState = newState;

        if (newState == GameState.PlayerAiming)
        {
            justOrderedPocket = false;
        }

        switch (newState)
        {
            case GameState.PlayerAiming:
                if (UIManager.Instance != null) UIManager.Instance.SetPocketPromptVisible(false);
                stopTimer = 0f;
                cueController.gameObject.SetActive(true);
                if (ballPlacer != null) ballPlacer.enabled = false;
                if (cameraController != null && (cueController == null || cueController.CanLocalControlCue())) cameraController.SnapToTarget();
                foreach (var pocket in allPockets) pocket.ClearScoredBalls();
                break;

            case GameState.SelectingPocket:
                cueController.gameObject.SetActive(false);
                if (UIManager.Instance != null) UIManager.Instance.SetAimingHUDVisible(false);
                if (UIManager.Instance != null) UIManager.Instance.SetPocketPromptVisible(true, CanLocalControlCurrentPlayer());
                break;

            case GameState.PlacingCueBall:
                if (UIManager.Instance != null) UIManager.Instance.SetPocketPromptVisible(false);
                if (cueController != null) cueController.gameObject.SetActive(false);
                if (ballPlacer != null) ballPlacer.enabled = true;
                if (UIManager.Instance != null) UIManager.Instance.SetAimingHUDVisible(false);
                break;

            case GameState.BallsMoving:
                if (UIManager.Instance != null) UIManager.Instance.SetPocketPromptVisible(false);
                cueController.gameObject.SetActive(false);
                if (UIManager.Instance != null) UIManager.Instance.SetAimingHUDVisible(false);
                break;

            case GameState.GameOver:
                if (UIManager.Instance != null) UIManager.Instance.SetPocketPromptVisible(false);
                cueController.gameObject.SetActive(false);
                if (UIManager.Instance != null) UIManager.Instance.SetAimingHUDVisible(false);
                break;
        }

        UpdateLocalAimingHudVisibility();
        BroadcastNetworkState();
    }


    public void BallPocketed(BallInfo ball, Pocket pocket)
    {
        Rigidbody ballRb = ball.GetComponent<Rigidbody>();
        if (allBalls.Contains(ballRb))
        {
            int index = allBalls.IndexOf(ballRb);
            if (index < lastFramePositions.Count) lastFramePositions.RemoveAt(index);
            allBalls.Remove(ballRb);
        }

        if (ball.type == BallInfo.BallType.CueBall)
        {
            wasFoulCommitted = true;
            ball.gameObject.SetActive(false);
            RespawnCueBall();
            if (NetworkBallSync.Instance != null)
            {
                NetworkBallSync.Instance.BroadcastNow();
            }
            return;
        }

        if (ball.type == BallInfo.BallType.EightBall)
        {
            if (HasPocketedAllBalls(currentPlayer) && pocket == orderedPocket && !wasFoulCommitted)
                EndGame(currentPlayer);
            else
            {
                Player winner = (currentPlayer == Player.Player1) ? Player.Player2 : Player.Player1;
                EndGame(winner);
            }
            if (NetworkBallSync.Instance != null)
            {
                NetworkBallSync.Instance.BroadcastNow();
            }
            return;
        }


        BallGroup pocketedBallGroup = (ball.type == BallInfo.BallType.Solid) ? BallGroup.Solids : BallGroup.Stripes;

        if (isTableOpen)
        {
            AssignBallGroups(ball.type);
            wasPlayerBallPocketed = true; 

            AddPocketedBallToInventory(ball.number, currentPlayer);
        }
        else
        {
            Player ballOwner = (player1Target == pocketedBallGroup) ? Player.Player1 : Player.Player2;

            AddPocketedBallToInventory(ball.number, ballOwner);

            BallGroup currentTarget = (currentPlayer == Player.Player1) ? player1Target : player2Target;
            if (pocketedBallGroup == currentTarget)
            {
                wasPlayerBallPocketed = true;
            }
        }
    }

    private void AssignBallGroups(BallInfo.BallType firstPocketedType)
    {
        isTableOpen = false;
        BallGroup targetGroup = (firstPocketedType == BallInfo.BallType.Solid) ? BallGroup.Solids : BallGroup.Stripes;
        BallGroup otherGroup = (targetGroup == BallGroup.Solids) ? BallGroup.Stripes : BallGroup.Solids;

        if (currentPlayer == Player.Player1) { player1Target = targetGroup; player2Target = otherGroup; }
        else { player2Target = targetGroup; player1Target = otherGroup; }
    }

    private void ResolveTurn()
    {
        if (!HasLocalRulesAuthority())
        {
            return;
        }

        if (wasFoulCommitted)
        {
            SwitchPlayer();
            ChangeState(GameState.PlacingCueBall);
        }
        else if (wasPlayerBallPocketed)
        {
            ChangeState(GameState.PlayerAiming);
        }
        else 
        {
            SwitchPlayer();
            ChangeState(GameState.PlayerAiming);
        }

        wasFoulCommitted = false;
        wasPlayerBallPocketed = false;
    }

    private void SwitchPlayer()
    {
        currentPlayer = (currentPlayer == Player.Player1) ? Player.Player2 : Player.Player1;
        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateActivePlayerUI(currentPlayer);
        }

        UpdateLocalAimingHudVisibility();
        BroadcastNetworkState();
    }

    public void ApplyNetworkState(Player networkCurrentPlayer, GameState networkState)
    {
        currentPlayer = networkCurrentPlayer;

        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateActivePlayerUI(currentPlayer);
        }

        UpdateLocalAimingHudVisibility();

        if (CurrentState != networkState)
        {
            try
            {
                isApplyingNetworkState = true;
                ChangeState(networkState);
            }
            finally
            {
                isApplyingNetworkState = false;
            }
        }
    }

    public void ApplyNetworkRules(
        BallGroup networkPlayer1Target,
        BallGroup networkPlayer2Target,
        bool networkTableOpen,
        int orderedPocketIndex,
        bool networkGameOver,
        Player networkWinner,
        int[] player1Balls,
        int[] player2Balls)
    {
        player1Target = networkPlayer1Target;
        player2Target = networkPlayer2Target;
        isTableOpen = networkTableOpen;
        isGameOver = networkGameOver;
        gameWinner = networkWinner;
        orderedPocket = GetPocketByIndex(orderedPocketIndex);

        player1PocketedBalls.Clear();
        player2PocketedBalls.Clear();
        if (player1Balls != null) player1PocketedBalls.AddRange(player1Balls);
        if (player2Balls != null) player2PocketedBalls.AddRange(player2Balls);

        if (UIManager.Instance != null)
        {
            UIManager.Instance.SetBallInventory(player1PocketedBalls, player2PocketedBalls);
            if (isGameOver)
            {
                UIManager.Instance.ShowGameOver(gameWinner);
            }
        }
    }

    private void BroadcastNetworkState()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
        {
            return;
        }

        NetworkPlayer.BroadcastGameState(currentPlayer, CurrentState);
        NetworkPlayer.BroadcastRulesState(
            player1Target,
            player2Target,
            isTableOpen,
            GetPocketIndex(orderedPocket),
            isGameOver,
            gameWinner,
            player1PocketedBalls.ToArray(),
            player2PocketedBalls.ToArray());

        if (NetworkBallSync.Instance != null)
        {
            NetworkBallSync.Instance.BroadcastNow();
        }
    }

    private void UpdateLocalAimingHudVisibility()
    {
        if (UIManager.Instance == null)
        {
            return;
        }

        bool canShowHud = CurrentState == GameState.PlayerAiming
            && cueController != null
            && cueController.CanLocalControlCue();

        UIManager.Instance.SetAimingHUDVisible(canShowHud);
    }

    private void AddPocketedBallToInventory(int ballNumber, Player owner)
    {
        List<int> targetList = owner == Player.Player1 ? player1PocketedBalls : player2PocketedBalls;
        if (!targetList.Contains(ballNumber))
        {
            targetList.Add(ballNumber);
        }

        if (UIManager.Instance != null)
        {
            UIManager.Instance.SetBallInventory(player1PocketedBalls, player2PocketedBalls);
        }

        BroadcastNetworkState();
    }

    private void RespawnCueBall()
    {
        Rigidbody cueBallRb = cueController.cueBall.GetComponent<Rigidbody>();
        Transform cueBall = cueController.cueBall;

        if (cueBallRb != null)
        {
            cueBallRb.isKinematic = true;
            cueBallRb.linearVelocity = Vector3.zero;
            cueBallRb.angularVelocity = Vector3.zero;
            if (!allBalls.Contains(cueBallRb)) allBalls.Add(cueBallRb);
        }

        Collider cueBallCollider = cueBall.GetComponent<Collider>();
        if (cueBallCollider != null)
        {
            cueBallCollider.enabled = false;
        }

        cueBall.position = GetSafeCueBallPlacementPosition();
        Physics.SyncTransforms();
    }

    private bool HasPocketedAllBalls(Player player)
    {
        BallGroup targetGroup = (player == Player.Player1) ? player1Target : player2Target;
        if (targetGroup == BallGroup.None) return false;
        foreach (var ball in allBalls)
        {
            if (ball == null) continue;
            BallInfo info = ball.GetComponent<BallInfo>();
            if (info.type == BallInfo.BallType.Solid && targetGroup == BallGroup.Solids) return false;
            if (info.type == BallInfo.BallType.Striped && targetGroup == BallGroup.Stripes) return false;
        }
        return true;
    }

    public void SetTargetPocket(Pocket pocket)
    {
        if (CurrentState != GameState.SelectingPocket) return;
        if (!CanLocalControlCurrentPlayer()) return;

        if (IsNetworkClientOnly())
        {
            NetworkPlayer.Local?.RequestPocketSelectionServerRpc(GetPocketIndex(pocket));
            return;
        }

        ApplyTargetPocket(pocket);
    }

    public bool TryApplyNetworkPocketSelectionFromServer(ulong senderClientId, int pocketIndex)
    {
        if (!IsNetworkServer() || CurrentState != GameState.SelectingPocket)
        {
            return false;
        }

        int slotIndex = currentPlayer == Player.Player1 ? 0 : 1;
        if (NetworkPlayer.TryGetClientIdForPlayerSlot(slotIndex, out ulong activeClientId) && senderClientId != activeClientId)
        {
            return false;
        }

        Pocket pocket = GetPocketByIndex(pocketIndex);
        if (pocket == null)
        {
            return false;
        }

        ApplyTargetPocket(pocket);
        return true;
    }

    private void ApplyTargetPocket(Pocket pocket)
    {
        orderedPocket = pocket;

        justOrderedPocket = true; 

        ChangeState(GameState.PlayerAiming);
    }

    private int GetPocketIndex(Pocket pocket)
    {
        if (pocket == null || allPockets == null)
        {
            return -1;
        }

        return allPockets.IndexOf(pocket);
    }

    private Pocket GetPocketByIndex(int pocketIndex)
    {
        if (allPockets == null || pocketIndex < 0 || pocketIndex >= allPockets.Count)
        {
            return null;
        }

        return allPockets[pocketIndex];
    }

    public void FinishPlacingBall()
    {
        StartCoroutine(FinishPlacingBallRoutine());
    }

    public bool TryApplyNetworkCueBallPlacementFromServer(ulong senderClientId, Vector3 position)
    {
        if (!IsNetworkServer() || CurrentState != GameState.PlacingCueBall || cueController == null || cueController.cueBall == null)
        {
            return false;
        }

        int slotIndex = currentPlayer == Player.Player1 ? 0 : 1;
        if (NetworkPlayer.TryGetClientIdForPlayerSlot(slotIndex, out ulong activeClientId) && senderClientId != activeClientId)
        {
            return false;
        }

        ApplyCueBallPlacement(ResolveServerCueBallPlacement(position));
        ChangeState(GameState.PlayerAiming);
        return true;
    }

    public bool TryBroadcastNetworkCueBallPreviewFromServer(ulong senderClientId, Vector3 position)
    {
        if (!IsNetworkServer() || CurrentState != GameState.PlacingCueBall || cueController == null || cueController.cueBall == null)
        {
            return false;
        }

        int slotIndex = currentPlayer == Player.Player1 ? 0 : 1;
        if (NetworkPlayer.TryGetClientIdForPlayerSlot(slotIndex, out ulong activeClientId) && senderClientId != activeClientId)
        {
            return false;
        }

        Transform cueBall = cueController.cueBall;
        Vector3 previewPosition = new Vector3(position.x, GetCueBallPlacementY(cueBall), position.z);
        NetworkPlayer.BroadcastCueBallPreview(previewPosition);
        return true;
    }

    public void ApplyNetworkCueBallPreview(Vector3 position)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if ((networkManager != null && networkManager.IsServer)
            || CurrentState != GameState.PlacingCueBall
            || CanLocalControlCurrentPlayer()
            || cueController == null
            || cueController.cueBall == null)
        {
            return;
        }

        Transform cueBall = cueController.cueBall;
        cueBall.gameObject.SetActive(true);
        cueBall.position = new Vector3(position.x, GetCueBallPlacementY(cueBall), position.z);

        Rigidbody cueBallRb = cueBall.GetComponent<Rigidbody>();
        if (cueBallRb != null)
        {
            cueBallRb.isKinematic = true;
            cueBallRb.linearVelocity = Vector3.zero;
            cueBallRb.angularVelocity = Vector3.zero;
        }

        Collider cueBallCollider = cueBall.GetComponent<Collider>();
        if (cueBallCollider != null)
        {
            cueBallCollider.enabled = false;
        }

        BallPhysics cueBallPhysics = cueBall.GetComponent<BallPhysics>();
        if (cueBallPhysics != null)
        {
            cueBallPhysics.enabled = false;
        }

        MeshRenderer[] renderers = cueBall.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer renderer in renderers)
        {
            renderer.enabled = true;
        }

        Physics.SyncTransforms();
    }

    public void ApplyLocalCueBallPlacement(Vector3 position)
    {
        ApplyCueBallPlacement(ResolveServerCueBallPlacement(position));
        FinishPlacingBall();
    }

    private void ApplyCueBallPlacement(Vector3 position)
    {
        Transform cueBall = cueController.cueBall;
        float fixedY = GetCueBallPlacementY(cueBall);

        Rigidbody cueBallRb = cueBall.GetComponent<Rigidbody>();
        Collider cueBallCollider = cueBall.GetComponent<Collider>();

        if (cueBallCollider != null)
        {
            cueBallCollider.enabled = false;
        }

        if (cueBallRb != null)
        {
            cueBallRb.isKinematic = true;
            cueBallRb.linearVelocity = Vector3.zero;
            cueBallRb.angularVelocity = Vector3.zero;
        }

        cueBall.position = new Vector3(position.x, fixedY, position.z);
        cueBall.gameObject.SetActive(true);
        Physics.SyncTransforms();

        if (cueBallRb != null)
        {
            cueBallRb.isKinematic = false;
            if (!allBalls.Contains(cueBallRb))
            {
                allBalls.Add(cueBallRb);
            }
        }

        if (cueBallCollider != null)
        {
            cueBallCollider.enabled = true;
        }

        BallPhysics cueBallPhysics = cueBall.GetComponent<BallPhysics>();
        if (cueBallPhysics != null)
        {
            cueBallPhysics.enabled = !IsNetworkClientOnly();
        }

        Physics.SyncTransforms();

        MeshRenderer[] renderers = cueBall.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer renderer in renderers)
        {
            renderer.enabled = true;
        }

        if (NetworkBallSync.Instance != null)
        {
            NetworkBallSync.Instance.BroadcastNow();
        }
    }

    private Vector3 ResolveServerCueBallPlacement(Vector3 requestedPosition)
    {
        Transform cueBall = cueController.cueBall;
        Vector3 normalizedRequestedPosition = new Vector3(requestedPosition.x, GetCueBallPlacementY(cueBall), requestedPosition.z);
        if (IsCueBallPlacementClear(normalizedRequestedPosition))
        {
            return normalizedRequestedPosition;
        }

        Vector3 safePosition = GetSafeCueBallPlacementPosition();
        return IsCueBallPlacementClear(safePosition) ? safePosition : normalizedRequestedPosition;
    }

    private bool IsCueBallPlacementClear(Vector3 position)
    {
        if (cueController == null || cueController.cueBall == null)
        {
            return false;
        }

        Rigidbody cueBallRb = cueController.cueBall.GetComponent<Rigidbody>();
        SphereCollider cueBallCollider = cueController.cueBall.GetComponent<SphereCollider>();
        if (cueBallCollider == null)
        {
            return true;
        }

        float cueBallRadius = cueBallCollider.radius * cueController.cueBall.localScale.x;
        foreach (Rigidbody otherBall in allBalls)
        {
            if (otherBall == null || otherBall == cueBallRb || !otherBall.gameObject.activeInHierarchy)
            {
                continue;
            }

            SphereCollider otherSphere = otherBall.GetComponent<SphereCollider>();
            float otherRadius = otherSphere != null ? otherSphere.radius * otherBall.transform.localScale.x : cueBallRadius;
            Vector2 cuePosition = new Vector2(position.x, position.z);
            Vector2 otherPosition = new Vector2(otherBall.position.x, otherBall.position.z);
            float minimumDistance = (cueBallRadius + otherRadius) * 0.95f;
            if ((cuePosition - otherPosition).sqrMagnitude < minimumDistance * minimumDistance)
            {
                return false;
            }
        }

        return true;
    }

    private Vector3 GetSafeCueBallPlacementPosition()
    {
        if (cueBallStartPoint != null)
        {
            return cueBallStartPoint.position;
        }

        Transform cueBall = cueController.cueBall;
        return new Vector3(cueBall.position.x, GetCueBallPlacementY(cueBall), cueBall.position.z);
    }

    private float GetCueBallPlacementY(Transform cueBall)
    {
        if (ballPlacer != null && ballPlacer.lockedY != 0f)
        {
            return ballPlacer.lockedY;
        }

        if (cueBallStartPoint != null)
        {
            return cueBallStartPoint.position.y;
        }

        return cueBall.position.y;
    }

    private IEnumerator FinishPlacingBallRoutine()
    {
        yield return null;

        ChangeState(GameState.PlayerAiming);
    }

    private void EndGame(Player winner)
    {
        isGameOver = true;
        gameWinner = winner;
        ChangeState(GameState.GameOver);
        UIManager.Instance.ShowGameOver(winner);
        BroadcastNetworkState();
    }

    private static bool IsNetworkServer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening && networkManager.IsServer;
    }

    private static bool IsNetworkClientOnly()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening && networkManager.IsClient && !networkManager.IsServer;
    }

    private static bool HasLocalRulesAuthority()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager == null || !networkManager.IsListening || networkManager.IsServer;
    }

    private static bool CanLocalControlCurrentPlayer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            return true;
        }

        if (GameManager.Instance == null || NetworkPlayer.Local == null)
        {
            return false;
        }

        int slotIndex = GameManager.Instance.currentPlayer == Player.Player1 ? 0 : 1;
        return NetworkPlayer.TryGetClientIdForPlayerSlot(slotIndex, out ulong activeClientId)
            && activeClientId == NetworkPlayer.Local.OwnerClientId;
    }
}
