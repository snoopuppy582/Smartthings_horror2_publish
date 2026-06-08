#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>
/// 씬 세팅이 올바른지 에디터에서 검증하는 유틸리티.
/// Unity 메뉴 → Tools → Validate Horror Scene Setup 에서 실행.
/// </summary>
public static class SceneSetupValidator
{
    [MenuItem("Tools/Validate Horror Scene Setup")]
    public static void Validate()
    {
        int errors = 0;
        int warnings = 0;

        // ── 필수 싱글턴 ───────────────────────────────────────────
        CheckRuntimeSingleton<GameManager>("GameManager", ref warnings);
        CheckRuntimeSingleton<SmartThingsEventSender>("SmartThingsEventSender", ref warnings);
        CheckRuntimeSingleton<ExperimentDirector>("ExperimentDirector", ref warnings);

        // ── Player ───────────────────────────────────────────────
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogError("[Setup] Player 태그 오브젝트 없음");
            errors++;
        }
        else
        {
            if (player.GetComponent<FirstPersonController>() == null)
            { Debug.LogError("[Setup] Player에 FirstPersonController 없음"); errors++; }
            if (player.GetComponent<CharacterController>() == null)
            { Debug.LogError("[Setup] Player에 CharacterController 없음"); errors++; }
            if (player.GetComponent<PlayerHealth>() == null)
            { Debug.LogWarning("[Setup] Player에 PlayerHealth 없음"); warnings++; }
        }

        // ── Enemy ────────────────────────────────────────────────
        var enemy = Object.FindFirstObjectByType<EnemyAI>();
        var killer = Object.FindFirstObjectByType<KillerAI>();
        if (enemy == null && killer == null)
        { Debug.LogWarning("[Setup] EnemyAI/KillerAI 없음 — 적 프리팹을 씬에 배치하세요"); warnings++; }
        if (enemy != null && enemy.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
        { Debug.LogError("[Setup] EnemyAI에 NavMeshAgent 없음"); errors++; }
        if (killer != null && killer.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
        { Debug.LogError("[Setup] KillerAI에 NavMeshAgent 없음"); errors++; }

        // ── Experiment objective ─────────────────────────────────
        if (Object.FindFirstObjectByType<ObjectiveItem>() == null)
        {
            Debug.LogError("[Setup] ObjectiveItem 없음 — 2층 목표물에 ObjectiveItem 트리거를 붙여야 성공 종료 가능");
            errors++;
        }

        // ── InputSystem_Actions C# 클래스 ─────────────────────────
        var inputType = System.Type.GetType("InputSystem_Actions");
        if (inputType == null)
        {
            Debug.LogError("[Setup] InputSystem_Actions C# 클래스 없음.\n" +
                           "→ Assets/InputSystem_Actions.inputactions 선택 →\n" +
                           "   Inspector에서 'Generate C# Class' 체크 → Apply");
            errors++;
        }

        // ── NavMesh Bake ─────────────────────────────────────────
        var surface = Object.FindFirstObjectByType<Unity.AI.Navigation.NavMeshSurface>();
        if (surface == null)
        { Debug.LogWarning("[Setup] NavMeshSurface 없음 — 바닥 오브젝트에 추가 후 Bake"); warnings++; }

        // ── Missing scripts ──────────────────────────────────────
        int missingScripts = CountMissingScripts();
        if (missingScripts > 0)
        {
            Debug.LogError($"[Setup] Missing Script 컴포넌트 {missingScripts}개 발견 — 삭제한 템플릿 스크립트 참조를 제거하세요");
            errors++;
        }

        // ── 결과 ─────────────────────────────────────────────────
        if (errors == 0 && warnings == 0)
            Debug.Log("[Setup] ✅ 모든 검증 통과!");
        else
            Debug.Log($"[Setup] 완료 — 오류 {errors}개 / 경고 {warnings}개");
    }

    private static void Check<T>(string name, ref int errors) where T : Object
    {
        if (Object.FindFirstObjectByType<T>() == null)
        {
            Debug.LogError($"[Setup] {name} 씬에 없음");
            errors++;
        }
    }

    private static void CheckRuntimeSingleton<T>(string name, ref int warnings) where T : Object
    {
        if (Object.FindFirstObjectByType<T>() == null)
        {
            Debug.LogWarning($"[Setup] {name} 씬에 없음 — ExperimentBootstrapper가 Play 시 생성하지만, 최종 실험 씬에는 명시 배치 권장");
            warnings++;
        }
    }

    private static int CountMissingScripts()
    {
        int count = 0;
        var objects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var go in objects)
            count += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);
        return count;
    }
}
#endif
