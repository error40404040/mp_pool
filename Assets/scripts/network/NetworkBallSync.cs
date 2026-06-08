using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkBallSync : MonoBehaviour
{
    public static NetworkBallSync Instance { get; private set; }

    [SerializeField] private float syncInterval = 0.05f;

    private readonly Dictionary<int, BallInfo> ballsByKey = new Dictionary<int, BallInfo>();
    private float nextSyncTime;

    private void Awake()
    {
        Instance = this;
        CacheBalls();
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void FixedUpdate()
    {
        if (!IsNetworkServer())
        {
            return;
        }

        if (GameManager.Instance == null || GameManager.Instance.CurrentState != GameState.BallsMoving)
        {
            return;
        }

        if (Time.time < nextSyncTime)
        {
            return;
        }

        nextSyncTime = Time.time + syncInterval;
        BroadcastNow();
    }

    public void BroadcastNow()
    {
        if (!IsNetworkServer())
        {
            return;
        }

        NetworkPlayer.BroadcastBallStates(BuildSnapshot());
    }

    public void ApplySnapshot(NetworkBallState[] states)
    {
        if (IsNetworkServer())
        {
            return;
        }

        CacheBalls();

        foreach (NetworkBallState state in states)
        {
            if (!ballsByKey.TryGetValue(GetKey(state.Type, state.Number), out BallInfo ball) || ball == null)
            {
                continue;
            }

            ApplyState(ball, state);
        }
    }

    private NetworkBallState[] BuildSnapshot()
    {
        CacheBalls();

        List<NetworkBallState> states = new List<NetworkBallState>(ballsByKey.Count);
        foreach (BallInfo ball in ballsByKey.Values)
        {
            if (ball == null)
            {
                continue;
            }

            Rigidbody rb = ball.GetComponent<Rigidbody>();
            states.Add(new NetworkBallState
            {
                Type = (int)ball.type,
                Number = ball.number,
                IsActive = IsBallActive(ball),
                Position = ball.transform.position,
                Rotation = ball.transform.rotation,
                LinearVelocity = rb != null ? rb.linearVelocity : Vector3.zero,
                AngularVelocity = rb != null ? rb.angularVelocity : Vector3.zero
            });
        }

        return states.ToArray();
    }

    private void ApplyState(BallInfo ball, NetworkBallState state)
    {
        if (!state.IsActive && ShouldIgnoreInactiveCueBallSnapshot(ball))
        {
            return;
        }

        if (ball.gameObject.activeSelf != state.IsActive)
        {
            ball.gameObject.SetActive(state.IsActive);
        }

        ball.transform.SetPositionAndRotation(state.Position, state.Rotation);

        Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = state.LinearVelocity;
            rb.angularVelocity = state.AngularVelocity;
        }

        Collider collider = ball.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = state.IsActive;
        }

        MeshRenderer[] renderers = ball.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer renderer in renderers)
        {
            renderer.enabled = state.IsActive;
        }
    }

    private void CacheBalls()
    {
        ballsByKey.Clear();

        BallInfo[] balls = Object.FindObjectsByType<BallInfo>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (BallInfo ball in balls)
        {
            int key = GetKey((int)ball.type, ball.number);
            if (!ballsByKey.ContainsKey(key))
            {
                ballsByKey.Add(key, ball);
            }
        }
    }

    private static bool IsBallActive(BallInfo ball)
    {
        if (ball.type == BallInfo.BallType.CueBall && GameManager.Instance != null && GameManager.Instance.CurrentState == GameState.PlacingCueBall)
        {
            return ball.gameObject.activeSelf;
        }

        MeshRenderer renderer = ball.GetComponentInChildren<MeshRenderer>(true);
        Collider collider = ball.GetComponent<Collider>();
        return ball.gameObject.activeSelf
            && (renderer == null || renderer.enabled)
            && (collider == null || collider.enabled);
    }

    private static int GetKey(int type, int number)
    {
        return type * 100 + number;
    }

    private static bool ShouldIgnoreInactiveCueBallSnapshot(BallInfo ball)
    {
        if (ball == null || ball.type != BallInfo.BallType.CueBall)
        {
            return false;
        }

        if (GameManager.Instance == null)
        {
            return false;
        }

        return GameManager.Instance.CurrentState != GameState.BallsMoving;
    }

    private static bool IsNetworkServer()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        return networkManager != null && networkManager.IsListening && networkManager.IsServer;
    }
}
