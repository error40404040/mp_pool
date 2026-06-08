using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections.Generic;
using System.Collections;

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

    private List<Vector3> lastFramePositions;
    private float stopTimer = 0f;
    private PlayerControls controls;
    private const float timeToConsiderStopped = 0.2f;

    private bool justOrderedPocket = false;

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

    private void OnEnable() { controls.Gameplay.Enable(); }
    private void OnDisable() { controls.Gameplay.Disable(); }

    private void Start()
    {
        currentPlayer = Player.Player1;
        isTableOpen = true;

        // при старте подсвечиваем первого игрока 
        if (UIManager.Instance != null)
        {
            UIManager.Instance.UpdateActivePlayerUI(currentPlayer);
        }

        ChangeState(GameState.PlayerAiming);
    }

    private void Update()
    {
        if (CurrentState == GameState.BallsMoving)
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

        if (newState == GameState.PlayerAiming)
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
                if (cameraController != null) cameraController.SnapToTarget();
                foreach (var pocket in allPockets) pocket.ClearScoredBalls();
                if (UIManager.Instance != null) UIManager.Instance.SetAimingHUDVisible(true);
                break;

            case GameState.SelectingPocket:
                cueController.gameObject.SetActive(false);
                if (UIManager.Instance != null) UIManager.Instance.SetAimingHUDVisible(false);
                if (UIManager.Instance != null) UIManager.Instance.SetPocketPromptVisible(true);
                break;

            case GameState.PlacingCueBall:
                if (UIManager.Instance != null) UIManager.Instance.SetPocketPromptVisible(false);
                cueController.gameObject.SetActive(false);
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
            return;
        }


        BallGroup pocketedBallGroup = (ball.type == BallInfo.BallType.Solid) ? BallGroup.Solids : BallGroup.Stripes;

        if (isTableOpen)
        {
            AssignBallGroups(ball.type);
            wasPlayerBallPocketed = true; 

            if (UIManager.Instance != null)
                UIManager.Instance.AddBallToInventory(ball.number, currentPlayer);
        }
        else
        {
            Player ballOwner = (player1Target == pocketedBallGroup) ? Player.Player1 : Player.Player2;

            if (UIManager.Instance != null)
                UIManager.Instance.AddBallToInventory(ball.number, ballOwner);

            BallGroup currentTarget = (currentPlayer == Player.Player1) ? player1Target : player2Target;
            if (pocketedBallGroup == currentTarget)
            {
                wasPlayerBallPocketed = true;
            }
            else
            {
                Debug.LogWarning($"Игрок {currentPlayer} забил шар соперника ({ballOwner})!");
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

        if (wasFoulCommitted)
        {
            Debug.Log("Был совершен фол. Передача хода.");
            SwitchPlayer();
            ChangeState(GameState.PlacingCueBall);
        }
        else if (wasPlayerBallPocketed)
        {
            Debug.Log("Был забит свой шар. Ход продолжается.");
            ChangeState(GameState.PlayerAiming);
        }
        else 
        {
            Debug.Log("Промах. Передача хода.");
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
    }

    private void RespawnCueBall()
    {
        Rigidbody cueBallRb = cueController.cueBall.GetComponent<Rigidbody>();
        cueBallRb.linearVelocity = Vector3.zero;
        cueBallRb.angularVelocity = Vector3.zero;
        if (!allBalls.Contains(cueBallRb)) allBalls.Add(cueBallRb);
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

        orderedPocket = pocket;
        Debug.Log("Луза заказана: " + pocket.name);

        justOrderedPocket = true; 

        ChangeState(GameState.PlayerAiming);
    }

    public void FinishPlacingBall()
    {
        StartCoroutine(FinishPlacingBallRoutine());
    }

    private IEnumerator FinishPlacingBallRoutine()
    {
        yield return null;

        ChangeState(GameState.PlayerAiming);
    }

    private void EndGame(Player winner)
    {
        ChangeState(GameState.GameOver);
        UIManager.Instance.ShowGameOver(winner);
    }
}