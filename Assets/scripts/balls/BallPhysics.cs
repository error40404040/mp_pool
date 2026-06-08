using UnityEngine;
using System.Collections.Generic;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class BallPhysics : MonoBehaviour
{
    [Header("Физические свойства")]
    [Range(0f, 1f)] public float rollingFrictionCoefficient = 0.01f;
    [Range(0f, 1f)] public float ballBounciness = 0.95f;
    [Range(0f, 1f)] public float wallBounciness = 0.8f;

    public LayerMask wallLayer;

    [HideInInspector] public Rigidbody rb;
    private SphereCollider sphereCollider;
    private float radius;
    private float gravityMagnitude;
    private float initialFixedY;

    public static List<BallPhysics> allBalls = new List<BallPhysics>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        sphereCollider = GetComponent<SphereCollider>();
        gravityMagnitude = Physics.gravity.magnitude;
        // Реальный радиус с учетом скейла
        radius = sphereCollider.radius * transform.localScale.x;
        initialFixedY = transform.position.y;
    }

    private void OnEnable() { if (!allBalls.Contains(this)) allBalls.Add(this); }
    private void OnDisable() { if (allBalls.Contains(this)) allBalls.Remove(this); }

    private void FixedUpdate()
    {
        if (IsNetworkClientOnly()) return;
        if (rb.isKinematic) return;

        // Лочим высоту 
        transform.position = new Vector3(transform.position.x, initialFixedY, transform.position.z);

        PredictiveWallCollision();

        ResolveBallCollisions();
        ResolveWallCollisions();

        ApplyRollingFriction();
    }

    // Защита от пролета стен 
    private void PredictiveWallCollision()
    {
        Vector3 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;

        if (speed < 0.1f) return;

        // Дистанция = V * dt
        float frameDistance = speed * Time.fixedDeltaTime;
        Vector3 direction = velocity.normalized;

        // Кидаем сферу вперед. Если попали - отражаем вручную
        if (Physics.SphereCast(transform.position, radius, direction, out RaycastHit hit, frameDistance, wallLayer))
        {
            // Формула отражения: V' = V - 2*(V*N)*N
            float velocityDotNormal = Vector3.Dot(velocity, hit.normal);

            // Применяем формулу: V_new = V_old - 2 * (V_old . N) * N
            Vector3 reflectedVelocity = velocity - 2 * velocityDotNormal * hit.normal; reflectedVelocity.y = 0;
            rb.linearVelocity = reflectedVelocity * wallBounciness;

            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayWallCollision(rb.linearVelocity.magnitude);
            }

            transform.position = hit.point + hit.normal * (radius + 0.001f);

            Debug.Log($"Предотвращен пролет сквозь стену: {gameObject.name}");
        }
    }

    // Обработка касания стен (если скорость маленькая или уже внутри)
    private void ResolveWallCollisions()
    {
        Collider[] hitWalls = Physics.OverlapSphere(transform.position, radius, wallLayer);

        foreach (Collider wallCollider in hitWalls)
        {
            Vector3 closestPoint = wallCollider.ClosestPoint(transform.position);
            Vector3 diff = transform.position - closestPoint;
            diff.y = 0;

            Vector3 normal = diff.normalized;
            float distance = diff.magnitude;

            // Если внутри
            if (distance < radius)
            {
                // Выталкиваем: Pos += Normal * (R - dist)
                float penetration = radius - distance;
                transform.position += normal * penetration;

                // Если летит в стену - отражаем
                if (Vector3.Dot(rb.linearVelocity, normal) < 0)
                {
                    if (SoundManager.Instance != null)
                    {
                        SoundManager.Instance.PlayWallCollision(rb.linearVelocity.magnitude);
                    }
                    Vector3 incomingVelocity = rb.linearVelocity;

                    // R = V - 2*(V.N)*N
                    float dot = Vector3.Dot(incomingVelocity, normal);
                    Vector3 reflectedVelocity = incomingVelocity - 2 * dot * normal;

                    reflectedVelocity.y = 0;
                    rb.linearVelocity = reflectedVelocity * wallBounciness;
                }
            }
        }
    }

    private void ResolveBallCollisions()
    {
        foreach (BallPhysics otherBall in allBalls)
        {
            // Пропускаем себя и дубликаты 
            if (otherBall == this || otherBall == null || otherBall.GetInstanceID() <= this.GetInstanceID()) continue;

            Vector2 pos1 = new Vector2(transform.position.x, transform.position.z);
            Vector2 pos2 = new Vector2(otherBall.transform.position.x, otherBall.transform.position.z);
            float dist = Vector2.Distance(pos1, pos2);
            float combinedRadius = radius + otherBall.radius;

            // Есть пересечение
            if (dist < combinedRadius)
            {
                Vector3 collisionNormal = (transform.position - otherBall.transform.position);
                collisionNormal.y = 0;
                collisionNormal.Normalize();

                // Разделение позиций пропорционально массе чтобы шары не слипались
                float penetration = combinedRadius - dist;
                float totalMass = rb.mass + otherBall.rb.mass;
                Vector3 moveVec = collisionNormal * penetration;

                transform.position += new Vector3(moveVec.x * (otherBall.rb.mass / totalMass), 0, moveVec.z * (otherBall.rb.mass / totalMass));
                otherBall.transform.position -= new Vector3(moveVec.x * (rb.mass / totalMass), 0, moveVec.z * (rb.mass / totalMass));

                // Расчет импульса
                Vector3 relativeVelocity = rb.linearVelocity - otherBall.rb.linearVelocity;
                // Проекция Vrel на нормаль (линию удара)
                float velocityAlongNormal = Vector3.Dot(relativeVelocity, collisionNormal);

                if (velocityAlongNormal > 0) continue;

                // Расчет скалярного импульса J (J = -(1 + e) * Vrel_n / (1/m1 + 1/m2))
                float j = -(1 + ballBounciness) * velocityAlongNormal;
                j /= (1 / rb.mass + 1 / otherBall.rb.mass);
                // Получение вектора импульса (J * n)
                Vector3 impulse = j * collisionNormal;

                if (SoundManager.Instance != null)
                {
                    SoundManager.Instance.PlayBallCollision(impulse.magnitude);
                }

                // Применяем импульс: V' = V + j/m
                rb.linearVelocity += impulse / rb.mass;
                otherBall.rb.linearVelocity -= impulse / otherBall.rb.mass;

                rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0, rb.linearVelocity.z);
                otherBall.rb.linearVelocity = new Vector3(otherBall.rb.linearVelocity.x, 0, otherBall.rb.linearVelocity.z);
            }
        }
    }

    // Трение качения
    private void ApplyRollingFriction()
    {
        if (rb.linearVelocity.sqrMagnitude < 0.0001f)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        // N = m * g (Нормальная сила)
        float N = rb.mass * Physics.gravity.magnitude;

        // v_hat - Вектор направления скорости
        Vector3 velocityDirection = rb.linearVelocity.normalized;

        // Вектор силы трения F_fr = -mu * N * v_hat
        Vector3 frictionForce = -rollingFrictionCoefficient * N * velocityDirection;

        rb.AddForce(frictionForce, ForceMode.Force);
    }

    private static bool IsNetworkClientOnly()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening && networkManager.IsClient && !networkManager.IsServer;
    }

    
}
