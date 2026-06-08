using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
#endif

/// <summary>
/// 실험 필수 싱글턴이 씬에 없을 때 런타임에 자동 생성한다.
/// 최종 씬에는 명시적으로 배치해도 되며, 이 스크립트는 누락 방지용이다.
/// </summary>
public static class ExperimentBootstrapper
{
    private const string PrimaryHouseColliderName = "Old_House_windows_separated_Collider";
    private const string KillerPrefabPath = "Assets/Prefabs/Killer.prefab";
#if UNITY_EDITOR
    private const string StopPlayModeFlagPath = "Temp/stop_experiment_playmode.flag";
#endif
    private static readonly Vector3 StairAssistBaseTop = new Vector3(-29.15f, 0.22f, -18.75f);
    private static readonly Vector3 StairAssistLandingTop = new Vector3(-27.35f, 2.94f, -17.25f);
    private static readonly Vector3 StairAssistExitTop = new Vector3(-24.55f, 2.94f, -15.35f);
    private static readonly Vector3 KillerSpawnPosition = new Vector3(-31.2f, 0.15f, -21.2f);
    private static readonly Vector3 KillerLookTarget = new Vector3(-24.6f, 0.15f, -17.2f);
    private const int StairAssistStepCount = 8;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureExperimentRuntime()
    {
#if UNITY_EDITOR
        if (File.Exists(StopPlayModeFlagPath))
        {
            File.Delete(StopPlayModeFlagPath);
            Debug.Log("[ExperimentBootstrapper] Stop PlayMode flag consumed; exiting Play Mode.");
            EditorApplication.ExitPlaymode();
            return;
        }

        string absoluteStopFlagPath = Path.Combine(Directory.GetParent(Application.dataPath).FullName, StopPlayModeFlagPath);
        if (File.Exists(absoluteStopFlagPath))
        {
            File.Delete(absoluteStopFlagPath);
            Debug.Log("[ExperimentBootstrapper] Stop PlayMode flag consumed from project Temp; exiting Play Mode.");
            EditorApplication.ExitPlaymode();
            return;
        }
#endif

        EnsureComponent<GameManager>("GameManager");
        EnsureComponent<SmartThingsEventSender>("SmartThingsEventSender");

        if (Object.FindFirstObjectByType<ExperimentDirector>() == null)
        {
            GameObject go = new GameObject("ExperimentDirector");
            go.AddComponent<ExperimentLogger>();
            go.AddComponent<ExperimentDirector>();
        }

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
        DisableLegacyStairBlockers();
        EnsurePrimaryHouseColliderEnabled();
        EnsureProgressMarkers();
        EnsureObjectiveItem();
        EnsureProceduralAmbience();
        EnsureExternalHorrorAudio();
        EnsureRuntimeWatchdog();
        EnsurePlayModeSmokeRunner();
    }

    private static void EnsureComponent<T>(string objectName) where T : Component
    {
        if (Object.FindFirstObjectByType<T>() != null) return;

        GameObject go = new GameObject(objectName);
        go.AddComponent<T>();
    }

    private static void EnsureSecondFloorAccessRamp()
    {
        if (SceneManager.GetActiveScene().name != "MainScene") return;

        CreateOrTuneRampSegment(
            "SecondFloorAccessRamp_Auto",
            StairAssistBaseTop,
            StairAssistLandingTop,
            width: 4.8f,
            thickness: 0.2f,
            overlap: 0.9f);
        SetColliderTrigger("SecondFloorAccessRamp_Auto", false);

        CreateOrTuneStairAssistSteps();

        CreateOrTuneRampSegment(
            "SecondFloorAccessRamp_Landing_Auto",
            StairAssistLandingTop,
            StairAssistExitTop,
            width: 4.2f,
            thickness: 0.18f,
            overlap: 0.65f);
        SetColliderTrigger("SecondFloorAccessRamp_Landing_Auto", false);

        DisableStaleSecondFloorAccessRampSegments();
        DisableStaleSecondFloorAccessStepSegments();
    }

