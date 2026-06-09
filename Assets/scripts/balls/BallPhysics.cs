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
    private float initialFixedY;

    public static List<BallPhysics> allBalls = new List<BallPhysics>();

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        sphereCollider = GetComponent<SphereCollider>();
        radius = sphereCollider.radius * transform.localScale.x;
        initialFixedY = transform.position.y;

        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    private void OnEnable() { if (!allBalls.Contains(this)) allBalls.Add(this); }
    private void OnDisable() { if (allBalls.Contains(this)) allBalls.Remove(this); }

    private void FixedUpdate()
    {
        if (IsNetworkClientOnly()) return;
        if (rb.isKinematic) return;

        transform.position = new Vector3(transform.position.x, initialFixedY, transform.position.z);

        PredictiveWallCollision();
        PredictiveBallCollisions();

        ResolveBallCollisions();
        ResolveWallCollisions();

        ApplyRollingFriction();
    }

    private void PredictiveWallCollision()
    {
        Vector3 velocity = rb.linearVelocity;
        float speed = velocity.magnitude;

        if (speed < 0.1f) return;

        float frameDistance = speed * Time.fixedDeltaTime;
        Vector3 direction = velocity.normalized;

        if (Physics.SphereCast(transform.position, radius, direction, out RaycastHit hit, frameDistance, wallLayer))
        {
            float velocityDotNormal = Vector3.Dot(velocity, hit.normal);

            Vector3 reflectedVelocity = velocity - 2 * velocityDotNormal * hit.normal; reflectedVelocity.y = 0;
            rb.linearVelocity = reflectedVelocity * wallBounciness;

            if (SoundManager.Instance != null)
            {
                SoundManager.Instance.PlayWallCollision(rb.linearVelocity.magnitude);
            }

            transform.position = hit.point + hit.normal * (radius + 0.001f);

        }
    }

    private void PredictiveBallCollisions()
    {
        foreach (BallPhysics otherBall in allBalls)
        {
            if (otherBall == this || otherBall == null || otherBall.GetInstanceID() <= this.GetInstanceID()) continue;
            if (otherBall.rb == null || otherBall.rb.isKinematic) continue;

            Vector2 pos1 = new Vector2(transform.position.x, transform.position.z);
            Vector2 pos2 = new Vector2(otherBall.transform.position.x, otherBall.transform.position.z);
            Vector2 relativePosition = pos1 - pos2;
            Vector2 relativeVelocity = new Vector2(rb.linearVelocity.x - otherBall.rb.linearVelocity.x, rb.linearVelocity.z - otherBall.rb.linearVelocity.z);

            float combinedRadius = radius + otherBall.radius;
            float combinedRadiusSqr = combinedRadius * combinedRadius;
            if (relativePosition.sqrMagnitude <= combinedRadiusSqr)
            {
                continue;
            }

            float a = Vector2.Dot(relativeVelocity, relativeVelocity);
            if (a < 0.0001f)
            {
                continue;
            }

            float b = 2f * Vector2.Dot(relativePosition, relativeVelocity);
            if (b >= 0f)
            {
                continue;
            }

            float c = Vector2.Dot(relativePosition, relativePosition) - combinedRadiusSqr;
            float discriminant = b * b - 4f * a * c;
            if (discriminant < 0f)
            {
                continue;
            }

            float timeOfImpact = (-b - Mathf.Sqrt(discriminant)) / (2f * a);
            if (timeOfImpact < 0f || timeOfImpact > Time.fixedDeltaTime)
            {
                continue;
            }

            Vector2 impactOffset = relativePosition + relativeVelocity * timeOfImpact;
            if (impactOffset.sqrMagnitude < 0.0001f)
            {
                impactOffset = relativePosition.normalized * combinedRadius;
            }

            Vector3 collisionNormal = new Vector3(impactOffset.x, 0f, impactOffset.y).normalized;
            ApplyBallCollisionImpulse(otherBall, collisionNormal);
        }
    }

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

            if (distance < radius)
            {
                float penetration = radius - distance;
                transform.position += normal * penetration;

                if (Vector3.Dot(rb.linearVelocity, normal) < 0)
                {
                    if (SoundManager.Instance != null)
                    {
                        SoundManager.Instance.PlayWallCollision(rb.linearVelocity.magnitude);
                    }
                    Vector3 incomingVelocity = rb.linearVelocity;

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
            if (otherBall == this || otherBall == null || otherBall.GetInstanceID() <= this.GetInstanceID()) continue;

            Vector2 pos1 = new Vector2(transform.position.x, transform.position.z);
            Vector2 pos2 = new Vector2(otherBall.transform.position.x, otherBall.transform.position.z);
            float dist = Vector2.Distance(pos1, pos2);
            float combinedRadius = radius + otherBall.radius;

            if (dist < combinedRadius)
            {
                Vector3 collisionNormal = (transform.position - otherBall.transform.position);
                collisionNormal.y = 0;
                collisionNormal.Normalize();

                float penetration = combinedRadius - dist;
                float totalMass = rb.mass + otherBall.rb.mass;
                Vector3 moveVec = collisionNormal * penetration;

                transform.position += new Vector3(moveVec.x * (otherBall.rb.mass / totalMass), 0, moveVec.z * (otherBall.rb.mass / totalMass));
                otherBall.transform.position -= new Vector3(moveVec.x * (rb.mass / totalMass), 0, moveVec.z * (rb.mass / totalMass));

                Vector3 relativeVelocity = rb.linearVelocity - otherBall.rb.linearVelocity;
                float velocityAlongNormal = Vector3.Dot(relativeVelocity, collisionNormal);

                if (velocityAlongNormal > 0) continue;

                ApplyBallCollisionImpulse(otherBall, collisionNormal);
            }
        }
    }

    private void ApplyBallCollisionImpulse(BallPhysics otherBall, Vector3 collisionNormal)
    {
        Vector3 relativeVelocity = rb.linearVelocity - otherBall.rb.linearVelocity;
        float velocityAlongNormal = Vector3.Dot(relativeVelocity, collisionNormal);

        if (velocityAlongNormal > 0f)
        {
            return;
        }

        float impulseMagnitude = -(1f + ballBounciness) * velocityAlongNormal;
        impulseMagnitude /= (1f / rb.mass + 1f / otherBall.rb.mass);
        Vector3 impulse = impulseMagnitude * collisionNormal;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayBallCollision(impulse.magnitude);
        }

        rb.linearVelocity += impulse / rb.mass;
        otherBall.rb.linearVelocity -= impulse / otherBall.rb.mass;

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        otherBall.rb.linearVelocity = new Vector3(otherBall.rb.linearVelocity.x, 0f, otherBall.rb.linearVelocity.z);
    }

    private void ApplyRollingFriction()
    {
        if (rb.linearVelocity.sqrMagnitude < 0.0001f)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            return;
        }

        float N = rb.mass * Physics.gravity.magnitude;

        Vector3 velocityDirection = rb.linearVelocity.normalized;

        Vector3 frictionForce = -rollingFrictionCoefficient * N * velocityDirection;

        rb.AddForce(frictionForce, ForceMode.Force);
    }

    private static bool IsNetworkClientOnly()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening && networkManager.IsClient && !networkManager.IsServer;
    }
}
