#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 실험 씬을 정리하기 위한 에디터 메뉴 도구.
/// </summary>
public static class ExperimentSceneTools
{
    private const string MainScenePath = "Assets/Scenes/MainScene.unity";
    private const string KillerPrefabPath = "Assets/Prefabs/Killer.prefab";
    private const string PrimaryHouseColliderName = "Old_House_windows_separated_Collider";
    private static readonly string[] PrefabCleanupSearchFolders =
    {
        "Assets/Prefabs",
        "Assets/Horror Multiplayer Template by Redicion Studio/Prefabs",
        "Assets/Horror Multiplayer Template by Redicion Studio/Models",
    };

    private static readonly HashSet<string> ExperimentUiObjectNames = new HashSet<string>
    {
        "ExperimentHUD_Auto",
        "ExperimentSuccessPanel_Auto",
        "ExperimentFailedPanel_Auto",
        ExperimentDirector.DamageVignetteName,
    };

    private static readonly Vector3 StairAssistBaseTop = new Vector3(-29.15f, 0.22f, -18.75f);
    private static readonly Vector3 StairAssistLandingTop = new Vector3(-27.35f, 2.94f, -17.25f);
    private static readonly Vector3 StairAssistExitTop = new Vector3(-24.55f, 2.94f, -15.35f);
    private static readonly Vector3 KillerSpawnPosition = new Vector3(-16.8f, 0.15f, -11.8f);
    private static readonly Vector3 KillerLookTarget = new Vector3(-24.6f, 0.15f, -17.2f);
    private static readonly Color ObjectivePurple = new Color(0.72f, 0.18f, 1f, 1f);

    [MenuItem("Tools/Experiment/Prepare Active Scene")]
    public static void PrepareActiveScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        int removed = RemoveMissingScriptsFromScene(scene);
        int prefabRemoved = CleanExperimentPrefabMissingScripts();

        EnsureSceneObject<GameManager>("GameManager");
        EnsureSceneObject<SmartThingsEventSender>("SmartThingsEventSender");
        EnsureExperimentDirector();
        EnsureMainSceneInBuildSettings();
        EnsurePlayerExperimentSetup();
        EnsureKillerExperimentSetup();
        EnsureSecondFloorAccessRamp();
        EnsureSecondFloorWalkableColliders();
        EnsureSecondFloorBoundaryColliders();
        EnsureOldHouseInteriorCollisionShell();
        EnsureDoorwayAccessAssist();
        EnsureDoorwayHouseCollisionGate();
        EnsureStairHouseCollisionGate();
        EnsureStairTraversalAssistZone();
        int legacyStairBlockers = DisableLegacyStairBlockers();
        int houseCollidersNeutralized = NeutralizePrimaryHouseMeshCollider();
        EnsureProgressMarkers();
        EnsureObjectiveItem();
        EnsureProceduralAmbience();
        EnsureExternalHorrorAudio();
        bool navMeshRebuilt = RebuildExperimentNavMesh();
        int uiDuplicatesRemoved = RemoveDuplicateExperimentUiObjects();

        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.ForceReserializeAssets(new[] { scene.path }, ForceReserializeAssetsOptions.ReserializeAssetsAndMetadata);