    private static void EnsureSecondFloorWalkableColliders()
    {
        if (SceneManager.GetActiveScene().name != "MainScene") return;

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
        if (SceneManager.GetActiveScene().name != "MainScene") return;

        CreateOrTuneInvisibleBox(
            "SecondFloorBoundaryWall_Auto_North",
            new Vector3(-23.75f, 3.95f, -12.25f),
            Quaternion.identity,
            new Vector3(5.2f, 2.0f, 0.22f));

        CreateOrTuneInvisibleBox(
            "SecondFloorBoundaryWall_Auto_South",
            new Vector3(-23.75f, 3.95f, -17.45f),
            Quaternion.identity,
            new Vector3(5.2f, 2.0f, 0.22f));

        CreateOrTuneInvisibleBox(
            "SecondFloorBoundaryWall_Auto_East",
            new Vector3(-21.15f, 3.95f, -14.85f),
            Quaternion.identity,
            new Vector3(0.22f, 2.0f, 5.2f));

        CreateOrTuneInvisibleBox(
            "SecondFloorBoundaryWall_Auto_West",
            new Vector3(-26.35f, 3.95f, -12.85f),
            Quaternion.identity,
            new Vector3(0.22f, 2.0f, 1.0f));
    }

    private static void EnsureOldHouseInteriorCollisionShell()
    {
        if (SceneManager.GetActiveScene().name != "MainScene") return;

        CreateOrTuneInvisibleBox(
            "OldHouseInteriorFirstFloor_Auto",
            new Vector3(-25.2f, 0.05f, -17.15f),
            Quaternion.identity,
            new Vector3(9.4f, 0.1f, 8.9f));

        CreateOrTuneInvisibleBox(
            "OldHouseInteriorNorthWall_Auto",
            new Vector3(-25.2f, 1.45f, -12.55f),
            Quaternion.identity,
            new Vector3(9.4f, 2.8f, 0.22f));

        CreateOrTuneInvisibleBox(
            "OldHouseInteriorWestWall_Auto",
            new Vector3(-30.0f, 1.45f, -16.85f),
            Quaternion.identity,
            new Vector3(0.22f, 2.8f, 8.15f));

        CreateOrTuneInvisibleBox(
            "OldHouseInteriorEastWall_Auto",
            new Vector3(-20.45f, 1.45f, -16.85f),
            Quaternion.identity,
            new Vector3(0.22f, 2.8f, 8.15f));

        CreateOrTuneInvisibleBox(
            "OldHouseInteriorSouthWall_Left_Auto",
            new Vector3(-28.45f, 1.45f, -21.55f),
            Quaternion.identity,
            new Vector3(3.0f, 2.8f, 0.22f));

        CreateOrTuneInvisibleBox(
            "OldHouseInteriorSouthWall_Right_Auto",
            new Vector3(-21.9f, 1.45f, -21.55f),
            Quaternion.identity,
            new Vector3(2.8f, 2.8f, 0.22f));
    }

    private static void EnsureDoorwayAccessAssist()
    {
        if (SceneManager.GetActiveScene().name != "MainScene") return;

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
        if (SceneManager.GetActiveScene().name != "MainScene") return;

        Transform door = GameObject.Find("DoorEntrance")?.transform;
        GameObject player = GameObject.FindGameObjectWithTag("Player");

        Vector3 position = door != null ? door.position : new Vector3(-24.286f, 1.47f, -21.119f);
        Quaternion rotation = door != null ? door.rotation : Quaternion.Euler(0f, 50f, 0f);
        float floorY = player != null ? player.transform.position.y : 0.2f;
        position.y = floorY + 0.12f;

        GameObject gate = GameObject.Find("DoorwayHouseCollisionGate_Auto");
        if (gate == null)
            gate = new GameObject("DoorwayHouseCollisionGate_Auto");

        gate.transform.SetPositionAndRotation(position, rotation);
        gate.transform.localScale = Vector3.one;

        DoorwayHouseCollisionGate collisionGate = gate.GetComponent<DoorwayHouseCollisionGate>();
        if (collisionGate == null)
            collisionGate = gate.AddComponent<DoorwayHouseCollisionGate>();

        collisionGate.Configure(PrimaryHouseColliderName, new Vector3(2.75f, 2.35f, 3.4f), new Vector3(0f, 1.05f, 0f));
    }

