#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

public static class ExperimentSubmissionQaTools
{
    private const string AutoRunFlagPath = "Temp/run_experiment_submission_qa.flag";
    private const string ReportPath = "Temp/experiment_submission_qa.json";
    private const string MainScenePath = "Assets/Scenes/MainScene.unity";
    private const string PrimaryHouseColliderName = "Old_House_windows_separated_Collider";
    private static readonly float[] FallbackAngles = { 180f, 135f, -135f, 90f, -90f, 45f, -45f, 0f };
    private static readonly Vector3[] InteriorRouteSamples =
    {
        new Vector3(-24.65f, 0.22f, -20.35f),
        new Vector3(-27.70f, 0.22f, -19.30f),
        new Vector3(-27.30f, 0.22f, -15.15f),
        new Vector3(-24.20f, 0.22f, -13.85f),
        new Vector3(-21.95f, 0.22f, -16.45f),
        new Vector3(-24.65f, 0.22f, -20.35f),
    };

    [MenuItem("Tools/Experiment/Run Submission QA")]
    public static void RunSubmissionQaFromMenu()
    {
        RunSubmissionQa(prepareScene: true);
    }

    public static void RunSubmissionQa(bool prepareScene)
    {
        Directory.CreateDirectory("Temp");

        QaReport report = new QaReport
        {
            timestampUtc = DateTime.UtcNow.ToString("O"),
            sceneName = SceneManager.GetActiveScene().name,
            scenePath = SceneManager.GetActiveScene().path,
        };

        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            report.errors.Add("Editor is in or entering Play Mode; run QA in Edit Mode.");
            WriteReport(report);
            return;
        }

