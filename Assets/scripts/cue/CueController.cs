using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

public class CueController : MonoBehaviour
{
    public static CueController Instance { get; private set; }

    [Header("Ссылки")]
    public Transform cueBall;
    public Transform playerCamera;

    [Header("Настройки прицеливания")]
    public float rotationSpeed = 100f;
    public float pitchSpeed = 100f;
    public float minPitchAngle = -5f;
    public float maxPitchAngle = 45f;
    public float baseOffsetFromBall = 0.7f;
    [Range(0.1f, 1f)] public float slowAimMultiplier = 0.25f;
    public float initialYawOnStart = 180f;
    public float initialPitchOnStart = 20f;

    [Header("Настройки удара")]
    public float maxPower = 1.5f;
    public float powerSpeed = 1.0f;
    public float maxPullbackDistance = 1f;

    [Header("UI и Визуализация Прицеливания")]
    public Transform strikePointVisualizer;
    public RectTransform aimUIBackground;
    public RectTransform aimUICrosshair;
    public float aimOffsetSensitivity = 1.0f;

    private float currentYaw = 0f;
    private float currentPitch = 5f;
    private bool isCharging = false;
    private float currentPower = 0f;
    private Vector2 aimOffset = Vector2.zero;
    private Vector2 lastMousePosition;
    private float maxAimOffset;
    private Vector3 lastAimedPoint; 
    private Vector3 remoteCuePosition;
    private Quaternion remoteCueRotation;
    private float nextCueSyncTime;
    private bool hasRemoteCueTransform = false;
    private const float cueSyncInterval = 0.05f;

    private PlayerControls controls;

    public Vector3 AimDirection { get; private set; }

    private void Awake()
    {
        Instance = this;
        controls = new PlayerControls();
        controls.Gameplay.Strike.started += ctx => StartCharge();
        controls.Gameplay.Strike.canceled += ctx => PerformStrike();
    }

    private void OnEnable() { controls.Gameplay.Enable(); }
    private void OnDisable() { controls.Gameplay.Disable(); }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Start()
    {
        if (cueBall != null)
        {
            currentYaw = initialYawOnStart;
            currentPitch = initialPitchOnStart;
            lastAimedPoint = cueBall.position;
        }

        if (aimUIBackground != null)
        {
            maxAimOffset = (aimUIBackground.sizeDelta.x / 2.0f) - 10f;
        }
        else
        {
            maxAimOffset = 70f; 
        }
    }

    private void Update()
    {
        if (GameManager.Instance == null)
        {
            SetCueVisualsVisible(false);
            return;
        }

        bool canControlCue = CanLocalControlCue();
        bool showCue = canControlCue || ShouldShowRemoteCue();
        SetCueVisualsVisible(showCue);

        if (GameManager.Instance.CurrentState != GameState.PlayerAiming || !canControlCue)
        {
            if (aimUICrosshair != null && aimUICrosshair.parent != null && aimUICrosshair.parent.gameObject.activeSelf)
                aimUICrosshair.parent.gameObject.SetActive(false);
            if (strikePointVisualizer != null && strikePointVisualizer.gameObject.activeSelf)
                strikePointVisualizer.gameObject.SetActive(false);
            return;
        }
        else
        {
            if (aimUICrosshair != null && aimUICrosshair.parent != null && !aimUICrosshair.parent.gameObject.activeSelf)
                aimUICrosshair.parent.gameObject.SetActive(true);
        }

        if (cueBall == null) return;

        float currentRotationSpeed = rotationSpeed;
        float currentPitchSpeed = pitchSpeed;
        if (controls.Gameplay.SlowAim.IsPressed())
        {
            currentRotationSpeed *= slowAimMultiplier;
            currentPitchSpeed *= slowAimMultiplier;
        }

        float yawInput = controls.Gameplay.CameraRotate.ReadValue<float>();
        float pitchInput = controls.Gameplay.CameraPitch.ReadValue<float>();
        currentYaw += yawInput * currentRotationSpeed * Time.deltaTime;
        currentPitch -= pitchInput * currentPitchSpeed * Time.deltaTime;
        currentPitch = Mathf.Clamp(currentPitch, minPitchAngle, maxPitchAngle);

        if (isCharging)
        {
            currentPower += powerSpeed * Time.deltaTime;
            currentPower = Mathf.Min(currentPower, maxPower);

            if (UIManager.Instance != null)
            {
                UIManager.Instance.UpdatePower(currentPower, maxPower);
            }
        }

        if (controls.Gameplay.EnableAimOffset.IsPressed())
        {
            if (strikePointVisualizer != null && !strikePointVisualizer.gameObject.activeSelf)
                strikePointVisualizer.gameObject.SetActive(true);

            Mouse currentMouse = Mouse.current;
            if (currentMouse == null) return;
            Vector2 currentMousePosition = currentMouse.position.ReadValue();
            Vector2 mouseDelta = currentMousePosition - lastMousePosition;

            aimOffset += mouseDelta * aimOffsetSensitivity;
            aimOffset = Vector2.ClampMagnitude(aimOffset, maxAimOffset);

            if (aimUICrosshair != null)
                aimUICrosshair.anchoredPosition = aimOffset;

            lastMousePosition = currentMousePosition;
        }
        else
        {
            if (strikePointVisualizer != null && strikePointVisualizer.gameObject.activeSelf)
                strikePointVisualizer.gameObject.SetActive(false);

            Mouse currentMouse = Mouse.current;
            if (currentMouse != null)
                lastMousePosition = currentMouse.position.ReadValue();
        }

    }

