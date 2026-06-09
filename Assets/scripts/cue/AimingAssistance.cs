using UnityEngine;

public class AimingAssistance : MonoBehaviour
{
    [Header("Ссылки")]
    public LineRenderer trajectoryLine; 
    public LineRenderer reflectionLine; 
    public Transform cueBall;

    [Header("Настройки")]
    public float maxPredictionLength = 10f;
    public float reflectionLength = 0.5f; 

    private CueController cueController;
    private BallPhysics cueBallPhysics;
    private float cueBallRadius;
    private bool linesAreActive = false;

    private void Awake()
    {
        cueController = GetComponent<CueController>();
        if (cueBall == null)
        {
            return;
        }

        cueBallPhysics = cueBall.GetComponent<BallPhysics>();
        SphereCollider cueBallCollider = cueBall.GetComponent<SphereCollider>();
        if (cueBallCollider != null)
        {
            cueBallRadius = cueBallCollider.radius * cueBall.transform.localScale.x;
        }
    }

    private void Update()
    {
        if (!CanShowAimingLines())
        {
            if (linesAreActive)
            {
                linesAreActive = false;
                HideLines();
            }

            return;
        }

        if (GameManager.Instance.CurrentState == GameState.PlayerAiming)
        {
            if (!linesAreActive) linesAreActive = true;
            UpdateLines();
        }
        else if (linesAreActive)
        {
            linesAreActive = false;
            HideLines();
        }
    }

    private void UpdateLines()
    {
        if (trajectoryLine == null || reflectionLine == null || cueController == null || cueBall == null || cueBallPhysics == null)
        {
            return;
        }

        trajectoryLine.enabled = true;
        reflectionLine.enabled = false; 

        Vector3 strikePosition = cueController.GetLastAimedPoint();
        Vector3 actualStrikeDir = (strikePosition - transform.position).normalized;
        Vector3 flatDir = new Vector3(actualStrikeDir.x, 0, actualStrikeDir.z).normalized;

        Transform closestBall = null;
        float minHitDist = maxPredictionLength;

        foreach (BallPhysics other in BallPhysics.allBalls)
        {
            if (other == cueBallPhysics || other == null) continue;

            Vector3 toOther = other.transform.position - cueBall.position;
            toOther.y = 0;

            float t = Vector3.Dot(toOther, flatDir);
            if (t < 0) continue; 

            float d2 = toOther.sqrMagnitude - t * t;
            SphereCollider otherCollider = other.GetComponent<SphereCollider>();
            if (otherCollider == null) continue;

            float otherR = otherCollider.radius * other.transform.localScale.x;
            float combinedR = cueBallRadius + otherR;

            if (d2 >= combinedR * combinedR) continue;

            float t_offset = Mathf.Sqrt(combinedR * combinedR - d2);
            float hitDist = t - t_offset;

            if (hitDist < minHitDist) { minHitDist = hitDist; closestBall = other.transform; }
        }

        RaycastHit wallHit;
        if (Physics.Raycast(cueBall.position, flatDir, out wallHit, minHitDist, cueBallPhysics.wallLayer))
        {
            trajectoryLine.SetPosition(0, cueBall.position);
            trajectoryLine.SetPosition(1, wallHit.point);
        }
        else if (closestBall != null)
        {
            Vector3 toTarget = closestBall.position - cueBall.position;
            Vector3 projectionPoint = cueBall.position + Vector3.Project(toTarget, flatDir);
            trajectoryLine.SetPosition(0, cueBall.position);
            trajectoryLine.SetPosition(1, projectionPoint);

            reflectionLine.enabled = true;

            Vector3 cueBallHitCenter = cueBall.position + flatDir * minHitDist;

            Vector3 bounceDir = (closestBall.position - cueBallHitCenter);
            bounceDir.y = 0;
            bounceDir.Normalize();

            reflectionLine.SetPosition(0, closestBall.position);
            reflectionLine.SetPosition(1, closestBall.position + bounceDir * reflectionLength);
        }
        else
        {
            trajectoryLine.SetPosition(0, cueBall.position);
            trajectoryLine.SetPosition(1, cueBall.position + flatDir * maxPredictionLength);
        }
    }

    private void HideLines()
    {
        if (trajectoryLine != null) trajectoryLine.enabled = false;
        if (reflectionLine != null) reflectionLine.enabled = false;
    }

    private bool CanShowAimingLines()
    {
        return GameManager.Instance != null
            && GameManager.Instance.CurrentState == GameState.PlayerAiming
            && cueController != null
            && cueController.CanLocalControlCue()
            && cueBall != null
            && cueBallPhysics != null
            && trajectoryLine != null
            && reflectionLine != null;
    }
}