    private static void EnsureStairHouseCollisionGate()
    {
        if (SceneManager.GetActiveScene().name != "MainScene") return;

        GameObject gate = GameObject.Find("StairHouseCollisionGate_Auto");
        if (gate == null)
            gate = new GameObject("StairHouseCollisionGate_Auto");

        gate.transform.SetPositionAndRotation(
            new Vector3(-26.1f, 1.55f, -16.35f),
            Quaternion.identity);
        gate.transform.localScale = Vector3.one;

        DoorwayHouseCollisionGate collisionGate = gate.GetComponent<DoorwayHouseCollisionGate>();
        if (collisionGate == null)
            collisionGate = gate.AddComponent<DoorwayHouseCollisionGate>();

        collisionGate.Configure(PrimaryHouseColliderName, new Vector3(7.6f, 4.9f, 6.6f), new Vector3(0f, 1.25f, 0f));
    }

    private static void EnsureStairTraversalAssistZone()
    {
        if (SceneManager.GetActiveScene().name != "MainScene") return;

        GameObject zone = GameObject.Find("StairTraversalAssistZone_Auto");
        if (zone == null)
            zone = new GameObject("StairTraversalAssistZone_Auto");

        zone.transform.SetPositionAndRotation(
            new Vector3(-26.1f, 1.55f, -16.35f),
            Quaternion.identity);
        zone.transform.localScale = Vector3.one;

        StairTraversalAssistZone assist = zone.GetComponent<StairTraversalAssistZone>();
        if (assist == null)
            assist = zone.AddComponent<StairTraversalAssistZone>();

        assist.Configure(
            StairAssistBaseTop,
            StairAssistExitTop,
            new Vector3(7.6f, 4.9f, 6.6f),
            new Vector3(0f, 1.25f, 0f));
    }

    private static void CreateOrTuneInvisibleBox(string objectName, Vector3 position, Quaternion rotation, Vector3 scale)
    {
        GameObject box = GameObject.Find(objectName);
        if (box == null)
        {
            box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = objectName;
        }

        box.transform.SetPositionAndRotation(position, rotation);
        box.transform.localScale = scale;

        if (box.GetComponent<BoxCollider>() == null)
            box.AddComponent<BoxCollider>();

        MeshRenderer renderer = box.GetComponent<MeshRenderer>();
        if (renderer != null)
            Object.Destroy(renderer);

        MeshFilter meshFilter = box.GetComponent<MeshFilter>();
        if (meshFilter != null)
            Object.Destroy(meshFilter);
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

    private static void CreateOrTuneStairAssistSteps()
    {
        Vector3 flatRun = new Vector3(
            StairAssistLandingTop.x - StairAssistBaseTop.x,
            0f,
            StairAssistLandingTop.z - StairAssistBaseTop.z);
        if (flatRun.sqrMagnitude < 0.0001f)
            return;

        Vector3 widthAxis = Vector3.Cross(Vector3.up, flatRun.normalized).normalized;
        Quaternion rotation = Quaternion.LookRotation(widthAxis, Vector3.up);
        const float treadLength = 0.84f;
        const float treadWidth = 4.85f;
        const float treadThickness = 0.16f;

        for (int i = 0; i <= StairAssistStepCount; i++)
        {
            float t = i / (float)StairAssistStepCount;
            Vector3 surface = Vector3.Lerp(StairAssistBaseTop, StairAssistLandingTop, t);
            string objectName = $"SecondFloorAccessStep_Auto_{i:00}";
            CreateOrTuneInvisibleBox(
                objectName,
                surface - Vector3.up * (treadThickness * 0.5f),
                rotation,
                new Vector3(treadLength, treadThickness, treadWidth));
            SetColliderTrigger(objectName, true);
        }
    }

    private static void DisableStaleSecondFloorAccessRampSegments()
    {
        GameObject[] objects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
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
            if (collider != null)
                collider.isTrigger = true;
        }
    }

