using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkPlayer : NetworkBehaviour
{
    public static event Action PlayersChanged;
    public static IReadOnlyList<NetworkPlayer> All => players;
    public static NetworkPlayer Local { get; private set; }

    private static readonly List<NetworkPlayer> players = new List<NetworkPlayer>();
    private static readonly HashSet<ulong> restartVotes = new HashSet<ulong>();

    private readonly NetworkVariable<FixedString64Bytes> nickname = new NetworkVariable<FixedString64Bytes>(
        "Player",
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<int> score = new NetworkVariable<int>(
        0,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private readonly NetworkVariable<bool> isReady = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public string Nickname => nickname.Value.ToString();
    public int Score => score.Value;
    public bool IsReady => isReady.Value;

    public override void OnNetworkSpawn()
    {
        if (!players.Contains(this))
        {
            players.Add(this);
        }

        nickname.OnValueChanged += OnNetworkValueChanged;
        score.OnValueChanged += OnNetworkValueChanged;
        isReady.OnValueChanged += OnNetworkValueChanged;

        if (IsOwner)
        {
            Local = this;
            SubmitNicknameServerRpc(NetworkClientData.LocalNickname);
        }

        NotifyPlayersChanged();
    }

    public override void OnNetworkDespawn()
    {
        nickname.OnValueChanged -= OnNetworkValueChanged;
        score.OnValueChanged -= OnNetworkValueChanged;
        isReady.OnValueChanged -= OnNetworkValueChanged;

        players.Remove(this);
        restartVotes.Remove(OwnerClientId);
        if (Local == this)
        {
            Local = null;
        }

        NotifyPlayersChanged();
    }

    [ServerRpc]
    public void SubmitNicknameServerRpc(string requestedNickname)
    {
        nickname.Value = SanitizeNickname(requestedNickname, OwnerClientId);
    }

    [ServerRpc]
    public void SetReadyServerRpc(bool ready)
    {
        isReady.Value = ready;
    }

    [ServerRpc]
    public void RequestCueStrikeServerRpc(Vector3 strikeDirection, Vector3 strikePosition, float power)
    {
        if (CueController.Instance == null)
        {
            return;
        }

        CueController.Instance.TryApplyNetworkStrikeFromServer(OwnerClientId, strikeDirection, strikePosition, power);
    }

    [ServerRpc]
    public void RequestCueTransformServerRpc(Vector3 position, Quaternion rotation)
    {
        if (CueController.Instance == null)
        {
            return;
        }

        CueController.Instance.TryBroadcastNetworkCueTransformFromServer(OwnerClientId, position, rotation);
    }

    [ServerRpc]
    public void RequestCueBallPlacementServerRpc(Vector3 position)
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        GameManager.Instance.TryApplyNetworkCueBallPlacementFromServer(OwnerClientId, position);
    }

    [ServerRpc]
    public void RequestCueBallPreviewServerRpc(Vector3 position)
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        GameManager.Instance.TryBroadcastNetworkCueBallPreviewFromServer(OwnerClientId, position);
    }

    [ServerRpc]
    public void RequestPocketSelectionServerRpc(int pocketIndex)
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        GameManager.Instance.TryApplyNetworkPocketSelectionFromServer(OwnerClientId, pocketIndex);
    }

    [ServerRpc]
    public void RequestRestartMatchServerRpc()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
        {
            return;
        }

        restartVotes.Add(OwnerClientId);
        BroadcastRestartVoteStatus();

        int requiredVotes = Mathf.Max(1, networkManager.ConnectedClientsIds.Count);
        if (restartVotes.Count < requiredVotes)
        {
            return;
        }

        restartVotes.Clear();
        LoadNetworkScene(SceneManager.GetActiveScene().name);
    }

    [ServerRpc]
    public void RequestReturnToMenuServerRpc()
    {
        LoadNetworkScene("menu");
    }

    [ClientRpc]
    public void SyncGameStateClientRpc(int playerValue, int stateValue)
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        GameManager.Instance.ApplyNetworkState((Player)playerValue, (GameState)stateValue);
    }

    public static void BroadcastGameState(Player player, GameState state)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
        {
            return;
        }

        NetworkPlayer broadcaster = GetBroadcaster();
        if (broadcaster != null)
        {
            broadcaster.SyncGameStateClientRpc((int)player, (int)state);
        }
    }

    [ClientRpc]
    public void SyncBallStatesClientRpc(NetworkBallState[] states)
    {
        if (NetworkBallSync.Instance == null)
        {
            return;
        }

        NetworkBallSync.Instance.ApplySnapshot(states);
    }

    public static void BroadcastBallStates(NetworkBallState[] states)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
        {
            return;
        }

        NetworkPlayer broadcaster = GetBroadcaster();
        if (broadcaster != null)
        {
            broadcaster.SyncBallStatesClientRpc(states);
        }
    }

    [ClientRpc]
    public void SyncCueTransformClientRpc(Vector3 position, Quaternion rotation)
    {
        if (CueController.Instance == null)
        {
            return;
        }

        CueController.Instance.ApplyRemoteCueTransform(position, rotation);
    }

    public static void BroadcastCueTransform(Vector3 position, Quaternion rotation)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
        {
            return;
        }

        NetworkPlayer broadcaster = GetBroadcaster();
        if (broadcaster != null)
        {
            broadcaster.SyncCueTransformClientRpc(position, rotation);
        }
    }

    [ClientRpc]
    public void SyncCueBallPreviewClientRpc(Vector3 position)
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        GameManager.Instance.ApplyNetworkCueBallPreview(position);
    }

    public static void BroadcastCueBallPreview(Vector3 position)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
        {
            return;
        }

        NetworkPlayer broadcaster = GetBroadcaster();
        if (broadcaster != null)
        {
            broadcaster.SyncCueBallPreviewClientRpc(position);
        }
    }

    [ClientRpc]
    public void SyncRulesStateClientRpc(
        int player1Target,
        int player2Target,
        bool tableOpen,
        int orderedPocketIndex,
        bool gameOver,
        int winner,
        int[] player1Balls,
        int[] player2Balls)
    {
        if (GameManager.Instance == null)
        {
            return;
        }

        GameManager.Instance.ApplyNetworkRules(
            (BallGroup)player1Target,
            (BallGroup)player2Target,
            tableOpen,
            orderedPocketIndex,
            gameOver,
            (Player)winner,
            player1Balls,
            player2Balls);
    }

    public static void BroadcastRulesState(
        BallGroup player1Target,
        BallGroup player2Target,
        bool tableOpen,
        int orderedPocketIndex,
        bool gameOver,
        Player winner,
        int[] player1Balls,
        int[] player2Balls)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
        {
            return;
        }

        NetworkPlayer broadcaster = GetBroadcaster();
        if (broadcaster != null)
        {
            broadcaster.SyncRulesStateClientRpc(
                (int)player1Target,
                (int)player2Target,
                tableOpen,
                orderedPocketIndex,
                gameOver,
                (int)winner,
                player1Balls,
                player2Balls);
        }
    }

    public void SetScoreServer(int value)
    {
        if (!IsServer)
        {
            return;
        }

        score.Value = Mathf.Max(0, value);
    }

    public static void RequestRestartMatch()
    {
        RequestNetworkSceneChange(restart: true);
    }

    public static void RequestReturnToMenu()
    {
        RequestNetworkSceneChange(restart: false);
    }

    [ClientRpc]
    public void SyncRestartVoteStatusClientRpc(int readyCount, int requiredCount)
    {
        if (UIManager.Instance != null)
        {
            UIManager.Instance.ShowRestartVoteStatus(readyCount, requiredCount);
        }
    }

    private static FixedString64Bytes SanitizeNickname(string requestedNickname, ulong ownerClientId)
    {
        string sanitized = string.IsNullOrWhiteSpace(requestedNickname)
            ? $"Player {ownerClientId}"
            : requestedNickname.Trim();

        if (sanitized.Length > 32)
        {
            sanitized = sanitized.Substring(0, 32);
        }

        return sanitized;
    }

    private static void NotifyPlayersChanged()
    {
        PlayersChanged?.Invoke();
    }

    private static void RequestNetworkSceneChange(bool restart)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            SceneManager.LoadScene(restart ? SceneManager.GetActiveScene().name : "menu");
            return;
        }

        if (Local == null)
        {
            return;
        }

        if (restart)
        {
            Local.RequestRestartMatchServerRpc();
        }
        else
        {
            Local.RequestReturnToMenuServerRpc();
        }
    }

    private void LoadNetworkScene(string sceneName)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
        {
            return;
        }

        networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
    }

    private static void BroadcastRestartVoteStatus()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening || !networkManager.IsServer)
        {
            return;
        }

        NetworkPlayer broadcaster = GetBroadcaster();
        if (broadcaster != null)
        {
            broadcaster.SyncRestartVoteStatusClientRpc(
                restartVotes.Count,
                Mathf.Max(1, networkManager.ConnectedClientsIds.Count));
        }
    }

    private static NetworkPlayer GetBroadcaster()
    {
        foreach (NetworkPlayer player in players)
        {
            if (player != null && player.IsSpawned)
            {
                return player;
            }
        }

        return null;
    }

    public static bool TryGetClientIdForPlayerSlot(int slotIndex, out ulong clientId)
    {
        clientId = 0;

        if (slotIndex < 0 || slotIndex >= players.Count)
        {
            return false;
        }

        List<NetworkPlayer> sortedPlayers = new List<NetworkPlayer>(players);
        sortedPlayers.Sort((left, right) => left.OwnerClientId.CompareTo(right.OwnerClientId));

        clientId = sortedPlayers[slotIndex].OwnerClientId;
        return true;
    }

    private void OnNetworkValueChanged(FixedString64Bytes previousValue, FixedString64Bytes newValue)
    {
        NotifyPlayersChanged();
    }

    private void OnNetworkValueChanged(int previousValue, int newValue)
    {
        NotifyPlayersChanged();
    }

    private void OnNetworkValueChanged(bool previousValue, bool newValue)
    {
        NotifyPlayersChanged();
    }
}
