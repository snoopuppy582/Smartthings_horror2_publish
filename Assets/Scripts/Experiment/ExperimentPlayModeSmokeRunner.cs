using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

/// <summary>
/// Optional Play Mode smoke test for the 120-second experiment loop.
/// It only runs when armed through PlayerPrefs/menu/command line.
/// </summary>
public class ExperimentPlayModeSmokeRunner : MonoBehaviour
{
    public const string PlayerPrefsKey = "ExperimentRunPlayModeSmoke";

    private const string ConditionPrefsKey = "ExperimentCondition";
    private const string PreviousConditionHadKey = "ExperimentSmokePreviousConditionHadValue";
    private const string PreviousConditionValueKey = "ExperimentSmokePreviousConditionValue";
    private const string ScenarioScalePrefsKey = "ExperimentScenarioTimeScale";
    private const string PreviousScenarioScaleHadKey = "ExperimentSmokePreviousScenarioScaleHadValue";
    private const string PreviousScenarioScaleValueKey = "ExperimentSmokePreviousScenarioScaleValue";
    private const string EditorReportPath = "Temp/experiment_playmode_smoke.json";
    private const string EditorIotReportPath = "Temp/experiment_playmode_iot_smoke.json";
    private const string MainScenePath = "Assets/Scenes/MainScene.unity";
    private const string PrimaryHouseColliderName = "Old_House_windows_separated_Collider";
    private const float SmokeScenarioTimeScale = 120f;
    private const float SmokeScenarioWaitSec = 1.15f;
    private static readonly float[] FallbackAngles = { 180f, 135f, -135f, 90f, -90f, 45f, -45f, 0f };
    private static readonly Vector3[] StairTraversalRoute =
    {
        new Vector3(-29.15f, 0.22f, -18.75f),
        new Vector3(-27.35f, 2.94f, -17.25f),
        new Vector3(-24.55f, 2.94f, -15.35f),
    };

    [SerializeField] private float initialDelaySec = 0.75f;
    [SerializeField] private float maxWaitForSessionSec = 5f;

    private bool _running;

    private void Start()
    {
        if (!ShouldRunSmoke())
            return;

        StartCoroutine(RunSmoke());
    }

    private static bool ShouldRunSmoke()
    {
        if (PlayerPrefs.GetInt(PlayerPrefsKey, 0) == 1)
            return true;

        string[] args = Environment.GetCommandLineArgs();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "-experimentSmokeTest", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private IEnumerator RunSmoke()
    {
        if (_running)
            yield break;

        _running = true;
        PlayerPrefs.DeleteKey(PlayerPrefsKey);
        PlayerPrefs.Save();

        SmokeReport report = new SmokeReport
        {
            timestampUtc = DateTime.UtcNow.ToString("O"),
            sceneName = SceneManager.GetActiveScene().name,
            scenePath = SceneManager.GetActiveScene().path,
            reportPath = ResolveReportPath(),
        };

        yield return new WaitForSeconds(initialDelaySec);

        ExperimentDirector director = ExperimentDirector.Instance ?? FindFirstObjectByType<ExperimentDirector>();
        float deadline = Time.realtimeSinceStartup + maxWaitForSessionSec;
        while ((director == null || !director.IsRunning) && Time.realtimeSinceStartup < deadline)
        {
            director = ExperimentDirector.Instance ?? FindFirstObjectByType<ExperimentDirector>();
            yield return null;
        }

        if (director == null)
        {
            report.errors.Add("ExperimentDirector missing in Play Mode.");
            FinishReport(report);
            yield break;
        }

        report.sessionId = director.SessionId;
        report.condition = director.ConditionName;
        report.reportPath = ResolveReportPath(report.condition);
        report.info.Add($"Director running: {director.IsRunning}");

        CheckRuntimeObjects(report);
        CheckKillerRoute(report);

        ExperimentLogger logger = FindFirstObjectByType<ExperimentLogger>();
        if (logger != null)
            report.logPath = logger.CurrentLogPath;

        if (!director.IsRunning)
        {
            report.errors.Add("ExperimentDirector did not enter running state.");
            FinishReport(report);
            yield break;
        }

        string failureLogPath = logger != null ? logger.CurrentLogPath : null;
        director.FailSession("timeout");
        yield return new WaitForSeconds(0.25f);
        CheckFailureState(report);
        CheckFailureLogFile(report, failureLogPath);
        if (!director.IsRunning)
            report.info.Add("Timeout failure path ended session and showed result UI.");

        if (string.Equals(report.condition, "GameWithIoT", StringComparison.Ordinal))
            yield return ResetIotServerBetweenSmokeSessions(report);

        director.BeginSession();
        yield return new WaitForSeconds(SmokeScenarioWaitSec);
        report.sessionId = director.SessionId;
        if (logger != null)
            report.logPath = logger.CurrentLogPath;

        if (!director.IsRunning)
        {
            report.errors.Add("ExperimentDirector did not restart after timeout failure smoke check.");
            FinishReport(report);
            yield break;
        }

        if (!director.KillerCueTriggered)
            report.errors.Add("Timed scenario did not trigger a killer/chase cue.");
        CheckAmbienceScenarioReaction(report);
        CheckExternalAudioScenarioReaction(report);

        int hitCountBefore = director.HitCount;
        director.ReportPlayerHit();
        yield return new WaitForSeconds(0.25f);
        if (director.HitCount <= hitCountBefore)
            report.errors.Add("ReportPlayerHit did not increment hit count.");

        yield return CollectObjectiveThroughObjectiveItem(report, director);
        yield return new WaitForSeconds(0.75f);

        CheckEndState(report);
        CheckLogFile(report);
        FinishReport(report);
    }

    private static void CheckRuntimeObjects(SmokeReport report)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            report.errors.Add("No Player-tagged object in Play Mode.");
        }
        else
        {
            CharacterController controller = player.GetComponent<CharacterController>();
            if (controller == null)
            {
                report.errors.Add("Player has no CharacterController.");
            }
            else
            {
                if (controller.height > 1.6f)
                    report.warnings.Add($"Player capsule is tall for doorways: height={controller.height:0.00}.");
                if (controller.radius > 0.24f)
                    report.warnings.Add($"Player capsule radius may catch on doorways: radius={controller.radius:0.00}.");
                if (controller.stepOffset < 0.45f)
                    report.warnings.Add($"Player stepOffset may catch on thresholds: stepOffset={controller.stepOffset:0.00}.");
                CheckDoorwayCapsuleClearance(report, player, controller);
                CheckStairRouteCapsuleClearance(report, player, controller);
                CheckStairRouteCharacterControllerTraversal(report, player, controller);
            }

            if (player.GetComponent<FirstPersonController>() == null)
                report.errors.Add("Player has no FirstPersonController.");
            if (player.GetComponent<NonLethalHitFeedback>() == null)
                report.errors.Add("Player has no NonLethalHitFeedback.");

            LanternController lantern = player.GetComponent<LanternController>();
            if (lantern == null)
            {
                report.errors.Add("Player has no LanternController.");
            }
            else if (!lantern.HasUsableLight)
            {
                report.errors.Add("Player lantern has no usable forward Spot Light.");
            }
            else
            {
                report.info.Add("Player lantern is present with a usable forward Spot Light.");
            }
        }

