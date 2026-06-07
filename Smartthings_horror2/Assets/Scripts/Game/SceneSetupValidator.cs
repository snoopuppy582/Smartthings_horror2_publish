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
        Check<GameManager>("GameManager", ref errors);
        Check<SmartThingsEventSender>("SmartThingsEventSender", ref errors);

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
        if (enemy == null)
        { Debug.LogWarning("[Setup] EnemyAI 없음 — 적 프리팹을 씬에 배치하세요"); warnings++; }
        else
        {
            if (enemy.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
            { Debug.LogError("[Setup] Enemy에 NavMeshAgent 없음"); errors++; }
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
}
#endif
