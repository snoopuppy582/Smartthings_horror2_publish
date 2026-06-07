using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

/// <summary>
/// Tools > Player Setup 실행 시 Player 오브젝트에
/// CharacterController + FirstPersonController 를 자동으로 세팅합니다.
/// </summary>
public static class PlayerSetupEditor
{
    [MenuItem("Tools/Player Setup")]
    public static void SetupPlayer()
    {
        GameObject player = GameObject.Find("Player");
        if (player == null)
        {
            EditorUtility.DisplayDialog("Player Setup", "씬에서 'Player' 오브젝트를 찾을 수 없습니다.", "확인");
            return;
        }

        Undo.RegisterFullObjectHierarchyUndo(player, "Player Setup");

        // ── 1. 위치 보정 (지면 위) ─────────────────────────────────
        Vector3 pos = player.transform.position;
        if (pos.y < 1f)
        {
            player.transform.position = new Vector3(pos.x, 4f, pos.z);
            Debug.Log($"[PlayerSetup] Y 위치를 {pos.y:F2} → 4 로 보정했습니다.");
        }

        // ── 2. 태그 ────────────────────────────────────────────────
        player.tag = "Player";

        // ── 3. CharacterController ─────────────────────────────────
        CharacterController cc = player.GetComponent<CharacterController>();
        if (cc == null)
            cc = Undo.AddComponent<CharacterController>(player);

        cc.height = 1.8f;
        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.radius = 0.3f;
        cc.slopeLimit = 45f;
        cc.stepOffset = 0.3f;

        // ── 4. CameraHolder 자식 오브젝트 ─────────────────────────
        Transform cameraHolder = player.transform.Find("CameraHolder");
        if (cameraHolder == null)
        {
            GameObject chGO = new GameObject("CameraHolder");
            Undo.RegisterCreatedObjectUndo(chGO, "Create CameraHolder");
            chGO.transform.SetParent(player.transform, false);
            chGO.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            chGO.transform.localRotation = Quaternion.identity;
            cameraHolder = chGO.transform;
            Debug.Log("[PlayerSetup] CameraHolder 자식 오브젝트를 생성했습니다.");
        }

        // ── 5. 기존 카메라를 CameraHolder 아래로 ─────────────────
        Camera existingCam = null;
        foreach (Transform child in player.transform)
        {
            Camera c = child.GetComponentInChildren<Camera>(true);
            if (c != null && child != cameraHolder)
            {
                existingCam = c;
                break;
            }
        }

        if (existingCam != null && existingCam.transform.parent != cameraHolder)
        {
            Undo.SetTransformParent(existingCam.transform, cameraHolder, "Reparent Camera");
            existingCam.transform.localPosition = Vector3.zero;
            existingCam.transform.localRotation = Quaternion.identity;
            Debug.Log($"[PlayerSetup] '{existingCam.name}' 을 CameraHolder 아래로 이동했습니다.");
        }

        // CameraHolder 자체에 카메라가 없으면 새로 추가
        if (cameraHolder.GetComponentInChildren<Camera>(true) == null)
        {
            GameObject camGO = new GameObject("Main Camera");
            Undo.RegisterCreatedObjectUndo(camGO, "Create Camera");
            camGO.tag = "MainCamera";
            camGO.transform.SetParent(cameraHolder, false);
            camGO.transform.localPosition = Vector3.zero;
            camGO.AddComponent<Camera>();
            camGO.AddComponent<AudioListener>();
            Debug.Log("[PlayerSetup] CameraHolder 아래에 새 Camera 를 생성했습니다.");
        }

        // ── 6. FirstPersonController ────────────────────────────────
        FirstPersonController fpc = player.GetComponent<FirstPersonController>();
        if (fpc == null)
            fpc = Undo.AddComponent<FirstPersonController>(player);

        // cameraHolder 필드 직접 주입
        SerializedObject soFpc = new SerializedObject(fpc);
        SerializedProperty propHolder = soFpc.FindProperty("cameraHolder");
        if (propHolder != null)
        {
            propHolder.objectReferenceValue = cameraHolder;
            soFpc.ApplyModifiedProperties();
            Debug.Log("[PlayerSetup] FirstPersonController.cameraHolder 연결 완료.");
        }
        else
        {
            Debug.LogWarning("[PlayerSetup] cameraHolder 프로퍼티를 찾지 못했습니다. Inspector에서 직접 연결하세요.");
        }

        // ── 7. PlayerHealth ─────────────────────────────────────────
        if (player.GetComponent<PlayerHealth>() == null)
        {
            Undo.AddComponent<PlayerHealth>(player);
            Debug.Log("[PlayerSetup] PlayerHealth 추가 완료.");
        }

        // ── 8. 씬 더티 마킹 및 선택 ────────────────────────────────
        EditorUtility.SetDirty(player);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Selection.activeGameObject = player;

        Debug.Log("[PlayerSetup] ✓ Player 세팅 완료! Inspector를 확인하세요.");
        EditorUtility.DisplayDialog("Player Setup 완료",
            "Player 세팅이 완료되었습니다.\n\n" +
            "• CharacterController (H=1.8, Center Y=0.9)\n" +
            "• CameraHolder (LocalPos Y=1.6)\n" +
            "• FirstPersonController (cameraHolder 연결됨)\n" +
            "• PlayerHealth\n\n" +
            "씬을 저장(Ctrl+S)하세요.", "확인");
    }
}