        Debug.Log($"[ExperimentTools] Active scene prepared. Missing scripts removed: {removed}, prefab missing scripts removed: {prefabRemoved}, legacy stair blockers converted: {legacyStairBlockers}, broad house mesh colliders neutralized: {houseCollidersNeutralized}, navMeshRebuilt: {navMeshRebuilt}, uiDuplicatesRemoved: {uiDuplicatesRemoved}");
    }

    [MenuItem("Tools/Experiment/Clean Killer Prefab Missing Scripts")]
    public static void CleanKillerPrefab()
    {
        GameObject prefabRoot = PrefabUtility.LoadPrefabContents(KillerPrefabPath);
        if (prefabRoot == null)
        {
            Debug.LogWarning($"[ExperimentTools] Killer prefab not found: {KillerPrefabPath}");
            return;
        }

        int removed = RemoveMissingScriptsFromTree(prefabRoot);
        if (removed > 0)
            PrefabUtility.SaveAsPrefabAsset(prefabRoot, KillerPrefabPath);

        PrefabUtility.UnloadPrefabContents(prefabRoot);
        Debug.Log($"[ExperimentTools] Killer prefab cleaned. Missing scripts removed: {removed}");
    }

    [MenuItem("Tools/Experiment/Clean Experiment Prefab Missing Scripts")]
    public static void CleanExperimentPrefabsFromMenu()
    {
        int removed = CleanExperimentPrefabMissingScripts();
        Debug.Log($"[ExperimentTools] Experiment prefab cleanup complete. Missing scripts removed: {removed}");
    }

    private static int CleanExperimentPrefabMissingScripts()
    {
        string[] validFolders = PrefabCleanupSearchFolders
            .Where(AssetDatabase.IsValidFolder)
            .ToArray();

        if (validFolders.Length == 0)
            return 0;

        int removed = 0;
        int failed = 0;
        HashSet<string> paths = new HashSet<string>();
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", validFolders))
            paths.Add(AssetDatabase.GUIDToAssetPath(guid));

        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string path in paths)
            {
                GameObject prefabRoot = null;
                try
                {
                    prefabRoot = PrefabUtility.LoadPrefabContents(path);
                    if (prefabRoot == null)
                        continue;

                    int prefabRemoved = RemoveMissingScriptsFromTree(prefabRoot);
                    if (prefabRemoved <= 0)
                        continue;

                    PrefabUtility.SaveAsPrefabAsset(prefabRoot, path);
                    removed += prefabRemoved;
                }
                catch (System.Exception ex)
                {
                    failed++;
                    Debug.LogWarning($"[ExperimentTools] Could not clean prefab '{path}': {ex.Message}");
                }
                finally
                {
                    if (prefabRoot != null)
                        PrefabUtility.UnloadPrefabContents(prefabRoot);
                }
            }
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
            AssetDatabase.SaveAssets();
        }

        if (failed > 0)
            Debug.LogWarning($"[ExperimentTools] Prefab missing script cleanup skipped {failed} prefab(s).");

        return removed;
    }

    private static int RemoveMissingScriptsFromScene(Scene scene)
    {
        int removed = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
            removed += RemoveMissingScriptsFromTree(root);
        return removed;
    }

    private static int RemoveMissingScriptsFromTree(GameObject root)
    {
        int removed = 0;
        foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
        return removed;
    }

    private static void EnsureSceneObject<T>(string objectName) where T : Component
    {
        if (UnityEngine.Object.FindFirstObjectByType<T>() != null) return;

        GameObject go = new GameObject(objectName);
        Undo.RegisterCreatedObjectUndo(go, $"Create {objectName}");
        go.AddComponent<T>();
    }

    private static void EnsureExperimentDirector()
    {
        ExperimentDirector director = UnityEngine.Object.FindFirstObjectByType<ExperimentDirector>();

        if (director == null)
        {
            GameObject go = new GameObject("ExperimentDirector");
            Undo.RegisterCreatedObjectUndo(go, "Create ExperimentDirector");
            go.AddComponent<ExperimentLogger>();
            director = go.AddComponent<ExperimentDirector>();
        }
        else if (director.GetComponent<ExperimentLogger>() == null)
        {
            Undo.AddComponent<ExperimentLogger>(director.gameObject);
        }

        if (director.GetComponent<ExperimentRuntimeWatchdog>() == null)
            Undo.AddComponent<ExperimentRuntimeWatchdog>(director.gameObject);

        if (director.GetComponent<ExperimentPlayModeSmokeRunner>() == null)
            Undo.AddComponent<ExperimentPlayModeSmokeRunner>(director.gameObject);

        EnsureExperimentUi(director);
    }

    private static void EnsureMainSceneInBuildSettings()
    {
        string guid = AssetDatabase.AssetPathToGUID(MainScenePath);
        if (string.IsNullOrEmpty(guid))
        {
            Debug.LogWarning($"[ExperimentTools] MainScene not found: {MainScenePath}");
            return;
        }

        List<EditorBuildSettingsScene> scenes = EditorBuildSettings.scenes.ToList();
        if (scenes.Any(s => s.path == MainScenePath))
            return;

        scenes.Add(new EditorBuildSettingsScene(MainScenePath, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    private static void EnsurePlayerExperimentSetup()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Debug.LogWarning("[ExperimentTools] Player tag object not found; skipped player tuning.");
            return;
        }

        Undo.RecordObject(player.transform, "Tune Player Transform");
        player.transform.localScale = Vector3.one;

        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            Undo.RecordObject(controller, "Tune Player CharacterController");
            controller.height = 1.55f;
            controller.radius = 0.22f;
            controller.center = new Vector3(0f, 0.775f, 0f);
            controller.slopeLimit = 60f;
            controller.stepOffset = 0.6f;
            controller.skinWidth = 0.08f;
            controller.minMoveDistance = 0f;
            EditorUtility.SetDirty(controller);
        }

        FirstPersonController firstPerson = player.GetComponent<FirstPersonController>();
        if (firstPerson != null)
        {
            SetSerializedFloat(firstPerson, "walkSpeed", 2.9f);
            SetSerializedFloat(firstPerson, "sprintSpeed", 4.5f);
            SetSerializedFloat(firstPerson, "crouchSpeed", 1.35f);
            SetSerializedFloat(firstPerson, "acceleration", 16f);
            SetSerializedFloat(firstPerson, "deceleration", 22f);
            SetSerializedFloat(firstPerson, "gravity", -16f);
            SetSerializedFloat(firstPerson, "standingHeight", 1.55f);
            SetSerializedFloat(firstPerson, "crouchHeight", 0.9f);
            SetSerializedFloat(firstPerson, "controllerRadius", 0.22f);
            SetSerializedFloat(firstPerson, "cameraStandingHeight", 1.32f);
            SetSerializedBool(firstPerson, "enableDoorwayShoulderAssist", true);
            SetSerializedBool(firstPerson, "enableStepAssist", true);
            SetSerializedFloat(firstPerson, "assistedStepHeight", 0.45f);
            SetSerializedFloat(firstPerson, "stepProbeDistance", 0.55f);
            SetSerializedFloat(firstPerson, "stepForwardNudge", 0.2f);
            SetSerializedFloat(firstPerson, "shoulderAssistDistance", 0.1f);
            SetSerializedFloat(firstPerson, "shoulderAssistForwardNudge", 0.12f);
            SetSerializedFloat(firstPerson, "bobAmplitude", 0.025f);
        }

        if (player.GetComponent<NonLethalHitFeedback>() == null)
        {
            NonLethalHitFeedback feedback = Undo.AddComponent<NonLethalHitFeedback>(player);
            EditorUtility.SetDirty(feedback);
        }

        if (player.GetComponent<AudioSource>() == null)
        {
            AudioSource audioSource = Undo.AddComponent<AudioSource>(player);
            EditorUtility.SetDirty(audioSource);
        }

        LanternController lantern = player.GetComponent<LanternController>();
        if (lantern == null)
            lantern = Undo.AddComponent<LanternController>(player);

        lantern.ConfigureForExperimentDefaults();
        EditorUtility.SetDirty(lantern);

        EnsureAudioListener(player);

        EditorUtility.SetDirty(player);
    }

    private static void EnsureAudioListener(GameObject player)
    {
        if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() != null) return;

        Camera camera = player.GetComponentInChildren<Camera>(true);
        GameObject target = camera != null ? camera.gameObject : player;
        AudioListener listener = Undo.AddComponent<AudioListener>(target);
        EditorUtility.SetDirty(listener);
        EditorUtility.SetDirty(target);
    }

    private static void EnsureKillerExperimentSetup()
    {
        KillerAI killer = EnsureKillerSceneInstance();
        if (killer == null)
        {
            Debug.LogWarning("[ExperimentTools] KillerAI not found and could not be created; skipped killer tuning.");
            return;
        }

        UnityEngine.AI.NavMeshAgent agent = killer.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null)
        {
            Undo.RecordObject(agent, "Tune Killer NavMeshAgent");
            agent.radius = 0.4f;
            agent.height = 2f;
            agent.speed = 1.75f;
            agent.acceleration = 6f;
            agent.angularSpeed = 300f;
            agent.stoppingDistance = 1.0f;
            agent.baseOffset = 0f;
            agent.autoBraking = true;
            agent.autoRepath = true;
            EditorUtility.SetDirty(agent);
        }

        Undo.RecordObject(killer, "Tune Killer Experiment Pacing");
        killer.ConfigureForExperimentDefaults();
        SetSerializedFloat(killer, "detectRange", 18f);
        SetSerializedFloat(killer, "loseRange", 30f);
        SetSerializedFloat(killer, "catchRange", 1.3f);
        SetSerializedFloat(killer, "walkSpeed", 1.05f);
        SetSerializedFloat(killer, "chaseSpeed", 1.75f);
        SetSerializedFloat(killer, "killerNearDistance", 6f);
        SetSerializedFloat(killer, "killerNearReportInterval", 18f);
        SetSerializedFloat(killer, "hitCooldown", 8f);
        SetSerializedFloat(killer, "attackWindupSec", 0.5f);
        SetSerializedFloat(killer, "attackRecoverySec", 1.6f);
        SetSerializedFloat(killer, "facePlayerTurnSpeed", 720f);
        SetSerializedBool(killer, "requireReachableAttackPath", true);
        SetSerializedFloat(killer, "attackVerticalTolerance", 0.95f);
        SetSerializedFloat(killer, "attackForwardConeDot", 0.25f);
        SetSerializedFloat(killer, "attackContactSlack", 0.2f);
        SetSerializedFloat(killer, "postHitBackoffDistance", 3.2f);
        SetSerializedFloat(killer, "postHitBackoffDurationSec", 1.8f);
        SetSerializedFloat(killer, "proceduralAttackLungeDistance", 0.32f);
        SetSerializedFloat(killer, "proceduralAttackLungeDurationSec", 0.22f);
        SetSerializedBool(killer, "relocateOnUnreachableForceChase", true);
        SetSerializedFloat(killer, "forceChaseRelocationDistance", 7f);
        SetSerializedFloat(killer, "forceChaseRelocationSearchRadius", 3f);
        SetSerializedFloat(killer, "forceChaseRelocationMinDistance", 4f);
        SetSerializedFloat(killer, "forceChaseRelocationVerticalTolerance", 0.85f);
        SetSerializedBool(killer, "avoidStairRouteDuringChase", true);
        SetSerializedVector3(killer, "stairSafetyCenter", new Vector3(-25.9f, 2.15f, -16.2f));
        SetSerializedVector3(killer, "stairSafetySize", new Vector3(8.4f, 4.9f, 7.2f));
        SetSerializedVector3(killer, "stairSafetyHoldPosition", new Vector3(-31.2f, 0.15f, -21.2f));
        SetSerializedFloat(killer, "stairSafetyHoldMinSec", 1.25f);
        SetSerializedFloat(killer, "stairSafetyHoldSampleRadius", 4f);

        KillerPlayerCollisionBypass collisionBypass = killer.GetComponent<KillerPlayerCollisionBypass>();
        if (collisionBypass == null)
            collisionBypass = Undo.AddComponent<KillerPlayerCollisionBypass>(killer.gameObject);
        collisionBypass.Configure("Player");
        EditorUtility.SetDirty(collisionBypass);

        Animator animator = killer.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.GetComponent<FootstepAnimationEventReceiver>() == null)
        {
            FootstepAnimationEventReceiver receiver = Undo.AddComponent<FootstepAnimationEventReceiver>(animator.gameObject);
            EditorUtility.SetDirty(receiver);
            EditorUtility.SetDirty(animator.gameObject);
        }
    }

    private static KillerAI EnsureKillerSceneInstance()
    {
        KillerAI killer = UnityEngine.Object
            .FindObjectsByType<KillerAI>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .FirstOrDefault();

        bool created = false;
        if (killer == null)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KillerPrefabPath);
            GameObject killerObject = prefab != null
                ? PrefabUtility.InstantiatePrefab(prefab) as GameObject
                : new GameObject("KILLER");

            if (killerObject == null)
                return null;

            Undo.RegisterCreatedObjectUndo(killerObject, "Create experiment KILLER");
            killerObject.name = "KILLER";
            if (killerObject.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
                Undo.AddComponent<UnityEngine.AI.NavMeshAgent>(killerObject);

            killer = killerObject.GetComponent<KillerAI>();
            if (killer == null)
                killer = Undo.AddComponent<KillerAI>(killerObject);

            created = true;
        }

        ActivateTransformChain(killer.transform);
        PlaceKillerAtExperimentSpawn(killer, created || !IsKillerInExperimentArea(killer.transform.position));
        EnsureKillerHasVisibleRenderer(killer);
        killer.gameObject.name = "KILLER";
        EditorUtility.SetDirty(killer.gameObject);
        return killer;
    }

    private static void ActivateTransformChain(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (current.gameObject.activeSelf)
                continue;

            Undo.RecordObject(current.gameObject, "Activate experiment object");
            current.gameObject.SetActive(true);
            EditorUtility.SetDirty(current.gameObject);
        }
    }

    private static bool IsKillerInExperimentArea(Vector3 position)
    {
        return position.x >= -36f &&
               position.x <= -18f &&
               position.y >= -0.5f &&
               position.y <= 4.5f &&
               position.z >= -27f &&
               position.z <= -8f;
    }

    private static void PlaceKillerAtExperimentSpawn(KillerAI killer, bool force)
    {
        if (killer == null)
            return;

        Vector3 target = KillerSpawnPosition;
        if (UnityEngine.AI.NavMesh.SamplePosition(KillerSpawnPosition, out UnityEngine.AI.NavMeshHit hit, 6f, UnityEngine.AI.NavMesh.AllAreas))
            target = hit.position;

        if (!force && Vector3.Distance(killer.transform.position, target) <= 0.75f)
            return;

        Vector3 look = KillerLookTarget - target;
        look.y = 0f;
        Quaternion rotation = look.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(look.normalized, Vector3.up)
            : killer.transform.rotation;

        Undo.RecordObject(killer.transform, "Place experiment KILLER");
        killer.transform.SetPositionAndRotation(target, rotation);
        killer.transform.localScale = Vector3.one;
        EditorUtility.SetDirty(killer.transform);
    }

    private static void EnsureKillerHasVisibleRenderer(KillerAI killer)
    {
        Renderer[] renderers = killer.GetComponentsInChildren<Renderer>(true);
        if (renderers.Any(renderer => renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy))
            return;

        foreach (Renderer renderer in renderers)
        {
            if (renderer == null)
                continue;

            ActivateTransformChain(renderer.transform);
            Undo.RecordObject(renderer, "Enable experiment KILLER renderer");
            renderer.enabled = true;
            EditorUtility.SetDirty(renderer);
            return;
        }
    }

    private static void EnsureSecondFloorAccessRamp()
    {
        CreateOrTuneRampSegment(
            "SecondFloorAccessRamp_Auto",
            StairAssistBaseTop,
            StairAssistLandingTop,
            width: 1.35f,
            thickness: 0.2f,
            overlap: 0.25f);
        SetColliderTrigger("SecondFloorAccessRamp_Auto", false);

        CreateOrTuneRampSegment(
            "SecondFloorAccessRamp_Landing_Auto",
            StairAssistLandingTop,
            StairAssistExitTop,
            width: 1.55f,
            thickness: 0.18f,
            overlap: 0.25f);
        SetColliderTrigger("SecondFloorAccessRamp_Landing_Auto", false);

        DisableStaleSecondFloorAccessRampSegments();
        DisableAllSecondFloorAccessStepSegments();
    }

    private static void EnsureSecondFloorWalkableColliders()
    {
        CreateOrTuneInvisibleBox(
            "SecondFloorWalkableFloor_Auto",
            new Vector3(-23.95f, 2.94f, -14.95f),
            Quaternion.identity,
            new Vector3(5.65f, 0.18f, 5.1f));

        CreateOrTuneInvisibleBox(
            "SecondFloorStairBridge_Auto",
            new Vector3(-26.55f, 2.94f, -16.65f),
            Quaternion.identity,
            new Vector3(1.95f, 0.18f, 1.95f));
        SetColliderTrigger("SecondFloorStairBridge_Auto", true);

        CreateOrTuneInvisibleBox(
            "SecondFloorStairLanding_Auto",
            new Vector3(-27.35f, 2.94f, -17.25f),
            Quaternion.identity,
            new Vector3(0.45f, 0.18f, 0.45f));
        SetColliderTrigger("SecondFloorStairLanding_Auto", true);
    }

    private static void EnsureSecondFloorBoundaryColliders()
    {
        CreateOrTuneWallBox(
            "SecondFloorBoundaryWall_Auto_North",
            new Vector3(-23.75f, 3.95f, -12.25f),
            Quaternion.identity,
            new Vector3(5.2f, 2.0f, 0.22f));

        CreateOrTuneWallBox(
            "SecondFloorBoundaryWall_Auto_South",
            new Vector3(-23.75f, 3.95f, -17.45f),
            Quaternion.identity,
            new Vector3(5.2f, 2.0f, 0.22f));

        CreateOrTuneWallBox(
            "SecondFloorBoundaryWall_Auto_East",
            new Vector3(-21.15f, 3.95f, -14.85f),
            Quaternion.identity,
            new Vector3(0.22f, 2.0f, 5.2f));

        CreateOrTuneWallBox(
            "SecondFloorBoundaryWall_Auto_West",
            new Vector3(-26.35f, 3.95f, -12.85f),
            Quaternion.identity,
            new Vector3(0.22f, 2.0f, 1.0f));
    }

    private static void EnsureOldHouseInteriorCollisionShell()
    {
        // 내·외부 전체 커버 바닥 — 읽기 불가 지형 MeshCollider 대신 NavMesh 베이크용
        CreateOrTuneInvisibleBox(
            "GroundPlaneNavMesh_Auto",
            new Vector3(-23f, 0f, -16f),
            Quaternion.identity,
            new Vector3(30f, 0.1f, 26f));

        // 집 내부 바닥 — NavMesh 베이크용 걷기 가능 영역
        CreateOrTuneInvisibleBox(
            "OldHouseInteriorFirstFloor_Auto",
            new Vector3(-25.2f, 0.05f, -17.15f),
            Quaternion.identity,
            new Vector3(9.4f, 0.1f, 8.9f));

        // 벽은 씬에서 수동 배치된 실제 콜라이더 사용
    }

    private const string WallDebugMaterialPath = "Assets/Materials/WallDebug_Transparent.mat";

    // 벽용 박스: 투명 파란색 Cube(시각 확인) + BoxCollider(물리 차단) + NavMeshObstacle(경로 카빙)
    private static void CreateOrTuneWallBox(string objectName, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        GameObject box = GameObject.Find(objectName);
        if (box == null)
        {
            box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = objectName;
            Undo.RegisterCreatedObjectUndo(box, $"Create {objectName}");
        }

        Undo.RecordObject(box.transform, $"Tune {objectName}");
        box.transform.SetPositionAndRotation(position, rotation);
        box.transform.localScale = scale;

        if (box.GetComponent<BoxCollider>() == null)
            Undo.AddComponent<BoxCollider>(box);

        MeshRenderer renderer = box.GetComponent<MeshRenderer>();
        if (renderer == null)
            renderer = Undo.AddComponent<MeshRenderer>(box);

        Material wallMat = GetOrCreateWallDebugMaterialAsset();
        if (renderer.sharedMaterial != wallMat)
        {
            Undo.RecordObject(renderer, $"Assign wall material {objectName}");
            renderer.sharedMaterial = wallMat;
            EditorUtility.SetDirty(renderer);
        }

        UnityEngine.AI.NavMeshObstacle obstacle = box.GetComponent<UnityEngine.AI.NavMeshObstacle>();
        if (obstacle == null)
            obstacle = Undo.AddComponent<UnityEngine.AI.NavMeshObstacle>(box);

        Undo.RecordObject(obstacle, $"Configure NavMeshObstacle {objectName}");
        obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
        obstacle.size = Vector3.one;
        obstacle.center = Vector3.zero;
        obstacle.carving = true;
        obstacle.carvingMoveThreshold = 0f;
        obstacle.carvingTimeToStationary = 0f;
        EditorUtility.SetDirty(obstacle);
        EditorUtility.SetDirty(box);
    }

    private static Material GetOrCreateWallDebugMaterialAsset()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(WallDebugMaterialPath);
        if (mat != null) return mat;

        System.IO.Directory.CreateDirectory("Assets/Materials");

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        bool isUrp = shader != null;
        if (!isUrp) shader = Shader.Find("Standard");

        mat = new Material(shader) { name = "WallDebug_Transparent" };

        if (isUrp)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        }
        else
        {
            mat.SetFloat("_Mode", 3f);
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.EnableKeyword("_ALPHABLEND_ON");
            mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        }

        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = 3000;
        mat.color = new Color(0.25f, 0.55f, 1f, 0.22f); // 반투명 파란색

        AssetDatabase.CreateAsset(mat, WallDebugMaterialPath);
        AssetDatabase.SaveAssets();
        return mat;
    }

    private static void EnsureDoorwayAccessAssist()
    {
        Transform door = GameObject.Find("DoorEntrance")?.transform;
        GameObject player = GameObject.FindGameObjectWithTag("Player");

        Vector3 position = door != null ? door.position : new Vector3(-24.286f, 1.47f, -21.119f);
        Quaternion rotation = door != null ? door.rotation : Quaternion.Euler(0f, 50f, 0f);
        float floorY = player != null ? player.transform.position.y : 0.2f;
        position.y = floorY + 0.12f;
        Vector3 throughDoor = rotation * Vector3.forward;

        CreateOrTuneInvisibleBox(
            "DoorEntranceThresholdBridge_Auto",
            position,
            rotation,
            new Vector3(2.25f, 0.08f, 2.8f));

        CreateOrTuneInvisibleBox(
            "DoorEntranceRampOutside_Auto",
            position - throughDoor * 0.9f + Vector3.up * 0.02f,
            rotation * Quaternion.Euler(-8f, 0f, 0f),
            new Vector3(2.35f, 0.08f, 1.9f));

        CreateOrTuneInvisibleBox(
            "DoorEntranceRampInside_Auto",
            position + throughDoor * 0.9f + Vector3.up * 0.02f,
            rotation * Quaternion.Euler(8f, 0f, 0f),
            new Vector3(2.35f, 0.08f, 1.9f));
    }

    private static void EnsureDoorwayHouseCollisionGate()
    {
        Transform door = GameObject.Find("DoorEntrance")?.transform;
        GameObject player = GameObject.FindGameObjectWithTag("Player");

        Vector3 position = door != null ? door.position : new Vector3(-24.286f, 1.47f, -21.119f);
        Quaternion rotation = door != null ? door.rotation : Quaternion.Euler(0f, 50f, 0f);
        float floorY = player != null ? player.transform.position.y : 0.2f;
        position.y = floorY + 0.12f;

        GameObject gate = GameObject.Find("DoorwayHouseCollisionGate_Auto");
        if (gate == null)
        {
            gate = new GameObject("DoorwayHouseCollisionGate_Auto");
            Undo.RegisterCreatedObjectUndo(gate, "Create DoorwayHouseCollisionGate_Auto");
        }

        Undo.RecordObject(gate.transform, "Tune DoorwayHouseCollisionGate_Auto");
        gate.transform.SetPositionAndRotation(position, rotation);
        gate.transform.localScale = Vector3.one;

        DoorwayHouseCollisionGate collisionGate = gate.GetComponent<DoorwayHouseCollisionGate>();
        if (collisionGate == null)
            collisionGate = Undo.AddComponent<DoorwayHouseCollisionGate>(gate);

        collisionGate.Configure(PrimaryHouseColliderName, new Vector3(2.75f, 2.35f, 3.4f), new Vector3(0f, 1.05f, 0f));
        EditorUtility.SetDirty(collisionGate);
        EditorUtility.SetDirty(gate);
    }

    private static void EnsureStairHouseCollisionGate()
    {
        GameObject gate = GameObject.Find("StairHouseCollisionGate_Auto");
        if (gate == null)
        {
            gate = new GameObject("StairHouseCollisionGate_Auto");
            Undo.RegisterCreatedObjectUndo(gate, "Create StairHouseCollisionGate_Auto");
        }

        Undo.RecordObject(gate.transform, "Tune StairHouseCollisionGate_Auto");
        gate.transform.SetPositionAndRotation(
            new Vector3(-26.1f, 1.55f, -16.35f),
            Quaternion.identity);
        gate.transform.localScale = Vector3.one;

        DoorwayHouseCollisionGate collisionGate = gate.GetComponent<DoorwayHouseCollisionGate>();
        if (collisionGate == null)
            collisionGate = Undo.AddComponent<DoorwayHouseCollisionGate>(gate);

        collisionGate.Configure(PrimaryHouseColliderName, new Vector3(7.6f, 4.9f, 6.6f), new Vector3(0f, 1.25f, 0f));
        EditorUtility.SetDirty(collisionGate);
        EditorUtility.SetDirty(gate);
    }

    private static void EnsureStairTraversalAssistZone()
    {
        GameObject zone = GameObject.Find("StairTraversalAssistZone_Auto");
        if (zone == null)
        {
            zone = new GameObject("StairTraversalAssistZone_Auto");
            Undo.RegisterCreatedObjectUndo(zone, "Create StairTraversalAssistZone_Auto");
        }

        Undo.RecordObject(zone.transform, "Tune StairTraversalAssistZone_Auto");
        zone.transform.SetPositionAndRotation(
            new Vector3(-26.1f, 1.55f, -16.35f),
            Quaternion.identity);
        zone.transform.localScale = Vector3.one;

        StairTraversalAssistZone assist = zone.GetComponent<StairTraversalAssistZone>();
        if (assist == null)
            assist = Undo.AddComponent<StairTraversalAssistZone>(zone);

        assist.Configure(
            StairAssistBaseTop,
            StairAssistExitTop,
            new Vector3(7.6f, 4.9f, 6.6f),
            new Vector3(0f, 1.25f, 0f));
        EditorUtility.SetDirty(assist);
        EditorUtility.SetDirty(zone);
    }

    private static int NeutralizePrimaryHouseMeshCollider()
    {
        int changed = 0;
        Collider[] colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Collider collider in colliders)
        {
            if (collider == null || collider.gameObject.name != PrimaryHouseColliderName)
                continue;

            if (!collider.gameObject.activeSelf)
            {
                Undo.RecordObject(collider.gameObject, "Enable broad house mesh collider object");
                collider.gameObject.SetActive(true);
                changed++;
            }

            Undo.RecordObject(collider, "Disable broad house mesh collider");
            if (collider.enabled)
                changed++;
            collider.enabled = false;
            EditorUtility.SetDirty(collider);
            EditorUtility.SetDirty(collider.gameObject);
        }

        return changed;
    }

    private static int DisableLegacyStairBlockers()
    {
        int changed = 0;
        Collider[] colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Collider collider in colliders)
        {
            if (!IsLegacyStairBlocker(collider))
                continue;

            Undo.RecordObject(collider, "Convert legacy stair blocker to trigger");
            collider.isTrigger = true;
            EditorUtility.SetDirty(collider);
            EditorUtility.SetDirty(collider.gameObject);
            changed++;
        }

        return changed;
    }

    private static bool IsLegacyStairBlocker(Collider collider)
    {
        if (collider == null || collider.isTrigger)
            return false;
        if (!collider.gameObject.name.StartsWith("Cube"))
            return false;

        Bounds bounds = collider.bounds;
        Vector2 center2 = new Vector2(bounds.center.x, bounds.center.z);
        Vector2 stair2 = new Vector2(-27.6f, -16.0f);
        return Vector2.Distance(center2, stair2) <= 2.25f &&
               bounds.center.y >= 0.35f &&
               bounds.center.y <= 3.0f &&
               bounds.size.y >= 1.0f &&
               bounds.size.x >= 1.5f &&
               bounds.size.z >= 1.5f;
    }

    private static bool RebuildExperimentNavMesh()
    {
        Unity.AI.Navigation.NavMeshSurface surface = UnityEngine.Object.FindFirstObjectByType<Unity.AI.Navigation.NavMeshSurface>();
        if (surface == null)
        {
            Debug.LogWarning("[ExperimentTools] NavMeshSurface not found; killer route rebuild skipped.");
            return false;
        }

        Undo.RecordObject(surface, "Configure Experiment NavMeshSurface");
        surface.collectObjects = Unity.AI.Navigation.CollectObjects.All;
        surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
        surface.layerMask = ~0;
        surface.ignoreNavMeshAgent = true;
        surface.ignoreNavMeshObstacle = true;
        EditorUtility.SetDirty(surface);

        surface.BuildNavMesh();
        return true;
    }

    private static void EnsureProceduralAmbience()
    {
        ProceduralHorrorAmbience ambience = UnityEngine.Object.FindFirstObjectByType<ProceduralHorrorAmbience>();
        if (ambience == null)
        {
            GameObject go = new GameObject("ExperimentProceduralHorrorAudio_Auto");
            Undo.RegisterCreatedObjectUndo(go, "Create ExperimentProceduralHorrorAudio_Auto");
            ambience = Undo.AddComponent<ProceduralHorrorAmbience>(go);
        }

        if (ambience.GetComponent<AudioSource>() == null)
            Undo.AddComponent<AudioSource>(ambience.gameObject);

        ambience.ConfigureForExperimentDefaults();
        EditorUtility.SetDirty(ambience);
        EditorUtility.SetDirty(ambience.gameObject);
    }

    private static void EnsureExternalHorrorAudio()
    {
        AmbientAudioManager audioManager = UnityEngine.Object.FindFirstObjectByType<AmbientAudioManager>();
        if (audioManager == null)
        {
            GameObject go = new GameObject("ExperimentExternalHorrorAudio_Auto");
            Undo.RegisterCreatedObjectUndo(go, "Create ExperimentExternalHorrorAudio_Auto");
            audioManager = Undo.AddComponent<AmbientAudioManager>(go);
        }

        AudioClip exploring = LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Music/Menu/MenuMusic01.wav");
        AudioClip ghostHint = LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Event/EventMusic01.wav");
        AudioClip chase = LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Music/Chase/Hunter01/Hunter01Chase01Loop.wav");
        AudioClip jumpScare = LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Music/Chase/Hunter01/Hunter01Chase03Loop.wav");
        AudioClip blackout = LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/StreetLight/StreetLightBreakSound.wav");
        AudioClip hit = LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Knife/KnifeCharacterHit.wav");
        AudioClip success = LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Event/MatchEndSound.wav");
        AudioClip failed = LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Vision/HuntersVisionSound.wav");
        AudioClip heartbeat = LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Survivor/Heartbeat.wav");

        AudioClip[] ambient =
        {
            LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Vision/VisionSound1.wav"),
            LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Vision/VisionSound2.wav"),
            LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Vision/VisionSound3.wav"),
            LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Light/LightTurnOffSound.wav"),
            LoadAudioClip("Assets/Horror Multiplayer Template by Redicion Studio/Audio/Light/LightTurnOnSound.wav"),
        };

        audioManager.SetExperimentClips(exploring, ghostHint, chase, jumpScare, ambient, blackout, hit, success, failed, heartbeat);
        EditorUtility.SetDirty(audioManager);
        EditorUtility.SetDirty(audioManager.gameObject);
    }

    private static AudioClip LoadAudioClip(string path)
    {
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
            Debug.LogWarning($"[ExperimentTools] Audio clip not found: {path}");
        return clip;
    }

    private static void CreateOrTuneInvisibleBox(string objectName, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        GameObject box = GameObject.Find(objectName);
        if (box == null)
        {
            box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = objectName;
            Undo.RegisterCreatedObjectUndo(box, $"Create {objectName}");
        }

        Undo.RecordObject(box.transform, $"Tune {objectName}");
        box.transform.SetPositionAndRotation(position, rotation);
        box.transform.localScale = scale;

        if (box.GetComponent<BoxCollider>() == null)
            Undo.AddComponent<BoxCollider>(box);

        MeshRenderer renderer = box.GetComponent<MeshRenderer>();
        if (renderer != null)
            UnityEngine.Object.DestroyImmediate(renderer);

        MeshFilter meshFilter = box.GetComponent<MeshFilter>();
        if (meshFilter != null)
            UnityEngine.Object.DestroyImmediate(meshFilter);

        EditorUtility.SetDirty(box);
    }

    private static void CreateOrTuneRampSegment(
        string objectName,
        Vector3 topStart,
        Vector3 topEnd,
        float width,
        float thickness,
        float overlap)
    {
        Vector3 run = topEnd - topStart;
        Vector3 flatRun = new Vector3(run.x, 0f, run.z);
        if (run.sqrMagnitude < 0.0001f || flatRun.sqrMagnitude < 0.0001f)
            return;

        Vector3 lengthAxis = run.normalized;
        Vector3 widthAxis = Vector3.Cross(Vector3.up, flatRun.normalized).normalized;
        Vector3 normal = Vector3.Cross(widthAxis, lengthAxis).normalized;
        Quaternion rotation = Quaternion.LookRotation(widthAxis, normal);
        Vector3 center = (topStart + topEnd) * 0.5f - normal * (thickness * 0.5f);
        Vector3 scale = new Vector3(run.magnitude + Mathf.Max(0f, overlap), thickness, width);

        CreateOrTuneInvisibleBox(objectName, center, rotation, scale);
    }

    private static void DisableStaleSecondFloorAccessRampSegments()
    {
        GameObject[] objects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject obj = objects[i];
            if (obj == null ||
                !obj.name.StartsWith("SecondFloorAccessRamp_") ||
                obj.name == "SecondFloorAccessRamp_Auto" ||
                obj.name == "SecondFloorAccessRamp_Landing_Auto")
            {
                continue;
            }

            Collider collider = obj.GetComponent<Collider>();
            if (collider != null && !collider.isTrigger)
            {
                Undo.RecordObject(collider, "Disable stale stair assist segment");
                collider.isTrigger = true;
                EditorUtility.SetDirty(collider);
            }
        }
    }

    private static void DisableAllSecondFloorAccessStepSegments()
    {
        GameObject[] objects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject obj = objects[i];
            if (obj == null || !obj.name.StartsWith("SecondFloorAccessStep_Auto_"))
                continue;

            Collider collider = obj.GetComponent<Collider>();
            if (collider != null && !collider.isTrigger)
            {
                Undo.RecordObject(collider, "Convert old stair step collider to trigger");
                collider.isTrigger = true;
                EditorUtility.SetDirty(collider);
            }
        }
    }

    private static void SetColliderTrigger(string objectName, bool isTrigger)
    {
        GameObject obj = GameObject.Find(objectName);
        Collider collider = obj != null ? obj.GetComponent<Collider>() : null;
        if (collider == null || collider.isTrigger == isTrigger)
            return;

        Undo.RecordObject(collider, $"Tune {objectName} trigger state");
        collider.isTrigger = isTrigger;
        EditorUtility.SetDirty(collider);
    }

    private static void EnsureProgressMarkers()
    {
        CreateProgressMarker(
            "ExperimentMarker_StairsReached_Auto",
            new Vector3(-27.7f, 1.0f, -17.8f),
            new Vector3(3.0f, 2.0f, 2.4f),
            ExperimentProgressMarker.MarkerType.StairsReached,
            "ghost_hint");

        CreateProgressMarker(
            "ExperimentMarker_SecondFloorCue_Auto",
            new Vector3(-24.6f, 3.05f, -15.5f),
            new Vector3(4.2f, 2.2f, 4.0f),
            ExperimentProgressMarker.MarkerType.KillerCueArea,
            "killer_near");

        CreateProgressMarker(
            "ExperimentMarker_ObjectiveArea_Auto",
            new Vector3(-19.2f, 1.4f, -17.6f),
            new Vector3(3.2f, 2.0f, 3.2f),
            ExperimentProgressMarker.MarkerType.ObjectiveAreaReached,
            null);
    }

    private static void CreateProgressMarker(
        string objectName,
        Vector3 position,
        Vector3 scale,
        ExperimentProgressMarker.MarkerType markerType,
        string eventId)
    {
        if (GameObject.Find(objectName) != null) return;

        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
        marker.name = objectName;
        Undo.RegisterCreatedObjectUndo(marker, $"Create {objectName}");
        marker.transform.position = position;
        marker.transform.localScale = scale;

        Collider collider = marker.GetComponent<Collider>();
        collider.isTrigger = true;

        MeshRenderer renderer = marker.GetComponent<MeshRenderer>();
        if (renderer != null)
            UnityEngine.Object.DestroyImmediate(renderer);

        MeshFilter meshFilter = marker.GetComponent<MeshFilter>();
        if (meshFilter != null)
            UnityEngine.Object.DestroyImmediate(meshFilter);

        ExperimentProgressMarker progressMarker = marker.AddComponent<ExperimentProgressMarker>();
        progressMarker.Configure(markerType, eventId);
        EditorUtility.SetDirty(progressMarker);
        EditorUtility.SetDirty(marker);
    }

    private static void EnsureObjectiveItem()
    {
        ObjectiveItem existing = UnityEngine.Object.FindFirstObjectByType<ObjectiveItem>();
        if (existing != null)
        {
            TuneObjective(existing.gameObject);
            return;
        }

        GameObject objective = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        objective.name = "SecondFloorObjective";
        Undo.RegisterCreatedObjectUndo(objective, "Create SecondFloorObjective");
        objective.transform.position = new Vector3(-24f, 0.5f, -13.6f);
        objective.transform.localScale = Vector3.one * 0.55f;

        Renderer renderer = objective.GetComponent<Renderer>();
        if (renderer != null)
            renderer.sharedMaterial = GetObjectiveMaterial();

        Light light = new GameObject("ObjectiveLight").AddComponent<Light>();
        Undo.RegisterCreatedObjectUndo(light.gameObject, "Create ObjectiveLight");
        light.transform.SetParent(objective.transform, false);
        light.type = LightType.Point;
        light.color = ObjectivePurple;
        light.intensity = 2.5f;
        light.range = 4f;

        ObjectiveItem objectiveItem = objective.AddComponent<ObjectiveItem>();
        TuneObjective(objective);
        EditorUtility.SetDirty(objectiveItem);
        EditorUtility.SetDirty(objective);
    }

    private static void TuneObjective(GameObject objective)
    {
        Undo.RecordObject(objective.transform, "Tune Objective Transform");
        objective.transform.position = new Vector3(-24f, 0.5f, -13.6f);
        if (objective.transform.localScale.x < 0.5f)
            objective.transform.localScale = Vector3.one * 0.55f;

        Renderer renderer = objective.GetComponent<Renderer>();
        if (renderer != null)
        {
            Undo.RecordObject(renderer, "Tune Objective Material");
            renderer.sharedMaterial = GetObjectiveMaterial();
            EditorUtility.SetDirty(renderer);
        }

        Collider collider = objective.GetComponent<Collider>();
        if (collider != null)
        {
            Undo.RecordObject(collider, "Tune Objective Collider");
            collider.isTrigger = true;

            if (collider is SphereCollider sphere)
                sphere.radius = Mathf.Max(sphere.radius, 1.6f);

            EditorUtility.SetDirty(collider);
        }

        if (objective.transform.Find("ObjectiveLight") == null)
        {
            Light light = new GameObject("ObjectiveLight").AddComponent<Light>();
            Undo.RegisterCreatedObjectUndo(light.gameObject, "Create ObjectiveLight");
            light.transform.SetParent(objective.transform, false);
            light.type = LightType.Point;
            light.color = ObjectivePurple;
            light.intensity = 2.5f;
            light.range = 4f;
            EditorUtility.SetDirty(light);
        }
        else
        {
            Transform lightTransform = objective.transform.Find("ObjectiveLight");
            Light light = lightTransform != null ? lightTransform.GetComponent<Light>() : null;
            if (light != null)
            {
                Undo.RecordObject(light, "Tune Objective Light");
                light.color = ObjectivePurple;
                light.intensity = 2.5f;
                light.range = 4f;
                EditorUtility.SetDirty(light);
            }
        }

        EditorUtility.SetDirty(objective);
    }

    private static void EnsureExperimentUi(ExperimentDirector director)
    {
        Canvas canvas = GetOrCreateHudCanvas();
        Transform root = canvas.transform;

        Text timer = GetOrCreateText(
            root,
            "ExperimentTimerText_Auto",
            new Vector2(0f, 1f),
            new Vector2(0f, 1f),
            new Vector2(24f, -24f),
            new Vector2(180f, 44f),
            28,
            TextAnchor.MiddleLeft,
            Color.white,
            "02:00");

        GameObject success = GetOrCreateResultPanel(root, "ExperimentSuccessPanel_Auto", "Mission Success", new Color(0.03f, 0.32f, 0.2f, 0.84f));
        GameObject failed = GetOrCreateResultPanel(root, "ExperimentFailedPanel_Auto", "Mission Failed", new Color(0.35f, 0.04f, 0.04f, 0.84f));
        Image damageVignette = GetOrCreateDamageVignette(root);

        success.SetActive(false);
        failed.SetActive(false);

        AudioSource source = director.GetComponent<AudioSource>();
        if (source == null)
            source = Undo.AddComponent<AudioSource>(director.gameObject);

        SetSerializedObjectRef(director, "timerText", timer);
        SetSerializedObjectRef(director, "successPanel", success);
        SetSerializedObjectRef(director, "failedPanel", failed);
        SetSerializedFloat(director, "resultRecoveryDelaySec", 2.5f);
        SetSerializedObjectRef(director, "audioSource", source);

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            PlayerHealth health = player.GetComponent<PlayerHealth>();
            if (health != null)
                SetSerializedObjectRef(health, "damageVignette", damageVignette);

            NonLethalHitFeedback feedback = player.GetComponent<NonLethalHitFeedback>();
            if (feedback != null)
                SetSerializedObjectRef(feedback, "damageVignette", damageVignette);
        }
    }

    private static Canvas GetOrCreateHudCanvas()
    {
        GameObject existing = FindGameObjectIncludingInactive("ExperimentHUD_Auto");
        if (existing != null && existing.TryGetComponent(out Canvas foundCanvas))
        {
            if (!existing.activeSelf)
                existing.SetActive(true);
            return foundCanvas;
        }

        GameObject go = new GameObject("ExperimentHUD_Auto", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(go, "Create ExperimentHUD_Auto");
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        EditorUtility.SetDirty(go);
        return canvas;
    }

    private static Text GetOrCreateText(
        Transform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        int fontSize,
        TextAnchor alignment,
        Color color,
        string initialText)
    {
        GameObject existing = FindGameObjectIncludingInactive(objectName);
        Text text = existing != null ? existing.GetComponent<Text>() : null;
        if (text == null)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            Undo.RegisterCreatedObjectUndo(go, $"Create {objectName}");
            go.transform.SetParent(parent, false);
            text = go.GetComponent<Text>();
        }
        else if (text.transform.parent != parent)
        {
            Undo.SetTransformParent(text.transform, parent, $"Parent {objectName}");
        }

        RectTransform rect = text.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = anchorMin;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.text = initialText;

        EditorUtility.SetDirty(text);
        return text;
    }

    private static GameObject GetOrCreateResultPanel(Transform parent, string objectName, string label, Color background)
    {
        GameObject panel = FindGameObjectIncludingInactive(objectName);
        if (panel == null)
        {
            panel = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panel, $"Create {objectName}");
            panel.transform.SetParent(parent, false);
        }
        else if (panel.transform.parent != parent)
        {
            Undo.SetTransformParent(panel.transform, parent, $"Parent {objectName}");
        }

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = panel.GetComponent<Image>();
        image.color = background;
        image.raycastTarget = false;

        GetOrCreateText(
            panel.transform,
            objectName + "_Text",
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(620f, 96f),
            44,
            TextAnchor.MiddleCenter,
            Color.white,
            label);

        EditorUtility.SetDirty(panel);
        return panel;
    }

    private static Image GetOrCreateDamageVignette(Transform parent)
    {
        GameObject existing = FindGameObjectIncludingInactive(ExperimentDirector.DamageVignetteName);
        Image image = existing != null ? existing.GetComponent<Image>() : null;
        if (image == null)
        {
            GameObject go = new GameObject(ExperimentDirector.DamageVignetteName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            Undo.RegisterCreatedObjectUndo(go, $"Create {ExperimentDirector.DamageVignetteName}");
            go.transform.SetParent(parent, false);
            image = go.GetComponent<Image>();
        }
        else if (image.transform.parent != parent)
        {
            Undo.SetTransformParent(image.transform, parent, $"Parent {ExperimentDirector.DamageVignetteName}");
        }

        RectTransform rect = image.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        image.color = new Color(0.85f, 0f, 0f, 0f);
        image.raycastTarget = false;
        image.transform.SetAsLastSibling();

        EditorUtility.SetDirty(image);
        return image;
    }

    private static GameObject FindGameObjectIncludingInactive(string objectName)
    {
        GameObject[] objects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i].name == objectName)
                return objects[i];
        }

        return null;
    }

    private static int RemoveDuplicateExperimentUiObjects()
    {
        GameObject[] objects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Dictionary<string, GameObject> keepers = new Dictionary<string, GameObject>();
        int removed = 0;

        for (int i = 0; i < objects.Length; i++)
        {
            GameObject current = objects[i];
            if (current == null || !ExperimentUiObjectNames.Contains(current.name))
                continue;

            if (!keepers.TryGetValue(current.name, out GameObject keeper) || ShouldPreferUiKeeper(current, keeper))
            {
                keepers[current.name] = current;
            }
        }

        for (int i = 0; i < objects.Length; i++)
        {
            GameObject current = objects[i];
            if (current == null || !ExperimentUiObjectNames.Contains(current.name))
                continue;

            if (!keepers.TryGetValue(current.name, out GameObject keeper) || current == keeper)
                continue;

            Undo.DestroyObjectImmediate(current);
            removed++;
        }

        return removed;
    }

    private static bool ShouldPreferUiKeeper(GameObject candidate, GameObject current)
    {
        if (candidate == null)
            return false;
        if (current == null)
            return true;

        bool candidateActive = candidate.activeInHierarchy;
        bool currentActive = current.activeInHierarchy;
        if (candidateActive != currentActive)
            return candidateActive;

        bool candidateUnderHud = IsUnderExperimentHud(candidate.transform);
        bool currentUnderHud = IsUnderExperimentHud(current.transform);
        return candidateUnderHud && !currentUnderHud;
    }

    private static bool IsUnderExperimentHud(Transform transform)
    {
        while (transform != null)
        {
            if (transform.name == "ExperimentHUD_Auto")
                return true;
            transform = transform.parent;
        }

        return false;
    }

    private static Font ResolveFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    private static Material GetObjectiveMaterial()
    {
        const string folder = "Assets/Materials";
        const string path = "Assets/Materials/ExperimentObjectiveGlow.mat";

        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
        {
            ConfigureObjectiveMaterial(existing);
            return existing;
        }

        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder("Assets", "Materials");

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");

        Material material = new Material(shader);
        material.name = "ExperimentObjectiveGlow";
        ConfigureObjectiveMaterial(material);
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        return material;
    }

    private static void ConfigureObjectiveMaterial(Material material)
    {
        material.SetColor("_Color", ObjectivePurple);
        material.SetColor("_BaseColor", ObjectivePurple);
        material.SetColor("_EmissionColor", ObjectivePurple * 2.8f);
        material.EnableKeyword("_EMISSION");
        EditorUtility.SetDirty(material);
    }

    private static void SetSerializedFloat(UnityEngine.Object target, string propertyName, float value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null) return;

        property.floatValue = value;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
    }

    private static void SetSerializedObjectRef(UnityEngine.Object target, string propertyName, UnityEngine.Object value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null) return;

        property.objectReferenceValue = value;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
    }

    private static void SetSerializedBool(UnityEngine.Object target, string propertyName, bool value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null) return;

        property.boolValue = value;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
    }

    private static void SetSerializedVector3(UnityEngine.Object target, string propertyName, Vector3 value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null) return;

        property.vector3Value = value;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
    }

    private static void SetSerializedString(UnityEngine.Object target, string propertyName, string value)
    {
        SerializedObject serializedObject = new SerializedObject(target);
        SerializedProperty property = serializedObject.FindProperty(propertyName);
        if (property == null) return;

        property.stringValue = value;
        serializedObject.ApplyModifiedProperties();
        EditorUtility.SetDirty(target);
    }
}
#endif