    private void LateUpdate()
    {
        if (ShouldShowRemoteCue())
        {
            transform.SetPositionAndRotation(remoteCuePosition, remoteCueRotation);
            return;
        }

        ForceUpdateTransform();
        SyncCueTransformIfNeeded();
    }

    public void ResetAngles()
    {
        currentYaw = 0f;
        currentPitch = 5f;

        if (cueBall != null)
        {
            lastAimedPoint = cueBall.position;
        }
    }

    public void ForceUpdateTransform()
    {
        if (cueBall == null) return;

        Quaternion rotation = Quaternion.Euler(currentPitch, currentYaw, 0);
        AimDirection = rotation * Vector3.forward;

        float pullback = (currentPower / maxPower) * maxPullbackDistance;
        Vector3 position = cueBall.position - AimDirection * (baseOffsetFromBall + pullback);
        transform.position = position;

        Vector3 pointToLookAt = lastAimedPoint;
        if (strikePointVisualizer != null && strikePointVisualizer.gameObject.activeSelf)
        {
            SphereCollider cueBallCollider = cueBall.GetComponent<SphereCollider>();
            if (cueBallCollider == null || playerCamera == null || maxAimOffset <= 0f)
            {
                transform.LookAt(pointToLookAt);
                return;
            }

            float ballRadius = cueBallCollider.radius * cueBall.transform.localScale.x;
            Vector2 normalizedOffset = aimOffset / maxAimOffset;
            Vector3 localOffset = (playerCamera.right * normalizedOffset.x + playerCamera.up * normalizedOffset.y) * ballRadius;
            Vector3 surfacePosition = cueBall.position + localOffset;

            Vector3 directionFromCenter = (surfacePosition - cueBall.position).normalized;
            if (directionFromCenter.sqrMagnitude < 0.0001f)
            {
                directionFromCenter = AimDirection.sqrMagnitude > 0.0001f ? AimDirection.normalized : Vector3.forward;
            }

            float visualizerRadius = strikePointVisualizer.transform.localScale.x / 2.0f;
            Vector3 visualizerPosition = surfacePosition + directionFromCenter * visualizerRadius;
            strikePointVisualizer.position = visualizerPosition;
            strikePointVisualizer.rotation = Quaternion.LookRotation(directionFromCenter);

            pointToLookAt = surfacePosition;
            lastAimedPoint = surfacePosition;
        }

        transform.LookAt(pointToLookAt);
    }

    private void StartCharge()
    {
        if (GameManager.Instance == null) return;
        if (Time.timeScale == 0 || isCharging || GameManager.Instance.CurrentState != GameState.PlayerAiming || !CanLocalControlCue()) return;

        isCharging = true;
        currentPower = 0f;
    }

    private void PerformStrike()
    {
        if (!isCharging) return;
        isCharging = false;

        if (strikePointVisualizer != null)
            strikePointVisualizer.gameObject.SetActive(false);

        if (currentPower < 0.1f)
        {
            currentPower = 0f;
            if (UIManager.Instance != null) UIManager.Instance.UpdatePower(0, maxPower);
            return;
        }

        Rigidbody cueBallRb = cueBall.GetComponent<Rigidbody>();
        SphereCollider cueBallCollider = cueBall.GetComponent<SphereCollider>();
        if (cueBallRb == null || cueBallCollider == null) return;

        Vector3 strikePosition = lastAimedPoint;
        Vector3 strikeDirection = (strikePosition - transform.position).normalized;

        if (IsNetworkClientOnly())
        {
            NetworkPlayer.Local?.RequestCueStrikeServerRpc(strikeDirection, strikePosition, currentPower);
            ResetStrikeState();
            return;
        }

        ApplyStrike(strikeDirection, strikePosition, currentPower);
        ResetStrikeState();
    }

