using UnityEngine;
using UnityEngine.InputSystem;

public class CueController : MonoBehaviour
{
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

    private PlayerControls controls;

    public Vector3 AimDirection { get; private set; }

    private void Awake()
    {
        controls = new PlayerControls();
        controls.Gameplay.Strike.started += ctx => StartCharge();
        controls.Gameplay.Strike.canceled += ctx => PerformStrike();
    }

    private void OnEnable() { controls.Gameplay.Enable(); }
    private void OnDisable() { controls.Gameplay.Disable(); }

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
        if (GameManager.Instance.CurrentState != GameState.PlayerAiming)
        {
            if (aimUICrosshair != null && aimUICrosshair.parent.gameObject.activeSelf)
                aimUICrosshair.parent.gameObject.SetActive(false);
            if (strikePointVisualizer != null && strikePointVisualizer.gameObject.activeSelf)
                strikePointVisualizer.gameObject.SetActive(false);
            return;
        }
        else
        {
            if (aimUICrosshair != null && !aimUICrosshair.parent.gameObject.activeSelf)
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
        ForceUpdateTransform();
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
            float ballRadius = cueBall.GetComponent<SphereCollider>().radius * cueBall.transform.localScale.x;
            Vector2 normalizedOffset = aimOffset / maxAimOffset;
            Vector3 localOffset = (playerCamera.right * normalizedOffset.x + playerCamera.up * normalizedOffset.y) * ballRadius;
            Vector3 surfacePosition = cueBall.position + localOffset;

            Vector3 directionFromCenter = (surfacePosition - cueBall.position).normalized;
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
        if (Time.timeScale == 0 || isCharging || GameManager.Instance.CurrentState != GameState.PlayerAiming) return;

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

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayCueStrike(currentPower, maxPower);
        }

        GameManager.Instance.ChangeState(GameState.BallsMoving);
        cueBallRb.AddForceAtPosition(strikeDirection * currentPower, strikePosition, ForceMode.Impulse);

        currentPower = 0f;
        aimOffset = Vector2.zero;
        if (aimUICrosshair != null)
            aimUICrosshair.anchoredPosition = Vector2.zero;
        if (cueBall != null)
            lastAimedPoint = cueBall.position;
        if (UIManager.Instance != null)
            UIManager.Instance.UpdatePower(0, maxPower);
    }

    public Vector3 GetLastAimedPoint()
    {
        return lastAimedPoint;
    }
}