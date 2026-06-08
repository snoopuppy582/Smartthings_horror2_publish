using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// New Input System(InputSystem_Actions) 기반 1인칭 캐릭터 컨트롤러.
/// CharacterController + 카메라 룩 + 크라우치 + 스프린트 구현.
///
/// 사용 전 필수:
///   1. InputSystem_Actions.inputactions 선택 → Inspector에서 "Generate C# Class" 체크 → Apply
///   2. 이 컴포넌트를 Player GameObject에 부착
///   3. Camera Holder(자식 Transform)를 Inspector에 연결
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonController : MonoBehaviour
{
    [Header("이동")]
    [SerializeField] private float walkSpeed = 2.9f;
    [SerializeField] private float sprintSpeed = 4.5f;
    [SerializeField] private float crouchSpeed = 1.5f;
    [SerializeField] private float acceleration = 16f;
    [SerializeField] private float deceleration = 22f;
    [SerializeField] private float airControl = 0.35f;
    [SerializeField] private float gravity = -15f;
    [SerializeField] private float jumpHeight = 1.0f;
    [SerializeField] private LayerMask groundMask;

    [Header("카메라")]
    [SerializeField] private Transform cameraHolder;
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float maxLookAngle = 80f;

    [Header("크라우치")]
    [SerializeField] private float standingHeight = 1.55f;
    [SerializeField] private float crouchHeight = 0.9f;
    [SerializeField] private float crouchTransitionSpeed = 8f;

    [Header("캡슐/문 통과")]
    [SerializeField] private float controllerRadius = 0.22f;
    [SerializeField] private float cameraStandingHeight = 1.32f;
    [SerializeField] private bool enableDoorwayShoulderAssist = true;
    [SerializeField] private float shoulderAssistDistance = 0.1f;
    [SerializeField] private float shoulderAssistForwardNudge = 0.12f;

    [Header("머리 보빙 (Head Bob)")]
    [SerializeField] private float bobFrequency = 1.8f;
    [SerializeField] private float bobAmplitude = 0.025f;

    [Header("계단 보정")]
    [SerializeField] private bool enableStepAssist = true;
    [SerializeField] private float assistedStepHeight = 0.45f;
    [SerializeField] private float stepProbeDistance = 0.5f;
    [SerializeField] private float stepForwardNudge = 0.2f;
    [SerializeField] private LayerMask stepAssistMask = ~0;

    // 컴포넌트
    private CharacterController _cc;
    private InputSystem_Actions _input;

    // 상태
    private Vector3 _velocity;
    private Vector3 _horizontalVelocity;
    private float _xRotation;
    private bool _isCrouching;
    private bool _isGrounded;
    private bool _movementLocked;
    private float _bobTimer;
    private float _targetHeight;
    private Vector3 _cameraLocalOrigin;
    private Vector2 _currentMoveInput;
    private Vector3 _currentMoveWorldDirection;
    private bool _syntheticInputActive;
    private Vector2 _syntheticMoveInput;
    private Vector2 _syntheticLookInput;
    private bool _syntheticSprint;
    private StairTraversalAssistZone _stairAssistZone;
    private float _nextStairAssistLookupTime;
    private bool _inputCallbacksRegistered;

    public bool IsMovementLocked => _movementLocked;
    public Vector2 CurrentMoveInput => _currentMoveInput;
    public Vector3 CurrentMoveWorldDirection => _currentMoveWorldDirection;
    public bool SyntheticInputActive => _syntheticInputActive;
    public bool StepAssistEnabled => enableStepAssist;

    void Awake()
    {
        EnsureRuntimeReferences();
        NormalizeControllerScale();
        _targetHeight = standingHeight;

        if (cameraHolder != null)
            _cameraLocalOrigin = cameraHolder.localPosition;
    }

    void OnEnable()
    {
        EnsureRuntimeReferences();
        if (_input == null)
        {
            Debug.LogWarning("[FirstPersonController] Input actions unavailable on enable.");
            return;
        }

        try
        {
            _input.Enable();
            if (!_inputCallbacksRegistered)
            {
                // 점프 이벤트 구독
                _input.Player.Jump.performed += OnJump;
                // 크라우치 토글
                _input.Player.Crouch.performed += OnCrouchToggle;
                _inputCallbacksRegistered = true;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[FirstPersonController] Failed to enable input actions: {ex.Message}");
        }
    }

    void OnDisable()
    {
        if (_input == null)
            return;

        try
        {
            if (_inputCallbacksRegistered)
            {
                _input.Player.Jump.performed -= OnJump;
                _input.Player.Crouch.performed -= OnCrouchToggle;
                _inputCallbacksRegistered = false;
            }

            _input.Disable();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[FirstPersonController] Failed to disable input actions cleanly: {ex.Message}");
        }
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (_cc == null || !_cc.enabled) return;
        if (_movementLocked) return;
        if (GameManager.Instance != null &&
            (GameManager.Instance.CurrentState == GameManager.GameState.GameOver ||
             GameManager.Instance.CurrentState == GameManager.GameState.Paused))
            return;

        HandleGroundCheck();
        HandleLook();
        HandleMove();
        HandleGravity();
        if (enableStepAssist && _currentMoveInput.sqrMagnitude > 0.01f && TryStairTraversalAssist())
            _velocity.y = Mathf.Max(_velocity.y, -0.5f);
        HandleCrouchTransition();
        HandleHeadBob();
    }

    // ── 지면 체크 ───────────────────────────────────────────────

    private void HandleGroundCheck()
    {
        _isGrounded = _cc.isGrounded;
        if (_isGrounded && _velocity.y < 0f)
            _velocity.y = -2f;
    }

    // ── 카메라 룩 ───────────────────────────────────────────────

    private void HandleLook()
    {
        Vector2 look = _syntheticInputActive ? _syntheticLookInput : _input.Player.Look.ReadValue<Vector2>();
        float mouseX = look.x * mouseSensitivity;
        float mouseY = look.y * mouseSensitivity;

        _xRotation -= mouseY;
        _xRotation = Mathf.Clamp(_xRotation, -maxLookAngle, maxLookAngle);

        if (cameraHolder != null)
            cameraHolder.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);

        transform.Rotate(Vector3.up * mouseX);
    }

    // ── 이동 ────────────────────────────────────────────────────

    private void HandleMove()
    {
        Vector2 rawMove = _syntheticInputActive ? _syntheticMoveInput : _input.Player.Move.ReadValue<Vector2>();
        Vector2 move = Vector2.ClampMagnitude(rawMove, 1f);
        _currentMoveInput = move;
        bool sprinting = (_syntheticInputActive ? _syntheticSprint : _input.Player.Sprint.IsPressed()) && !_isCrouching;

        float speed = _isCrouching ? crouchSpeed : (sprinting ? sprintSpeed : walkSpeed);
        Vector3 dir = transform.right * move.x + transform.forward * move.y;
        if (dir.sqrMagnitude > 1f)
            dir.Normalize();
        _currentMoveWorldDirection = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector3.zero;

        if (enableStepAssist && move.sqrMagnitude > 0.01f && TryStairTraversalAssist())
            _velocity.y = Mathf.Max(_velocity.y, -0.5f);

        Vector3 targetVelocity = dir * speed;
        float moveRate = move.sqrMagnitude > 0.01f ? acceleration : deceleration;
        if (!_isGrounded)
            moveRate *= airControl;

        _horizontalVelocity = Vector3.MoveTowards(
            _horizontalVelocity,
            targetVelocity,
            moveRate * Time.deltaTime);

        Vector3 horizontalMove = _horizontalVelocity * Time.deltaTime;
        CollisionFlags flags = _cc.Move(horizontalMove);

        if (enableStepAssist && _isGrounded && move.sqrMagnitude > 0.01f &&
            (flags & CollisionFlags.Sides) != 0)
        {
            bool stepped = TryStepAssist(horizontalMove);
            if (!stepped && enableDoorwayShoulderAssist)
                TryDoorwayShoulderAssist(horizontalMove);
        }

        if (enableStepAssist && move.sqrMagnitude > 0.01f && TryStairTraversalAssist())
            _velocity.y = Mathf.Max(_velocity.y, -0.5f);

        // 이동 중 머리 보빙 타이머
        if (_horizontalVelocity.sqrMagnitude > 0.05f && _isGrounded)
            _bobTimer += Time.deltaTime * bobFrequency * (sprinting ? 1.35f : 1f);
        else
            _bobTimer = 0f;
    }

    // ── 중력 / 점프 ─────────────────────────────────────────────

    private void HandleGravity()
    {
        _velocity.y += gravity * Time.deltaTime;
        _cc.Move(_velocity * Time.deltaTime);
    }

    private bool TryStepAssist(Vector3 attemptedMove)
    {
        Vector3 moveDir = attemptedMove;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.0001f) return false;

        moveDir.Normalize();
        Vector3 lowerProbe = transform.position + Vector3.up * Mathf.Max(0.05f, _cc.skinWidth + 0.08f);
        Vector3 upperProbe = transform.position + Vector3.up * Mathf.Min(_cc.height - 0.1f, assistedStepHeight + 0.12f);

        bool lowBlocked = Physics.Raycast(lowerProbe, moveDir, stepProbeDistance, stepAssistMask, QueryTriggerInteraction.Ignore);
        bool upperClear = !Physics.Raycast(upperProbe, moveDir, stepProbeDistance, stepAssistMask, QueryTriggerInteraction.Ignore);
        if (!lowBlocked || !upperClear) return false;

        float stepHeight = Mathf.Min(assistedStepHeight, Mathf.Max(0.05f, _cc.stepOffset + 0.05f));
        _cc.Move(Vector3.up * stepHeight);
        _cc.Move(moveDir * Mathf.Max(attemptedMove.magnitude, stepForwardNudge));
        _cc.Move(Vector3.down * stepHeight);
        return true;
    }

    private void TryDoorwayShoulderAssist(Vector3 attemptedMove)
    {
        Vector3 moveDir = attemptedMove;
        moveDir.y = 0f;
        if (moveDir.sqrMagnitude < 0.0001f) return;

        moveDir.Normalize();
        Vector3 side = Vector3.Cross(Vector3.up, moveDir).normalized;
        float forward = Mathf.Max(attemptedMove.magnitude, shoulderAssistForwardNudge);

        if (TryShoulderMove(side, moveDir, forward)) return;
        TryShoulderMove(-side, moveDir, forward);
    }

    private bool TryStairTraversalAssist()
    {
        if (_cc == null || !_cc.enabled)
            return false;

        if (_currentMoveWorldDirection.sqrMagnitude < 0.0001f)
            return false;

        if (_stairAssistZone == null && Time.time >= _nextStairAssistLookupTime)
        {
            _nextStairAssistLookupTime = Time.time + 0.5f;
            _stairAssistZone = FindFirstObjectByType<StairTraversalAssistZone>();
        }

        if (_stairAssistZone != null)
            return _stairAssistZone.TryAssist(_cc, _currentMoveWorldDirection, Time.deltaTime);

        return false;
    }

    private bool TryShoulderMove(Vector3 side, Vector3 forwardDir, float forwardDistance)
    {
        Vector3 before = transform.position;
        _cc.Move(side * shoulderAssistDistance);
        CollisionFlags flags = _cc.Move(forwardDir * forwardDistance);
        if ((flags & CollisionFlags.Sides) == 0)
            return true;

        _cc.Move(before - transform.position);
        return false;
    }

    private void OnJump(InputAction.CallbackContext ctx)
    {
        if (_movementLocked) return;
        if (_isGrounded && !_isCrouching)
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    // ── 크라우치 ────────────────────────────────────────────────

    private void OnCrouchToggle(InputAction.CallbackContext ctx)
    {
        if (_movementLocked) return;
        // 서있을 때만 웅크리기 (천장 확인 포함)
        if (!_isCrouching)
        {
            _isCrouching = true;
            _targetHeight = crouchHeight;
        }
        else
        {
            // 일어설 공간 확인
            if (!Physics.Raycast(transform.position, Vector3.up, standingHeight - crouchHeight + 0.1f))
            {
                _isCrouching = false;
                _targetHeight = standingHeight;
            }
        }
    }

    private void HandleCrouchTransition()
    {
        float currentHeight = _cc.height;
        if (Mathf.Abs(currentHeight - _targetHeight) > 0.01f)
        {
            float newHeight = Mathf.Lerp(currentHeight, _targetHeight, Time.deltaTime * crouchTransitionSpeed);
            float delta = newHeight - currentHeight;
            _cc.height = newHeight;
            // 카메라가 같이 따라가도록 센터 조정
            _cc.center = new Vector3(0, newHeight / 2f, 0);
            if (cameraHolder != null)
                cameraHolder.localPosition += new Vector3(0, delta * 0.5f, 0);
        }
    }

    // ── 머리 보빙 ───────────────────────────────────────────────

    private void HandleHeadBob()
    {
        if (cameraHolder == null) return;
        float bobY = Mathf.Sin(_bobTimer * Mathf.PI * 2f) * bobAmplitude;
        Vector3 targetPos = _cameraLocalOrigin + new Vector3(0, bobY, 0);
        // 크라우치 중에는 카메라 높이 오프셋 적용
        if (_isCrouching)
            targetPos.y -= (standingHeight - crouchHeight) * 0.5f;
        cameraHolder.localPosition = Vector3.Lerp(
            cameraHolder.localPosition, targetPos, Time.deltaTime * 10f);
    }

    // ── 외부에서 이동 잠금 (게임오버/컷씬 등) ───────────────────

    public void LockMovement(bool locked)
    {
        LockMovement(locked, true);
    }

    public void LockMovement(bool locked, bool releaseCursor)
    {
        if (locked)
            _horizontalVelocity = Vector3.zero;

        _cc.enabled = !locked;
        _movementLocked = locked;

        if (!releaseCursor) return;

        if (locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    public void BeginSyntheticInput()
    {
        _syntheticInputActive = true;
        _syntheticMoveInput = Vector2.zero;
        _syntheticLookInput = Vector2.zero;
        _syntheticSprint = false;
        _horizontalVelocity = Vector3.zero;
    }

    public void SetSyntheticInput(Vector2 moveInput, Vector2 lookInput, bool sprint)
    {
        _syntheticInputActive = true;
        _syntheticMoveInput = Vector2.ClampMagnitude(moveInput, 1f);
        _syntheticLookInput = lookInput;
        _syntheticSprint = sprint;
    }

    public void EndSyntheticInput()
    {
        _syntheticInputActive = false;
        _syntheticMoveInput = Vector2.zero;
        _syntheticLookInput = Vector2.zero;
        _syntheticSprint = false;
        _currentMoveInput = Vector2.zero;
        _currentMoveWorldDirection = Vector3.zero;
        _horizontalVelocity = Vector3.zero;
    }

    public void ConfigureForExperimentDefaults()
    {
        walkSpeed = 2.9f;
        sprintSpeed = 4.5f;
        crouchSpeed = 1.35f;
        acceleration = 16f;
        deceleration = 22f;
        gravity = -16f;
        standingHeight = 1.55f;
        crouchHeight = 0.9f;
        controllerRadius = 0.22f;
        cameraStandingHeight = 1.32f;
        enableDoorwayShoulderAssist = true;
        shoulderAssistDistance = 0.1f;
        shoulderAssistForwardNudge = 0.12f;
        enableStepAssist = true;
        assistedStepHeight = 0.45f;
        stepProbeDistance = 0.55f;
        stepForwardNudge = 0.2f;
        bobAmplitude = 0.025f;
        NormalizeControllerScale();
    }

    private void NormalizeControllerScale()
    {
        if (_cc == null)
            _cc = GetComponent<CharacterController>();

        if (_cc == null)
            return;

        if (transform.localScale != Vector3.one)
        {
            transform.localScale = Vector3.one;
        }

        standingHeight = Mathf.Clamp(standingHeight, 1.35f, 1.55f);
        crouchHeight = Mathf.Clamp(crouchHeight, 0.75f, standingHeight - 0.15f);
        controllerRadius = Mathf.Clamp(controllerRadius, 0.18f, 0.24f);

        _cc.height = standingHeight;
        _cc.radius = controllerRadius;
        _cc.center = new Vector3(0f, standingHeight * 0.5f, 0f);
        _cc.stepOffset = Mathf.Min(0.6f, standingHeight - 0.2f);
        _cc.slopeLimit = Mathf.Max(_cc.slopeLimit, 60f);
        _cc.minMoveDistance = 0f;

        if (cameraHolder != null)
        {
            Vector3 cameraPosition = cameraHolder.localPosition;
            cameraPosition.y = Mathf.Min(cameraPosition.y, cameraStandingHeight);
            cameraHolder.localPosition = cameraPosition;
        }
    }

    private void EnsureRuntimeReferences()
    {
        if (_cc == null)
            _cc = GetComponent<CharacterController>();
        if (_input != null)
            return;

        try
        {
            _input = new InputSystem_Actions();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[FirstPersonController] Failed to create input actions: {ex.Message}");
            _input = null;
        }
    }
}