        if (FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            report.errors.Add("No AudioListener in Play Mode.");

        ProceduralHorrorAmbience ambience = FindFirstObjectByType<ProceduralHorrorAmbience>();
        if (ambience == null)
            report.errors.Add("ProceduralHorrorAmbience missing in Play Mode.");
        else if (!ambience.HasUsableOutput)
            report.errors.Add("ProceduralHorrorAmbience is not configured with usable output.");
        else if (!ambience.IsPlaying)
            report.errors.Add("ProceduralHorrorAmbience is configured but not playing.");
        else if (!ambience.UsesProceduralClip)
            report.errors.Add($"ProceduralHorrorAmbience is not using its generated clip. clip={ambience.RuntimeClipName}");
        else
            report.info.Add($"Procedural horror ambience is playing. clip={ambience.RuntimeClipName}, tension={ambience.CurrentTension:0.00}.");

        AmbientAudioManager audioManager = FindFirstObjectByType<AmbientAudioManager>();
        if (audioManager == null)
            report.errors.Add("AmbientAudioManager missing in Play Mode.");
        else if (!audioManager.HasUsableExternalBgm)
            report.errors.Add("AmbientAudioManager has no usable external BGM clips.");
        else if (!audioManager.IsBgmPlaying)
            report.errors.Add("AmbientAudioManager external BGM is configured but not playing.");
        else
            report.info.Add($"External horror BGM is playing. clip={audioManager.ActiveBgmName}.");

        ObjectiveItem objective = FindFirstObjectByType<ObjectiveItem>();
        if (objective == null)
        {
            report.errors.Add("No ObjectiveItem in Play Mode.");
        }
        else
        {
            Collider collider = objective.GetComponent<Collider>();
            if (collider == null || !collider.isTrigger)
                report.errors.Add("ObjectiveItem collider is missing or not a trigger.");
            if (objective.transform.position.y < 2f)
                report.warnings.Add($"Objective appears below 2F: y={objective.transform.position.y:0.00}.");
            if (objective.GetComponentInChildren<Light>(true) == null)
                report.warnings.Add("Objective has no child Light; visibility may be weak.");
        }

        RequireNamedObject(report, "SecondFloorAccessRamp_Auto", false);
        RequireNamedObject(report, "DoorEntranceThresholdBridge_Auto", false);
        RequireNamedObject(report, "DoorEntranceRampOutside_Auto", false);
        RequireNamedObject(report, "DoorEntranceRampInside_Auto", false);
        RequireNamedObject(report, "DoorwayHouseCollisionGate_Auto", false);
        RequireNamedObject(report, "StairHouseCollisionGate_Auto", false);
        RequireNamedObject(report, "StairTraversalAssistZone_Auto", false);
        RequireNamedObject(report, "SecondFloorWalkableFloor_Auto", false);
        RequireNamedObject(report, "SecondFloorStairLanding_Auto", false);
        RequireNamedObject(report, "SecondFloorBoundaryWall_Auto_North", false);
        RequireNamedObject(report, "SecondFloorBoundaryWall_Auto_South", false);
        RequireNamedObject(report, "SecondFloorBoundaryWall_Auto_East", false);
        RequireNamedObject(report, "SecondFloorBoundaryWall_Auto_West", false);
        RequireNamedObject(report, "ExperimentMarker_StairsReached_Auto", false);
        RequireNamedObject(report, "ExperimentMarker_SecondFloorCue_Auto", false);
        RequireNamedObject(report, "ExperimentMarker_ObjectiveArea_Auto", false);
        RequireNamedObject(report, "ExperimentHUD_Auto", false);

        CheckDoorwayCollisionGate(report);
        CheckStairHouseCollisionGate(report);
        CheckPrimaryHouseCollider(report);
        CheckSecondFloorSupport(report);
    }

