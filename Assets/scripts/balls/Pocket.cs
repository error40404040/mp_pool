using UnityEngine;
using System.Collections.Generic;

[RequireComponent(typeof(Collider))]
public class Pocket : MonoBehaviour
{
    private List<GameObject> scoredBallsThisTurn;

    private void Awake()
    {
        scoredBallsThisTurn = new List<GameObject>();
    }

    public void ClearScoredBalls()
    {
        scoredBallsThisTurn.Clear();
    }   

    private void OnTriggerEnter(Collider other)
    {
        if (other.CompareTag("Ball") && !scoredBallsThisTurn.Contains(other.gameObject))
        {
            ScoreBall(other.gameObject);
        }
    }

    private void ScoreBall(GameObject ball)
    {
        BallInfo ballInfo = ball.GetComponent<BallInfo>();
        if (ballInfo == null) return;

        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayPocketSound();
        }
        // передаем информацию о шаре и ссылку на лузу 
        GameManager.Instance.BallPocketed(ballInfo, this);

        if (ballInfo.type == BallInfo.BallType.CueBall) return;

        scoredBallsThisTurn.Add(ball);

        if (ball.GetComponent<Collider>() != null) ball.GetComponent<Collider>().enabled = false;
        if (ball.GetComponent<Rigidbody>() != null)
        {
            Rigidbody rb = ball.GetComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.linearVelocity = Vector3.zero;
        }

        MeshRenderer visualModel = ball.GetComponentInChildren<MeshRenderer>();
        if (visualModel != null) visualModel.enabled = false;

        Destroy(ball, 2f);
    }
}