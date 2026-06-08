using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class NetworkBootstrap
{
    private const string NetworkManagerName = "_NetworkManager";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureNetworkManager()
    {
        ConfigureOrCreateNetworkManager();
        SceneManager.sceneLoaded += (_, _) => ConfigureOrCreateNetworkManager();
    }

    private static void ConfigureOrCreateNetworkManager()
    {
        if (NetworkManager.Singleton != null)
        {
            ConfigureNetworkManager(NetworkManager.Singleton);
            EnsureSceneNetworkHelpers();
            return;
        }

        GameObject networkObject = new GameObject(NetworkManagerName);
        Object.DontDestroyOnLoad(networkObject);

        UnityTransport transport = networkObject.AddComponent<UnityTransport>();
        transport.ConnectionData.Address = "127.0.0.1";
        transport.ConnectionData.Port = 7777;

        NetworkManager networkManager = networkObject.AddComponent<NetworkManager>();
        ConfigureNetworkManager(networkManager);
        EnsureSceneNetworkHelpers();
    }

    private static void ConfigureNetworkManager(NetworkManager networkManager)
    {
        if (networkManager == null)
        {
            return;
        }

        UnityTransport transport = networkManager.GetComponent<UnityTransport>();
        if (transport == null)
        {
            transport = networkManager.gameObject.AddComponent<UnityTransport>();
        }

        transport.ConnectionData.Address = "127.0.0.1";
        transport.ConnectionData.Port = 7777;

        networkManager.NetworkConfig.NetworkTransport = transport;
        networkManager.NetworkConfig.EnableSceneManagement = true;
        networkManager.NetworkConfig.ConnectionApproval = false;
        if (networkManager.GetComponent<NetworkDisconnectHandler>() == null)
        {
            networkManager.gameObject.AddComponent<NetworkDisconnectHandler>();
        }

        Object.DontDestroyOnLoad(networkManager.gameObject);
    }

    private static void EnsureSceneNetworkHelpers()
    {
        if (NetworkBallSync.Instance != null)
        {
            return;
        }

        GameManager gameManager = Object.FindFirstObjectByType<GameManager>();
        if (gameManager != null)
        {
            gameManager.gameObject.AddComponent<NetworkBallSync>();
        }
    }
}