    private static void CheckDoorwayCollisionGate(SmokeReport report)
    {
        GameObject gate = FindObjectByName("DoorwayHouseCollisionGate_Auto");
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

    private static void CheckStairHouseCollisionGate(SmokeReport report)
    {
        GameObject gate = FindObjectByName("StairHouseCollisionGate_Auto");
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
            report.errors.Add($"StairHouseCollisionGate_Auto is too small for the stair capsule route: size={collisionGate.GateSize}.");
    }

    private static void CheckPrimaryHouseCollider(SmokeReport report)
    {
        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Collider activeSolidCollider = null;
        int matchingCount = 0;

        foreach (Collider collider in colliders)
        {
            if (collider == null || collider.gameObject.name != PrimaryHouseColliderName)
                continue;

            matchingCount++;
            if (collider.gameObject.activeInHierarchy && collider.enabled && !collider.isTrigger)
                activeSolidCollider = collider;
        }

        if (matchingCount == 0)
        {
            report.errors.Add($"{PrimaryHouseColliderName} missing in Play Mode; primary old-house wall/floor collision is absent.");
            return;
        }

        if (activeSolidCollider == null)
        {
            report.errors.Add($"{PrimaryHouseColliderName} exists but is not an active enabled solid collider in Play Mode.");
            return;
        }

        Bounds bounds = activeSolidCollider.bounds;
        report.info.Add($"Primary house collider enabled: {activeSolidCollider.GetType().Name}, bounds={bounds.size.x:0.0}x{bounds.size.y:0.0}x{bounds.size.z:0.0}.");
    }

    private static void CheckSecondFloorSupport(SmokeReport report)
    {
        Vector3[] probePositions =
        {
            new Vector3(-24.6f, 4.4f, -15.5f),
            new Vector3(-23.9f, 4.6f, -15.2f),
            new Vector3(-27.35f, 4.2f, -17.25f),
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

    private static void CheckAmbienceScenarioReaction(SmokeReport report)
    {
        ProceduralHorrorAmbience ambience = FindFirstObjectByType<ProceduralHorrorAmbience>();
        if (ambience == null)
            return;

        if (!ambience.IsPlaying)
            report.errors.Add("ProceduralHorrorAmbience stopped during the timed scenario.");
        if (!ambience.UsesProceduralClip)
            report.errors.Add($"ProceduralHorrorAmbience switched away from generated clip. clip={ambience.RuntimeClipName}");
        if (ambience.ActiveEventTension < 0.75f)
            report.errors.Add($"Timed scenario did not raise ambience event tension enough: {ambience.ActiveEventTension:0.00}.");
        if (ambience.CurrentTension < 0.55f)
            report.errors.Add($"Timed scenario did not raise audible ambience tension enough: {ambience.CurrentTension:0.00}.");

        report.info.Add($"Timed scenario raised procedural ambience: tension={ambience.CurrentTension:0.00}, eventTension={ambience.ActiveEventTension:0.00}.");
    }

    private static void CheckExternalAudioScenarioReaction(SmokeReport report)
    {
        AmbientAudioManager audioManager = FindFirstObjectByType<AmbientAudioManager>();
        if (audioManager == null)
            return;

        if (!audioManager.IsBgmPlaying)
            report.errors.Add("External horror BGM stopped during the timed scenario.");
        if (!audioManager.ActiveBgmName.Contains("Chase"))
            report.errors.Add($"Timed scenario did not switch external BGM to chase music. clip={audioManager.ActiveBgmName}");

        report.info.Add($"Timed scenario external BGM clip: {audioManager.ActiveBgmName}.");
    }

    private static void CheckDoorwayCapsuleClearance(SmokeReport report, GameObject player, CharacterController controller)
    {
        GameObject bridge = FindObjectByName("DoorEntranceThresholdBridge_Auto");
        if (bridge == null)
            return;

        float radius = controller.radius;
        float height = Mathf.Max(controller.height, radius * 2f + 0.1f);
        Vector3 throughDoor = bridge.transform.forward;

        for (int i = -2; i <= 2; i++)
        {
            Vector3 sample = bridge.transform.position + throughDoor * (i * 0.45f);
            sample.y = player.transform.position.y;

            Vector3 bottom = sample + Vector3.up * (radius + 0.04f);
            Vector3 top = sample + Vector3.up * (height - radius);
            Collider[] hits = Physics.OverlapCapsule(bottom, top, radius, ~0, QueryTriggerInteraction.Ignore);

            for (int h = 0; h < hits.Length; h++)
            {
                Collider hit = hits[h];
                if (hit == null || IsAllowedDoorwayProbeHit(hit, player, bottom, radius))
                    continue;

                report.warnings.Add($"Doorway capsule probe may be blocked near {sample.x:0.0},{sample.y:0.0},{sample.z:0.0} by {hit.name}.");
                return;
            }
        }

        report.info.Add("Doorway capsule probe clear for Player dimensions.");
    }

    private static bool IsAllowedDoorwayProbeHit(Collider hit, GameObject player, Vector3 bottom, float radius)
    {
        if (hit.isTrigger)
            return true;
        if (hit.transform == player.transform || hit.transform.IsChildOf(player.transform))
            return true;
        if (hit.bounds.max.y < bottom.y - radius * 0.35f)
            return true;

        string objectName = hit.gameObject.name;
        if (objectName == PrimaryHouseColliderName)
        {
            DoorwayHouseCollisionGate gate = FindFirstObjectByType<DoorwayHouseCollisionGate>();
            return gate != null && gate.IsConfigured && gate.HouseColliderName == PrimaryHouseColliderName;
        }

        return objectName.StartsWith("DoorEntrance") ||
               objectName.StartsWith("ExperimentMarker") ||
               objectName.StartsWith("SecondFloorAccessRamp") ||
               objectName.StartsWith("SecondFloorAccessStep") ||
               objectName.StartsWith("SecondFloorObjective");
    }

    private static void CheckKillerRoute(SmokeReport report)
    {
        KillerAI killer = FindFirstObjectByType<KillerAI>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        ObjectiveItem objective = FindFirstObjectByType<ObjectiveItem>();
        if (killer == null)
        {
            report.errors.Add("No KillerAI in Play Mode.");
            return;
        }

        NavMeshAgent agent = killer.GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            report.errors.Add("KillerAI has no NavMeshAgent.");
            return;
        }

        if (killer.HitCooldownSec < 7f)
            report.errors.Add($"Killer hit cooldown is too short for the 2-minute IoT experience: {killer.HitCooldownSec:0.0}s.");
        if (killer.AttackRecoverySec < 1.4f)
            report.errors.Add($"Killer attack recovery is too short for readable IoT feedback: {killer.AttackRecoverySec:0.0}s.");
        if (killer.PostHitBackoffDurationSec < 1.4f || killer.PostHitBackoffDistance < 2.8f)
            report.errors.Add($"Killer post-hit backoff is too short: {killer.PostHitBackoffDistance:0.0}m/{killer.PostHitBackoffDurationSec:0.0}s.");
        if (killer.KillerNearReportIntervalSec < 15f)
            report.errors.Add($"Killer near report interval is too short for IoT cooldowns: {killer.KillerNearReportIntervalSec:0.0}s.");
        if (!killer.AvoidsStairRouteDuringChase)
            report.errors.Add("Killer stair/2F safety hold is disabled; forced chase may warp into the stair route.");

        if (!agent.isOnNavMesh)
        {
            report.warnings.Add("Killer NavMeshAgent is not on NavMesh in Play Mode.");
            return;
        }

        if (player != null)
            CheckPath(report, agent.transform.position, player.transform.position, "killer -> player start", 6f);
        if (objective != null)
            CheckPath(report, agent.transform.position, objective.transform.position, "killer -> objective area", 8f);

        KillerPlayerCollisionBypass collisionBypass = killer.GetComponent<KillerPlayerCollisionBypass>();
        if (collisionBypass == null)
            report.errors.Add("KillerPlayerCollisionBypass missing; killer colliders may block the player route.");
        else if (!collisionBypass.IsConfigured)
            report.errors.Add("KillerPlayerCollisionBypass is not configured with the Player collider.");
        else
            report.info.Add($"Killer collision bypass active for player route. ignoredColliders={collisionBypass.IgnoredColliderCount}.");
    }

    private static void CheckStairRouteCapsuleClearance(SmokeReport report, GameObject player, CharacterController controller)
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

                report.errors.Add($"Stair route capsule probe blocked at sample {i} near {samples[i].x:0.0},{samples[i].y:0.0},{samples[i].z:0.0} by {hit.name}.");
                return;
            }
        }