        try
        {
            if (prepareScene)
                ExperimentSceneTools.PrepareActiveScene();

            InspectScene(report);
            WriteReport(report);
            Debug.Log($"[ExperimentQA] errors={report.errors.Count}, warnings={report.warnings.Count}, report={ReportPath}");
        }
        catch (Exception ex)
        {
            report.errors.Add($"QA exception: {ex.GetType().Name}: {ex.Message}");
            WriteReport(report);
            Debug.LogError($"[ExperimentQA] Failed: {ex}");
        }
    }

    private static void InspectScene(QaReport report)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.path != MainScenePath)
            report.warnings.Add($"Active scene is {scene.path}; expected {MainScenePath}.");

        CheckBuildSettings(report);
        CheckMissingScripts(report);
        CheckSingletons(report);
        CheckExperimentDirector(report);
        CheckRuntimeWatchdog(report);
        CheckProceduralAmbience(report);
        CheckExternalHorrorAudio(report);
        CheckPlayer(report);
        CheckObjective(report);
        CheckProgressAndTraversalHelpers(report);
        CheckKiller(report);
        CheckRouteFeasibility(report);
        CheckSmartThingsServer(report);

        report.info.Add($"Root objects: {scene.rootCount}");
        report.info.Add($"Scene dirty: {scene.isDirty}");
    }

    private static void CheckBuildSettings(QaReport report)
    {
        bool foundMainScene = false;
        foreach (EditorBuildSettingsScene buildScene in EditorBuildSettings.scenes)
        {
            if (buildScene.path == MainScenePath && buildScene.enabled)
            {
                foundMainScene = true;
                break;
            }
        }

        if (!foundMainScene)
            report.errors.Add("MainScene is not enabled in Build Settings.");
    }

    private static void CheckMissingScripts(QaReport report)
    {
        int missing = 0;
        foreach (GameObject go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            missing += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(go);

        if (missing > 0)
            report.errors.Add($"Missing Script components found: {missing}");
    }

    private static void CheckSingletons(QaReport report)
    {
        RequireObject<GameManager>(report, "GameManager");
        RequireObject<SmartThingsEventSender>(report, "SmartThingsEventSender");
        RequireObject<ExperimentDirector>(report, "ExperimentDirector");
        RequireObject<ExperimentLogger>(report, "ExperimentLogger");
    }

    private static void CheckExperimentDirector(QaReport report)
    {
        ExperimentDirector director = UnityEngine.Object.FindFirstObjectByType<ExperimentDirector>();
        if (director == null) return;

        SerializedObject so = new SerializedObject(director);
        RequireObjectRef(report, so, "timerText", "ExperimentDirector.timerText");
        RequireObjectRef(report, so, "objectiveText", "ExperimentDirector.objectiveText");
        RequireObjectRef(report, so, "successPanel", "ExperimentDirector.successPanel");
        RequireObjectRef(report, so, "failedPanel", "ExperimentDirector.failedPanel");

        float duration = so.FindProperty("sessionDurationSec")?.floatValue ?? 0f;
        if (Mathf.Abs(duration - 120f) > 0.01f)
            report.errors.Add($"ExperimentDirector.sessionDurationSec is {duration}, expected 120.");

        SerializedProperty timedScenario = so.FindProperty("enableTimedScenario");
        if (timedScenario == null || !timedScenario.boolValue)
            report.errors.Add("ExperimentDirector timed 2-minute scenario is disabled.");

        float scenarioScale = so.FindProperty("scenarioTimeScale")?.floatValue ?? 0f;
        if (scenarioScale <= 0f)
            report.errors.Add("ExperimentDirector.scenarioTimeScale must be greater than 0.");
        else if (Mathf.Abs(scenarioScale - 1f) > 0.01f)
            report.warnings.Add($"ExperimentDirector.scenarioTimeScale is {scenarioScale:0.##}; submission play should use 1.");

        string objectiveLabel = so.FindProperty("objectiveLabel")?.stringValue ?? string.Empty;
        if (string.IsNullOrWhiteSpace(objectiveLabel))
            report.errors.Add("ExperimentDirector.objectiveLabel is empty.");
        else
            report.info.Add($"Objective label: {objectiveLabel}");
    }

    private static void CheckPlayer(QaReport report)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            report.errors.Add("No GameObject tagged Player.");
            return;
        }

        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller == null)
        {
            report.errors.Add("Player has no CharacterController.");
        }
        else
        {
            if (controller.height > 1.6f)
                report.warnings.Add($"Player CharacterController height is {controller.height:0.00}; doorway-safe target is <= 1.60.");
            if (controller.radius > 0.24f)
                report.warnings.Add($"Player CharacterController radius is {controller.radius:0.00}; doorway-safe target is <= 0.24.");
            if (controller.stepOffset < 0.45f)
                report.warnings.Add($"Player stepOffset is {controller.stepOffset:0.00}; small thresholds may catch.");
            CheckInteriorCapsuleClearance(report, player, controller);
            CheckStairRouteCapsuleClearance(report, player, controller);
        }

        RequireComponent<FirstPersonController>(report, player, "Player FirstPersonController");
        RequireComponent<PlayerHealth>(report, player, "Player PlayerHealth");
        RequireComponent<NonLethalHitFeedback>(report, player, "Player NonLethalHitFeedback");

        LanternController lantern = player.GetComponent<LanternController>();
        if (lantern == null)
            report.errors.Add("Player LanternController missing.");
        else if (!lantern.HasUsableLight)
            report.errors.Add("Player lantern does not have a usable forward Spot Light.");

        if (UnityEngine.Object.FindFirstObjectByType<AudioListener>() == null)
            report.errors.Add("No AudioListener in scene.");

        Camera camera = player.GetComponentInChildren<Camera>(true);
        if (camera == null)
            report.warnings.Add("Player has no child Camera; first-person view may rely on another scene camera.");
    }

    private static void CheckRuntimeWatchdog(QaReport report)
    {
        if (UnityEngine.Object.FindFirstObjectByType<ExperimentRuntimeWatchdog>() == null)
            report.warnings.Add("ExperimentRuntimeWatchdog not found; Play Mode runtime QA warnings will be unavailable.");

        if (UnityEngine.Object.FindFirstObjectByType<ExperimentPlayModeSmokeRunner>() == null)
            report.warnings.Add("ExperimentPlayModeSmokeRunner not found; menu-driven Play Mode smoke reports will be unavailable.");
    }

    private static void CheckProceduralAmbience(QaReport report)
    {
        ProceduralHorrorAmbience ambience = UnityEngine.Object.FindFirstObjectByType<ProceduralHorrorAmbience>();
        if (ambience == null)
        {
            report.errors.Add("ProceduralHorrorAmbience not found; horror BGM/ambience is missing.");
            return;
        }

        if (!ambience.HasUsableOutput)
            report.errors.Add("ProceduralHorrorAmbience is not configured with usable output.");
        else
            report.info.Add("Procedural horror ambience is configured.");
    }

    private static void CheckExternalHorrorAudio(QaReport report)
    {
        AmbientAudioManager audioManager = UnityEngine.Object.FindFirstObjectByType<AmbientAudioManager>();
        if (audioManager == null)
        {
            report.errors.Add("AmbientAudioManager not found; external horror BGM/SFX layer is missing.");
            return;
        }

        if (!audioManager.HasUsableExternalBgm)
            report.errors.Add("AmbientAudioManager does not have usable external exploration/chase BGM clips.");
        else
            report.info.Add("External horror BGM/SFX layer is configured.");
    }

    private static void CheckObjective(QaReport report)
    {
        ObjectiveItem objective = UnityEngine.Object.FindFirstObjectByType<ObjectiveItem>();
        if (objective == null)
        {
            report.errors.Add("No ObjectiveItem in scene.");
            return;
        }

        Collider collider = objective.GetComponent<Collider>();
        if (collider == null)
            report.errors.Add("ObjectiveItem has no Collider.");
        else if (!collider.isTrigger)
            report.errors.Add("ObjectiveItem Collider is not a trigger.");

        if (objective.transform.position.y < 2f)
            report.warnings.Add($"Objective y position is {objective.transform.position.y:0.00}; plan expects 2F objective.");

        if (objective.GetComponentInChildren<Light>(true) == null)
            report.warnings.Add("ObjectiveItem has no child Light; visibility may be weak.");
    }

    private static void CheckProgressAndTraversalHelpers(QaReport report)
    {
        RequireNamedObject(report, "SecondFloorAccessRamp_Auto", warningOnly: false);
        RequireNamedObject(report, "DoorEntranceThresholdBridge_Auto", warningOnly: false);
        RequireNamedObject(report, "DoorEntranceRampOutside_Auto", warningOnly: false);
        RequireNamedObject(report, "DoorEntranceRampInside_Auto", warningOnly: false);
        RequireNamedObject(report, "DoorwayHouseCollisionGate_Auto", warningOnly: false);
        RequireNamedObject(report, "StairHouseCollisionGate_Auto", warningOnly: false);
        RequireNamedObject(report, "StairTraversalAssistZone_Auto", warningOnly: false);
        RequireNamedObject(report, "SecondFloorWalkableFloor_Auto", warningOnly: false);
        RequireNamedObject(report, "SecondFloorStairBridge_Auto", warningOnly: false);
        RequireNamedObject(report, "SecondFloorStairLanding_Auto", warningOnly: false);
        RequireNamedObject(report, "SecondFloorBoundaryWall_Auto_North", warningOnly: false);
        RequireNamedObject(report, "SecondFloorBoundaryWall_Auto_South", warningOnly: false);
        RequireNamedObject(report, "SecondFloorBoundaryWall_Auto_East", warningOnly: false);
        RequireNamedObject(report, "SecondFloorBoundaryWall_Auto_West", warningOnly: false);
        RequireNamedObject(report, "OldHouseInteriorFirstFloor_Auto", warningOnly: false);
        RequireNamedObject(report, "OldHouseInteriorNorthWall_Auto", warningOnly: false);
        RequireNamedObject(report, "OldHouseInteriorSouthWall_Left_Auto", warningOnly: false);
        RequireNamedObject(report, "OldHouseInteriorSouthWall_Right_Auto", warningOnly: false);
        RequireNamedObject(report, "ExperimentMarker_StairsReached_Auto", warningOnly: false);
        RequireNamedObject(report, "ExperimentMarker_SecondFloorCue_Auto", warningOnly: false);
        RequireNamedObject(report, "ExperimentMarker_ObjectiveArea_Auto", warningOnly: false);

        CheckDoorwayCollisionGate(report);
        CheckStairHouseCollisionGate(report);
        CheckPrimaryHouseCollider(report);
        CheckStairTransitionColliders(report);
        CheckNoLegacyStairBlockers(report);
        CheckSecondFloorSupport(report);
    }

    private static void CheckDoorwayCollisionGate(QaReport report)
    {
        GameObject gate = GameObject.Find("DoorwayHouseCollisionGate_Auto");
        if (gate == null)
            return;

        DoorwayHouseCollisionGate collisionGate = gate.GetComponent<DoorwayHouseCollisionGate>();
        if (collisionGate == null)
        {
            report.errors.Add("DoorwayHouseCollisionGate_Auto has no DoorwayHouseCollisionGate component.");
            return;
        }

        BoxCollider box = gate.GetComponent<BoxCollider>();
        if (box == null || !box.isTrigger)
            report.errors.Add("DoorwayHouseCollisionGate_Auto must have a trigger BoxCollider.");
        if (collisionGate.HouseColliderName != PrimaryHouseColliderName)
            report.errors.Add($"DoorwayHouseCollisionGate_Auto targets {collisionGate.HouseColliderName}, expected {PrimaryHouseColliderName}.");
    }

    private static void CheckStairHouseCollisionGate(QaReport report)
    {
        GameObject gate = GameObject.Find("StairHouseCollisionGate_Auto");
        if (gate == null)
            return;

        DoorwayHouseCollisionGate collisionGate = gate.GetComponent<DoorwayHouseCollisionGate>();
        if (collisionGate == null)
        {
            report.errors.Add("StairHouseCollisionGate_Auto has no DoorwayHouseCollisionGate component.");
            return;
        }

        BoxCollider box = gate.GetComponent<BoxCollider>();
        if (box == null || !box.isTrigger)
            report.errors.Add("StairHouseCollisionGate_Auto must have a trigger BoxCollider.");
        if (collisionGate.HouseColliderName != PrimaryHouseColliderName)
            report.errors.Add($"StairHouseCollisionGate_Auto targets {collisionGate.HouseColliderName}, expected {PrimaryHouseColliderName}.");
        if (collisionGate.GateSize.x < 7.0f || collisionGate.GateSize.y < 4.5f || collisionGate.GateSize.z < 6.0f)
            report.errors.Add($"StairHouseCollisionGate_Auto is too small for the stair/2F transition route: size={collisionGate.GateSize}.");
    }

    private static void CheckPrimaryHouseCollider(QaReport report)
    {
        Collider[] colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Collider activeSolidCollider = null;
        Collider nonblockingCollider = null;
        int matchingCount = 0;

        foreach (Collider collider in colliders)
        {
            if (collider == null || collider.gameObject.name != PrimaryHouseColliderName)
                continue;

            matchingCount++;
            if (collider.gameObject.activeInHierarchy && collider.enabled && !collider.isTrigger)
                activeSolidCollider = collider;
            else
                nonblockingCollider = collider;
        }

        if (matchingCount == 0)
        {
            report.errors.Add($"{PrimaryHouseColliderName} not found; broad old-house mesh collider state cannot be audited.");
            return;
        }

        if (activeSolidCollider != null)
        {
            report.errors.Add($"{PrimaryHouseColliderName} is still solid; it can block 1F interior movement and the stair route. It must be disabled or trigger while OldHouseInterior*_Auto colliders provide walls/floors.");
            return;
        }

        if (nonblockingCollider == null)
        {
            report.errors.Add($"{PrimaryHouseColliderName} exists but no nonblocking collider instance could be confirmed.");
            return;
        }

        Bounds bounds = nonblockingCollider.bounds;
        report.info.Add($"Broad house mesh collider nonblocking: {nonblockingCollider.GetType().Name}, enabled={nonblockingCollider.enabled}, trigger={nonblockingCollider.isTrigger}, bounds={bounds.size.x:0.0}x{bounds.size.y:0.0}x{bounds.size.z:0.0}.");
    }

    private static void CheckSecondFloorSupport(QaReport report)
    {
        Vector3[] probePositions =
        {
            new Vector3(-24.6f, 4.4f, -15.5f),
            new Vector3(-23.9f, 4.6f, -15.2f),
            new Vector3(-24.55f, 4.4f, -15.35f),
            new Vector3(-26.55f, 4.4f, -16.65f),
        };

        for (int i = 0; i < probePositions.Length; i++)
        {
            if (!Physics.Raycast(probePositions[i], Vector3.down, out RaycastHit hit, 2.4f, ~0, QueryTriggerInteraction.Ignore))
            {
                report.errors.Add($"Second floor support raycast missed at probe {i}.");
                continue;
            }

            if (hit.collider == null || hit.collider.isTrigger)
            {
                report.errors.Add($"Second floor support probe {i} hit no solid collider.");
                continue;
            }

            if (hit.point.y < 2.7f)
                report.errors.Add($"Second floor support probe {i} hit too low at y={hit.point.y:0.00} ({hit.collider.name}).");
        }

        report.info.Add("Second floor support raycasts found solid walkable colliders.");
    }

    private static void CheckNoLegacyStairBlockers(QaReport report)
    {
        Collider[] colliders = UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (!IsLegacyStairBlocker(collider))
                continue;

            report.errors.Add($"Legacy stair blocker remains solid: {DescribeCollider(collider)}.");
            return;
        }

        report.info.Add("Legacy generic Cube stair blockers are disabled or converted to triggers.");
    }

    private static bool IsLegacyStairBlocker(Collider collider)
    {
        if (collider == null || collider.isTrigger)
            return false;
        if (!collider.gameObject.name.StartsWith("Cube", StringComparison.Ordinal))
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

    private static void CheckStairTransitionColliders(QaReport report)
    {
        RequireSolidCollider(report, "SecondFloorAccessRamp_Auto");
        RequireSolidCollider(report, "SecondFloorAccessRamp_Landing_Auto");
        RequireNoSolidStairStepColliders(report);
        RequireTriggerCollider(report, "SecondFloorStairBridge_Auto");
        RequireTriggerCollider(report, "SecondFloorStairLanding_Auto");
    }

    private static void RequireNoSolidStairStepColliders(QaReport report)
    {
        int found = 0;
        GameObject[] objects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            GameObject obj = objects[i];
            if (obj == null || !obj.name.StartsWith("SecondFloorAccessStep_Auto_", StringComparison.Ordinal))
                continue;

            Collider collider = obj != null ? obj.GetComponent<Collider>() : null;
            if (collider == null || !collider.enabled)
                continue;

            found++;
            if (!collider.isTrigger)
                report.errors.Add($"{obj.name} must be nonblocking. The stair route uses the narrow solid SecondFloorAccessRamp_Auto collider so old step boxes cannot block the 1F interior.");
        }

        if (found > 0)
            report.info.Add($"Legacy stair step colliders are nonblocking triggers: {found}.");
    }

    private static void RequireSolidCollider(QaReport report, string objectName)
    {
        GameObject obj = GameObject.Find(objectName);
        Collider collider = obj != null ? obj.GetComponent<Collider>() : null;
        if (collider == null)
        {
            report.errors.Add($"{objectName} has no Collider.");
            return;
        }

        if (collider.isTrigger)
            report.errors.Add($"{objectName} must be solid so the player physically walks up the visible stair route instead of passing through it.");
    }

    private static void RequireTriggerCollider(QaReport report, string objectName)
    {
        GameObject obj = GameObject.Find(objectName);
        Collider collider = obj != null ? obj.GetComponent<Collider>() : null;
        if (collider == null)
        {
            report.errors.Add($"{objectName} has no Collider.");
            return;
        }

        if (!collider.isTrigger)
            report.errors.Add($"{objectName} must be a trigger so the stair route cannot become an invisible blocking floor.");
    }

    private static void CheckStairRouteCapsuleClearance(QaReport report, GameObject player, CharacterController controller)
    {
        float radius = controller.radius;
        float height = Mathf.Max(controller.height, radius * 2f + 0.1f);
        Vector3[] samples =
        {
            new Vector3(-28.95f, 0.45f, -18.55f),
            new Vector3(-28.35f, 1.05f, -17.70f),
            new Vector3(-27.85f, 1.55f, -16.75f),
            new Vector3(-27.35f, 2.25f, -16.25f),
            new Vector3(-27.35f, 2.94f, -17.25f),
            new Vector3(-24.55f, 2.94f, -15.35f),
        };

        for (int i = 0; i < samples.Length; i++)
        {
            Vector3 bottom = samples[i] + Vector3.up * (radius + 0.04f);
            Vector3 top = samples[i] + Vector3.up * (height - radius);
            Collider[] hits = Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);

            for (int h = 0; h < hits.Length; h++)
            {
                Collider hit = hits[h];
                if (hit == null || IsAllowedStairRouteProbeHit(hit, player, bottom, radius))
                    continue;

                report.errors.Add($"Stair route capsule probe blocked at sample {i} near {samples[i].x:0.0},{samples[i].y:0.0},{samples[i].z:0.0} by {DescribeCollider(hit)}.");
                return;
            }
        }

        report.info.Add("Stair route capsule probe clear for Player dimensions.");
    }

    private static void CheckInteriorCapsuleClearance(QaReport report, GameObject player, CharacterController controller)
    {
        float radius = controller.radius;
        float height = Mathf.Max(controller.height, radius * 2f + 0.1f);

        for (int i = 0; i < InteriorRouteSamples.Length; i++)
        {
            Vector3 sample = InteriorRouteSamples[i];
            Vector3 bottom = sample + Vector3.up * (radius + 0.04f);
            Vector3 top = sample + Vector3.up * (height - radius);
            Collider[] hits = Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);

            for (int h = 0; h < hits.Length; h++)
            {
                Collider hit = hits[h];
                if (hit == null || IsAllowedInteriorProbeHit(hit, player, bottom, radius))
                    continue;

                report.errors.Add($"1F interior capsule probe blocked at sample {i} near {sample.x:0.0},{sample.y:0.0},{sample.z:0.0} by {DescribeCollider(hit)}.");
                return;
            }
        }

        report.info.Add("1F interior capsule probe clear for Player dimensions.");
    }

    private static bool IsAllowedInteriorProbeHit(Collider hit, GameObject player, Vector3 bottom, float radius)
    {
        if (hit.isTrigger)
            return true;
        if (hit.transform == player.transform || hit.transform.IsChildOf(player.transform))
            return true;
        if (hit.bounds.max.y < bottom.y - radius * 0.35f)
            return true;

        string objectName = hit.gameObject.name;
        return objectName.StartsWith("DoorEntrance") ||
               objectName.StartsWith("DoorwayHouseCollisionGate") ||
               objectName.StartsWith("StairHouseCollisionGate") ||
               objectName.StartsWith("StairTraversalAssistZone") ||
               objectName.StartsWith("ExperimentMarker");
    }

    private static bool IsAllowedStairRouteProbeHit(Collider hit, GameObject player, Vector3 bottom, float radius)
    {
        if (hit.isTrigger)
            return true;
        if (hit.transform == player.transform || hit.transform.IsChildOf(player.transform))
            return true;
        if (hit.bounds.max.y < bottom.y - radius * 0.35f)
            return true;

        KillerAI killer = UnityEngine.Object.FindFirstObjectByType<KillerAI>();
        if (killer != null && hit.transform.IsChildOf(killer.transform))
            return killer.GetComponent<KillerPlayerCollisionBypass>() != null;

        string objectName = hit.gameObject.name;
        if (objectName == PrimaryHouseColliderName)
            return false;

        return objectName.StartsWith("SecondFloorAccessRamp") ||
               objectName.StartsWith("SecondFloorAccessStep") ||
               objectName.StartsWith("SecondFloorWalkableFloor") ||
               objectName.StartsWith("SecondFloorStairBridge") ||
               objectName.StartsWith("SecondFloorStairLanding") ||
               objectName.StartsWith("ExperimentMarker") ||
               objectName.StartsWith("SecondFloorObjective");
    }

    private static bool IsConfiguredHouseCollisionGate(string gateName)
    {
        GameObject gate = GameObject.Find(gateName);
        if (gate == null)
            return false;

        DoorwayHouseCollisionGate collisionGate = gate.GetComponent<DoorwayHouseCollisionGate>();
        BoxCollider box = gate.GetComponent<BoxCollider>();
        return collisionGate != null &&
               collisionGate.HouseColliderName == PrimaryHouseColliderName &&
               box != null &&
               box.isTrigger;
    }

    private static string DescribeCollider(Collider collider)
    {
        if (collider == null)
            return "<null>";

        Bounds bounds = collider.bounds;
        return $"{GetTransformPath(collider.transform)} [{collider.GetType().Name}, trigger={collider.isTrigger}, bounds={bounds.size.x:0.00}x{bounds.size.y:0.00}x{bounds.size.z:0.00}, center={FormatVector(bounds.center)}]";
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "<null>";

        string path = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            path = transform.name + "/" + path;
        }

        return path;
    }

    private static void CheckKiller(QaReport report)
    {
        KillerAI killer = UnityEngine.Object.FindFirstObjectByType<KillerAI>();
        if (killer == null)
        {
            report.errors.Add("No KillerAI in scene.");
            return;
        }

        RequireComponent<UnityEngine.AI.NavMeshAgent>(report, killer.gameObject, "Killer NavMeshAgent");
        RequireComponent<KillerPlayerCollisionBypass>(report, killer.gameObject, "KillerPlayerCollisionBypass");
        if (!killer.AvoidsStairRouteDuringChase)
            report.errors.Add("Killer stair/2F safety hold is disabled.");
        if (killer.WalkSpeed > 1.2f)
            report.errors.Add($"Killer walk speed is too fast for slow horror pacing: {killer.WalkSpeed:0.00}m/s.");
        if (killer.ChaseSpeed > 1.9f)
            report.errors.Add($"Killer chase speed is too fast for 60% player-walk pacing: {killer.ChaseSpeed:0.00}m/s.");
        UnityEngine.AI.NavMeshAgent agent = killer.GetComponent<UnityEngine.AI.NavMeshAgent>();
        if (agent != null && agent.speed > 1.9f)
            report.errors.Add($"Killer NavMeshAgent speed is too fast for 60% player-walk pacing: {agent.speed:0.00}m/s.");
        if (!HasVisibleRenderer(killer))
            report.errors.Add("KillerAI has no active enabled Renderer; KILLER will be invisible in Play Mode.");
        if (killer.GetComponentInChildren<Animator>(true) == null)
            report.warnings.Add("KillerAI has no Animator in children.");

        if (UnityEngine.Object.FindFirstObjectByType<Unity.AI.Navigation.NavMeshSurface>() == null)
            report.warnings.Add("No NavMeshSurface found; killer route must be verified manually.");
    }

    private static bool HasVisibleRenderer(KillerAI killer)
    {
        if (killer == null || !killer.gameObject.activeInHierarchy)
            return false;

        Renderer[] renderers = killer.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer != null && renderer.enabled && renderer.gameObject.activeInHierarchy)
                return true;
        }

        return false;
    }

    private static void CheckRouteFeasibility(QaReport report)
    {
        KillerAI killer = UnityEngine.Object.FindFirstObjectByType<KillerAI>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        ObjectiveItem objective = UnityEngine.Object.FindFirstObjectByType<ObjectiveItem>();
        if (killer == null || player == null || objective == null)
            return;

        bool killerOnMesh = TrySampleNavMesh(killer.transform.position, 4f, out NavMeshHit killerHit);
        bool playerNearMesh = TrySampleNavMesh(player.transform.position, 6f, out NavMeshHit playerHit);
        bool objectiveNearMesh = TrySampleNavMesh(objective.transform.position, 8f, out NavMeshHit objectiveHit);

        if (!killerOnMesh)
        {
            report.warnings.Add("Killer is not near baked NavMesh; chase route cannot be proven.");
            return;
        }

        if (!playerNearMesh)
            report.warnings.Add("Player start is not near baked NavMesh; killer initial route to player cannot be proven.");
        else
            CheckNavMeshPath(report, killerHit.position, playerHit.position, "killer -> player start");

        if (!objectiveNearMesh)
            report.warnings.Add("Objective is not near baked NavMesh; killer route to 2F objective area cannot be proven.");
        else
            CheckNavMeshPath(
                report,
                killerHit.position,
                objectiveHit.position,
                "killer -> objective area",
                warnOnIncomplete: !killer.AvoidsStairRouteDuringChase);
    }

    private static bool TrySampleNavMesh(Vector3 position, float maxDistance, out NavMeshHit hit)
    {
        return NavMesh.SamplePosition(position, out hit, maxDistance, NavMesh.AllAreas);
    }

    private static void CheckNavMeshPath(QaReport report, Vector3 from, Vector3 to, string label, bool warnOnIncomplete = true)
    {
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(from, to, NavMesh.AllAreas, path))
        {
            if (warnOnIncomplete)
                report.warnings.Add($"NavMesh path check failed: {label}.");
            else
                report.info.Add($"NavMesh path intentionally optional: {label}. KILLER stair safety hold keeps it off the player's stair/2F route.");
            return;
        }

        if (path.status != NavMeshPathStatus.PathComplete)
        {
            if (TryFindFallbackChaseStart(to, out Vector3 fallbackStart, out float fallbackLength))
            {
                report.info.Add($"NavMesh fallback chase start OK: {label}, start={FormatVector(fallbackStart)}, length={fallbackLength:0.0}m");
                return;
            }

            if (warnOnIncomplete)
                report.warnings.Add($"NavMesh path is {path.status}: {label}.");
            else
                report.info.Add($"NavMesh path intentionally optional ({path.status}): {label}. KILLER stair safety hold keeps it off the player's stair/2F route.");
            return;
        }

        report.info.Add($"NavMesh path OK: {label}, length={EstimatePathLength(path):0.0}m");
    }

    private static bool TryFindFallbackChaseStart(Vector3 target, out Vector3 fallbackStart, out float pathLength)
    {
        fallbackStart = Vector3.zero;
        pathLength = 0f;

        Vector3 forward = Vector3.forward;
        float[] distances = { 5f, 7f, 9f, 11f };
        for (int d = 0; d < distances.Length; d++)
        {
            for (int i = 0; i < FallbackAngles.Length; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(FallbackAngles[i], Vector3.up) * forward;
                Vector3 desired = target + direction.normalized * distances[d];
                if (!NavMesh.SamplePosition(desired, out NavMeshHit candidate, 3f, NavMesh.AllAreas))
                    continue;

                if (Vector3.Distance(candidate.position, target) < 4f)
                    continue;

                if (!NavMesh.SamplePosition(target, out NavMeshHit targetHit, 2.5f, NavMesh.AllAreas))
                    continue;

                NavMeshPath fallbackPath = new NavMeshPath();
                if (!NavMesh.CalculatePath(candidate.position, targetHit.position, NavMesh.AllAreas, fallbackPath) ||
                    fallbackPath.status != NavMeshPathStatus.PathComplete)
                    continue;

                fallbackStart = candidate.position;
                pathLength = EstimatePathLength(fallbackPath);
                return true;
            }
        }

        return false;
    }

    private static string FormatVector(Vector3 value)
    {
        return $"{value.x:0.0},{value.y:0.0},{value.z:0.0}";
    }

    private static float EstimatePathLength(NavMeshPath path)
    {
        if (path.corners == null || path.corners.Length < 2)
            return 0f;

        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);

        return length;
    }

    private static void CheckSmartThingsServer(QaReport report)
    {
        try
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:3000/health");
            request.Timeout = 2000;
            using WebResponse response = request.GetResponse();
            using Stream stream = response.GetResponseStream();
            using StreamReader reader = new StreamReader(stream);
            string body = reader.ReadToEnd();
            report.info.Add($"SmartThings server health: {body}");
            if (body.Contains("\"simulation\":true"))
                report.warnings.Add("SmartThings server is in simulation mode; set .env for real devices.");
        }
        catch (Exception ex)
        {
            report.warnings.Add($"SmartThings server health unavailable: {ex.Message}");
        }
    }

    private static void RequireObject<T>(QaReport report, string label) where T : UnityEngine.Object
    {
        if (UnityEngine.Object.FindFirstObjectByType<T>() == null)
            report.errors.Add($"{label} not found.");
    }

    private static void RequireComponent<T>(QaReport report, GameObject go, string label) where T : Component
    {
        if (go.GetComponent<T>() == null)
            report.errors.Add($"{label} missing.");
    }

    private static void RequireNamedObject(QaReport report, string objectName, bool warningOnly)
    {
        if (GameObject.Find(objectName) != null) return;

        if (warningOnly)
            report.warnings.Add($"{objectName} not found.");
        else
            report.errors.Add($"{objectName} not found.");
    }

    private static void RequireObjectRef(QaReport report, SerializedObject so, string propertyName, string label)
    {
        SerializedProperty property = so.FindProperty(propertyName);
        if (property == null || property.objectReferenceValue == null)
            report.errors.Add($"{label} is unassigned.");
    }

    private static void WriteReport(QaReport report)
    {
        report.errorCount = report.errors.Count;
        report.warningCount = report.warnings.Count;
        File.WriteAllText(ReportPath, report.ToJson());
        AssetDatabase.Refresh();
    }

    private sealed class QaReport
    {
        public string timestampUtc;
        public string sceneName;
        public string scenePath;
        public int errorCount;
        public int warningCount;
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public readonly List<string> info = new List<string>();

        public string ToJson()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            AppendProperty(sb, "timestampUtc", timestampUtc, comma: true);
            AppendProperty(sb, "sceneName", sceneName, comma: true);
            AppendProperty(sb, "scenePath", scenePath, comma: true);
            sb.AppendLine($"  \"errorCount\": {errorCount},");
            sb.AppendLine($"  \"warningCount\": {warningCount},");
            AppendArray(sb, "errors", errors, comma: true);
            AppendArray(sb, "warnings", warnings, comma: true);
            AppendArray(sb, "info", info, comma: false);
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendProperty(StringBuilder sb, string name, string value, bool comma)
        {
            sb.Append("  \"").Append(Escape(name)).Append("\": \"").Append(Escape(value)).Append("\"");
            sb.AppendLine(comma ? "," : string.Empty);
        }

        private static void AppendArray(StringBuilder sb, string name, List<string> values, bool comma)
        {
            sb.Append("  \"").Append(Escape(name)).AppendLine("\": [");
            for (int i = 0; i < values.Count; i++)
            {
                sb.Append("    \"").Append(Escape(values[i])).Append("\"");
                sb.AppendLine(i < values.Count - 1 ? "," : string.Empty);
            }
            sb.Append("  ]");
            sb.AppendLine(comma ? "," : string.Empty);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
        }
    }
}

