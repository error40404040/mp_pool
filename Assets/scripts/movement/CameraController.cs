using UnityEngine;
using UnityEngine.InputSystem;

public class CameraController : MonoBehaviour
{
    [Header("Ссылки")]
    public Transform cue;
    public Transform cueBall;
    public CueController cueController;

    [Header("Точка над столом")]
    public Transform topDownViewPoint; 
    public float transitionSpeed = 5f; 

    [Header("Прицеливание")]
    public float distanceBehindCue = 0.5f; 
    public float heightAboveCue = 0.1f;    
    public float smoothSpeed = 10f;        

    [Header("Зум")]
    public float zoomSpeed = 20f;
    public float minZoomDistance = 0.2f;
    public float maxZoomDistance = 1.0f;

    private PlayerControls controls;
    private float currentDistance; 

    void Awake()
    {
        controls = new PlayerControls();
        currentDistance = distanceBehindCue; 
    }
    void OnEnable() { controls.Gameplay.Enable(); }
    void OnDisable() { controls.Gameplay.Disable(); }

    void LateUpdate()
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        GameState state = GameManager.Instance.CurrentState;

        if (state == GameState.PlayerAiming && CanUseAimingCamera())
        {
            if (cue == null || cueBall == null || cueController == null) return;

            float zoomInput = controls.Gameplay.CameraZoom.ReadValue<float>() * 0.01f;
            currentDistance -= zoomInput * zoomSpeed * Time.deltaTime;
            currentDistance = Mathf.Clamp(currentDistance, minZoomDistance, maxZoomDistance);

            Vector3 desiredPosition = cue.position;
            desiredPosition -= cueController.AimDirection * currentDistance;
            desiredPosition += Vector3.up * heightAboveCue;

            transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
            transform.LookAt(cueBall); 
        }
        else if (state == GameState.PlayerAiming || state == GameState.BallsMoving || state == GameState.PlacingCueBall ||
                 state == GameState.SelectingPocket || state == GameState.GameOver)
        {
            if (topDownViewPoint == null) return;

            transform.position = Vector3.Lerp(transform.position, topDownViewPoint.position, transitionSpeed * Time.deltaTime);
            transform.rotation = Quaternion.Lerp(transform.rotation, topDownViewPoint.rotation, transitionSpeed * Time.deltaTime);
        }
    }

    public void SnapToTarget()
    {
        if (cueController == null || cueBall == null) return;

        cueController.ResetAngles();
        cueController.ForceUpdateTransform();

        Vector3 desiredPosition = cueController.transform.position;
        desiredPosition -= cueController.AimDirection * currentDistance;
        desiredPosition += Vector3.up * heightAboveCue;

        transform.position = desiredPosition;
        transform.LookAt(cueBall);
    }

    private bool CanUseAimingCamera()
    {
        return cueController == null || cueController.CanLocalControlCue();
    }
}
