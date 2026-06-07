using System.Collections;
using UnityEngine;

/// <summary>
/// 실제 오디오 에셋 기반 BGM/SFX 레이어를 관리한다.
/// ProceduralHorrorAmbience는 바닥 드론을 만들고, 이 매니저는 음악과 이벤트 음향을 얹는다.
/// </summary>
[DisallowMultipleComponent]
public class AmbientAudioManager : MonoBehaviour
{
    [Header("BGM 클립")]
    [SerializeField] private AudioClip exploringBgm;
    [SerializeField] private AudioClip ghostHintBgm;
    [SerializeField] private AudioClip chaseBgm;
    [SerializeField] private AudioClip jumpScareBgm;

    [Header("이벤트 SFX")]
    [SerializeField] private AudioClip[] ambientSounds;
    [SerializeField] private AudioClip blackoutStinger;
    [SerializeField] private AudioClip hitStinger;
    [SerializeField] private AudioClip successStinger;
    [SerializeField] private AudioClip failedStinger;
    [SerializeField] private AudioClip heartbeatLoop;
    [SerializeField] private float ambientMinInterval = 7f;
    [SerializeField] private float ambientMaxInterval = 18f;

    [Header("오디오 소스")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;
    [SerializeField] private AudioSource heartbeatSource;

    [Header("믹스")]
    [SerializeField] private float bgmVolume = 0.38f;
    [SerializeField] private float sfxVolume = 0.75f;
    [SerializeField] private float heartbeatVolume = 0.34f;
    [SerializeField] private float crossfadeDuration = 1.2f;

    private Coroutine _crossfadeCoroutine;
    private Coroutine _ambientCoroutine;
    private GameManager.GameState _lastState = GameManager.GameState.Exploring;
    private int _activeTensionLevel;
    private float _heartbeatTarget;

    public bool HasUsableExternalBgm => bgmSource != null && exploringBgm != null && chaseBgm != null;
    public bool IsBgmPlaying => bgmSource != null && bgmSource.isPlaying && bgmSource.clip != null;
    public string ActiveBgmName => bgmSource != null && bgmSource.clip != null ? bgmSource.clip.name : string.Empty;

    private void Awake()
    {
        ConfigureForExperimentDefaults();
    }

    private void Start()
    {
        PlayBgm(exploringBgm, instant: true, tensionLevel: 0);
        StartAmbientLoopIfNeeded();
    }

    private void Update()
    {
        UpdateFromGameState();
        UpdateHeartbeat();

        if (HasUsableExternalBgm && !IsBgmPlaying)
            PlayBgm(exploringBgm, instant: true, tensionLevel: 0);
    }

    public void ConfigureForExperimentDefaults()
    {
        bgmSource = EnsureSource(bgmSource, "ExternalBgmSource", loop: true, bgmVolume, priority: 80);
        sfxSource = EnsureSource(sfxSource, "ExternalSfxSource", loop: false, sfxVolume, priority: 64);
        heartbeatSource = EnsureSource(heartbeatSource, "ExternalHeartbeatSource", loop: true, 0f, priority: 72);

        bgmSource.spatialBlend = 0f;
        sfxSource.spatialBlend = 0f;
        heartbeatSource.spatialBlend = 0f;
        bgmSource.dopplerLevel = 0f;
        sfxSource.dopplerLevel = 0f;
        heartbeatSource.dopplerLevel = 0f;

        bgmVolume = Mathf.Clamp(bgmVolume, 0.05f, 0.75f);
        sfxVolume = Mathf.Clamp(sfxVolume, 0.1f, 1f);
        heartbeatVolume = Mathf.Clamp(heartbeatVolume, 0.05f, 0.7f);
        crossfadeDuration = Mathf.Max(0.15f, crossfadeDuration);
        ambientMinInterval = Mathf.Max(2f, ambientMinInterval);
        ambientMaxInterval = Mathf.Max(ambientMinInterval + 1f, ambientMaxInterval);

        if (heartbeatLoop != null && heartbeatSource.clip != heartbeatLoop)
        {
            heartbeatSource.clip = heartbeatLoop;
            heartbeatSource.loop = true;
        }
    }

    public void SetExperimentClips(
        AudioClip exploring,
        AudioClip ghostHint,
        AudioClip chase,
        AudioClip jumpScare,
        AudioClip[] ambient,
        AudioClip blackout,
        AudioClip hit,
        AudioClip success,
        AudioClip failed,
        AudioClip heartbeat)
    {
        exploringBgm = exploring;
        ghostHintBgm = ghostHint != null ? ghostHint : exploring;
        chaseBgm = chase;
        jumpScareBgm = jumpScare != null ? jumpScare : chase;
        ambientSounds = ambient ?? new AudioClip[0];
        blackoutStinger = blackout;
        hitStinger = hit;
        successStinger = success;
        failedStinger = failed;
        heartbeatLoop = heartbeat;
        ConfigureForExperimentDefaults();
    }

    public void ReactToExperimentEvent(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
            return;

        switch (eventId)
        {
            case "game_start":
                _activeTensionLevel = 0;
                _heartbeatTarget = 0f;
                PlayBgm(exploringBgm, instant: false, tensionLevel: 0);
                break;
            case "ghost_hint":
                PlayBgm(ghostHintBgm != null ? ghostHintBgm : exploringBgm, instant: false, tensionLevel: 1);
                PlayRandomAmbient();
                _heartbeatTarget = 0.12f;
                break;
            case "killer_near":
                PlayBgm(ghostHintBgm != null ? ghostHintBgm : chaseBgm, instant: false, tensionLevel: 2);
                PlayRandomAmbient();
                _heartbeatTarget = 0.45f;
                break;
            case "blackout":
                PlayOneShot(blackoutStinger);
                _heartbeatTarget = Mathf.Max(_heartbeatTarget, 0.42f);
                break;
            case "chase":
                PlayBgm(chaseBgm, instant: true, tensionLevel: 3);
                _heartbeatTarget = 1f;
                break;
            case "player_hit":
                PlayOneShot(hitStinger);
                _heartbeatTarget = 1f;
                break;
            case "mission_success":
                PlayOneShot(successStinger);
                _heartbeatTarget = 0f;
                _activeTensionLevel = 0;
                PlayBgm(exploringBgm, instant: false, tensionLevel: 0, force: true);
                break;
            case "mission_failed":
                PlayOneShot(failedStinger);
                _heartbeatTarget = 0.15f;
                _activeTensionLevel = 0;
                PlayBgm(exploringBgm, instant: false, tensionLevel: 0, force: true);
                break;
        }
    }

    private AudioSource EnsureSource(AudioSource source, string objectName, bool loop, float volume, int priority)
    {
        Transform child = transform.Find(objectName);
        GameObject go = child != null ? child.gameObject : null;

        if (go != null && !TryResolveHealthyAudioSource(go, out source))
        {
            DestroyAudioSourceObject(go);
            go = null;
            source = null;
        }

        if (go == null)
        {
            go = new GameObject(objectName);
            go.transform.SetParent(transform, false);
            source = go.AddComponent<AudioSource>();
        }

        go.name = objectName;

        source.playOnAwake = false;
        source.loop = loop;
        source.volume = volume;
        source.priority = priority;
        return source;
    }

    private static bool TryResolveHealthyAudioSource(GameObject sourceObject, out AudioSource source)
    {
        source = null;
        if (sourceObject == null)
            return false;

        try
        {
            source = sourceObject.GetComponent<AudioSource>();
            if (source == null)
                return false;

            _ = source.playOnAwake;
            return true;
        }
        catch (MissingComponentException)
        {
            source = null;
            return false;
        }
    }

    private static void DestroyAudioSourceObject(GameObject sourceObject)
    {
        if (sourceObject == null)
            return;

        if (Application.isPlaying)
            UnityEngine.Object.Destroy(sourceObject);
        else
            UnityEngine.Object.DestroyImmediate(sourceObject);
    }

    private void UpdateFromGameState()
    {
        if (GameManager.Instance == null)
            return;

        GameManager.GameState state = GameManager.Instance.CurrentState;
        if (state == _lastState)
            return;

        _lastState = state;
        switch (state)
        {
            case GameManager.GameState.Exploring:
                ReactToExperimentEvent("game_start");
                break;
            case GameManager.GameState.GhostHint:
            case GameManager.GameState.GhostNear:
                ReactToExperimentEvent("ghost_hint");
                break;
            case GameManager.GameState.Chase:
                ReactToExperimentEvent("chase");
                break;
            case GameManager.GameState.JumpScare:
                PlayBgm(jumpScareBgm != null ? jumpScareBgm : chaseBgm, instant: true, tensionLevel: 4);
                _heartbeatTarget = 1f;
                break;
            case GameManager.GameState.GameOver:
                ReactToExperimentEvent("mission_failed");
                break;
        }
    }

    private void PlayBgm(AudioClip clip, bool instant, int tensionLevel, bool force = false)
    {
        if (bgmSource == null || clip == null)
            return;
        if (!force && tensionLevel < _activeTensionLevel)
            return;
        if (bgmSource.clip == clip && bgmSource.isPlaying)
        {
            _activeTensionLevel = Mathf.Max(_activeTensionLevel, tensionLevel);
            return;
        }

        _activeTensionLevel = Mathf.Max(_activeTensionLevel, tensionLevel);
        if (_crossfadeCoroutine != null)
            StopCoroutine(_crossfadeCoroutine);

        if (instant)
        {
            PlayInstant(clip);
            _crossfadeCoroutine = null;
            return;
        }

        _crossfadeCoroutine = StartCoroutine(Crossfade(clip));
    }

    private void PlayInstant(AudioClip clip)
    {
        bgmSource.clip = clip;
        bgmSource.loop = true;
        bgmSource.volume = bgmVolume;
        bgmSource.Play();
    }

    private IEnumerator Crossfade(AudioClip newClip)
    {
        float duration = Mathf.Max(0.05f, crossfadeDuration * 0.5f);
        float elapsed = 0f;
        float startVol = bgmSource.volume;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            bgmSource.volume = Mathf.Lerp(startVol, 0f, elapsed / duration);
            yield return null;
        }

        bgmSource.clip = newClip;
        bgmSource.loop = true;
        bgmSource.Play();
        elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            bgmSource.volume = Mathf.Lerp(0f, bgmVolume, elapsed / duration);
            yield return null;
        }