[InitializeOnLoad]
public static class ExperimentSubmissionQaAutoRun
{
    private const string QaFlagPath = "Temp/run_experiment_submission_qa.flag";
    private const string SmokeFlagPath = "Temp/run_experiment_playmode_smoke.flag";
    private const string IotSmokeFlagPath = "Temp/run_experiment_iot_smoke.flag";
    private const string StopPlayModeFlagPath = "Temp/stop_experiment_playmode.flag";
    private const string MainScenePath = "Assets/Scenes/MainScene.unity";
    private const string ConditionPrefsKey = "ExperimentCondition";
    private const string PreviousConditionHadKey = "ExperimentSmokePreviousConditionHadValue";
    private const string PreviousConditionValueKey = "ExperimentSmokePreviousConditionValue";
    private const string ScenarioScalePrefsKey = "ExperimentScenarioTimeScale";
    private const string PreviousScenarioScaleHadKey = "ExperimentSmokePreviousScenarioScaleHadValue";
    private const string PreviousScenarioScaleValueKey = "ExperimentSmokePreviousScenarioScaleValue";
    private const float SmokeScenarioTimeScale = 120f;

    private static double nextFlagCheckTime;

    static ExperimentSubmissionQaAutoRun()
    {
        EditorApplication.update += PollRunFlag;
        EditorApplication.projectChanged += RunIfRequested;
        EditorApplication.delayCall += RunIfRequested;
    }

