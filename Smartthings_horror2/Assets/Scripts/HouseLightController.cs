using System.Collections;
using UnityEngine;

public class HouseLightController : MonoBehaviour
{
    [Header("MainHouseLight 그룹을 드래그 (자식 PointLight 전부 자동 수집)")]
    [SerializeField] private Transform lightGroup;

    [Header("페이드 시간(초). 0이면 즉시 전환")]
    [SerializeField] private float fadeDuration = 0.3f;

    private Light[] lights;
    private float[] originalIntensities;
    private Coroutine activeRoutine;

    private void Awake()
    {
        lights = lightGroup != null
            ? lightGroup.GetComponentsInChildren<Light>(true)
            : new Light[0];

        originalIntensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
            originalIntensities[i] = lights[i].intensity;

        Debug.Log($"[HouseLight] 조명 {lights.Length}개 수집됨");
    }

    // ── 6가지 이벤트 반응 (CLAUDE.md 기준) ──────────────────────────

    public void OnEnemyHint()   => FadeTo(0.5f);             // 밝기 50%
    public void OnEnemyNear()   => FadeTo(0.25f);            // 밝기 25%
    public void OnBlackout()    => Run(BlackoutSequence());  // 20% → 1초 → 100%
    public void OnChase()       => FadeTo(0.25f);            // 밝기 25%
    public void OnJumpScare()   => Run(JumpScareSequence()); // 100% → 0.2초 → 원래
    public void OnRecovery()    => FadeTo(1.0f);             // 밝기 100% 복구

    // 레거시 호환
    public void Blackout() => OnBlackout();
    public void Recover()  => OnRecovery();

    // ── 내부 구현 ─────────────────────────────────────────────────

    private void FadeTo(float multiplier)
    {
        float[] targets = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
            targets[i] = originalIntensities[i] * multiplier;
        Run(FadeCoroutine(targets));
    }

    private void Run(IEnumerator routine)
    {
        if (activeRoutine != null) StopCoroutine(activeRoutine);
        activeRoutine = StartCoroutine(routine);
    }

    private IEnumerator BlackoutSequence()
    {
        Snap(0.2f);
        yield return new WaitForSeconds(1f);
        Snap(1.0f);
        activeRoutine = null;
    }

    private IEnumerator JumpScareSequence()
    {
        float[] before = GetCurrentIntensities();
        Snap(1.0f);
        yield return new WaitForSeconds(0.2f);
        Apply(before);
        activeRoutine = null;
    }

    private IEnumerator FadeCoroutine(float[] targets)
    {
        if (fadeDuration <= 0f) { Apply(targets); activeRoutine = null; yield break; }

        float[] start = GetCurrentIntensities();
        float t = 0f;
        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / fadeDuration);
            for (int i = 0; i < lights.Length; i++)
                lights[i].intensity = Mathf.Lerp(start[i], targets[i], k);
            yield return null;
        }
        Apply(targets);
        activeRoutine = null;
    }

    // 즉시 배율 적용 (코루틴 내부 전용 — activeRoutine 건드리지 않음)
    private void Snap(float multiplier)
    {
        for (int i = 0; i < lights.Length; i++)
            lights[i].intensity = originalIntensities[i] * multiplier;
    }

    private void Apply(float[] intensities)
    {
        for (int i = 0; i < lights.Length; i++)
            lights[i].intensity = intensities[i];
    }

    private float[] GetCurrentIntensities()
    {
        float[] cur = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
            cur[i] = lights[i].intensity;
        return cur;
    }
}