        report.info.Add("Stair route capsule probe clear for Player dimensions.");
    }

    private static bool IsAllowedStairRouteProbeHit(Collider hit, GameObject player, Vector3 bottom, float radius)
    {
        if (hit.isTrigger)
            return true;
        if (hit.transform == player.transform || hit.transform.IsChildOf(player.transform))
            return true;
        if (hit.bounds.max.y < bottom.y - radius * 0.35f)
            return true;

        KillerAI killer = FindFirstObjectByType<KillerAI>();
        if (killer != null && hit.transform.IsChildOf(killer.transform))
        {
            KillerPlayerCollisionBypass bypass = killer.GetComponent<KillerPlayerCollisionBypass>();
            return bypass != null && bypass.IsConfigured && bypass.IsIgnoringCollider(hit);
        }

        string objectName = hit.gameObject.name;
        if (objectName == PrimaryHouseColliderName)
            return IsConfiguredHouseCollisionGate("StairHouseCollisionGate_Auto");

        if (objectName.StartsWith("SecondFloorWalkableFloor") ||
            objectName.StartsWith("SecondFloorStairLanding"))
        {
            return hit.bounds.max.y < bottom.y - radius * 0.35f;
        }

        return objectName.StartsWith("SecondFloorAccessRamp") ||
               objectName.StartsWith("SecondFloorAccessStep") ||
               objectName.StartsWith("ExperimentMarker") ||
               objectName.StartsWith("SecondFloorObjective");
    }

    private static bool IsConfiguredHouseCollisionGate(string gateName)
    {
        GameObject gate = FindObjectByName(gateName);
        if (gate == null)
            return false;

        DoorwayHouseCollisionGate collisionGate = gate.GetComponent<DoorwayHouseCollisionGate>();
        BoxCollider box = gate.GetComponent<BoxCollider>();
        return collisionGate != null &&
               collisionGate.HouseColliderName == PrimaryHouseColliderName &&
               box != null &&
               box.isTrigger;
    }

    private static void CheckStairRouteCharacterControllerTraversal(SmokeReport report, GameObject player, CharacterController sourceController)
    {
        GameObject probe = new GameObject("StairRouteTraversalProbe_Temp");
        CharacterController controller = probe.AddComponent<CharacterController>();
        controller.height = sourceController.height;
        controller.radius = sourceController.radius;
        controller.center = sourceController.center;
        controller.stepOffset = Mathf.Max(sourceController.stepOffset, 0.6f);
        controller.slopeLimit = Mathf.Max(sourceController.slopeLimit, 60f);
        controller.skinWidth = Mathf.Max(sourceController.skinWidth, 0.08f);
        controller.minMoveDistance = 0f;

        try
        {
            probe.transform.position = StairTraversalRoute[0];
            Physics.SyncTransforms();
            IgnoreTraversalProbeNonRouteColliders(controller, player);

            StairTraversalAssistZone assistZone = FindFirstObjectByType<StairTraversalAssistZone>();
            for (int i = 1; i < StairTraversalRoute.Length; i++)
            {
                if (!MoveProbeToward(controller, StairTraversalRoute[i], assistZone, out string failure))
                {
                    report.errors.Add($"Stair route CharacterController traversal failed at waypoint {i}: {failure}");
                    return;
                }
            }

            if (probe.transform.position.y < 2.55f)
            {
                report.errors.Add($"Stair route CharacterController traversal ended too low: y={probe.transform.position.y:0.00}.");
                return;
            }

            report.info.Add($"Stair route CharacterController traversal reached 2F. end={probe.transform.position.x:0.0},{probe.transform.position.y:0.0},{probe.transform.position.z:0.0}.");
        }
        finally
        {
            UnityEngine.Object.Destroy(probe);
        }
    }

    private static bool MoveProbeToward(CharacterController controller, Vector3 target, StairTraversalAssistZone assistZone, out string failure)
    {
        const float stepDistance = 0.08f;
        const float groundStick = 0.08f;
        float bestFlatDistance = float.MaxValue;
        int stalledFrames = 0;
        int assistMoves = 0;

        for (int i = 0; i < 260; i++)
        {
            Vector3 position = controller.transform.position;
            Vector2 flatPosition = new Vector2(position.x, position.z);
            Vector2 flatTarget = new Vector2(target.x, target.z);
            float flatDistance = Vector2.Distance(flatPosition, flatTarget);

            if (flatDistance <= 0.22f)
            {
                failure = null;
                return true;
            }

            Vector3 flatDelta = new Vector3(target.x - position.x, 0f, target.z - position.z);
            Vector3 move = flatDelta.normalized * Mathf.Min(stepDistance, flatDistance);
            CollisionFlags flags = controller.Move(move + Vector3.down * groundStick);

            if ((flags & CollisionFlags.Sides) != 0)
            {
                float stepLift = Mathf.Clamp(controller.stepOffset + 0.05f, 0.45f, 0.7f);
                controller.Move(Vector3.up * stepLift);
                controller.Move(move * 1.25f);
                controller.Move(Vector3.down * stepLift);
            }

            if (assistZone != null && assistZone.TryAssist(controller, move.normalized, 0.04f))
                assistMoves++;

            Vector3 after = controller.transform.position;
            float afterDistance = Vector2.Distance(new Vector2(after.x, after.z), flatTarget);
            if (afterDistance < bestFlatDistance - 0.01f)
            {
                bestFlatDistance = afterDistance;
                stalledFrames = 0;
            }
            else
            {
                stalledFrames++;
            }

            if (stalledFrames > 70)
            {
                failure = $"stalled near {after.x:0.0},{after.y:0.0},{after.z:0.0}, target={target.x:0.0},{target.y:0.0},{target.z:0.0}, remaining={afterDistance:0.00}m, assist={(assistZone == null ? "missing" : assistMoves.ToString())}. {DescribeProbeContacts(controller)}";
                return false;
            }
        }

        Vector3 finalPosition = controller.transform.position;
        failure = $"iteration limit near {finalPosition.x:0.0},{finalPosition.y:0.0},{finalPosition.z:0.0}, target={target.x:0.0},{target.y:0.0},{target.z:0.0}, assist={(assistZone == null ? "missing" : assistMoves.ToString())}. {DescribeProbeContacts(controller)}";
        return false;
    }

    private static string DescribeProbeContacts(CharacterController controller)
    {
        float radius = controller.radius;
        float height = Mathf.Max(controller.height, radius * 2f + 0.1f);
        Vector3 worldCenter = controller.transform.TransformPoint(controller.center);
        Vector3 bottom = worldCenter + Vector3.down * (height * 0.5f - radius);
        Vector3 top = worldCenter + Vector3.up * (height * 0.5f - radius);
        Collider[] hits = Physics.OverlapCapsule(bottom, top, radius + 0.03f, ~0, QueryTriggerInteraction.Ignore);

        List<string> contactNames = new List<string>();
        for (int i = 0; i < hits.Length && contactNames.Count < 6; i++)
        {
            Collider hit = hits[i];
            if (hit == null || hit == controller)
                continue;

            contactNames.Add(DescribeCollider(hit));
        }

        string ground = "ground=none";
        if (Physics.Raycast(controller.transform.position + Vector3.up * 0.4f, Vector3.down, out RaycastHit rayHit, 3f, ~0, QueryTriggerInteraction.Ignore))
            ground = $"ground={DescribeCollider(rayHit.collider)}";

        string contacts = contactNames.Count == 0 ? "contacts=none" : $"contacts={string.Join(" | ", contactNames)}";
        return $"{contacts}; {ground}.";
    }

    private static string DescribeCollider(Collider collider)
    {
        if (collider == null)
            return "null";

        Bounds bounds = collider.bounds;
        return $"{GetTransformPath(collider.transform)} [{collider.GetType().Name}, trigger={collider.isTrigger}, bounds={bounds.size.x:0.00}x{bounds.size.y:0.00}x{bounds.size.z:0.00}, center={FormatVector(bounds.center)}]";
    }

    private static string GetTransformPath(Transform transform)
    {
        if (transform == null)
            return "<null>";

        List<string> parts = new List<string>();
        Transform current = transform;
        while (current != null)
        {
            parts.Add(current.name);
            current = current.parent;
        }

        parts.Reverse();
        return string.Join("/", parts);
    }

    private static void IgnoreTraversalProbeNonRouteColliders(CharacterController probe, GameObject player)
    {
        Collider[] playerColliders = player.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < playerColliders.Length; i++)
        {
            if (playerColliders[i] != null)
                Physics.IgnoreCollision(probe, playerColliders[i], true);
        }

        KillerAI killer = FindFirstObjectByType<KillerAI>();
        if (killer != null)
        {
            Collider[] killerColliders = killer.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < killerColliders.Length; i++)
            {
                if (killerColliders[i] != null)
                    Physics.IgnoreCollision(probe, killerColliders[i], true);
            }
        }

        Collider[] allColliders = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < allColliders.Length; i++)
        {
            Collider collider = allColliders[i];
            if (collider != null && collider.gameObject.name == PrimaryHouseColliderName && IsConfiguredHouseCollisionGate("StairHouseCollisionGate_Auto"))
                Physics.IgnoreCollision(probe, collider, true);
        }
    }

    private static void CheckPath(SmokeReport report, Vector3 from, Vector3 to, string label, float targetSampleDistance)
    {
        if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, 4f, NavMesh.AllAreas))
        {
            report.warnings.Add($"NavMesh path source missing: {label}.");
            return;
        }

        if (!NavMesh.SamplePosition(to, out NavMeshHit toHit, targetSampleDistance, NavMesh.AllAreas))
        {
            report.warnings.Add($"NavMesh path target missing: {label}.");
            return;
        }

        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path) ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            if (TryFindFallbackChaseStart(toHit.position, out Vector3 fallbackStart, out float fallbackLength))
            {
                report.info.Add($"NavMesh fallback chase start OK: {label}, start={FormatVector(fallbackStart)}, length={fallbackLength:0.0}m.");
                return;
            }

            report.warnings.Add($"NavMesh path incomplete: {label}.");
            return;
        }

        report.info.Add($"NavMesh path OK: {label}, length={EstimatePathLength(path):0.0}m.");
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

                NavMeshPath fallbackPath = new NavMeshPath();
                if (!NavMesh.CalculatePath(candidate.position, target, NavMesh.AllAreas, fallbackPath) ||
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

    private static void CheckEndState(SmokeReport report)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            FirstPersonController controller = player.GetComponent<FirstPersonController>();
            if (controller == null)
                report.errors.Add("Cannot verify input lock because FirstPersonController is missing.");
            else if (!controller.IsMovementLocked)
                report.errors.Add("Player input was not locked after mission success.");
        }

        GameObject successPanel = FindPreferredObjectByName("ExperimentSuccessPanel_Auto");
        if (successPanel == null || !successPanel.activeInHierarchy)
            report.errors.Add("Mission Success panel is not visible after smoke completion.");
    }

    private static void CheckFailureState(SmokeReport report)
    {
        GameObject failedPanel = FindPreferredObjectByName("ExperimentFailedPanel_Auto");
        if (failedPanel == null || !failedPanel.activeInHierarchy)
        {
            report.errors.Add("Mission Failed/Time Over panel is not visible after timeout failure.");
            return;
        }

        UnityEngine.UI.Text text = failedPanel.GetComponentInChildren<UnityEngine.UI.Text>(true);
        if (text == null || !text.text.Contains("Time Over"))
            report.errors.Add("Timeout failure panel text is not Time Over.");
    }

    private static IEnumerator ResetIotServerBetweenSmokeSessions(SmokeReport report)
    {
        SmartThingsEventSender sender = SmartThingsEventSender.Instance;
        if (sender == null)
        {
            report.errors.Add("Cannot reset IoT server cooldown because SmartThingsEventSender is missing.");
            yield break;
        }

        bool done = false;
        bool ok = false;
        sender.SendEmergencyStop(result =>
        {
            ok = result;
            done = true;
        });

        float deadline = Time.realtimeSinceStartup + 4f;
        while (!done && Time.realtimeSinceStartup < deadline)
            yield return null;

        if (!done || !ok)
        {
            report.errors.Add("IoT server emergency-stop reset failed between smoke sessions.");
            yield break;
        }

        yield return new WaitForSeconds(0.5f);
        report.info.Add("IoT server cooldown reset before success-path smoke session.");
    }

    private static IEnumerator CollectObjectiveThroughObjectiveItem(SmokeReport report, ExperimentDirector director)
    {
        ObjectiveItem objective = FindFirstObjectByType<ObjectiveItem>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (objective == null || player == null)
        {
            report.errors.Add("Cannot verify objective pickup because player or ObjectiveItem is missing.");
            yield break;
        }

        Collider playerCollider = player.GetComponent<Collider>();
        if (playerCollider == null)
        {
            report.errors.Add("Cannot verify objective pickup because Player has no Collider/CharacterController.");
            yield break;
        }

        if (!objective.TryCollect(playerCollider))
            report.errors.Add("ObjectiveItem.TryCollect did not accept the Player collider.");

        yield return new WaitForSeconds(0.25f);

        if (director.IsRunning)
        {
            report.errors.Add("ObjectiveItem pickup did not end the session.");
            yield break;
        }

        report.info.Add("ObjectiveItem pickup path ended session with Mission Success.");
    }

    private static void CheckLogFile(SmokeReport report)
    {
        if (string.IsNullOrEmpty(report.logPath))
        {
            report.warnings.Add("ExperimentLogger path was unavailable.");
            return;
        }

        if (!File.Exists(report.logPath))
        {
            report.errors.Add($"Experiment log file was not written: {report.logPath}");
            return;
        }

        string logText = File.ReadAllText(report.logPath);
        RequireLogEvent(report, logText, "game_start");
        RequireLogEvent(report, logText, "scenario_cue");
        RequireLogEvent(report, logText, "ghost_hint");
        RequireLogEvent(report, logText, "killer_near");
        RequireLogEvent(report, logText, "blackout");
        RequireLogEvent(report, logText, "chase");
        RequireLogEvent(report, logText, "player_hit");
        RequireLogEvent(report, logText, "mission_success");
        RequireLogEvent(report, logText, "session_success");

        if (string.Equals(report.condition, "GameWithIoT", StringComparison.Ordinal))
        {
            RequireIotOkEvent(report, logText, "game_start");
            RequireIotOkEvent(report, logText, "ghost_hint");
            RequireIotOkEvent(report, logText, "killer_near");
            RequireIotOkEvent(report, logText, "blackout");
            RequireIotOkEvent(report, logText, "chase");
            RequireIotOkEvent(report, logText, "player_hit");
            RequireIotOkEvent(report, logText, "mission_success");
            report.info.Add("GameWithIoT log contains accepted Unity-to-server requests for the timed scenario.");
        }

        report.info.Add("Timed scenario log contains ghost_hint, killer_near, blackout, and chase cues.");
    }

    private static void CheckFailureLogFile(SmokeReport report, string logPath)
    {
        if (string.IsNullOrEmpty(logPath))
        {
            report.warnings.Add("Failure-path ExperimentLogger path was unavailable.");
            return;
        }

        if (!File.Exists(logPath))
        {
            report.errors.Add($"Failure-path experiment log file was not written: {logPath}");
            return;
        }

        string logText = File.ReadAllText(logPath);
        RequireLogEvent(report, logText, "mission_failed");
        RequireLogEvent(report, logText, "session_failed");
        if (string.Equals(report.condition, "GameWithIoT", StringComparison.Ordinal))
            RequireIotOkEvent(report, logText, "mission_failed");
        report.info.Add("Failure log contains mission_failed and session_failed events.");
    }

    private static void RequireLogEvent(SmokeReport report, string logText, string eventName)
    {
        if (!logText.Contains($"\"event_name\":\"{eventName}\""))
            report.errors.Add($"Experiment log missing event: {eventName}");
    }

    private static void RequireIotOkEvent(SmokeReport report, string logText, string eventName)
    {
        string needle = $"\"event_name\":\"{eventName}\"";
        string[] lines = logText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Contains(needle) && lines[i].Contains("\"iot_request_ok\":true"))
                return;
        }

        report.errors.Add($"GameWithIoT log missing accepted IoT request for event: {eventName}");
    }

    private static void RequireNamedObject(SmokeReport report, string objectName, bool warningOnly)
    {
        if (FindObjectByName(objectName) != null)
            return;

        if (warningOnly)
            report.warnings.Add($"{objectName} missing in Play Mode.");
        else
            report.errors.Add($"{objectName} missing in Play Mode.");
    }

    private static GameObject FindObjectByName(string objectName)
    {
        return FindPreferredObjectByName(objectName);
    }

    private static GameObject FindPreferredObjectByName(string objectName)
    {
        GameObject[] objects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        GameObject inactiveMatch = null;
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i].name != objectName)
                continue;

            if (objects[i].activeInHierarchy)
                return objects[i];
            if (inactiveMatch == null)
                inactiveMatch = objects[i];
        }

        return inactiveMatch;
    }

    private static void FinishReport(SmokeReport report)
    {
        report.errorCount = report.errors.Count;
        report.warningCount = report.warnings.Count;
        report.success = report.errorCount == 0;

        WriteReport(report);
        RestoreConditionOverride();

        string level = report.success ? "passed" : "failed";
        Debug.Log($"[ExperimentSmoke] {level}: errors={report.errorCount}, warnings={report.warningCount}, report={report.reportPath}");

#if UNITY_EDITOR
        EditorApplication.delayCall += () =>
        {
            if (EditorApplication.isPlaying)
                EditorApplication.ExitPlaymode();
        };
#endif
    }

    private static string ResolveReportPath(string condition = null)
    {
#if UNITY_EDITOR
        string resolvedCondition = condition;
        if (string.IsNullOrEmpty(resolvedCondition))
            resolvedCondition = PlayerPrefs.GetString(ConditionPrefsKey, string.Empty);

        return string.Equals(resolvedCondition, "GameWithIoT", StringComparison.OrdinalIgnoreCase)
            ? EditorIotReportPath
            : EditorReportPath;
#else
        string dir = Path.Combine(Application.persistentDataPath, "ExperimentLogs");
        return Path.Combine(dir, "experiment_playmode_smoke.json");
#endif
    }

    private static void WriteReport(SmokeReport report)
    {
        string path = report.reportPath;
        string directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(path, report.ToJson());

#if UNITY_EDITOR
        AssetDatabase.Refresh();
#endif
    }

    private static void RestoreConditionOverride()
    {
        if (PlayerPrefs.GetInt(PreviousConditionHadKey, 0) == 1)
            PlayerPrefs.SetString(ConditionPrefsKey, PlayerPrefs.GetString(PreviousConditionValueKey, string.Empty));
        else
            PlayerPrefs.DeleteKey(ConditionPrefsKey);

        if (PlayerPrefs.GetInt(PreviousScenarioScaleHadKey, 0) == 1)
            PlayerPrefs.SetFloat(ScenarioScalePrefsKey, PlayerPrefs.GetFloat(PreviousScenarioScaleValueKey, 1f));
        else
            PlayerPrefs.DeleteKey(ScenarioScalePrefsKey);

        PlayerPrefs.DeleteKey(PreviousConditionHadKey);
        PlayerPrefs.DeleteKey(PreviousConditionValueKey);
        PlayerPrefs.DeleteKey(PreviousScenarioScaleHadKey);
        PlayerPrefs.DeleteKey(PreviousScenarioScaleValueKey);
        PlayerPrefs.Save();
    }

