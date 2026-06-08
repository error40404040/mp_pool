using UnityEngine;
using TMPro;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.Audio;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Header("Панели игроков")]
    public CanvasGroup p1CanvasGroup;
    public CanvasGroup p2CanvasGroup;

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


    public GameObject pauseMenuPanel;
    public AudioMixer masterMixer;
    public Slider musicSlider; 
    public Slider sfxSlider;
    private bool isPaused = false;



    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);

        if (gameOverPanel != null) gameOverPanel.SetActive(false);
        if (pauseMenuPanel != null) pauseMenuPanel.SetActive(false);
    }

    private void Start()
    {
        if (musicSlider != null) SetMusicVolume(musicSlider.value);
        if (sfxSlider != null) SetSFXVolume(sfxSlider.value);
    }

    public void SetPocketPromptVisible(bool visible)
    {
        if (pocketPrompt != null) pocketPrompt.SetActive(visible);
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
        SceneManager.LoadScene("menu");
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
                winMessageText.text = (winner == Player.Player1) ? "ПОБЕДИЛ ИГРОК 1!" : "ПОБЕДИЛ ИГРОК 2!";
                winMessageText.color = (winner == Player.Player1) ? Color.white : Color.yellow;
            }
        }
    }

    public void OnRestartBtnClick()
    {
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    public void OnMainMenuBtnClick()
    {
        SceneManager.LoadScene("menu");
    }

}