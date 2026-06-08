using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 1인칭 플레이어 컨트롤러 (비전투·도주·은신 중심)
/// RequireComponent: CharacterController
/// 자식 오브젝트: GroundCheck (발 위치 빈 오브젝트)
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FPSPlayerController : MonoBehaviour
{
    // ─── 이동 속도 ────────────────────────────────
    [Header("이동 속도 (m/s)")]
    [SerializeField] float walkSpeed    = 3.0f;
    [SerializeField] float sprintSpeed  = 6.0f;
    [SerializeField] float crouchSpeed  = 1.5f;

    // ─── 스태미나 ──────────────────────────────────
    [Header("스태미나")]
    [SerializeField] float maxStamina       = 100f;
    [SerializeField] float staminaDrainRate = 20f;   // 달리기 중 초당 소모량
    [SerializeField] float staminaRegenRate = 10f;   // 달리지 않을 때 초당 회복량
    [SerializeField] float staminaRegenDelay = 1.5f; // 달리기 멈춘 후 회복 시작까지 딜레이

    // ─── 앉기 ──────────────────────────────────────
    [Header("앉기")]
    [SerializeField] float standHeight         = 1.8f;
    [SerializeField] float crouchHeight        = 0.9f;
    [SerializeField] float standCenterY        = 0f;
    [SerializeField] float crouchCenterY       = -0.45f;
    [SerializeField] float crouchTransitionSpd = 8f;

    // ─── 중력 ──────────────────────────────────────
    [Header("중력")]
    [SerializeField] float gravity           = -15f;
    [SerializeField] Transform groundCheck;           // 발 위치 빈 오브젝트
    [SerializeField] float groundCheckRadius = 0.25f;
    [SerializeField] LayerMask groundMask;

    // ─── 오디오 ────────────────────────────────────
    [Header("발소리")]
    [SerializeField] AudioSource footstepSource;
    [SerializeField] AudioClip[] walkFootsteps;
    [SerializeField] AudioClip[] sprintFootsteps;
    [SerializeField] AudioClip[] crouchFootsteps;
    [SerializeField] float footstepInterval      = 0.5f;   // 걷기 발소리 간격
    [SerializeField] float sprintFootstepInterval = 0.3f;  // 달리기 발소리 간격

    // ─── 공개 상태 ─────────────────────────────────
    public enum PlayerState { Normal, Sprinting, Crouching, Hiding }
    public PlayerState State        { get; private set; } = PlayerState.Normal;
    public float       Stamina      { get; private set; }
    public bool        IsMoving     { get; private set; }
    public bool        IsGrounded   { get; private set; }
    public float       CurrentSpeed { get; private set; }

    // ─── 이벤트 (SmartThings 연동용) ───────────────
    public System.Action<PlayerState> OnStateChanged;  // 상태 변경 시 호출
    public System.Action<float>       OnStaminaRatio;  // 스태미나 비율 0~1

    // ─── 컴포넌트 ──────────────────────────────────
    CharacterController _cc;

    // ─── 내부 변수 ─────────────────────────────────
    Vector3 _moveVelocity;
    float   _verticalVelocity;
    bool    _isCrouchHeld;
    bool    _canStand  = true;
    float   _regenDelayTimer = 0f;
    float   _footstepTimer   = 0f;

    // ─────────────────────────────────────────────────
    void Awake()
    {
        _cc     = GetComponent<CharacterController>();
        Stamina = maxStamina;
    }

    void Update()
    {
        HandleGroundCheck();
        HandleGravity();
        HandleCrouch();
        HandleMovement();
        HandleStamina();
        HandleFootstep();
    }

    // ─── 지면 감지 ─────────────────────────────────
    void HandleGroundCheck()
    {
        if (groundCheck != null)
            IsGrounded = Physics.CheckSphere(groundCheck.position, groundCheckRadius, groundMask);
        else
            IsGrounded = _cc.isGrounded;

        // 지면에 닿았을 때 낙하 속도 초기화
        if (IsGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
    }

    // ─── 중력 계산 ─────────────────────────────────
    void HandleGravity()
    {
        if (!IsGrounded)
            _verticalVelocity += gravity * Time.deltaTime;
    }

    // ─── WASD 이동 ─────────────────────────────────
    void HandleMovement()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        // 입력값 수집
        float h = 0f, v = 0f;
        if (kb.wKey.isPressed || kb.upArrowKey.isPressed)    v += 1f;
        if (kb.sKey.isPressed || kb.downArrowKey.isPressed)  v -= 1f;
        if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) h += 1f;
        if (kb.aKey.isPressed || kb.leftArrowKey.isPressed)  h -= 1f;

        // 달리기 의도 판별
        bool sprintHeld   = kb.leftShiftKey.isPressed;
        bool wantsToSprint = sprintHeld
                             && Stamina > 0f
                             && State != PlayerState.Crouching
                             && State != PlayerState.Hiding;

        // 숨기 상태가 아닐 때만 상태 전환
        if (State != PlayerState.Hiding)
        {
            PlayerState next;
            if (_isCrouchHeld)
                next = PlayerState.Crouching;
            else if (wantsToSprint && (h != 0f || v != 0f))
                next = PlayerState.Sprinting;
            else
                next = PlayerState.Normal;

            SetState(next);
        }

        // 속도 결정
        float speed = State switch
        {
            PlayerState.Sprinting => sprintSpeed,
            PlayerState.Crouching => crouchSpeed,
            PlayerState.Hiding    => 0f,
            _                     => walkSpeed,
        };

        // 이동 벡터 계산 (대각선 이동 정규화)
        Vector3 dir = transform.right * h + transform.forward * v;
        if (dir.magnitude > 1f) dir.Normalize();

        IsMoving     = dir.magnitude > 0.05f;
        CurrentSpeed = IsMoving ? speed : 0f;

        // 수평 이동 + 수직 속도 합산 후 적용
        _moveVelocity = dir * speed;
        _cc.Move((_moveVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime);
    }

    // ─── 앉기 처리 ─────────────────────────────────
    void HandleCrouch()
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        _isCrouchHeld = kb.leftCtrlKey.isPressed || kb.cKey.isPressed;

        // 일어설 때 머리 위 천장 체크
        if (!_isCrouchHeld)
            _canStand = !Physics.CheckSphere(
                transform.position + Vector3.up * (standHeight - 0.1f), 0.2f);

        bool shouldCrouch = _isCrouchHeld || !_canStand;
        float targetH  = shouldCrouch ? crouchHeight  : standHeight;
        float targetCY = shouldCrouch ? crouchCenterY : standCenterY;

        _cc.height = Mathf.Lerp(_cc.height, targetH,
            Time.deltaTime * crouchTransitionSpd);

        var center = _cc.center;
        center.y   = Mathf.Lerp(center.y, targetCY,
            Time.deltaTime * crouchTransitionSpd);
        _cc.center = center;
    }

    // ─── 스태미나 관리 ──────────────────────────────
    void HandleStamina()
    {
        if (State == PlayerState.Sprinting && IsMoving)
        {
            Stamina          = Mathf.Max(0f, Stamina - staminaDrainRate * Time.deltaTime);
            _regenDelayTimer = staminaRegenDelay;

            // 스태미나 소진 → 강제 Normal 전환
            if (Stamina <= 0f) SetState(PlayerState.Normal);
        }
        else
        {
            if (_regenDelayTimer > 0f)
                _regenDelayTimer -= Time.deltaTime;
            else
                Stamina = Mathf.Min(maxStamina, Stamina + staminaRegenRate * Time.deltaTime);
        }

        OnStaminaRatio?.Invoke(Stamina / maxStamina);
    }

    // ─── 발소리 ────────────────────────────────────
    void HandleFootstep()
    {
        if (!IsMoving || !IsGrounded || footstepSource == null) return;

        float interval = State == PlayerState.Sprinting
            ? sprintFootstepInterval : footstepInterval;

        _footstepTimer += Time.deltaTime;
        if (_footstepTimer < interval) return;
        _footstepTimer = 0f;

        AudioClip[] pool = State switch
        {
            PlayerState.Sprinting => sprintFootsteps,
            PlayerState.Crouching => crouchFootsteps,
            _                     => walkFootsteps,
        };

        if (pool != null && pool.Length > 0)
        {
            AudioClip clip = pool[Random.Range(0, pool.Length)];
            footstepSource.PlayOneShot(clip);
        }
    }

    // ─── 상태 전환 ─────────────────────────────────
    void SetState(PlayerState next)
    {
        if (next == State) return;
        State = next;
        OnStateChanged?.Invoke(next);
    }

    // ─── 외부 API (숨기 시스템에서 호출) ──────────
    public void EnterHiding()  => SetState(PlayerState.Hiding);
    public void ExitHiding()   => SetState(PlayerState.Normal);

    // ─── 에디터 Gizmo ──────────────────────────────
    void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = IsGrounded ? Color.green : Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, groundCheckRadius);
    }
}
