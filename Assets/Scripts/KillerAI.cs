using UnityEngine;
using UnityEngine.AI;
using System.Collections;

// killer 프리팹용 싱글플레이어 추격 AI (Mirror 불필요)
[RequireComponent(typeof(NavMeshAgent))]
public class KillerAI : MonoBehaviour
{
    [Header("타겟")]
    [SerializeField] private string playerTag = "Player";

    [Header("추격 거리 설정")]
    [SerializeField] private float detectRange = 15f; // 안에 들어오면 추격 시작
    [SerializeField] private float loseRange   = 25f; // 밖으로 벗어나면 추격 포기
    [SerializeField] private float catchRange  = 1.3f; // 안 = 처치(게임오버)

    [Header("이동 속도")]
    [SerializeField] private float walkSpeed  = 1.5f; // 배회
    [SerializeField] private float chaseSpeed = 4.0f; // 추격

    [Header("실험 이벤트")]
    [SerializeField] private float killerNearDistance = 6f;
    [SerializeField] private float killerNearReportInterval = 18f;
    [SerializeField] private float hitCooldown = 8f;
    [SerializeField] private float attackWindupSec = 0.5f;
    [SerializeField] private float attackRecoverySec = 1.6f;
    [SerializeField] private float facePlayerTurnSpeed = 720f;
    [SerializeField] private bool requireReachableAttackPath = true;
    [SerializeField] private float attackVerticalTolerance = 0.95f;
    [SerializeField] private float attackForwardConeDot = 0.25f;
    [SerializeField] private float attackContactSlack = 0.2f;
    [SerializeField] private float postHitBackoffDistance = 3.2f;
    [SerializeField] private float postHitBackoffDurationSec = 1.8f;
    [SerializeField] private float proceduralAttackLungeDistance = 0.32f;
    [SerializeField] private float proceduralAttackLungeDurationSec = 0.22f;
    [SerializeField] private string attackTriggerParam = "Attack";

    [Header("NavMesh 경로 보정")]
    [SerializeField] private bool relocateOnUnreachableForceChase = true;
    [SerializeField] private float forceChaseRelocationDistance = 7f;
    [SerializeField] private float forceChaseRelocationSearchRadius = 3f;
    [SerializeField] private float forceChaseRelocationMinDistance = 4f;
    [SerializeField] private float forceChaseRelocationVerticalTolerance = 0.85f;

    [Header("계단/2층 안전 구역")]
    [SerializeField] private bool avoidStairRouteDuringChase = true;
    [SerializeField] private Vector3 stairSafetyCenter = new Vector3(-25.9f, 2.15f, -16.2f);
    [SerializeField] private Vector3 stairSafetySize = new Vector3(8.4f, 4.9f, 7.2f);
    [SerializeField] private Vector3 stairSafetyHoldPosition = new Vector3(-31.2f, 0.15f, -21.2f);
    [SerializeField] private float stairSafetyHoldMinSec = 1.25f;
    [SerializeField] private float stairSafetyHoldSampleRadius = 4f;

    [Header("배회 지점 (비우면 제자리 대기)")]
    [SerializeField] private Transform[] patrolPoints;

    [Header("애니메이터 (PlayerAIController — float 3개)")]
    [SerializeField] private Animator animator;
    [SerializeField] private string idleParam = "Idle";
    [SerializeField] private string walkParam = "Walk";
    [SerializeField] private string runParam  = "Run";

    private NavMeshAgent agent;
    private Transform player;
    private PlayerHealth playerHealth;
    private int patrolIndex = 0;
    private bool isChasing = false;
    private bool attackInProgress = false;
    private float lastNearReportTime = -999f;
    private float lastHitTime = -999f;
    private bool isBackingOff;
    private float backoffEndTime;
    private float forcedChaseUntil = -999f;
    private bool isHoldingForStairSafety;
    private float stairSafetyHoldUntil = -999f;
    private NavMeshPath attackPath;
    private bool hasAttackTrigger;
    private bool hasIdleParam;
    private bool hasWalkParam;
    private bool hasRunParam;
    private AnimatorControllerParameterType idleParamType;
    private AnimatorControllerParameterType walkParamType;
    private AnimatorControllerParameterType runParamType;
    private int lastAnimatorState = -1;
    private static readonly float[] RelocationAngles = { 180f, 135f, -135f, 90f, -90f, 45f, -45f, 0f };

