using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;

public class NetworkConnectionMenu : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private TMP_InputField nicknameInput;
    [SerializeField] private TMP_InputField addressInput;
    [SerializeField] private TMP_InputField portInput;

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
    [SerializeField] private string defaultAddress = "127.0.0.1";
    [SerializeField] private ushort defaultPort = 7777;

    private readonly StringBuilder playersBuilder = new StringBuilder();

    private void Awake()
    {
        ApplyDefaultInputValues();
        RefreshView("Disconnected");
    }

    private void OnEnable()
    {
        if (hostButton != null)
        {
            hostButton.onClick.AddListener(StartHost);
        }

        if (clientButton != null)
        {
            clientButton.onClick.AddListener(StartClient);
        }

        if (disconnectButton != null)
        {
            disconnectButton.onClick.AddListener(Disconnect);
        }

        if (backButton != null)
        {
            backButton.onClick.AddListener(BackToMultiplayerMenu);
        }

        SubscribeNetworkCallbacks();
        RefreshView(GetCurrentStatus());
    }

    private void OnDisable()
    {
        if (hostButton != null)
        {
            hostButton.onClick.RemoveListener(StartHost);
        }

        if (clientButton != null)
        {
            clientButton.onClick.RemoveListener(StartClient);
        }

        if (disconnectButton != null)
        {
            disconnectButton.onClick.RemoveListener(Disconnect);
        }

        if (backButton != null)
        {
            backButton.onClick.RemoveListener(BackToMultiplayerMenu);
        }

        UnsubscribeNetworkCallbacks();
    }

    public void StartHost()
    {
        EnsureHostPortAvailable();

        if (!TryPrepareNetwork("Starting host..."))
        {
            return;
        }

        bool started = NetworkManager.Singleton.StartHost();
        RefreshView(started ? $"Host running on port {GetPort()}" : "Host start failed");
        if (started && menuNavigator != null)
        {
            menuNavigator.ShowLobby();
        }
    }

    public void StartClient()
    {
        if (!TryPrepareNetwork("Connecting as client..."))
        {
            return;
        }

        bool started = NetworkManager.Singleton.StartClient();
        RefreshView(started ? "Client connecting" : "Client start failed");
        if (started && menuNavigator != null)
        {
            menuNavigator.ShowLobby();
        }
    }

    public void Disconnect()
    {
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }

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

        RefreshView("Disconnected");
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

        if (addressInput != null && string.IsNullOrWhiteSpace(addressInput.text))
        {
            addressInput.text = defaultAddress;
        }

        if (portInput != null && string.IsNullOrWhiteSpace(portInput.text))
        {
            portInput.text = defaultPort.ToString();
        }
    }

    private bool TryPrepareNetwork(string status)
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

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            RefreshView("UnityTransport missing");
            return false;
        }

        string nickname = GetNickname();
        string address = GetAddress();
        ushort port = GetPort();

        NetworkClientData.LocalNickname = nickname;
        transport.ConnectionData.Address = address;
        transport.ConnectionData.Port = port;
        networkManager.NetworkConfig.ConnectionData = Encoding.UTF8.GetBytes(nickname);

        RefreshView(status);
        return true;
    }

    private string GetNickname()
    {
        if (nicknameInput == null || string.IsNullOrWhiteSpace(nicknameInput.text))
        {
            return defaultNickname;
        }

        return nicknameInput.text.Trim();
    }

    private string GetAddress()
    {
        if (addressInput == null || string.IsNullOrWhiteSpace(addressInput.text))
        {
            return defaultAddress;
        }

        return addressInput.text.Trim();
    }

    private ushort GetPort()
    {
        if (portInput == null || !ushort.TryParse(portInput.text, out ushort port))
        {
            return defaultPort;
        }

        return port;
    }

    private void EnsureHostPortAvailable()
    {
        ushort requestedPort = GetPort();
        if (IsUdpPortAvailable(requestedPort))
        {
            return;
        }

        ushort availablePort = FindAvailableUdpPort(requestedPort);
        if (availablePort == requestedPort)
        {
            RefreshView($"Port {requestedPort} is busy");
            return;
        }

        if (portInput != null)
        {
            portInput.text = availablePort.ToString();
        }

        RefreshView($"Port {requestedPort} busy, using {availablePort}");
    }

    private ushort FindAvailableUdpPort(ushort startPort)
    {
        for (int offset = 1; offset <= 100; offset++)
        {
            int candidate = startPort + offset;
            if (candidate > ushort.MaxValue)
            {
                break;
            }

            ushort port = (ushort)candidate;
            if (IsUdpPortAvailable(port))
            {
                return port;
            }
        }

        return startPort;
    }

    private bool IsUdpPortAvailable(ushort port)
    {
        try
        {
            using UdpClient client = new UdpClient(AddressFamily.InterNetwork);
            client.Client.ExclusiveAddressUse = true;
            client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
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
    }

    private void OnPlayersChanged()
    {
        RefreshView(GetCurrentStatus());
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
            return "Host running";
        }

        return networkManager.IsClient ? "Client connected" : "Connected";
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
            hostButton.interactable = !isConnected;
        }

        if (clientButton != null)
        {
            clientButton.interactable = !isConnected;
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