    public bool TryApplyNetworkStrikeFromServer(ulong senderClientId, Vector3 strikeDirection, Vector3 strikePosition, float requestedPower)
    {
        if (!IsNetworkServer())
        {
            return false;
        }

        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.PlayerAiming)
        {
            return false;
        }

        int slotIndex = GameManager.Instance.currentPlayer == Player.Player1 ? 0 : 1;
        if (NetworkPlayer.TryGetClientIdForPlayerSlot(slotIndex, out ulong activeClientId) && senderClientId != activeClientId)
        {
            return false;
        }

        float clampedPower = Mathf.Clamp(requestedPower, 0f, maxPower);
        if (clampedPower < 0.1f)
        {
            return false;
        }

        ApplyStrike(strikeDirection.normalized, strikePosition, clampedPower);
        return true;
    }

    public bool TryBroadcastNetworkCueTransformFromServer(ulong senderClientId, Vector3 position, Quaternion rotation)
    {
        if (!IsNetworkServer() || GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.PlayerAiming)
        {
            return false;
        }

        int slotIndex = GameManager.Instance.currentPlayer == Player.Player1 ? 0 : 1;
        if (NetworkPlayer.TryGetClientIdForPlayerSlot(slotIndex, out ulong activeClientId) && senderClientId != activeClientId)
        {
            return false;
        }

        NetworkPlayer.BroadcastCueTransform(position, rotation);
        return true;
    }

    private void ApplyStrike(Vector3 strikeDirection, Vector3 strikePosition, float strikePower)
    {
        Rigidbody cueBallRb = cueBall.GetComponent<Rigidbody>();
        if (cueBallRb == null) return;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayCueStrike(strikePower, maxPower);
        }

        GameManager.Instance.ChangeState(GameState.BallsMoving);
        cueBallRb.AddForceAtPosition(strikeDirection * strikePower, strikePosition, ForceMode.Impulse);
    }

    private void ResetStrikeState()
    {
        currentPower = 0f;
        aimOffset = Vector2.zero;
        if (aimUICrosshair != null)
            aimUICrosshair.anchoredPosition = Vector2.zero;
        if (cueBall != null)
            lastAimedPoint = cueBall.position;
        if (UIManager.Instance != null)
            UIManager.Instance.UpdatePower(0, maxPower);
    }

    public bool CanLocalControlCue()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            return true;
        }

        if (NetworkPlayer.Local == null || GameManager.Instance == null)
        {
            return false;
        }

        int slotIndex = GameManager.Instance.currentPlayer == Player.Player1 ? 0 : 1;
        return NetworkPlayer.TryGetClientIdForPlayerSlot(slotIndex, out ulong activeClientId)
            && activeClientId == NetworkPlayer.Local.OwnerClientId;
    }

    private bool IsNetworkClientOnly()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening && networkManager.IsClient && !networkManager.IsServer;
    }

    private bool IsNetworkServer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening && networkManager.IsServer;
    }

    public Vector3 GetLastAimedPoint()
    {
        return lastAimedPoint;
    }

    public void ApplyRemoteCueTransform(Vector3 position, Quaternion rotation)
    {
        if (CanLocalControlCue())
        {
            return;
        }

        remoteCuePosition = position;
        remoteCueRotation = rotation;
        hasRemoteCueTransform = true;
    }

    private void SetCueVisualsVisible(bool visible)
    {
        MeshRenderer[] renderers = GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer renderer in renderers)
        {
            renderer.enabled = visible;
        }
    }

    private void SyncCueTransformIfNeeded()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || Time.time < nextCueSyncTime || !CanLocalControlCue())
        {
            return;
        }

        nextCueSyncTime = Time.time + cueSyncInterval;
        if (networkManager.IsServer)
        {
            NetworkPlayer.BroadcastCueTransform(transform.position, transform.rotation);
            return;
        }

        NetworkPlayer.Local?.RequestCueTransformServerRpc(transform.position, transform.rotation);
    }

    private bool ShouldShowRemoteCue()
    {
        return hasRemoteCueTransform
            && GameManager.Instance != null
            && GameManager.Instance.CurrentState == GameState.PlayerAiming
            && !CanLocalControlCue();
    }
}