    private static void DisableStaleSecondFloorAccessStepSegments()
    {
        GameObject[] objects = Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject obj = objects[i];
            if (obj == null || !obj.name.StartsWith("SecondFloorAccessStep_Auto_"))
                continue;

            string suffix = obj.name.Substring("SecondFloorAccessStep_Auto_".Length);
            if (int.TryParse(suffix, out int index) && index >= 0 && index <= StairAssistStepCount)
                continue;

            Collider collider = obj.GetComponent<Collider>();
            if (collider != null)
                collider.isTrigger = true;
        }
    }

    private static void SetColliderTrigger(string objectName, bool isTrigger)
    {
        GameObject obj = GameObject.Find(objectName);
        Collider collider = obj != null ? obj.GetComponent<Collider>() : null;
        if (collider != null)
            collider.isTrigger = isTrigger;
    }

    private static void DisableLegacyStairBlockers()
    {
        if (SceneManager.GetActiveScene().name != "MainScene") return;

        Collider[] colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int changed = 0;
        foreach (Collider collider in colliders)
        {
            if (!IsLegacyStairBlocker(collider))
                continue;

            collider.isTrigger = true;
            changed++;
        }

        if (changed > 0)
            Debug.Log($"[ExperimentBootstrapper] Converted {changed} legacy stair blocker collider(s) to triggers.");
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

    private static void EnsurePrimaryHouseColliderEnabled()
    {
        Collider[] colliders = Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int restored = 0;

        foreach (Collider collider in colliders)
        {
            if (collider == null || collider.gameObject.name != PrimaryHouseColliderName)
                continue;

            if (!collider.gameObject.activeSelf)
                collider.gameObject.SetActive(true);
            if (!collider.enabled)
                restored++;
            collider.enabled = true;
            collider.isTrigger = false;
        }

        if (restored > 0)
            Debug.Log($"[ExperimentBootstrapper] Restored {restored} primary house collider(s).");
    }

    private static void EnsureProgressMarkers()
    {
        if (SceneManager.GetActiveScene().name != "MainScene") return;

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
            new Vector3(-23.9f, 3.55f, -15.2f),
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
        marker.transform.position = position;
        marker.transform.localScale = scale;

        Collider collider = marker.GetComponent<Collider>();
        collider.isTrigger = true;

        Object.Destroy(marker.GetComponent<MeshRenderer>());
        Object.Destroy(marker.GetComponent<MeshFilter>());

        ExperimentProgressMarker progressMarker = marker.AddComponent<ExperimentProgressMarker>();
        progressMarker.Configure(markerType, eventId);
    }

    private static void EnsurePlayerExperimentSetup()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        if (player.transform.localScale != Vector3.one)
            player.transform.localScale = Vector3.one;

        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
        {
            controller.height = 1.55f;
            controller.radius = 0.22f;
            controller.center = new Vector3(0f, 0.775f, 0f);
            controller.stepOffset = 0.6f;
            controller.slopeLimit = Mathf.Max(controller.slopeLimit, 60f);
            controller.minMoveDistance = 0f;
        }

        if (player.GetComponent<NonLethalHitFeedback>() == null)
            player.AddComponent<NonLethalHitFeedback>();

        FirstPersonController firstPerson = player.GetComponent<FirstPersonController>();
        if (firstPerson != null)
            firstPerson.ConfigureForExperimentDefaults();

        if (player.GetComponent<AudioSource>() == null)
            player.AddComponent<AudioSource>();

        LanternController lantern = player.GetComponent<LanternController>();
        if (lantern == null)
            lantern = player.AddComponent<LanternController>();

        lantern.ConfigureForExperimentDefaults();
        EnsureAudioListener(player);
    }

    private static void EnsureKillerExperimentSetup()
    {
        KillerAI killer = EnsureKillerRuntimeInstance();
        if (killer == null) return;

        UnityEngine.AI.NavMeshAgent agent = killer.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null)
        {
            agent.radius = 0.4f;
            agent.height = 2f;
            agent.speed = 1.75f;
            agent.acceleration = 6f;
            agent.angularSpeed = 300f;
            agent.stoppingDistance = 1.0f;
            agent.baseOffset = 0f;
            agent.autoBraking = true;
            agent.autoRepath = true;
        }

        killer.ConfigureForExperimentDefaults();

        KillerPlayerCollisionBypass collisionBypass = killer.GetComponent<KillerPlayerCollisionBypass>();
        if (collisionBypass == null)
            collisionBypass = killer.gameObject.AddComponent<KillerPlayerCollisionBypass>();
        collisionBypass.Configure("Player");

        Animator animator = killer.GetComponentInChildren<Animator>(true);
        if (animator != null && animator.GetComponent<FootstepAnimationEventReceiver>() == null)
            animator.gameObject.AddComponent<FootstepAnimationEventReceiver>();
    }

    private static KillerAI EnsureKillerRuntimeInstance()
    {
        KillerAI killer = null;
        KillerAI[] killers = Object.FindObjectsByType<KillerAI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < killers.Length; i++)
        {
            if (killers[i] != null)
            {
                killer = killers[i];
                break;
            }
        }

        if (killer == null)
        {
            GameObject killerObject = null;
#if UNITY_EDITOR
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KillerPrefabPath);
            if (prefab != null)
                killerObject = Object.Instantiate(prefab);
