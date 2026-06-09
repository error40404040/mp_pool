using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

public class NetworkDisconnectHandler : MonoBehaviour
{
    [SerializeField] private string menuSceneName = "menu";

    private bool isHandlingDisconnect;

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void Unsubscribe()
    {
        if (NetworkManager.Singleton == null)
        {
            return;
        }

        NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
    }

    private void OnClientDisconnected(ulong clientId)
    {
        NetworkManager networkManager = NetworkManager.Singleton;
        if (networkManager == null || isHandlingDisconnect)
        {
            return;
        }

        if (networkManager.IsServer)
        {
            HandleServerSideDisconnect(networkManager, clientId);
            return;
        }

        if (clientId == networkManager.LocalClientId)
        {
            ReturnLocalClientToMenu(networkManager);
        }
    }

    private void HandleServerSideDisconnect(NetworkManager networkManager, ulong clientId)
    {
        if (clientId == networkManager.LocalClientId)
        {
            ReturnLocalClientToMenu(networkManager);
            return;
        }

        ReturnLocalClientToMenu(networkManager);
    }

    private void ReturnLocalClientToMenu(NetworkManager networkManager)
    {
        isHandlingDisconnect = true;

        if (networkManager.IsListening)
        {
            networkManager.Shutdown();
        }

        if (!IsInMenuScene())
        {
            SceneManager.LoadScene(menuSceneName);
        }

        isHandlingDisconnect = false;
    }

    private bool IsInMenuScene()
    {
        return SceneManager.GetActiveScene().name == menuSceneName;
    }
}