    public float HitCooldownSec => hitCooldown;
    public float AttackRecoverySec => attackRecoverySec;
    public float PostHitBackoffDurationSec => postHitBackoffDurationSec;
    public float PostHitBackoffDistance => postHitBackoffDistance;
    public float KillerNearReportIntervalSec => killerNearReportInterval;
    public bool AvoidsStairRouteDuringChase => avoidStairRouteDuringChase;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        var p = GameObject.FindGameObjectWithTag(playerTag);
        if (p != null)
        {
            player = p.transform;
            playerHealth = p.GetComponent<PlayerHealth>();
        }
        if (animator == null) animator = GetComponentInChildren<Animator>();
        attackPath = new NavMeshPath();
        hasAttackTrigger = HasAnimatorParameter(attackTriggerParam, AnimatorControllerParameterType.Trigger);
        hasIdleParam = TryGetAnimatorParameterType(idleParam, out idleParamType);
        hasWalkParam = TryGetAnimatorParameterType(walkParam, out walkParamType);
        hasRunParam = TryGetAnimatorParameterType(runParam, out runParamType);
    }

    private void Start()
    {
        agent.speed = walkSpeed;
        agent.stoppingDistance = Mathf.Max(agent.stoppingDistance, catchRange * 0.75f);
        agent.angularSpeed = Mathf.Max(agent.angularSpeed, 240f);
        agent.acceleration = Mathf.Max(agent.acceleration, 10f);
        if (agent.enabled && agent.isOnNavMesh)
            GoToNextPatrol();
    }

    public void ConfigureForExperimentDefaults()
    {
        detectRange = 18f;
        loseRange = 30f;
        catchRange = 1.3f;
        walkSpeed = 1.35f;
        chaseSpeed = 4.0f;
        killerNearDistance = 6f;
        killerNearReportInterval = 18f;
        hitCooldown = 8f;
        attackWindupSec = 0.5f;
        attackRecoverySec = 1.6f;
        facePlayerTurnSpeed = 720f;
        requireReachableAttackPath = true;
        attackVerticalTolerance = 0.95f;
        attackForwardConeDot = 0.25f;
        attackContactSlack = 0.2f;
        postHitBackoffDistance = 3.2f;
        postHitBackoffDurationSec = 1.8f;
        proceduralAttackLungeDistance = 0.32f;
        proceduralAttackLungeDurationSec = 0.22f;
        relocateOnUnreachableForceChase = true;
        forceChaseRelocationDistance = 7f;
        forceChaseRelocationSearchRadius = 3f;
        forceChaseRelocationMinDistance = 4f;
        forceChaseRelocationVerticalTolerance = 0.85f;
        avoidStairRouteDuringChase = true;
        stairSafetyCenter = new Vector3(-25.9f, 2.15f, -16.2f);
        stairSafetySize = new Vector3(8.4f, 4.9f, 7.2f);
        stairSafetyHoldPosition = new Vector3(-31.2f, 0.15f, -21.2f);
        stairSafetyHoldMinSec = 1.25f;
        stairSafetyHoldSampleRadius = 4f;

        if (agent == null)
            agent = GetComponent<NavMeshAgent>();

        if (agent != null)
        {
            agent.radius = 0.4f;
            agent.height = 2f;
            agent.speed = chaseSpeed;
            agent.acceleration = 12f;
            agent.angularSpeed = 300f;
            agent.stoppingDistance = 1.0f;
            agent.baseOffset = 0f;
            agent.autoBraking = true;
            agent.autoRepath = true;
        }
    }

    private void Update()
    {
        ResolvePlayer();
        if (attackInProgress || player == null) return;

        if (!agent.enabled || !agent.isOnNavMesh)
        {
            if (!relocateOnUnreachableForceChase || !TryRelocateForForcedChase())
            {
                UpdateAnimator(0f);
                return;
            }
        }

        if (isBackingOff)
        {
            UpdateBackoff();
            UpdateAnimator(agent.velocity.magnitude);
            return;
        }

        if (ShouldHoldForStairSafety())
        {
            UpdateStairSafetyHold();
            UpdateAnimator(agent.velocity.magnitude);
            return;
        }
        isHoldingForStairSafety = false;

        float dist = Vector3.Distance(transform.position, player.position);
        bool forceChaseActive = Time.time < forcedChaseUntil;

        if (!isChasing && (dist <= detectRange || forceChaseActive)) { isChasing = true; agent.speed = chaseSpeed; }
        else if (isChasing && dist >= loseRange && !forceChaseActive) { isChasing = false; agent.speed = walkSpeed; GoToNextPatrol(); }

        if (isChasing)
        {
            TrySetDestination(player.position);
            ReportNearIfNeeded(dist);

            if (dist <= catchRange && Time.time - lastHitTime >= hitCooldown && CanAttackPlayer(dist))
                StartCoroutine(AttackSequence());
        }
        else Patrol();

        UpdateAnimator(agent.velocity.magnitude);
    }

    private void Patrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.2f)
            GoToNextPatrol();
    }

    private void GoToNextPatrol()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        TrySetDestination(patrolPoints[patrolIndex].position);
        patrolIndex = (patrolIndex + 1) % patrolPoints.Length;
    }

    public void ForceChase(float minDurationSec = 10f)
    {
        ResolvePlayer();
        if (player == null) return;

        forcedChaseUntil = Mathf.Max(forcedChaseUntil, Time.time + Mathf.Max(0.1f, minDurationSec));
        isChasing = true;
        isBackingOff = false;
        if (ShouldHoldForStairSafety())
        {
            UpdateStairSafetyHold();
            return;
        }

        TryRelocateForForcedChase();
        if (agent.enabled && agent.isOnNavMesh)
            agent.isStopped = false;
        agent.speed = chaseSpeed;
        TrySetDestination(player.position);
    }

    private IEnumerator AttackSequence()
    {
        lastHitTime = Time.time;
        attackInProgress = true;
        if (agent.enabled && agent.isOnNavMesh)
        {
            agent.isStopped = true;
            agent.velocity = Vector3.zero;
        }

        FacePlayer(Time.deltaTime, true);
        if (animator != null && hasAttackTrigger)
            animator.SetTrigger(attackTriggerParam);

        Vector3 lungeDirection = GetFlatDirectionToPlayer();
        float elapsed = 0f;
        float previousLungeOffset = 0f;
        while (elapsed < attackWindupSec)
        {
            elapsed += Time.deltaTime;
            FacePlayer(Time.deltaTime, true);
            UpdateAnimator(0f);

            if (!hasAttackTrigger)
                previousLungeOffset = ApplyProceduralAttackLunge(elapsed, lungeDirection, previousLungeOffset);

            yield return null;
        }

        bool hitConnected = HasAttackContact();
        if (playerHealth != null && hitConnected)
            playerHealth.CaughtByEnemy();

        if (ExperimentDirector.Instance != null && ExperimentDirector.Instance.IsRunning)
        {
            Debug.Log(hitConnected
                ? "[KillerAI] 실험 모드 피격 - 공격 후 추격 복귀"
                : "[KillerAI] 공격 회피됨 - 추격 복귀");
            yield return new WaitForSeconds(attackRecoverySec);
            ResumeAfterExperimentHit();
            yield break;
        }

        if (playerHealth == null && hitConnected)
            GameManager.Instance?.SetState(GameManager.GameState.GameOver);
    }

    private void ResumeAfterExperimentHit()
    {
        attackInProgress = false;
        if (agent.enabled && agent.isOnNavMesh)
            agent.isStopped = false;
        isChasing = true;

        if (TryStartBackoff())
            return;

        agent.speed = chaseSpeed;
        if (player != null)
            TrySetDestination(player.position);
    }

    private bool CanAttackPlayer(float straightDistance)
    {
        if (Mathf.Abs(player.position.y - transform.position.y) > attackVerticalTolerance)
            return false;

        if (!requireReachableAttackPath)
            return true;

        if (!NavMesh.SamplePosition(player.position, out NavMeshHit targetHit, 2f, agent.areaMask))
            return false;

        if (!NavMesh.CalculatePath(transform.position, targetHit.position, agent.areaMask, attackPath))
            return false;

        if (attackPath.status != NavMeshPathStatus.PathComplete)
            return false;

        return EstimatePathLength(attackPath, straightDistance) <= catchRange + 0.75f;
    }

    private bool HasAttackContact()
    {
        if (player == null)
            return false;

        float distance = Vector3.Distance(transform.position, player.position);
        if (distance > catchRange + attackContactSlack)
            return false;

        Vector3 toPlayer = GetFlatDirectionToPlayer();
        if (toPlayer.sqrMagnitude > 0.0001f &&
            Vector3.Dot(transform.forward, toPlayer) < attackForwardConeDot)
        {
            return false;
        }

        return CanAttackPlayer(distance);
    }

    private Vector3 GetFlatDirectionToPlayer()
    {
        if (player == null)
            return transform.forward;

        Vector3 direction = player.position - transform.position;
        direction.y = 0f;
        return direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
    }

    private float ApplyProceduralAttackLunge(float elapsed, Vector3 direction, float previousOffset)
    {
        if (proceduralAttackLungeDistance <= 0.01f || proceduralAttackLungeDurationSec <= 0.01f)
            return previousOffset;

        float t = Mathf.Clamp01(elapsed / proceduralAttackLungeDurationSec);
        float targetOffset = Mathf.Sin(t * Mathf.PI) * proceduralAttackLungeDistance;
        float delta = targetOffset - previousOffset;
        if (Mathf.Abs(delta) > 0.0001f && agent.enabled && agent.isOnNavMesh)
            agent.Move(direction * delta);

        return targetOffset;
    }

    private static float EstimatePathLength(NavMeshPath path, float fallback)
    {
        if (path == null || path.corners == null || path.corners.Length < 2)
            return fallback;

        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++)
            length += Vector3.Distance(path.corners[i - 1], path.corners[i]);

        return length;
    }

    private bool TryStartBackoff()
    {
        if (postHitBackoffDistance <= 0.01f || player == null)
            return false;

        Vector3 away = transform.position - player.position;
        away.y = 0f;
        if (away.sqrMagnitude < 0.0001f)
            away = -transform.forward;

        Vector3 desired = transform.position + away.normalized * postHitBackoffDistance;
        if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, postHitBackoffDistance + 1f, agent.areaMask))
            return false;

        isBackingOff = true;
        backoffEndTime = Time.time + Mathf.Max(0.1f, postHitBackoffDurationSec);
        agent.speed = Mathf.Max(walkSpeed, chaseSpeed * 0.65f);
        TrySetDestination(hit.position);
        return true;
    }

    private void UpdateBackoff()
    {
        if (Time.time < backoffEndTime)
            return;

        isBackingOff = false;
        agent.speed = chaseSpeed;
        if (player != null)
            TrySetDestination(player.position);
    }

    private bool TrySetDestination(Vector3 destination)
    {
        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return false;

        return agent.SetDestination(destination);
    }

    private bool TryRelocateForForcedChase()
    {
        if (!relocateOnUnreachableForceChase || agent == null || !agent.enabled || player == null)
            return false;

        if (ShouldHoldForStairSafety())
            return false;

        if (agent.isOnNavMesh && HasCompletePath(transform.position, player.position, 2.5f))
            return false;

        if (!TryFindReachableChaseStartNearPlayer(out Vector3 chaseStart))
            return false;

        bool warped = agent.Warp(chaseStart);
        if (!warped)
            return false;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);

        Debug.Log($"[KillerAI] Relocated to reachable chase start at {chaseStart} because the baked NavMesh route was incomplete.");
        return true;
    }

    private bool TryFindReachableChaseStartNearPlayer(out Vector3 chaseStart)
    {
        chaseStart = Vector3.zero;

        Vector3 playerForward = player.forward;
        playerForward.y = 0f;
        if (playerForward.sqrMagnitude < 0.001f)
            playerForward = Vector3.forward;
        playerForward.Normalize();

        float baseDistance = Mathf.Max(forceChaseRelocationMinDistance, forceChaseRelocationDistance);
        for (int distanceStep = 0; distanceStep < 3; distanceStep++)
        {
            float distance = baseDistance + distanceStep * 1.75f;
            for (int i = 0; i < RelocationAngles.Length; i++)
            {
                Vector3 direction = Quaternion.AngleAxis(RelocationAngles[i], Vector3.up) * playerForward;
                Vector3 desired = player.position + direction.normalized * distance;
                if (!NavMesh.SamplePosition(desired, out NavMeshHit candidate, forceChaseRelocationSearchRadius, agent.areaMask))
                    continue;

                if (Mathf.Abs(candidate.position.y - player.position.y) > forceChaseRelocationVerticalTolerance)
                    continue;

                if (IsPointInsideStairSafetyVolume(candidate.position))
                    continue;

                if (Vector3.Distance(candidate.position, player.position) < forceChaseRelocationMinDistance)
                    continue;

                if (!HasCompletePath(candidate.position, player.position, 2.5f))
                    continue;

                chaseStart = candidate.position;
                return true;
            }
        }

        return false;
    }

    private bool ShouldHoldForStairSafety()
    {
        if (!avoidStairRouteDuringChase || player == null)
            return false;

        if (IsPointInsideStairSafetyVolume(player.position))
            return true;

        return isHoldingForStairSafety && Time.time < stairSafetyHoldUntil;
    }

    private void UpdateStairSafetyHold()
    {
        bool playerInside = player != null && IsPointInsideStairSafetyVolume(player.position);
        if (playerInside)
            stairSafetyHoldUntil = Mathf.Max(stairSafetyHoldUntil, Time.time + Mathf.Max(0.1f, stairSafetyHoldMinSec));
        else if (Time.time >= stairSafetyHoldUntil)
        {
            isHoldingForStairSafety = false;
            return;
        }

        isHoldingForStairSafety = true;
        isChasing = true;

        if (agent == null || !agent.enabled || !agent.isOnNavMesh)
            return;

        agent.isStopped = false;
        agent.speed = walkSpeed;

        if (!NavMesh.SamplePosition(stairSafetyHoldPosition, out NavMeshHit holdHit, stairSafetyHoldSampleRadius, agent.areaMask))
            return;

        if (IsPointInsideStairSafetyVolume(transform.position))
        {
            agent.Warp(holdHit.position);
            return;
        }

        if (!HasCompletePath(transform.position, holdHit.position, 2f))
        {
            agent.Warp(holdHit.position);
            return;
        }

        TrySetDestination(holdHit.position);
    }

    private bool IsPointInsideStairSafetyVolume(Vector3 point)
    {
        Vector3 half = stairSafetySize * 0.5f;
        Vector3 offset = point - stairSafetyCenter;
        return Mathf.Abs(offset.x) <= half.x &&
               Mathf.Abs(offset.y) <= half.y &&
               Mathf.Abs(offset.z) <= half.z;
    }

    private bool HasCompletePath(Vector3 from, Vector3 to, float targetSampleDistance)
    {
        if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, 2f, agent.areaMask))
            return false;

        if (!NavMesh.SamplePosition(to, out NavMeshHit toHit, targetSampleDistance, agent.areaMask))
            return false;

        NavMeshPath path = new NavMeshPath();
        return NavMesh.CalculatePath(fromHit.position, toHit.position, agent.areaMask, path) &&
               path.status == NavMeshPathStatus.PathComplete;
    }

    private void ResolvePlayer()
    {
        if (player != null) return;

        GameObject p = GameObject.FindGameObjectWithTag(playerTag);
        if (p == null) return;

        player = p.transform;
        playerHealth = p.GetComponent<PlayerHealth>();
    }

    private void ReportNearIfNeeded(float distance)
    {
        if (distance > killerNearDistance) return;
        if (ExperimentDirector.Instance == null || !ExperimentDirector.Instance.IsRunning) return;
        if (Time.time - lastNearReportTime < killerNearReportInterval) return;

        lastNearReportTime = Time.time;
        ExperimentDirector.Instance.ReportKillerNear(distance);
    }

    private void FacePlayer(float deltaTime, bool immediate = false)
    {
        if (player == null) return;

        Vector3 toPlayer = player.position - transform.position;
        toPlayer.y = 0f;
        if (toPlayer.sqrMagnitude < 0.0001f) return;

        Quaternion target = Quaternion.LookRotation(toPlayer.normalized, Vector3.up);
        transform.rotation = immediate
            ? target
            : Quaternion.RotateTowards(transform.rotation, target, facePlayerTurnSpeed * deltaTime);
    }

    private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType type)
    {
        if (animator == null || string.IsNullOrEmpty(parameterName)) return false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.type == type && parameter.name == parameterName)
                return true;
        }

        return false;
    }

    private bool TryGetAnimatorParameterType(string parameterName, out AnimatorControllerParameterType type)
    {
        type = default;
        if (animator == null || string.IsNullOrEmpty(parameterName)) return false;

        foreach (AnimatorControllerParameter parameter in animator.parameters)
        {
            if (parameter.name != parameterName) continue;

            type = parameter.type;
            return true;
        }

        return false;
    }

    // 개별 State + Any State 전환 구조 — 한 개만 1, 나머지 0
    private void UpdateAnimator(float speed)
    {
        if (animator == null) return;

        bool isIdle = speed < 0.1f;
        bool isRun  = speed > walkSpeed;
        bool isWalk = !isIdle && !isRun;

        int state = isIdle ? 0 : (isWalk ? 1 : 2);
        if (state == lastAnimatorState && UsesTriggerMovementParams())
            return;

        SetAnimatorMovementParam(idleParam, idleParamType, hasIdleParam, isIdle);
        SetAnimatorMovementParam(walkParam, walkParamType, hasWalkParam, isWalk);
        SetAnimatorMovementParam(runParam, runParamType, hasRunParam, isRun);
        lastAnimatorState = state;
    }

    private bool UsesTriggerMovementParams()
    {
        return (hasIdleParam && idleParamType == AnimatorControllerParameterType.Trigger) ||
               (hasWalkParam && walkParamType == AnimatorControllerParameterType.Trigger) ||
               (hasRunParam && runParamType == AnimatorControllerParameterType.Trigger);
    }

    private void SetAnimatorMovementParam(
        string parameterName,
        AnimatorControllerParameterType parameterType,
        bool exists,
        bool active)
    {
        if (!exists || string.IsNullOrEmpty(parameterName)) return;

        switch (parameterType)
        {
            case AnimatorControllerParameterType.Float:
                animator.SetFloat(parameterName, active ? 1f : 0f);
                break;
            case AnimatorControllerParameterType.Int:
                animator.SetInteger(parameterName, active ? 1 : 0);
                break;
            case AnimatorControllerParameterType.Bool:
                animator.SetBool(parameterName, active);
                break;
            case AnimatorControllerParameterType.Trigger:
                if (active)
                    animator.SetTrigger(parameterName);
                else
                    animator.ResetTrigger(parameterName);
                break;
        }
    }

    // 에디터에서 감지 범위 시각화
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow; Gizmos.DrawWireSphere(transform.position, detectRange);
        Gizmos.color = Color.cyan;   Gizmos.DrawWireSphere(transform.position, killerNearDistance);
        Gizmos.color = Color.red;    Gizmos.DrawWireSphere(transform.position, catchRange);
    }
}
