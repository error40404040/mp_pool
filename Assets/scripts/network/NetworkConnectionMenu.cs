using System;
using System.Text;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;
using UnityEngine.UI;

public class NetworkConnectionMenu : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private TMP_InputField codeInput;

    [Header("Buttons")]
    [SerializeField] private Button hostButton;
    [SerializeField] private Button clientButton;
    [SerializeField] private Button disconnectButton;
    [SerializeField] private Button backButton;

    [Header("Text")]
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TextMeshProUGUI localClientText;
    [SerializeField] private TextMeshProUGUI playersText;

    [Header("Navigation")]
    [SerializeField] private NetworkMenuNavigator menuNavigator;

    [Header("Settings")]
    [SerializeField] private string defaultNickname = "Player";
    [SerializeField] private int maxRelayConnections = 1;
    [SerializeField] private string relayConnectionType = "dtls";

    private readonly StringBuilder playersBuilder = new StringBuilder();
    private bool isStarting;
    private string currentJoinCode;

    private void Awake()
    {
        ResolveMenuNavigator();
        ApplyDefaultInputValues();
        RefreshView("Disconnected");
    }

    private void OnEnable()
    {
        if (hostButton != null) hostButton.onClick.AddListener(StartHost);
        if (clientButton != null) clientButton.onClick.AddListener(StartClient);
        if (disconnectButton != null) disconnectButton.onClick.AddListener(Disconnect);
        if (backButton != null) backButton.onClick.AddListener(BackToMultiplayerMenu);

        SubscribeNetworkCallbacks();
        RefreshView(GetCurrentStatus());
        ShowLobbyIfConnected();
    }

    private void OnDisable()
    {
        if (hostButton != null) hostButton.onClick.RemoveListener(StartHost);
        if (clientButton != null) clientButton.onClick.RemoveListener(StartClient);
        if (disconnectButton != null) disconnectButton.onClick.RemoveListener(Disconnect);
        if (backButton != null) backButton.onClick.RemoveListener(BackToMultiplayerMenu);

        UnsubscribeNetworkCallbacks();
    }

    public void StartHost()
    {
        _ = StartHostWithRelayAsync();
    }

    public void StartClient()
    {
        _ = StartClientWithRelayAsync();
    }

    public void Disconnect()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        currentJoinCode = null;
        NetworkClientData.RelayJoinCode = null;
        isStarting = false;
        RefreshView("Disconnected");
        if (menuNavigator != null)
        {
            menuNavigator.ShowMenu();
        }
    }

    public void BackToMultiplayerMenu()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

        currentJoinCode = null;
        NetworkClientData.RelayJoinCode = null;
        isStarting = false;
        RefreshView("Disconnected");
        ResolveMenuNavigator();
        if (menuNavigator != null)
        {
            menuNavigator.ShowMultiplayerMenu();
        }
    }

    private void ApplyDefaultInputValues()
    {
        if (nicknameInput != null && string.IsNullOrWhiteSpace(nicknameInput.text))
        {
            nicknameInput.text = defaultNickname;
        }

        if (codeInput != null)
        {
            codeInput.text = string.Empty;
            codeInput.characterLimit = 12;
            SetInputPlaceholder(codeInput, "Code");
        }
    }

    private void SetInputPlaceholder(TMP_InputField inputField, string text)
    {
        if (inputField.placeholder is TextMeshProUGUI placeholderText)
        {
            placeholderText.text = text;
        }
    }

    private async Task StartHostWithRelayAsync()
    {
        if (!TryBeginNetworkStart("Starting Relay host..."))
        {
            return;
        }

        bool started = false;
        try
        {
            UnityTransport transport = await PrepareRelayTransportAsync();
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxRelayConnections);
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, relayConnectionType));
            currentJoinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            NetworkClientData.RelayJoinCode = currentJoinCode;
            GUIUtility.systemCopyBuffer = currentJoinCode;

            started = NetworkManager.Singleton.StartHost();
            isStarting = false;
            RefreshView(started ? $"Relay Host code: {currentJoinCode}" : "Relay host start failed");

            if (started && menuNavigator != null)
            {
                menuNavigator.ShowLobby();
            }
        }
        catch (Exception ex)
        {
            currentJoinCode = null;
            isStarting = false;
            RefreshView($"Relay host failed: {ex.Message}");
        }

        if (started)
        {
            RefreshView(GetCurrentStatus());
        }
    }

    private async Task StartClientWithRelayAsync()
    {
        if (!TryBeginNetworkStart("Joining Relay..."))
        {
            return;
        }

        string joinCode = GetJoinCode();
        if (string.IsNullOrWhiteSpace(joinCode))
        {
            isStarting = false;
            RefreshView("Enter Relay code");
            return;
        }

        bool started = false;
        try
        {
            UnityTransport transport = await PrepareRelayTransportAsync();
            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);
            transport.SetRelayServerData(AllocationUtils.ToRelayServerData(allocation, relayConnectionType));
            currentJoinCode = joinCode;
            NetworkClientData.RelayJoinCode = joinCode;

            started = NetworkManager.Singleton.StartClient();
            isStarting = false;
            RefreshView(started ? $"Relay client joining: {joinCode}" : "Relay client start failed");

            if (started && menuNavigator != null)
            {
                menuNavigator.ShowLobby();
            }
        }
        catch (Exception ex)
        {
            currentJoinCode = null;
            isStarting = false;
            RefreshView($"Relay join failed: {ex.Message}");
        }

        if (started)
        {
            RefreshView(GetCurrentStatus());
        }
    }

    private bool TryBeginNetworkStart(string status)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null)
        {
            RefreshView("NetworkManager missing");
            return false;
        }

        if (networkManager.IsListening)
        {
            RefreshView("Already connected");
            return false;
        }

        if (isStarting)
        {
            RefreshView("Connection already starting");
            return false;
        }

        NetworkClientData.LocalNickname = GetNickname();
        NetworkClientData.RelayJoinCode = null;
        networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(NetworkClientData.LocalNickname);

        isStarting = true;
        RefreshView(status);
        return true;
    }

    private async Task<UnityTransport> PrepareRelayTransportAsync()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            throw new InvalidOperationException("UnityTransport missing");
        }

        if (UnityServices.State == ServicesInitializationState.Uninitialized)
        {
            await UnityServices.InitializeAsync();
        }

        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        return transport;
    }

    private string GetNickname()
    {
        if (nicknameInput == null || string.IsNullOrWhiteSpace(nicknameInput.text))
        {
            return defaultNickname;
        }

        return nicknameInput.text.Trim();
    }

    private string GetJoinCode()
    {
        if (codeInput == null || string.IsNullOrWhiteSpace(codeInput.text))
        {
            return string.Empty;
        }

        return codeInput.text.Trim().ToUpperInvariant();
    }

    private void SubscribeNetworkCallbacks()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientListChanged;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientListChanged;
        NetworkPlayer.PlayersChanged += OnPlayersChanged;
    }

    private void UnsubscribeNetworkCallbacks()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientListChanged;
        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientListChanged;
        NetworkPlayer.PlayersChanged -= OnPlayersChanged;
    }

    private void OnClientListChanged(ulong _)
    {
        RefreshView(GetCurrentStatus());
        ShowLobbyIfConnected();
    }

    private void OnPlayersChanged()
    {
        RefreshView(GetCurrentStatus());
        ShowLobbyIfConnected();
    }

    private void ShowLobbyIfConnected()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            return;
        }

        ResolveMenuNavigator();
        if (menuNavigator != null)
        {
            menuNavigator.ShowLobby();
        }
    }

    private void ResolveMenuNavigator()
    {
        if (menuNavigator == null)
        {
            menuNavigator = GetComponent<NetworkMenuNavigator>();
        }

        if (menuNavigator == null)
        {
            menuNavigator = FindFirstObjectByType<NetworkMenuNavigator>();
        }
    }

    private string GetCurrentStatus()
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || !networkManager.IsListening)
        {
            return "Disconnected";
        }

        if (networkManager.IsHost)
        {
            return string.IsNullOrEmpty(currentJoinCode)
                ? "Relay host running"
                : $"Relay Host code: {currentJoinCode}";
        }

        return networkManager.IsClient ? "Relay client connected" : "Connected";
    }

    private void RefreshView(string status)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        bool isConnected = networkManager != null && networkManager.IsListening;

        if (statusText != null)
        {
            statusText.text = $"Status: {status}";
        }

        if (localClientText != null)
        {
            localClientText.text = isConnected
                ? $"Local client: {networkManager.LocalClientId}"
                : "Local client: -";
        }

        if (playersText != null)
        {
            playersText.text = BuildPlayersText(networkManager, isConnected);
        }

        if (hostButton != null)
        {
            hostButton.interactable = !isConnected && !isStarting;
        }

        if (clientButton != null)
        {
            clientButton.interactable = !isConnected && !isStarting;
        }

        if (disconnectButton != null)
        {
            disconnectButton.interactable = isConnected;
        }
    }

    private string BuildPlayersText(NetworkManager networkManager, bool isConnected)
    {
        playersBuilder.Clear();
        playersBuilder.AppendLine("Connected players:");

        if (!isConnected || networkManager == null)
        {
            playersBuilder.AppendLine("-");
            return playersBuilder.ToString();
        }

        if (NetworkPlayer.All.Count > 0)
        {
            foreach (NetworkPlayer player in NetworkPlayer.All)
            {
                string localMarker = player.OwnerClientId == networkManager.LocalClientId ? " (local)" : string.Empty;
                string readyMarker = player.IsReady ? " ready" : " not ready";
                playersBuilder.AppendLine($"{player.Nickname} | Client {player.OwnerClientId}{localMarker} | Score {player.Score} | {readyMarker}");
            }

            return playersBuilder.ToString();
        }

        foreach (ulong clientId in networkManager.ConnectedClientsIds)
        {
            string localMarker = clientId == networkManager.LocalClientId ? " (local)" : string.Empty;
            playersBuilder.AppendLine($"Client {clientId}{localMarker}");
        }

        return playersBuilder.ToString();
    }
}
