using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 실험 모드에서 적에게 맞았을 때 사망 대신 짧은 피격 피드백을 제공한다.
/// </summary>
public class NonLethalHitFeedback : MonoBehaviour
{
    [Header("시각 효과")]
    [SerializeField] private Image damageVignette;
    [SerializeField] private float vignetteAlpha = 0.75f;
    [SerializeField] private float feedbackDurationSec = 0.65f;
    [SerializeField] private Transform cameraTransform;
    [SerializeField] private float shakeAmount = 0.08f;

    [Header("이동 제한")]
    [SerializeField] private bool stunMovement = true;
    [SerializeField] private float stunDurationSec = 0.35f;

    [Header("오디오")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip hitClip;

    private static AudioClip s_defaultHitClip;
    private Coroutine _feedbackRoutine;

    private void Awake()
    {
        ResolveDamageVignette();

        if (cameraTransform == null)
        {
            Camera cameraInChildren = GetComponentInChildren<Camera>(true);
            if (cameraInChildren != null)
                cameraTransform = cameraInChildren.transform;
        }

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
    }

    public void PlayFeedback()
    {
        ResolveDamageVignette();

        if (_feedbackRoutine != null)
            StopCoroutine(_feedbackRoutine);

        _feedbackRoutine = StartCoroutine(FeedbackRoutine());
    }

    private IEnumerator FeedbackRoutine()
    {
        AudioClip clip = hitClip != null ? hitClip : GetDefaultHitClip();
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);

        FirstPersonController controller = GetComponent<FirstPersonController>();
        if (stunMovement && controller != null)
            controller.LockMovement(true, false);

        Vector3 originalCameraPosition = cameraTransform != null ? cameraTransform.localPosition : Vector3.zero;
        float elapsed = 0f;

        while (elapsed < feedbackDurationSec)
        {
            elapsed += Time.deltaTime;
            float t = 1f - Mathf.Clamp01(elapsed / feedbackDurationSec);

            SetVignetteAlpha(vignetteAlpha * t);

            if (cameraTransform != null)
            {
                Vector3 shake = Random.insideUnitSphere * (shakeAmount * t);
                shake.z = 0f;
                cameraTransform.localPosition = originalCameraPosition + shake;
            }

            if (stunMovement && controller != null && elapsed >= stunDurationSec)
            {
                if (CanReleaseMovementLock())
                    controller.LockMovement(false, false);
                controller = null;
            }

            yield return null;
        }

        SetVignetteAlpha(0f);
        if (cameraTransform != null)
            cameraTransform.localPosition = originalCameraPosition;

        if (stunMovement && controller != null && CanReleaseMovementLock())
            controller.LockMovement(false, false);

        _feedbackRoutine = null;
    }

    private static bool CanReleaseMovementLock()
    {
        return ExperimentDirector.Instance == null || ExperimentDirector.Instance.IsRunning;
    }

    private void SetVignetteAlpha(float alpha)
    {
        if (damageVignette == null)
            ResolveDamageVignette();
        if (damageVignette == null) return;

        Color color = damageVignette.color;
        color.a = Mathf.Clamp01(alpha);
        damageVignette.color = color;
    }

    private void ResolveDamageVignette()
    {
        if (damageVignette != null) return;

        GameObject vignette = GameObject.Find(ExperimentDirector.DamageVignetteName);
        if (vignette != null)
            damageVignette = vignette.GetComponent<Image>();
    }

    private static AudioClip GetDefaultHitClip()
    {
        if (s_defaultHitClip != null)
            return s_defaultHitClip;

        const int sampleRate = 44100;
        const float durationSec = 0.18f;
        int sampleCount = Mathf.CeilToInt(sampleRate * durationSec);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float fade = 1f - Mathf.Clamp01(t / durationSec);
            float tone = Mathf.Sin(2f * Mathf.PI * 82f * t) * 0.55f;
            float noiseSeed = Mathf.Sin(i * 12.9898f) * 43758.5453f;
            float noise = (noiseSeed - Mathf.Floor(noiseSeed)) * 2f - 1f;
            samples[i] = (tone + noise * 0.35f) * fade * 0.45f;
        }

        s_defaultHitClip = AudioClip.Create("DefaultExperimentHit", sampleCount, 1, sampleRate, false);
        s_defaultHitClip.SetData(samples, 0);
        return s_defaultHitClip;
    }
}
