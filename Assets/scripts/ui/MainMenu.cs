using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Audio; 

public class MainMenuController : MonoBehaviour
{
    [Header("Настройки")]
    public string gameSceneName = "SampleScene";

    [Header("Звук")]
    public AudioMixer mainMixer; 
    [Range(0.0001f, 1f)]
    public float startVolume = 0.2f; 

    private void Start()
    {
        QualitySettings.vSyncCount = 0; 
        Application.targetFrameRate = 120; 

        if (mainMixer != null)
        {
            float dB = Mathf.Log10(startVolume) * 20;

            mainMixer.SetFloat("MusicVol", dB);
            mainMixer.SetFloat("SFXVol", dB);
        }
    }

    public void StartGame()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    public void QuitGame()
    {
        Application.Quit();
    }
}