#if UNITY_EDITOR
    [MenuItem("Tools/Experiment/Run Play Mode Smoke Test (GameOnly)")]
    private static void RunPlayModeSmokeFromMenu()
    {
        RunPlayModeSmokeFromMenu("GameOnly");
    }

    [MenuItem("Tools/Experiment/Run Play Mode Smoke Test (GameWithIoT Simulation)")]
    private static void RunIotPlayModeSmokeFromMenu()
    {
        RunPlayModeSmokeFromMenu("GameWithIoT");
    }

    private static void RunPlayModeSmokeFromMenu(string condition)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[ExperimentSmoke] Stop Play Mode before starting the smoke test.");
            return;
        }

        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        if (SceneManager.GetActiveScene().path != MainScenePath)
            EditorSceneManager.OpenScene(MainScenePath);

        StoreConditionOverride();
        PlayerPrefs.SetString(ConditionPrefsKey, condition);
        PlayerPrefs.SetFloat(ScenarioScalePrefsKey, SmokeScenarioTimeScale);
        PlayerPrefs.SetInt(PlayerPrefsKey, 1);
        PlayerPrefs.Save();

        Debug.Log($"[ExperimentSmoke] Armed Play Mode smoke test in {condition} condition.");
        EditorApplication.isPlaying = true;
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
#endif

    private sealed class SmokeReport
    {
        public string timestampUtc;
        public string sceneName;
        public string scenePath;
        public string sessionId;
        public string condition;
        public string logPath;
        public string reportPath;
        public bool success;
        public int errorCount;
        public int warningCount;
        public readonly List<string> errors = new List<string>();
        public readonly List<string> warnings = new List<string>();
        public readonly List<string> info = new List<string>();

        public string ToJson()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("{");
            AppendProperty(sb, "timestampUtc", timestampUtc, true);
            AppendProperty(sb, "sceneName", sceneName, true);
            AppendProperty(sb, "scenePath", scenePath, true);
            AppendProperty(sb, "sessionId", sessionId, true);
            AppendProperty(sb, "condition", condition, true);
            AppendProperty(sb, "logPath", logPath, true);
            AppendProperty(sb, "reportPath", reportPath, true);
            sb.AppendLine($"  \"success\": {success.ToString().ToLowerInvariant()},");
            sb.AppendLine($"  \"errorCount\": {errorCount},");
            sb.AppendLine($"  \"warningCount\": {warningCount},");
            AppendArray(sb, "errors", errors, true);
            AppendArray(sb, "warnings", warnings, true);
            AppendArray(sb, "info", info, false);
            sb.AppendLine("}");
            return sb.ToString();
        }

        private static void AppendProperty(System.Text.StringBuilder sb, string name, string value, bool comma)
        {
            sb.Append("  \"").Append(Escape(name)).Append("\": ");
            if (value == null)
                sb.Append("null");
            else
                sb.Append("\"").Append(Escape(value)).Append("\"");
            sb.AppendLine(comma ? "," : string.Empty);
        }

        private static void AppendArray(System.Text.StringBuilder sb, string name, List<string> values, bool comma)
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
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value
                .Replace("\\", "\\\\")
                .Replace("\"", "\\\"")
                .Replace("\r", "\\r")
                .Replace("\n", "\\n");
        }
    }
}
