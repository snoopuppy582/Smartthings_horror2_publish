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
    [SerializeField] private float walkSpeed = 3f;
    [SerializeField] private float sprintSpeed = 6f;
    [SerializeField] private float crouchSpeed = 1.5f;
    [SerializeField] private float gravity = -15f;
    [SerializeField] private float jumpHeight = 1.0f;
    [SerializeField] private LayerMask groundMask;

    [Header("카메라")]
    [SerializeField] private Transform cameraHolder;
    [SerializeField] private float mouseSensitivity = 0.12f;
    [SerializeField] private float maxLookAngle = 80f;

    [Header("크라우치")]
    [SerializeField] private float standingHeight = 1.8f;
    [SerializeField] private float crouchHeight = 0.9f;
    [SerializeField] private float crouchTransitionSpeed = 8f;

    [Header("머리 보빙 (Head Bob)")]
    [SerializeField] private float bobFrequency = 1.8f;
    [SerializeField] private float bobAmplitude = 0.04f;

    // 컴포넌트
    private CharacterController _cc;
    private InputSystem_Actions _input;

    // 상태
    private Vector3 _velocity;
    private float _xRotation;
    private bool _isCrouching;
    private bool _isGrounded;
    private float _bobTimer;
    private float _targetHeight;
    private Vector3 _cameraLocalOrigin;

    void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _input = new InputSystem_Actions();
        _targetHeight = standingHeight;

        if (cameraHolder != null)
            _cameraLocalOrigin = cameraHolder.localPosition;
    }

    void OnEnable()
    {
        _input.Enable();
        // 점프 이벤트 구독
        _input.Player.Jump.performed += OnJump;
        // 크라우치 토글
        _input.Player.Crouch.performed += OnCrouchToggle;
    }

    void OnDisable()
    {
        _input.Player.Jump.performed -= OnJump;
        _input.Player.Crouch.performed -= OnCrouchToggle;
        _input.Disable();
    }

    void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        if (GameManager.Instance != null &&
            (GameManager.Instance.CurrentState == GameManager.GameState.GameOver ||
             GameManager.Instance.CurrentState == GameManager.GameState.Paused))
            return;

        HandleGroundCheck();
        HandleLook();
        HandleMove();
        HandleGravity();
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
        Vector2 look = _input.Player.Look.ReadValue<Vector2>();
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
        Vector2 move = _input.Player.Move.ReadValue<Vector2>();
        bool sprinting = _input.Player.Sprint.IsPressed() && !_isCrouching;

        float speed = _isCrouching ? crouchSpeed : (sprinting ? sprintSpeed : walkSpeed);
        Vector3 dir = transform.right * move.x + transform.forward * move.y;
        _cc.Move(dir * (speed * Time.deltaTime));

        // 이동 중 머리 보빙 타이머
        if (move.sqrMagnitude > 0.01f && _isGrounded)
            _bobTimer += Time.deltaTime * bobFrequency * (sprinting ? 1.5f : 1f);
        else
            _bobTimer = 0f;
    }

    // ── 중력 / 점프 ─────────────────────────────────────────────

    private void HandleGravity()
    {
        _velocity.y += gravity * Time.deltaTime;
        _cc.Move(_velocity * Time.deltaTime);
    }

    private void OnJump(InputAction.CallbackContext ctx)
    {
        if (_isGrounded && !_isCrouching)
            _velocity.y = Mathf.Sqrt(jumpHeight * -2f * gravity);
    }

    // ── 크라우치 ────────────────────────────────────────────────

    private void OnCrouchToggle(InputAction.CallbackContext ctx)
    {
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
        _cc.enabled = !locked;
        if (locked) Cursor.lockState = CursorLockMode.None;
        else Cursor.lockState = CursorLockMode.Locked;
    }
}
