using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

/// <summary>
/// Play Mode에서만 드러나는 실험 씬 문제를 콘솔과 실험 로그에 남긴다.
/// 게임을 중단하지 않고 제출 전 QA 신호만 제공한다.
/// </summary>
public class ExperimentRuntimeWatchdog : MonoBehaviour
{
    private const string PrimaryHouseColliderName = "Old_House_windows_separated_Collider";

    [SerializeField] private float startupCheckDelaySec = 1.5f;
    [SerializeField] private float routeCheckDelaySec = 3.0f;
    [SerializeField] private bool logToExperimentLogger = true;

    private readonly HashSet<string> _reported = new HashSet<string>();

    private void Start()
    {
        StartCoroutine(RunChecks());
    }

    private IEnumerator RunChecks()
    {
        yield return new WaitForSeconds(startupCheckDelaySec);
        CheckStartupState();

        yield return new WaitForSeconds(routeCheckDelaySec);
        CheckRouteState();
    }

    private void CheckStartupState()
    {
        if (SceneManager.GetActiveScene().name != "MainScene")
            return;

        ExperimentDirector director = ExperimentDirector.Instance;
        if (director == null)
            Warn("director_missing", "ExperimentDirector is missing at runtime.");

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null)
        {
            Warn("player_missing", "No runtime Player tagged object.");
            return;
        }

        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller == null)
            Warn("player_controller_missing", "Player has no CharacterController.");
        else
        {
            if (controller.height > 1.6f || controller.radius > 0.25f)
                Warn("player_capsule_large", $"Player capsule may catch on doorways. height={controller.height:0.00}, radius={controller.radius:0.00}");
            if (controller.stepOffset < 0.45f)
                Warn("player_step_low", $"Player stepOffset is low for thresholds: {controller.stepOffset:0.00}");
        }

        if (player.GetComponent<FirstPersonController>() == null)
            Warn("fpc_missing", "Player has no FirstPersonController.");

        if (FindObjectsByType<AudioListener>(FindObjectsInactive.Include, FindObjectsSortMode.None).Length == 0)
            Warn("audio_listener_missing", "No AudioListener exists in runtime scene.");

        ProceduralHorrorAmbience ambience = FindFirstObjectByType<ProceduralHorrorAmbience>();
        if (ambience == null || !ambience.HasUsableOutput)
            Warn("procedural_ambience_missing", "Procedural horror ambience is missing or not configured.");

        AmbientAudioManager audioManager = FindFirstObjectByType<AmbientAudioManager>();
        if (audioManager == null || !audioManager.HasUsableExternalBgm)
            Warn("external_horror_audio_missing", "External horror BGM/SFX layer is missing or not configured.");

        ObjectiveItem objective = FindFirstObjectByType<ObjectiveItem>();
        if (objective == null)
        {
            Warn("objective_missing", "No ObjectiveItem exists at runtime.");
        }
        else
        {
            Collider objectiveCollider = objective.GetComponent<Collider>();
            if (objectiveCollider == null || !objectiveCollider.isTrigger)
                Warn("objective_trigger_invalid", "ObjectiveItem collider is missing or not a trigger.");
            if (objective.transform.position.y < 2f)
                Warn("objective_not_2f", $"Objective y={objective.transform.position.y:0.00}; expected 2F placement.");
        }

        RequireNamedRuntimeObject("SecondFloorAccessRamp_Auto");
        RequireNamedRuntimeObject("SecondFloorWalkableFloor_Auto");
        RequireNamedRuntimeObject("SecondFloorStairLanding_Auto");
        RequireNamedRuntimeObject("SecondFloorBoundaryWall_Auto_North");
        RequireNamedRuntimeObject("SecondFloorBoundaryWall_Auto_South");
        RequireNamedRuntimeObject("SecondFloorBoundaryWall_Auto_East");
        RequireNamedRuntimeObject("SecondFloorBoundaryWall_Auto_West");
        RequireNamedRuntimeObject("DoorEntranceThresholdBridge_Auto");
        RequireNamedRuntimeObject("DoorEntranceRampOutside_Auto");
        RequireNamedRuntimeObject("DoorEntranceRampInside_Auto");
        RequireNamedRuntimeObject("DoorwayHouseCollisionGate_Auto");
        RequireNamedRuntimeObject("StairHouseCollisionGate_Auto");
        RequireNamedRuntimeObject("StairTraversalAssistZone_Auto");
        RequireHouseCollisionGate("DoorwayHouseCollisionGate_Auto", "doorway_house_collision_gate");
        RequireHouseCollisionGate("StairHouseCollisionGate_Auto", "stair_house_collision_gate");
        RequirePrimaryHouseCollider();
        RequireNamedRuntimeObject("ExperimentMarker_StairsReached_Auto");
        RequireNamedRuntimeObject("ExperimentMarker_SecondFloorCue_Auto");
        RequireNamedRuntimeObject("ExperimentMarker_ObjectiveArea_Auto");
    }

    private void RequirePrimaryHouseCollider()
    {
        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        bool found = false;

        foreach (Collider collider in colliders)
        {
            if (collider == null || collider.gameObject.name != PrimaryHouseColliderName)
                continue;

            found = true;
            if (!collider.gameObject.activeInHierarchy || !collider.enabled || collider.isTrigger)
                Warn("primary_house_collider_disabled", $"{PrimaryHouseColliderName} is not active enabled solid collision.");
            return;
        }

        if (!found)
            Warn("primary_house_collider_missing", $"{PrimaryHouseColliderName} is missing; old-house walls/floors may be non-solid.");
    }

    private void RequireHouseCollisionGate(string objectName, string code)
    {
        GameObject gateObject = GameObject.Find(objectName);
        DoorwayHouseCollisionGate gate = gateObject != null ? gateObject.GetComponent<DoorwayHouseCollisionGate>() : null;
        if (gate == null)
        {
            Warn(code + "_missing", $"{objectName} is missing; primary house collider may block traversal.");
            return;
        }

        if (!gate.IsConfigured || gate.HouseColliderName != PrimaryHouseColliderName)
            Warn(code + "_invalid", $"{objectName} is not configured for the primary old-house collider.");
    }

    private void CheckRouteState()
    {
        if (SceneManager.GetActiveScene().name != "MainScene")
            return;

        KillerAI killer = FindFirstObjectByType<KillerAI>();
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        ObjectiveItem objective = FindFirstObjectByType<ObjectiveItem>();
        if (killer == null || player == null || objective == null)
            return;

        NavMeshAgent agent = killer.GetComponent<NavMeshAgent>();
        if (agent == null)
        {
            Warn("killer_agent_missing", "KillerAI has no NavMeshAgent.");
            return;
        }

        if (!agent.isOnNavMesh)
        {
            Warn("killer_off_navmesh", "Killer NavMeshAgent is not on NavMesh at runtime.");
            return;
        }

        KillerPlayerCollisionBypass collisionBypass = killer.GetComponent<KillerPlayerCollisionBypass>();
        if (collisionBypass == null || !collisionBypass.IsConfigured)
            Warn("killer_collision_bypass_missing", "KillerPlayerCollisionBypass is missing or not configured; killer colliders may block the player route.");

        if (!killer.AvoidsStairRouteDuringChase)
            Warn("killer_stair_safety_disabled", "Killer stair/2F safety hold is disabled; forced chase may enter the stair route.");

        CheckPath(agent.transform.position, player.transform.position, "killer_to_player_start");
        CheckPath(agent.transform.position, objective.transform.position, "killer_to_objective_area");
    }

    private void CheckPath(Vector3 from, Vector3 to, string code)
    {
        if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, 4f, NavMesh.AllAreas))
        {
            Warn(code + "_from_missing", $"{code}: source point is not near NavMesh.");
            return;
        }

        if (!NavMesh.SamplePosition(to, out NavMeshHit toHit, 8f, NavMesh.AllAreas))
        {
            Warn(code + "_to_missing", $"{code}: target point is not near NavMesh.");
            return;
        }

        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, path) ||
            path.status != NavMeshPathStatus.PathComplete)
        {
            Warn(code + "_incomplete", $"{code}: NavMesh path is incomplete.");
        }
    }

    private void RequireNamedRuntimeObject(string objectName)
    {
        if (GameObject.Find(objectName) == null)
            Warn("missing_" + objectName, $"{objectName} is missing at runtime.");
    }

    private void Warn(string code, string message)
    {
        if (!_reported.Add(code))
            return;

        Debug.LogWarning($"[ExperimentWatchdog] {message}");

        if (!logToExperimentLogger || ExperimentDirector.Instance == null)
            return;

        ExperimentLogger logger = FindFirstObjectByType<ExperimentLogger>();
        if (logger == null)
            return;

        logger.LogEvent(
            "runtime_watchdog_warning",
            "watchdog",
            ExperimentDirector.Instance.ElapsedSec,
            ExperimentDirector.Instance.ConditionName,
            ExperimentDirector.Instance.HitCount,
            null,
            "detail",
            $"{code}: {message}");
    }
}