#endif
            if (killerObject == null)
            {
                killerObject = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                killerObject.transform.localScale = new Vector3(0.9f, 1.15f, 0.9f);
            }

            killerObject.name = "KILLER";
            if (killerObject.GetComponent<UnityEngine.AI.NavMeshAgent>() == null)
                killerObject.AddComponent<UnityEngine.AI.NavMeshAgent>();
            killer = killerObject.GetComponent<KillerAI>();
            if (killer == null)
                killer = killerObject.AddComponent<KillerAI>();
        }

        ActivateTransformChain(killer.transform);
        PlaceKillerAtExperimentSpawn(killer, !IsKillerInExperimentArea(killer.transform.position));
        EnsureKillerHasVisibleRenderer(killer);
        killer.gameObject.name = "KILLER";
        return killer;
    }

    private static void ActivateTransformChain(Transform transform)
    {
        for (Transform current = transform; current != null; current = current.parent)
        {
            if (!current.gameObject.activeSelf)
                current.gameObject.SetActive(true);
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

        killer.transform.SetPositionAndRotation(target, rotation);
        killer.transform.localScale = Vector3.one;
    }

    private static void EnsureKillerHasVisibleRenderer(KillerAI killer)
    {
        Renderer[] renderers = killer.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                return;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null)
                continue;

            ActivateTransformChain(renderer.transform);
            renderer.enabled = true;
            return;
        }
    }

    private static void EnsureAudioListener(GameObject player)
    {
        if (Object.FindFirstObjectByType<AudioListener>() != null) return;

        Camera camera = player.GetComponentInChildren<Camera>(true);
        GameObject target = camera != null ? camera.gameObject : player;
        target.AddComponent<AudioListener>();
    }

    private static void EnsureObjectiveItem()
    {
        if (SceneManager.GetActiveScene().name != "MainScene") return;
        ObjectiveItem existing = Object.FindFirstObjectByType<ObjectiveItem>();
        if (existing != null)
        {
            TuneObjective(existing.gameObject);
            return;
        }

        GameObject objective = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        objective.name = "SecondFloorObjective_Auto";
        objective.transform.position = new Vector3(-23.9f, 3.55f, -15.2f);
        objective.transform.localScale = Vector3.one * 0.55f;

        Renderer renderer = objective.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
                shader = Shader.Find("Standard");

            if (shader != null)
            {
                Material material = new Material(shader);
                material.name = "SecondFloorObjectiveGlow";
                material.SetColor("_Color", new Color(0.2f, 0.95f, 1f, 1f));
                material.SetColor("_BaseColor", new Color(0.2f, 0.95f, 1f, 1f));
                material.SetColor("_EmissionColor", new Color(0.2f, 0.95f, 1f, 1f) * 2.5f);
                material.EnableKeyword("_EMISSION");
                renderer.material = material;
            }
        }

        Light light = new GameObject("ObjectiveLight").AddComponent<Light>();
        light.transform.SetParent(objective.transform, false);
        light.type = LightType.Point;
        light.color = new Color(0.2f, 0.95f, 1f, 1f);
        light.intensity = 2.5f;
        light.range = 4f;

        objective.AddComponent<ObjectiveItem>();
        TuneObjective(objective);
        Debug.Log("[ExperimentBootstrapper] SecondFloorObjective_Auto created for mission success.");
    }

    private static void EnsureRuntimeWatchdog()
    {
        if (Object.FindFirstObjectByType<ExperimentRuntimeWatchdog>() != null) return;

        ExperimentDirector director = Object.FindFirstObjectByType<ExperimentDirector>();
        GameObject target = director != null ? director.gameObject : new GameObject("ExperimentRuntimeWatchdog");
        target.AddComponent<ExperimentRuntimeWatchdog>();
    }

    private static void EnsurePlayModeSmokeRunner()
    {
        if (Object.FindFirstObjectByType<ExperimentPlayModeSmokeRunner>() != null) return;

        ExperimentDirector director = Object.FindFirstObjectByType<ExperimentDirector>();
        GameObject target = director != null ? director.gameObject : new GameObject("ExperimentPlayModeSmokeRunner");
        target.AddComponent<ExperimentPlayModeSmokeRunner>();
    }

    private static void EnsureProceduralAmbience()
    {
        ProceduralHorrorAmbience ambience = Object.FindFirstObjectByType<ProceduralHorrorAmbience>();
        if (ambience == null)
        {
            GameObject go = new GameObject("ExperimentProceduralHorrorAudio_Auto");
            ambience = go.AddComponent<ProceduralHorrorAmbience>();
        }

        ambience.ConfigureForExperimentDefaults();
    }

    private static void EnsureExternalHorrorAudio()
    {
        AmbientAudioManager audioManager = Object.FindFirstObjectByType<AmbientAudioManager>();
        if (audioManager == null)
        {
            GameObject go = new GameObject("ExperimentExternalHorrorAudio_Auto");
            audioManager = go.AddComponent<AmbientAudioManager>();
        }

        audioManager.ConfigureForExperimentDefaults();
    }

    private static void TuneObjective(GameObject objective)
    {
        if (objective.transform.localScale.x < 0.5f)
            objective.transform.localScale = Vector3.one * 0.55f;

        Collider collider = objective.GetComponent<Collider>();
        if (collider != null)
            collider.isTrigger = true;

        if (collider is SphereCollider sphere)
            sphere.radius = Mathf.Max(sphere.radius, 1.6f);

        if (objective.transform.Find("ObjectiveLight") == null)
        {
            Light light = new GameObject("ObjectiveLight").AddComponent<Light>();
            light.transform.SetParent(objective.transform, false);
            light.type = LightType.Point;
            light.color = new Color(0.2f, 0.95f, 1f, 1f);
            light.intensity = 2.5f;
            light.range = 4f;
        }
    }
}
