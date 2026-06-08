using UnityEngine;
using UnityEngine.InputSystem;

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
        controls.Gameplay.Enable();
        if (cueBall != null)
        {
            cueBall.gameObject.SetActive(true);

            // выключаем физику и коллайдеры
            Rigidbody rb = cueBall.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = true;
            
            if (cueBallCollider != null) cueBallCollider.enabled = false;

            cueBallPhysics = cueBall.GetComponent<BallPhysics>();
            if (cueBallPhysics != null) cueBallPhysics.enabled = false;

            cueBallRenderer = cueBall.GetComponentInChildren<MeshRenderer>();
            if (cueBallRenderer != null) originalMaterial = cueBallRenderer.material;

            MoveBallToMouse();
        }
    }

    private void OnDisable()
    {
        controls.Gameplay.Disable();

        if (cueBall != null)
        {
            Rigidbody rb = cueBall.GetComponent<Rigidbody>();
            if (rb != null) rb.isKinematic = false;

            if (cueBallCollider != null) cueBallCollider.enabled = true;
            if (cueBallPhysics != null) cueBallPhysics.enabled = true;
            if (cueBallRenderer != null) cueBallRenderer.material = originalMaterial;
        }

    }

    private void Update()
    {
        if (cueBall == null) return;

        MoveBallToMouse();
        CheckPositionValidity();

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
        GameManager.Instance.FinishPlacingBall();

        this.enabled = false;
    }
}