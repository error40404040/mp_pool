using UnityEngine;
using UnityEngine.UI;

public class NetworkMenuNavigator : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private GameObject menu;
    [SerializeField] private GameObject menuMP;
    [SerializeField] private GameObject lobby;
    [SerializeField] private GameObject mpInfo;

    [Header("Buttons")]
    [SerializeField] private Button playMPButton;

    private void Awake()
    {
        ShowMenu();
    }

    private void OnEnable()
    {
        if (playMPButton != null)
        {
            playMPButton.onClick.AddListener(ShowMultiplayerMenu);
        }

    }

    private void OnDisable()
    {
        if (playMPButton != null)
        {
            playMPButton.onClick.RemoveListener(ShowMultiplayerMenu);
        }

    }

    public void ShowMenu()
    {
        SetActivePanel(menu);
    }

    public void ShowMultiplayerMenu()
    {
        SetActivePanel(menuMP);
    }

    public void ShowLobby()
    {
        SetActivePanel(lobby);
    }

    private void SetActivePanel(GameObject activePanel)
    {
        if (menu != null) menu.SetActive(menu == activePanel);
        if (menuMP != null) menuMP.SetActive(menuMP == activePanel);
        if (lobby != null) lobby.SetActive(lobby == activePanel);
        if (mpInfo != null) mpInfo.SetActive(activePanel == menuMP || activePanel == lobby);
    }
}