    [UnityEditor.Callbacks.DidReloadScripts]
    private static void OnScriptsReloaded()
    {
        EditorApplication.delayCall += RunIfRequested;
    }

    private static void PollRunFlag()
    {
        if (EditorApplication.timeSinceStartup < nextFlagCheckTime) return;

        nextFlagCheckTime = EditorApplication.timeSinceStartup + 1.0;
        RunIfRequested();
    }

    private static void RunIfRequested()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        StopPlayModeIfRequested();
        RunSubmissionQaIfRequested();
        RunPlayModeSmokeIfRequested();
    }

    private static void StopPlayModeIfRequested()
    {
        if (!File.Exists(StopPlayModeFlagPath)) return;

        TryDeleteFlag(StopPlayModeFlagPath, "ExperimentStopPlayMode");
        if (!EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.Log("[ExperimentSmoke] Stop PlayMode flag consumed while Editor was already in Edit Mode.");
            return;
        }

        Debug.Log("[ExperimentSmoke] Stop PlayMode flag consumed; exiting Play Mode.");
        EditorApplication.ExitPlaymode();
    }

    private static void RunSubmissionQaIfRequested()
    {
        if (!File.Exists(QaFlagPath)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        TryDeleteFlag(QaFlagPath, "ExperimentQA");
        ExperimentSubmissionQaTools.RunSubmissionQa(prepareScene: true);
    }

    private static void RunPlayModeSmokeIfRequested()
    {
        string flagPath;
        string condition;
        if (File.Exists(SmokeFlagPath))
        {
            flagPath = SmokeFlagPath;
            condition = "GameOnly";
        }
        else if (File.Exists(IotSmokeFlagPath))
        {
            flagPath = IotSmokeFlagPath;
            condition = "GameWithIoT";
        }
        else
        {
            return;
        }

        if (EditorApplication.isPlayingOrWillChangePlaymode) return;

        TryDeleteFlag(flagPath, "ExperimentSmoke");
        if (SceneManager.GetActiveScene().path != MainScenePath)
            EditorSceneManager.OpenScene(MainScenePath);

        EditorSceneManager.SaveOpenScenes();
        StoreConditionOverride();
        PlayerPrefs.SetString(ConditionPrefsKey, condition);
        PlayerPrefs.SetFloat(ScenarioScalePrefsKey, SmokeScenarioTimeScale);
        PlayerPrefs.SetInt(ExperimentPlayModeSmokeRunner.PlayerPrefsKey, 1);
        PlayerPrefs.Save();

        Debug.Log($"[ExperimentSmoke] Flag consumed; entering Play Mode smoke test in {condition} condition.");
        EditorApplication.isPlaying = true;
    }

    private static void TryDeleteFlag(string path, string label)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[{label}] Could not delete auto-run flag: {ex.Message}");
        }
    }

    private static void StoreConditionOverride()
    {
        if (PlayerPrefs.HasKey(ConditionPrefsKey))
        {
            PlayerPrefs.SetInt(PreviousConditionHadKey, 1);
            PlayerPrefs.SetString(PreviousConditionValueKey, PlayerPrefs.GetString(ConditionPrefsKey));
        }
        else
        {
            PlayerPrefs.SetInt(PreviousConditionHadKey, 0);
            PlayerPrefs.DeleteKey(PreviousConditionValueKey);
        }

        if (PlayerPrefs.HasKey(ScenarioScalePrefsKey))
        {
            PlayerPrefs.SetInt(PreviousScenarioScaleHadKey, 1);
            PlayerPrefs.SetFloat(PreviousScenarioScaleValueKey, PlayerPrefs.GetFloat(ScenarioScalePrefsKey));
        }
        else
        {
            PlayerPrefs.SetInt(PreviousScenarioScaleHadKey, 0);
            PlayerPrefs.DeleteKey(PreviousScenarioScaleValueKey);
        }
    }
}
#endif
