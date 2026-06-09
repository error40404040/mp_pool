using UnityEngine;
using UnityEngine.Audio;

public class SoundManager : MonoBehaviour
{
    public static SoundManager Instance { get; private set; }

    [Header("Группы микшера")]
    public AudioMixerGroup musicGroup;
    public AudioMixerGroup sfxGroup;

    [Header("Звуки")]
    public AudioClip[] ballHitClips;
    public AudioClip wallHitClip;
    public AudioClip pocketClip;
    public AudioClip cueStrikeClip;

    [Header("Музыка")]
    public AudioClip backgroundMusic;

    private AudioSource musicSource;
    private AudioSource sfxSource;

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
            return;
        }

        musicSource = gameObject.AddComponent<AudioSource>();
        sfxSource = gameObject.AddComponent<AudioSource>();

        musicSource.outputAudioMixerGroup = musicGroup;
        sfxSource.outputAudioMixerGroup = sfxGroup;

        musicSource.clip = backgroundMusic;
        musicSource.loop = true;
        musicSource.playOnAwake = true;
    }

    private void Start()
    {
        if (musicSource.clip != null) musicSource.Play();
    }

    private void PlayRandomized(AudioClip clip, float volume)
    {
        if (clip == null) return;
        sfxSource.pitch = Random.Range(0.95f, 1.05f);
        sfxSource.PlayOneShot(clip, Mathf.Clamp01(volume));
    }

    public void PlayCueStrike(float power, float maxPower)
    {
        float volume = Mathf.Lerp(0.2f, 1.0f, power / maxPower);
        PlayRandomized(cueStrikeClip, volume);
    }

    public void PlayBallCollision(float impulseMagnitude)
    {
        if (ballHitClips.Length == 0) return;

        AudioClip clip = ballHitClips[Random.Range(0, ballHitClips.Length)];

        float volume = impulseMagnitude / 2f;
        PlayRandomized(clip, volume);
    }

    public void PlayWallCollision(float speed)
    {
        float volume = speed / 5f;
        PlayRandomized(wallHitClip, volume);
    }

    public void PlayPocketSound()
    {
        PlayRandomized(pocketClip, 1.0f);
    }
}
