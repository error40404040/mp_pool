using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;

public class NetworkLobbyManager : MonoBehaviour
{
    [Header("Buttons")]
    [SerializeField] private Button readyButton;
    [SerializeField] private Button startGameButton;
    [SerializeField] private Button backButton;

    [Header("Text")]
    [SerializeField] private TextMeshProUGUI lobbyStateText;

    [Header("Settings")]
    [SerializeField] private string gameSceneName = "SampleScene";
    [SerializeField] private int requiredPlayers = 2;

    private TextMeshProUGUI readyButtonText;
    private TextMeshProUGUI startGameButtonText;
    private NetworkMenuNavigator menuNavigator;

    private void Awake()
    {
        ResolveBackButton();
        ResolveMenuNavigator();

        if (readyButton != null)
        {
            readyButtonText = readyButton.GetComponentInChildren<TextMeshProUGUI>();
        }

        if (startGameButton != null)
        {
            startGameButtonText = startGameButton.GetComponentInChildren<TextMeshProUGUI>();
        }
    }

    private void OnEnable()
    {
        if (readyButton != null)
        {
            readyButton.onClick.AddListener(ToggleReady);
        }

        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(StartGame);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(BackToMultiplayerMenu);
        }

        Subscribe();
        RefreshView();
    }

    private void Start()
    {
        RefreshView();
    }

    private void OnDisable()
    {
        if (readyButton != null)
        {
            readyButton.onClick.RemoveListener(ToggleReady);
        }

        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveListener(StartGame);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(BackToMultiplayerMenu);
        }

        Unsubscribe();
    }

    private void Subscribe()
    {
        NetworkPlayer.PlayersChanged += RefreshView;

        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientChanged;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientChanged;
    }

    private void Unsubscribe()
    {
        NetworkPlayer.PlayersChanged -= RefreshView;

        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientChanged;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientChanged;
    }

    private void OnClientChanged(ulong _)
    {
        RefreshView();
    }

    private void ToggleReady()
    {
        NetworkPlayer localPlayer = GetLocalPlayer();
        if (localPlayer == null)
        {
            RefreshView();
            return;
        }

        localPlayer.SetReadyServerRpc(!localPlayer.IsReady);
    }

    private void StartGame()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsServer || !CanStartGame())
        {
            RefreshView();
            return;
        }

        SetLobbyText("Starting game...");
        NetworkPlayer.LoadSceneForAll(gameSceneName);
    }

    private void BackToMultiplayerMenu()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager != null && networkManager.IsListening)
        {
            networkManager.Shutdown();
        }

        NetworkClientData.RelayJoinCode = null;
        ResolveMenuNavigator();

        if (menuNavigator != null)
        {
            menuNavigator.ShowMultiplayerMenu();
        }

        RefreshView();
    }

    private void RefreshView()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        bool isConnected = networkManager != null && networkManager.IsListening;
        NetworkPlayer localPlayer = GetLocalPlayer();
        bool canStart = isConnected && networkManager.IsServer && CanStartGame();

        if (readyButton != null)
        {
            readyButton.gameObject.SetActive(isConnected);
            readyButton.interactable = isConnected && localPlayer != null;
        }

        if (startGameButton != null)
        {
            startGameButton.interactable = canStart;
            startGameButton.gameObject.SetActive(isConnected && networkManager.IsServer);
        }

        if (readyButtonText != null)
        {
            readyButtonText.text = localPlayer != null && localPlayer.IsReady ? "Cancel Ready" : "Ready";
        }

        if (startGameButtonText != null)
        {
            startGameButtonText.text = "Start Game";
        }

        SetLobbyText(BuildLobbyStateText(networkManager, isConnected, canStart));
    }

    private string BuildLobbyStateText(NetworkManager networkManager, bool isConnected, bool canStart)
    {
        if (!isConnected || networkManager == null)
        {
            return "Lobby: disconnected";
        }

        int playerCount = NetworkPlayer.All.Count;
        int readyCount = CountReadyPlayers();

        if (playerCount < requiredPlayers)
        {
            return $"Lobby: waiting for players ({playerCount}/{requiredPlayers})";
        }

        if (!AllPlayersReady())
        {
            return $"Lobby: waiting for ready ({readyCount}/{playerCount})";
        }

        return canStart ? "Lobby: ready to start" : "Lobby: waiting for host";
    }

    private bool CanStartGame()
    {
        return NetworkPlayer.All.Count >= requiredPlayers && AllPlayersReady();
    }

    private bool AllPlayersReady()
    {
        if (NetworkPlayer.All.Count == 0)
        {
            return false;
        }

        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (!player.IsReady)
            {
                return false;
            }
        }

        return true;
    }

    private int CountReadyPlayers()
    {
        int count = 0;

        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player.IsReady)
            {
                count++;
            }
        }

        return count;
    }

    private NetworkPlayer GetLocalPlayer()
    {
        foreach (NetworkPlayer player in NetworkPlayer.All)
        {
            if (player.IsOwner)
            {
                return player;
            }
        }

        return null;
    }

    private void ResolveBackButton()
    {
        if (backButton != null)
        {
            return;
        }

        foreach (Button button in GetComponentsInChildren<Button>(true))
        {
            if (button.name == "BackButton")
            {
                backButton = button;
                return;
            }
        }
    }

    private void ResolveMenuNavigator()
    {
        if (menuNavigator == null)
        {
            menuNavigator = GetComponentInParent<NetworkMenuNavigator>(true);
        }

        if (menuNavigator == null)
        {
            menuNavigator = FindFirstObjectByType<NetworkMenuNavigator>();
        }
    }

    private void SetLobbyText(string text)
    {
        if (lobbyStateText != null)
        {
            lobbyStateText.text = text;
        }
    }
}
