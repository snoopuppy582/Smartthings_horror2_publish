using System.Collections;
using UnityEngine;

/// <summary>
/// 게임 상태에 따라 BGM과 공포 분위기 효과음을 관리한다.
/// GameManager.CurrentState를 매 프레임 폴링해 BGM을 전환한다.
/// </summary>
public class AmbientAudioManager : MonoBehaviour
{
    [Header("BGM 클립 (게임 상태별)")]
    [SerializeField] private AudioClip exploringBgm;   // 탐색 중 음악
    [SerializeField] private AudioClip ghostHintBgm;   // 기척 음악
    [SerializeField] private AudioClip chaseBgm;       // 추격 음악
    [SerializeField] private AudioClip jumpScareBgm;   // 점프 스케어 음악

    [Header("주변 효과음 (랜덤 재생)")]
    [SerializeField] private AudioClip[] ambientSounds; // 바람, 삐걱 등
    [SerializeField] private float ambientMinInterval = 8f;
    [SerializeField] private float ambientMaxInterval = 20f;

    [Header("오디오 소스")]
    [SerializeField] private AudioSource bgmSource;
    [SerializeField] private AudioSource sfxSource;

    [Header("전환 설정")]
    [SerializeField] private float crossfadeDuration = 1.5f;

    private GameManager.GameState _lastState;
    private Coroutine _crossfadeCoroutine;
    private Coroutine _ambientCoroutine;

    void Start()
    {
        _lastState = GameManager.GameState.Exploring;
        PlayBgm(exploringBgm, instant: true);
        _ambientCoroutine = StartCoroutine(PlayAmbientLoop());
    }

    void Update()
    {
        if (GameManager.Instance == null) return;
        var state = GameManager.Instance.CurrentState;
        if (state == _lastState) return;

        _lastState = state;
        AudioClip target = state switch
        {
            GameManager.GameState.Exploring  => exploringBgm,
            GameManager.GameState.GhostHint  => ghostHintBgm,
            GameManager.GameState.GhostNear  => ghostHintBgm,
            GameManager.GameState.Chase      => chaseBgm,
            GameManager.GameState.JumpScare  => jumpScareBgm,
            _ => exploringBgm,
        };
        PlayBgm(target);
    }

    private void PlayBgm(AudioClip clip, bool instant = false)
    {
        if (clip == null || bgmSource.clip == clip) return;
        if (_crossfadeCoroutine != null) StopCoroutine(_crossfadeCoroutine);
        _crossfadeCoroutine = StartCoroutine(instant
            ? PlayInstant(clip)
            : Crossfade(clip));
    }

    private IEnumerator PlayInstant(AudioClip clip)
    {
        bgmSource.clip = clip;
        bgmSource.loop = true;
        bgmSource.Play();
        yield break;
    }

    private IEnumerator Crossfade(AudioClip newClip)
    {
        float elapsed = 0f;
        float startVol = bgmSource.volume;

        // 페이드 아웃
        while (elapsed < crossfadeDuration * 0.5f)
        {
            elapsed += Time.deltaTime;
            bgmSource.volume = Mathf.Lerp(startVol, 0f, elapsed / (crossfadeDuration * 0.5f));
            yield return null;
        }

        bgmSource.clip = newClip;
        bgmSource.loop = true;
        bgmSource.Play();
        elapsed = 0f;

        // 페이드 인
        while (elapsed < crossfadeDuration * 0.5f)
        {
            elapsed += Time.deltaTime;
            bgmSource.volume = Mathf.Lerp(0f, startVol, elapsed / (crossfadeDuration * 0.5f));
            yield return null;
        }
        bgmSource.volume = startVol;
    }

    private IEnumerator PlayAmbientLoop()
    {
        while (true)
        {
            float wait = Random.Range(ambientMinInterval, ambientMaxInterval);
            yield return new WaitForSeconds(wait);

            if (ambientSounds == null || ambientSounds.Length == 0) continue;
            if (GameManager.Instance?.CurrentState == GameManager.GameState.GameOver) continue;

            var clip = ambientSounds[Random.Range(0, ambientSounds.Length)];
            if (clip != null) sfxSource.PlayOneShot(clip);
        }
    }
}
