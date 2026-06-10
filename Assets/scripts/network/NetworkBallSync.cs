using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class NetworkBallSync : MonoBehaviour
{
    public static NetworkBallSync Instance { get; private set; }

    [SerializeField] private float syncInterval = 0.08f;
    [SerializeField] private float interpolationSpeed = 16f;
    [SerializeField] private float snapDistance = 1f;

    private readonly Dictionary<int, BallInfo> ballsByKey = new Dictionary<int, BallInfo>();
    private readonly Dictionary<int, InterpolatedBallState> targetStatesByKey = new Dictionary<int, InterpolatedBallState>();
    private float nextSyncTime;

    private struct InterpolatedBallState
    {
        public bool IsActive;
        public Vector3 Position;
        public Quaternion Rotation;
        public Vector3 LinearVelocity;
        public Vector3 AngularVelocity;
    }

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

    private void Update()
    {
        if (IsNetworkServer())
        {
            return;
        }

        InterpolateTargetStates();
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

        int key = GetKey(state.Type, state.Number);
        bool shouldInterpolate = state.IsActive
            && GameManager.Instance != null
            && GameManager.Instance.CurrentState == GameState.BallsMoving;

        if (!shouldInterpolate)
        {
            targetStatesByKey.Remove(key);
            ApplyStateImmediate(ball, state);
            return;
        }

        EnsureClientBallPresentation(ball, state.IsActive);

        Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = state.LinearVelocity;
            rb.angularVelocity = state.AngularVelocity;
        }

        targetStatesByKey[key] = new InterpolatedBallState
        {
            IsActive = state.IsActive,
            Position = state.Position,
            Rotation = state.Rotation,
            LinearVelocity = state.LinearVelocity,
            AngularVelocity = state.AngularVelocity
        };
    }

    private void ApplyStateImmediate(BallInfo ball, NetworkBallState state)
    {
        EnsureClientBallPresentation(ball, state.IsActive);
        ball.transform.SetPositionAndRotation(state.Position, state.Rotation);

        Rigidbody rb = ball.GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.isKinematic = true;
            rb.linearVelocity = state.LinearVelocity;
            rb.angularVelocity = state.AngularVelocity;
        }
    }

    private void InterpolateTargetStates()
    {
        if (targetStatesByKey.Count == 0)
        {
            return;
        }

        float t = 1f - Mathf.Exp(-interpolationSpeed * Time.deltaTime);

        foreach (KeyValuePair<int, InterpolatedBallState> pair in targetStatesByKey)
        {
            if (!ballsByKey.TryGetValue(pair.Key, out BallInfo ball) || ball == null)
            {
                continue;
            }

            InterpolatedBallState target = pair.Value;
            EnsureClientBallPresentation(ball, target.IsActive);

            float distance = Vector3.Distance(ball.transform.position, target.Position);
            if (distance > snapDistance)
            {
                ball.transform.SetPositionAndRotation(target.Position, target.Rotation);
            }
            else
            {
                ball.transform.position = Vector3.Lerp(ball.transform.position, target.Position, t);
                ball.transform.rotation = Quaternion.Slerp(ball.transform.rotation, target.Rotation, t);
            }

            Rigidbody rb = ball.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.isKinematic = true;
                rb.linearVelocity = target.LinearVelocity;
                rb.angularVelocity = target.AngularVelocity;
            }
        }
    }

    private static void EnsureClientBallPresentation(BallInfo ball, bool isActive)
    {
        if (ball.gameObject.activeSelf != isActive)
        {
            ball.gameObject.SetActive(isActive);
        }

        Collider collider = ball.GetComponent<Collider>();
        if (collider != null)
        {
            collider.enabled = isActive;
        }

        MeshRenderer[] renderers = ball.GetComponentsInChildren<MeshRenderer>(true);
        foreach (MeshRenderer renderer in renderers)
        {
            renderer.enabled = isActive;
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
