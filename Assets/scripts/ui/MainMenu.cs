using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Audio; 

public class MainMenuController : MonoBehaviour
{
    private const string MusicVolumeKey = "MusicVolume";
    private const string SfxVolumeKey = "SFXVolume";

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
            ApplyMixerVolume("MusicVol", PlayerPrefs.GetFloat(MusicVolumeKey, startVolume));
            ApplyMixerVolume("SFXVol", PlayerPrefs.GetFloat(SfxVolumeKey, startVolume));
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

    private void ApplyMixerVolume(string exposedParameter, float sliderValue)
    {
        float dB = Mathf.Log10(Mathf.Clamp(sliderValue, 0.0001f, 1f)) * 20;
        mainMixer.SetFloat(exposedParameter, dB);
    }
}
