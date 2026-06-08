using UnityEngine;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class BallRollingVisuals : MonoBehaviour
{
    public Transform visualModelTransform; 

    private Rigidbody _rb;
    private SphereCollider sphereCollider;
    private Vector3 lastPosition;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        sphereCollider = GetComponent<SphereCollider>();
        if (visualModelTransform == null)
        {
            visualModelTransform = transform;
        }

        lastPosition = transform.position;
    }

    private void FixedUpdate()
    {
        Vector3 velocity = GetVisualVelocity();
        if (velocity.magnitude < 0.01f || sphereCollider == null)
        {
            lastPosition = transform.position;
            return;
        }

        Vector3 directionOfMovement = velocity.normalized;

        Vector3 axisOfRotation = Vector3.Cross(Vector3.up, directionOfMovement);

        // угловая скорость w = |V| / R
        float angularSpeed = velocity.magnitude / (sphereCollider.radius * transform.localScale.x);

        // расчет поворота за кадр w * dt
        Quaternion frameRotation = Quaternion.AngleAxis(angularSpeed * Mathf.Rad2Deg * Time.fixedDeltaTime, axisOfRotation);

        // поворот к существующему вращению rotation = R_frame * R_current
        visualModelTransform.rotation = frameRotation * visualModelTransform.rotation;
        lastPosition = transform.position;
    }

    private Vector3 GetVisualVelocity()
    {
        if (!IsNetworkClientOnly())
        {
            return _rb != null ? _rb.linearVelocity : Vector3.zero;
        }

        Vector3 delta = transform.position - lastPosition;
        return delta / Mathf.Max(Time.fixedDeltaTime, 0.0001f);
    }

    private static bool IsNetworkClientOnly()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening && networkManager.IsClient && !networkManager.IsServer;
    }
}
