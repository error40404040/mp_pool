using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class BallPlacer : MonoBehaviour
{
    [Header("Настройки")]
    public Transform cueBall;
    public SphereCollider cueBallCollider;
    public LayerMask tableLayer; 
    public LayerMask ballsLayer; 

    [Header("Визуализация")]
    public Material validMaterial;
    public Material invalidMaterial;
    public float lockedY; 

    private Material originalMaterial;
    private MeshRenderer cueBallRenderer;
    private Camera mainCamera;
    private PlayerControls controls;
    private BallPhysics cueBallPhysics;
    private bool isValidPosition = false;
    private bool preparedForPlacement = false;
    private float nextPreviewSyncTime = 0f;
    private const float previewSyncInterval = 0.05f;

    private void Awake()
    {
        mainCamera = Camera.main;
        controls = new PlayerControls();
    }

    private void Start()
    {
        if (lockedY == 0 && cueBall != null)
        {
            lockedY = cueBall.position.y;
        }
    }

    private void OnEnable()
    {
        if (controls == null)
        {
            controls = new PlayerControls();
        }

        controls.Gameplay.Enable();
        if (!CanLocalPlaceCueBall()) return;
        ResolveCueBall();

        if (cueBall != null)
        {
            PrepareCueBallForPlacement();
            MoveBallToMouse();
        }
    }

    private void OnDisable()
    {
        controls?.Gameplay.Disable();

        if (cueBall != null && preparedForPlacement)
        {
            Rigidbody rb = cueBall.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = IsNetworkClientOnly();
            }

            if (cueBallCollider != null) cueBallCollider.enabled = true;
            if (cueBallPhysics != null) cueBallPhysics.enabled = !IsNetworkClientOnly();
            if (cueBallRenderer != null) cueBallRenderer.material = originalMaterial;
        }

        preparedForPlacement = false;
    }

    private void Update()
    {
        if (!CanLocalPlaceCueBall()) return;
        ResolveCueBall();
        if (cueBall == null) return;

        PrepareCueBallForPlacement();
        MoveBallToMouse();
        CheckPositionValidity();
        SyncPreviewIfNeeded();

        if (isValidPosition && controls.Gameplay.Strike.WasPressedThisFrame())
        {
            PlaceBall();
        }
    }

    private void MoveBallToMouse()
    {
        Vector2 mousePosition = Mouse.current.position.ReadValue();
        // луч из камеры сквозь курсор
        Ray ray = mainCamera.ScreenPointToRay(mousePosition);

        // Если луч попал в стол ставим шар в точку попадания
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, tableLayer))
        {
            cueBall.position = new Vector3(hit.point.x, lockedY, hit.point.z);
        }
    }

    // можно ли поставить шар в текущем месте
    private void CheckPositionValidity()
    {
        // создаем сферу 
        float radius = cueBallCollider.radius * cueBall.localScale.x;
        bool collisionFound = Physics.CheckSphere(cueBall.position, radius * 0.9f, ballsLayer);
        isValidPosition = !collisionFound;

        if (cueBallRenderer != null)
            cueBallRenderer.material = isValidPosition ? validMaterial : invalidMaterial;
    }

    private void PlaceBall()
    {
        if (IsNetworkClientOnly())
        {
            NetworkPlayer.Local?.RequestCueBallPlacementServerRpc(cueBall.position);
            this.enabled = false;
            return;
        }

        GameManager.Instance.ApplyLocalCueBallPlacement(cueBall.position);

        this.enabled = false;
    }

    private void SyncPreviewIfNeeded()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || Time.time < nextPreviewSyncTime)
        {
            return;
        }

        nextPreviewSyncTime = Time.time + previewSyncInterval;

        if (networkManager.IsServer)
        {
            GameManager.Instance?.TryBroadcastNetworkCueBallPreviewFromServer(networkManager.LocalClientId, cueBall.position);
            return;
        }

        NetworkPlayer.Local?.RequestCueBallPreviewServerRpc(cueBall.position);
    }

    private bool CanLocalPlaceCueBall()
    {
        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.PlacingCueBall)
        {
            return false;
        }

        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            return true;
        }

        if (NetworkPlayer.Local == null)
        {
            return false;
        }

        int slotIndex = GameManager.Instance.currentPlayer == Player.Player1 ? 0 : 1;
        return NetworkPlayer.TryGetClientIdForPlayerSlot(slotIndex, out ulong activeClientId)
            && activeClientId == NetworkPlayer.Local.OwnerClientId;
    }

    private static bool IsNetworkClientOnly()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening && networkManager.IsClient && !networkManager.IsServer;
    }

    private void PrepareCueBallForPlacement()
    {
        if (cueBall == null)
        {
            this.enabled = false;
            return;
        }

        preparedForPlacement = true;
        cueBall.gameObject.SetActive(true);

        Rigidbody rb = cueBall.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }

        if (cueBallCollider != null)
        {
            cueBallCollider.enabled = false;
        }

        cueBallPhysics = cueBall.GetComponent<BallPhysics>();
        if (cueBallPhysics != null)
        {
            cueBallPhysics.enabled = false;
        }

        MeshRenderer[] renderers = cueBall.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer renderer in renderers)
        {
            renderer.enabled = true;
        }

        cueBallRenderer = cueBall.GetComponentInChildren<MeshRenderer>(true);
        if (cueBallRenderer != null && originalMaterial == null)
        {
            originalMaterial = cueBallRenderer.material;
        }
    }

    private void ResolveCueBall()
    {
        if (cueBall != null)
        {
            return;
        }

        if (GameManager.Instance != null && GameManager.Instance.cueController != null)
        {
            cueBall = GameManager.Instance.cueController.cueBall;
        }

        if (cueBall != null)
        {
            cueBallCollider = cueBall.GetComponent<SphereCollider>();
            cueBallPhysics = cueBall.GetComponent<BallPhysics>();
        }
    }
}
