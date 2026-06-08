using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class BallRollingVisuals : MonoBehaviour
{
    public Transform visualModelTransform; 

    private Rigidbody _rb;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (visualModelTransform == null)
        {
            visualModelTransform = transform;
        }
    }

    private void FixedUpdate()
    {
        if (_rb.linearVelocity.magnitude < 0.01f)
        {
            return;
        }
        Vector3 directionOfMovement = _rb.linearVelocity.normalized;

        Vector3 axisOfRotation = Vector3.Cross(Vector3.up, directionOfMovement);

        // угловая скорость w = |V| / R
        float angularSpeed = _rb.linearVelocity.magnitude / (GetComponent<SphereCollider>().radius * transform.localScale.x);

        // расчет поворота за кадр w * dt
        Quaternion frameRotation = Quaternion.AngleAxis(angularSpeed * Mathf.Rad2Deg * Time.fixedDeltaTime, axisOfRotation);

        // поворот к существующему вращению rotation = R_frame * R_current
        visualModelTransform.rotation = frameRotation * visualModelTransform.rotation;
    }
}