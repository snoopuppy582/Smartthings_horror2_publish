using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 언데드 기사 추적 AI.
/// NavMeshAgent + 상태 머신 (Patrol → Suspicious → Chasing → Catch)
/// 각 상태 전환 시 SmartThings 이벤트를 GameManager를 통해 전송한다.
///
/// 씬 설정:
///   - NavMeshSurface로 바닥을 Bake한 뒤 사용
///   - PatrolPoints 배열에 순찰 경유지 Transform 연결
///   - Player 레이어를 Inspector에서 설정 (시야 감지용)
/// </summary>
[RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
public class EnemyAI : MonoBehaviour
{
    // ── 공개 상태 (다른 스크립트에서 참조 가능) ──────────────────
    public enum State { Patrol, Suspicious, Chasing, Catch }
    public State CurrentState { get; private set; } = State.Patrol;

    [Header("감지")]
    [SerializeField] private float patrolDetectRange = 12f;     // Patrol 중 감지 거리
    [SerializeField] private float chaseDetectRange = 20f;      // Chasing 중 재감지 거리
    [SerializeField] private float fieldOfViewAngle = 110f;     // 시야각(도)
    [SerializeField] private float catchRange = 1.5f;           // 잡기 거리
    [SerializeField] private LayerMask playerLayer;
    [SerializeField] private LayerMask obstacleMask;            // 시야 차단 레이어
    [SerializeField] private Transform eyePosition;             // 눈 위치 Transform

    [Header("이동 속도")]
    [SerializeField] private float patrolSpeed = 1.5f;
    [SerializeField] private float suspiciousSpeed = 2.0f;
    [SerializeField] private float chaseSpeed = 5.5f;

    [Header("순찰 경유지")]
    [SerializeField] private Transform[] patrolPoints;
    [SerializeField] private float patrolWaitTime = 2f;

    [Header("상태 유지 시간")]
    [SerializeField] private float suspiciousTimeout = 4f;      // Suspicious 후 포기까지
    [SerializeField] private float lostPlayerTimeout = 6f;      // Chasing 중 시야 놓친 후 포기까지

    [Header("실험 모드")]
    [SerializeField] private float experimentHitCooldown = 4f;
    [SerializeField] private float killerNearReportInterval = 15f;

    [Header("오디오")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] footstepClips;
    [SerializeField] private AudioClip growlClip;
    [SerializeField] private AudioClip roarClip;                // 추격 시작 울음

    // 컴포넌트
    private NavMeshAgent _agent;
    private Animator _anim;
    private Transform _player;
    private PlayerHealth _playerHealth;

    // 순찰 인덱스
    private int _patrolIndex;
    private bool _waitingAtPoint;

    // 타이머
    private float _suspiciousTimer;
    private float _lostPlayerTimer;
    private Vector3 _lastKnownPlayerPos;

    // 이미 전송한 이벤트 중복 방지
    private State _lastReportedState = State.Patrol;
    private float _lastExperimentHitTime = -999f;
    private float _lastKillerNearReportTime = -999f;

    // ── Animator 파라미터 해시 ─────────────────────────────────
    private static readonly int AnimSpeed    = Animator.StringToHash("Speed");
    private static readonly int AnimChasing  = Animator.StringToHash("IsChasing");
    private static readonly int AnimAttack   = Animator.StringToHash("Attack");

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _anim = GetComponent<Animator>();
    }

    void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null)
        {
            _player = playerObj.transform;
            _playerHealth = playerObj.GetComponent<PlayerHealth>();
        }

        SetState(State.Patrol);
    }

    void Update()
    {
        if (_player == null) return;
        if (GameManager.Instance != null &&
            (GameManager.Instance.CurrentState == GameManager.GameState.GameOver ||
             GameManager.Instance.CurrentState == GameManager.GameState.Paused))
            return;

        switch (CurrentState)
        {
            case State.Patrol:      UpdatePatrol();     break;
            case State.Suspicious:  UpdateSuspicious(); break;
            case State.Chasing:     UpdateChasing();    break;
            case State.Catch:                           break;
        }

        UpdateAnimator();
    }

    // ── Patrol ─────────────────────────────────────────────────

    private void UpdatePatrol()
    {
        // 시야 내 플레이어 감지 → Suspicious
        if (CanSeePlayer(patrolDetectRange))
        {
            SetState(State.Suspicious);
            return;
        }
        // 소리(발소리 거리) 감지 — 단순 거리 체크
        float dist = Vector3.Distance(transform.position, _player.position);
        if (dist < patrolDetectRange * 0.4f)
        {
            SetState(State.Suspicious);
            return;
        }

        MoveToNextPatrolPoint();
    }

    private void MoveToNextPatrolPoint()
    {
        if (patrolPoints == null || patrolPoints.Length == 0) return;
        if (_waitingAtPoint) return;

        if (!_agent.pathPending && _agent.remainingDistance < 0.5f)
            StartCoroutine(WaitAndNextPatrolPoint());
    }

    private IEnumerator WaitAndNextPatrolPoint()
    {
        _waitingAtPoint = true;
        yield return new WaitForSeconds(patrolWaitTime);
        _patrolIndex = (_patrolIndex + 1) % patrolPoints.Length;
        _agent.SetDestination(patrolPoints[_patrolIndex].position);
        _waitingAtPoint = false;
    }

    // ── Suspicious ──────────────────────────────────────────────

    private void UpdateSuspicious()
    {
        _suspiciousTimer += Time.deltaTime;

        // 계속 시야에 있으면 → Chase
        if (CanSeePlayer(patrolDetectRange))
        {
            _lastKnownPlayerPos = _player.position;
            if (_suspiciousTimer > 1.0f) // 1초 지속 확인 후 추격
            {
                SetState(State.Chasing);
                return;
            }
        }

        // 마지막 감지 위치로 이동
        _agent.SetDestination(_lastKnownPlayerPos);

        // 타임아웃 → 포기
        if (_suspiciousTimer > suspiciousTimeout)
            SetState(State.Patrol);
    }

    // ── Chasing ─────────────────────────────────────────────────

    private void UpdateChasing()
    {
        float dist = Vector3.Distance(transform.position, _player.position);

        // ghost_near 거리 진입 (6m 이내) 시 상태 보고
        if (dist < 6f && ExperimentDirector.Instance != null && ExperimentDirector.Instance.IsRunning &&
            Time.time - _lastKillerNearReportTime >= killerNearReportInterval)
        {
            _lastKillerNearReportTime = Time.time;
            ExperimentDirector.Instance.ReportKillerNear(dist);
        }

        if (dist < 6f && GameManager.Instance?.CurrentState != GameManager.GameState.GhostNear
                      && GameManager.Instance?.CurrentState != GameManager.GameState.Chase)
        {
            GameManager.Instance?.SetState(GameManager.GameState.GhostNear);
        }

        // 잡기 거리 진입
        if (dist < catchRange && Time.time - _lastExperimentHitTime >= experimentHitCooldown)
        {
            SetState(State.Catch);
            return;
        }

        // 시야 유지 중
        if (CanSeePlayer(chaseDetectRange))
        {
            _lastKnownPlayerPos = _player.position;
            _lostPlayerTimer = 0f;
            _agent.SetDestination(_player.position);
        }
        else
        {
            // 시야 잃었을 때 마지막 위치로
            _lostPlayerTimer += Time.deltaTime;
            _agent.SetDestination(_lastKnownPlayerPos);

            if (_lostPlayerTimer > lostPlayerTimeout)
                SetState(State.Patrol);
        }
    }

    // ── Catch ──────────────────────────────────────────────────

    private IEnumerator CatchSequence()
    {
        _agent.isStopped = true;
        _anim.SetTrigger(AnimAttack);

        if (audioSource != null && roarClip != null)
            audioSource.PlayOneShot(roarClip);

        yield return new WaitForSeconds(0.5f);

        _lastExperimentHitTime = Time.time;
        _playerHealth?.CaughtByEnemy();

        yield return new WaitForSeconds(0.6f);

        if (ExperimentDirector.Instance != null && ExperimentDirector.Instance.IsRunning)
            SetState(State.Chasing);
    }

    // ── 상태 전환 ───────────────────────────────────────────────

    private void SetState(State newState)
    {
        if (CurrentState == newState) return;
        CurrentState = newState;

        switch (newState)
        {
            case State.Patrol:
                _agent.speed = patrolSpeed;
                _agent.isStopped = false;
                _suspiciousTimer = 0f;
                _lostPlayerTimer = 0f;
                if (patrolPoints != null && patrolPoints.Length > 0)
                    _agent.SetDestination(patrolPoints[_patrolIndex].position);
                // 플레이어가 탈출한 경우 → recovery
                if (_lastReportedState == State.Chasing || _lastReportedState == State.Suspicious)
                    GameManager.Instance?.SetState(GameManager.GameState.Exploring);
                break;

            case State.Suspicious:
                _agent.speed = suspiciousSpeed;
                _agent.isStopped = false;
                _suspiciousTimer = 0f;
                _lastKnownPlayerPos = _player.position;
                // ghost_hint 이벤트
                if (_lastReportedState == State.Patrol)
                    GameManager.Instance?.SetState(GameManager.GameState.GhostHint);
                break;

            case State.Chasing:
                _agent.speed = chaseSpeed;
                _agent.isStopped = false;
                _lostPlayerTimer = 0f;
                GameManager.Instance?.SetState(GameManager.GameState.Chase);
                if (audioSource != null && roarClip != null)
                    audioSource.PlayOneShot(roarClip);
                break;

            case State.Catch:
                StartCoroutine(CatchSequence());
                break;
        }

        _lastReportedState = newState;
    }

    // ── 시야 감지 ───────────────────────────────────────────────

    private bool CanSeePlayer(float range)
    {
        if (_player == null) return false;
        float dist = Vector3.Distance(transform.position, _player.position);
        if (dist > range) return false;

        Vector3 dirToPlayer = (_player.position - EyePos).normalized;
        float angle = Vector3.Angle(transform.forward, dirToPlayer);
        if (angle > fieldOfViewAngle * 0.5f) return false;

        // 장애물 체크 (Raycast)
        if (Physics.Raycast(EyePos, dirToPlayer, dist, obstacleMask))
            return false;

        return true;
    }

    private Vector3 EyePos =>
        eyePosition != null ? eyePosition.position : transform.position + Vector3.up * 1.6f;

    // ── Animator 업데이트 ────────────────────────────────────────

    private void UpdateAnimator()
    {
        _anim.SetFloat(AnimSpeed, _agent.velocity.magnitude);
        _anim.SetBool(AnimChasing, CurrentState == State.Chasing || CurrentState == State.Catch);
    }

    // ── 에디터 시각화 ────────────────────────────────────────────

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, patrolDetectRange);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, catchRange);
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 6f); // ghost_near 반경
    }
#endif
}