        bgmSource.volume = bgmVolume;
    }

    private void PlayOneShot(AudioClip clip)
    {
        if (clip != null && sfxSource != null)
            sfxSource.PlayOneShot(clip, sfxVolume);
    }

    private void PlayRandomAmbient()
    {
        if (ambientSounds == null || ambientSounds.Length == 0)
            return;

        AudioClip clip = ambientSounds[Random.Range(0, ambientSounds.Length)];
        PlayOneShot(clip);
    }

    private void StartAmbientLoopIfNeeded()
    {
        if (_ambientCoroutine != null)
            StopCoroutine(_ambientCoroutine);

        _ambientCoroutine = StartCoroutine(PlayAmbientLoop());
    }

    private IEnumerator PlayAmbientLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Random.Range(ambientMinInterval, ambientMaxInterval));
            if (GameManager.Instance != null && GameManager.Instance.CurrentState == GameManager.GameState.GameOver)
                continue;

            PlayRandomAmbient();
        }
    }

    private void UpdateHeartbeat()
    {
        if (heartbeatSource == null || heartbeatLoop == null)
            return;

        if (heartbeatSource.clip != heartbeatLoop)
            heartbeatSource.clip = heartbeatLoop;
        if (!heartbeatSource.isPlaying)
            heartbeatSource.Play();

        float targetVolume = _heartbeatTarget * heartbeatVolume;
        heartbeatSource.volume = Mathf.MoveTowards(heartbeatSource.volume, targetVolume, Time.deltaTime * 0.45f);
    }
}
