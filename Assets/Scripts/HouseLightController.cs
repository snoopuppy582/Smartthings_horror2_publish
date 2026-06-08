using System.Collections;
using UnityEngine;

// MainHouseLight 그룹 아래의 모든 Light를 자동 수집해 일괄 제어
public class HouseLightController : MonoBehaviour
{
    [Header("MainHouseLight 그룹을 드래그 (자식 PointLight 전부 자동 수집)")]
    [SerializeField] private Transform lightGroup;

    [Header("암전/복구 페이드 시간(초). 0이면 즉시")]
    [SerializeField] private float fadeDuration = 0.4f;

    private Light[] lights;             // 그룹 아래 모든 Light
    private float[] originalIntensities; // 원래 밝기 (복구용)
    private Coroutine fadeRoutine;

    private void Awake()
    {
        // 비활성 자식까지 포함해서(true) Light 전부 수집
        lights = lightGroup != null
            ? lightGroup.GetComponentsInChildren<Light>(true)
            : new Light[0];

        originalIntensities = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++)
            originalIntensities[i] = lights[i].intensity;

        Debug.Log($"[HouseLight] 조명 {lights.Length}개 수집됨");
    }

    public void Blackout() => StartFade(false); // 암전 (recovery 전까지 유지)
    public void Recover()  => StartFade(true);  // 기본 밝기 복구

    // 이벤트명으로 조명 반응 분기
    public void ReactToEvent(string eventId)
    {
        switch (eventId)
        {
            case "chase":         SetIntensityScale(0.2f); break;
            case "player_hit":    StartPlayerHitFlash();   break;
            case "mission_success": SetIntensityScale(0.7f); break;
            case "mission_failed":  SetIntensityScale(0.3f); break;
            case "recovery":      Recover();               break;
            case "blackout":      Blackout();              break;
        }
    }

    // originalIntensities 기준 배수로 즉시 적용
    public void SetIntensityScale(float scale)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        for (int i = 0; i < lights.Length; i++)
            lights[i].intensity = originalIntensities[i] * scale;
    }

    // player_hit: 0.2 → 0.7 → 0.2 순으로 빠르게 전환
    private void StartPlayerHitFlash()
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(PlayerHitFlashRoutine());
    }

    private IEnumerator PlayerHitFlashRoutine()
    {
        for (int i = 0; i < lights.Length; i++)
            lights[i].intensity = originalIntensities[i] * 0.2f;

        yield return new WaitForSeconds(0.15f);

        for (int i = 0; i < lights.Length; i++)
            lights[i].intensity = originalIntensities[i] * 0.7f;

        yield return new WaitForSeconds(0.15f);

        for (int i = 0; i < lights.Length; i++)
            lights[i].intensity = originalIntensities[i] * 0.2f;

        fadeRoutine = null;
    }

    private void StartFade(bool toOriginal)
    {
        if (fadeRoutine != null) StopCoroutine(fadeRoutine);

        if (fadeDuration <= 0f) // 즉시 처리
        {
            for (int i = 0; i < lights.Length; i++)
                lights[i].intensity = toOriginal ? originalIntensities[i] : originalIntensities[i] * 0.5f;
            return;
        }
        fadeRoutine = StartCoroutine(Fade(toOriginal));
    }

    private IEnumerator Fade(bool toOriginal)
    {
        float t = 0f;
        float[] start = new float[lights.Length];
        for (int i = 0; i < lights.Length; i++) start[i] = lights[i].intensity;

        while (t < fadeDuration)
        {
            t += Time.deltaTime;
            float k = t / fadeDuration;
            for (int i = 0; i < lights.Length; i++)
                lights[i].intensity = Mathf.Lerp(start[i], toOriginal ? originalIntensities[i] : originalIntensities[i] * 0.5f, k);
            yield return null;
        }
        for (int i = 0; i < lights.Length; i++)
            lights[i].intensity = toOriginal ? originalIntensities[i] : originalIntensities[i] * 0.5f;
    }
}