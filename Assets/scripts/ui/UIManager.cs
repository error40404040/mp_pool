using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Audio;
using System.Collections.Generic;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Панели игроков")]
    public CanvasGroup p1CanvasGroup;
    public CanvasGroup p2CanvasGroup;
    [SerializeField] private TextMeshProUGUI p1NameText;
    [SerializeField] private TextMeshProUGUI p2NameText;

    [Header("Инвентарь шаров")]
    public RectTransform p1BallContainer;
    public RectTransform p2BallContainer;
    public GameObject ballIconPrefab;
    public Sprite[] ballSprites;

    [Header("HUD Прицеливания")]
    public GameObject aimingHUD;
    public Slider powerSlider;

    [Header("Экран завершения")]
    public GameObject gameOverPanel;
    public TextMeshProUGUI winMessageText;

    public GameObject pocketPrompt;
    private TextMeshProUGUI pocketPromptText;


    public GameObject pauseMenuPanel;
    public AudioMixer masterMixer;
    public Slider musicSlider; 
    public Slider sfxSlider;
    private bool isPaused = false;



    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (aimingHUD != null) aimingHUD.SetActive(false);
        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        if (pocketPrompt != null) pocketPromptText = pocketPrompt.GetComponentInChildren<TextMeshProUGUI>(true);
        ResolvePlayerNameTexts();
    }

    private void OnEnable()
    {
        NetworkPlayer.PlayersChanged += RefreshPlayerNames;
    }

    private void OnDisable()
    {
        NetworkPlayer.PlayersChanged -= RefreshPlayerNames;
    }

    private void Start()
    {
        if (musicSlider != null) SetMusicVolume(musicSlider.value);
        if (sfxSlider != null) SetSFXVolume(sfxSlider.value);
        RefreshPlayerNames();
    }

    public void SetPocketPromptVisible(bool visible)
    {
        if (pocketPrompt != null) pocketPrompt.SetActive(visible);
    }

    public void SetPocketPromptVisible(bool visible, bool localPlayerChoosing)
    {
        if (pocketPromptText == null && pocketPrompt != null)
        {
            pocketPromptText = pocketPrompt.GetComponentInChildren<TextMeshProUGUI>(true);
        }

        if (pocketPromptText != null)
        {
            pocketPromptText.text = localPlayerChoosing ? "Выберите лузу" : "Противник выбирает лузу...";
        }

        SetPocketPromptVisible(visible);
    }
    public void TogglePause()
    {
        if (GameManager.Instance.CurrentState == GameState.GameOver) return;

        isPaused = !isPaused;
        pauseMenuPanel.SetActive(isPaused);

        Time.timeScale = isPaused ? 0f : 1f;

    }
    public void OnMainMenuClick()
    {
        Time.timeScale = 1f;
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
        NetworkPlayer.RequestReturnToMenu();
    }

    public void OnResumeClick()
    {
        TogglePause();
    }

    public void SetMusicVolume(float sliderValue)
    {
        if (masterMixer != null)
        {
            float dB = Mathf.Log10(Mathf.Clamp(sliderValue, 0.0001f, 1f)) * 20;
            masterMixer.SetFloat("MusicVol", dB);
        }
    }

    public void SetSFXVolume(float sliderValue)
    {
        if (masterMixer != null)
        {
            float dB = Mathf.Log10(Mathf.Clamp(sliderValue, 0.0001f, 1f)) * 20;
            masterMixer.SetFloat("SFXVol", dB);
        }
    }


    public void UpdateActivePlayerUI(Player currentPlayer)
    {
        p1CanvasGroup.alpha = (currentPlayer == Player.Player1) ? 1f : 0.4f;
        p2CanvasGroup.alpha = (currentPlayer == Player.Player2) ? 1f : 0.4f;
        RefreshPlayerNames();
    }

    public void AddBallToInventory(int ballNumber, Player pocketedBy)
    {
        RectTransform targetContainer = (pocketedBy == Player.Player1) ? p1BallContainer : p2BallContainer;

        if (ballIconPrefab != null && targetContainer != null)
        {
            GameObject newIcon = Instantiate(ballIconPrefab, targetContainer);
            Image img = newIcon.GetComponent<Image>();

            if (img != null && ballNumber > 0 && ballNumber <= ballSprites.Length)
            {
                img.sprite = ballSprites[ballNumber - 1];
            }
        }
    }

    public void SetBallInventory(IReadOnlyList<int> player1Balls, IReadOnlyList<int> player2Balls)
    {
        ClearBallInventory(p1BallContainer);
        ClearBallInventory(p2BallContainer);

        if (player1Balls != null)
        {
            foreach (int ballNumber in player1Balls)
            {
                AddBallToInventory(ballNumber, Player.Player1);
            }
        }

        if (player2Balls != null)
        {
            foreach (int ballNumber in player2Balls)
            {
                AddBallToInventory(ballNumber, Player.Player2);
            }
        }
    }

    private void ClearBallInventory(RectTransform container)
    {
        if (container == null)
        {
            return;
        }

        for (int i = container.childCount - 1; i >= 0; i--)
        {
            Destroy(container.GetChild(i).gameObject);
        }
    }


    public void SetAimingHUDVisible(bool visible)
    {
        if (aimingHUD != null) aimingHUD.SetActive(visible);
    }

    public void UpdatePower(float current, float max)
    {
        if (powerSlider != null)
        {
            powerSlider.maxValue = max;
            powerSlider.value = current;
        }
    }


    public void ShowGameOver(Player winner)
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);

            if (winMessageText != null)
            {
                winMessageText.text = $"ПОБЕДИЛ {GetPlayerDisplayName(winner).ToUpperInvariant()}!";
                winMessageText.color = (winner == Player.Player1) ? Color.white : Color.yellow;
            }
        }
    }

    public void ShowRestartVoteStatus(int readyCount, int requiredCount)
    {
        if (gameOverPanel != null)
        {
            gameOverPanel.SetActive(true);
        }

        if (winMessageText != null)
        {
            winMessageText.text = $"НОВАЯ ИГРА: {readyCount}/{requiredCount}";
            winMessageText.color = Color.white;
        }
    }

    public void OnRestartBtnClick()
    {
        Time.timeScale = 1f;
        NetworkPlayer.RequestRestartMatch();
    }

    public void OnMainMenuBtnClick()
    {
        Time.timeScale = 1f;
        NetworkPlayer.RequestReturnToMenu();
    }

    private void RefreshPlayerNames()
    {
        ResolvePlayerNameTexts();

        if (p1NameText != null)
        {
            p1NameText.text = GetPlayerDisplayName(Player.Player1);
        }

        if (p2NameText != null)
        {
            p2NameText.text = GetPlayerDisplayName(Player.Player2);
        }
    }

    private string GetPlayerDisplayName(Player player)
    {
        int slotIndex = player == Player.Player1 ? 0 : 1;
        if (NetworkPlayer.TryGetClientIdForPlayerSlot(slotIndex, out ulong clientId))
        {
            foreach (NetworkPlayer networkPlayer in NetworkPlayer.All)
            {
                if (networkPlayer != null && networkPlayer.OwnerClientId == clientId)
                {
                    return string.IsNullOrWhiteSpace(networkPlayer.Nickname)
                        ? $"Игрок {slotIndex + 1}"
                        : networkPlayer.Nickname;
                }
            }
        }

        return $"Игрок {slotIndex + 1}";
    }

    private void ResolvePlayerNameTexts()
    {
        if (p1NameText == null)
        {
            p1NameText = FindNameTextInPanel(p1CanvasGroup, "Player 1", "P1", "p1");
        }

        if (p2NameText == null)
        {
            p2NameText = FindNameTextInPanel(p2CanvasGroup, "Player 2", "P2", "p2");
        }
    }

    private TextMeshProUGUI FindNameTextInPanel(CanvasGroup panel, params string[] expectedTexts)
    {
        if (panel == null)
        {
            return null;
        }

        TextMeshProUGUI[] texts = panel.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI text in texts)
        {
            foreach (string expectedText in expectedTexts)
            {
                if (string.Equals(text.text.Trim(), expectedText, System.StringComparison.OrdinalIgnoreCase))
                {
                    return text;
                }
            }
        }

        return texts.Length > 0 ? texts[0] : null;
    }

}
