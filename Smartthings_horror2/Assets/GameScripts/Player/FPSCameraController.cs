using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 1인칭 카메라 컨트롤러
/// 이 스크립트는 카메라 GameObject에 부착한다.
/// 플레이어 몸체(playerBody)는 인스펙터에서 연결.
/// </summary>
public class FPSCameraController : MonoBehaviour
{
    // ─── 마우스 감도 ───────────────────────────────
    [Header("마우스 감도")]
    [SerializeField] float sensitivityX = 0.15f;  // 좌우 감도
    [SerializeField] float sensitivityY = 0.15f;  // 상하 감도

    // ─── 상하 각도 제한 ────────────────────────────
    [Header("카메라 상하 제한 (도)")]
    [SerializeField] float minPitch = -80f;
    [SerializeField] float maxPitch =  80f;

    // ─── 헤드 밥 ───────────────────────────────────
    [Header("헤드 밥")]
    [SerializeField] float walkBobFreq   = 1.5f;  // 걷기 주파수
    [SerializeField] float walkBobAmp    = 0.04f; // 걷기 진폭
    [SerializeField] float sprintBobFreq = 2.5f;  // 달리기 주파수
    [SerializeField] float sprintBobAmp  = 0.07f; // 달리기 진폭
    [SerializeField] float crouchBobFreq = 0.8f;  // 앉기 주파수
    [SerializeField] float crouchBobAmp  = 0.025f;// 앉기 진폭
    [SerializeField] float bobReturnSpd  = 6f;    // 멈출 때 원위치 복귀 속도

    // ─── 숨소리 & 공포 흔들림 ──────────────────────
    [Header("공포 연출")]
    [SerializeField] AudioSource breathingSource;
    [SerializeField] AudioClip   heavyBreathing;   // 달리기/공포 시 재생
    [SerializeField] float breathingFadeSpeed = 2f;

    // ─── 참조 ──────────────────────────────────────
    [Header("참조")]
    [SerializeField] Transform playerBody;  // 플레이어 몸체 (좌우 회전용)

    // ─── 내부 변수 ─────────────────────────────────
    FPSPlayerController _player;
    float   _pitch        = 0f;
    float   _bobTimer     = 0f;
    Vector3 _defaultLocalPos;
    Vector3 _bobOffset;
    Vector3 _shakeOffset;
    Coroutine _shakeRoutine;
    float _targetBreathVolume = 0f;

    // ─────────────────────────────────────────────────
    void Start()
    {
        _defaultLocalPos = transform.localPosition;

        if (playerBody != null)
            _player = playerBody.GetComponent<FPSPlayerController>();

        // 마우스 커서 잠금
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible   = false;

        // 달리기 상태 변경 이벤트 구독
        if (_player != null)
            _player.OnStateChanged += HandleStateChanged;
    }

    void OnDestroy()
    {
        if (_player != null)
            _player.OnStateChanged -= HandleStateChanged;
    }

    void LateUpdate()
    {
        HandleMouseLook();
        UpdateHeadBob();
        UpdateBreathing();

        // 최종 위치 = 기본 위치 + 밥 오프셋 + 흔들림 오프셋
        transform.localPosition = _defaultLocalPos + _bobOffset + _shakeOffset;

        // ESC 키로 커서 잠금 해제 (메뉴용)
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            ToggleCursorLock();
    }

    // ─── 마우스 룩 ─────────────────────────────────
    void HandleMouseLook()
    {
        var mouse = Mouse.current;
        if (mouse == null) return;

        Vector2 delta = mouse.delta.ReadValue();

        // 좌우 회전 → 플레이어 몸체 Y축
        if (playerBody != null)
            playerBody.Rotate(Vector3.up * delta.x * sensitivityX * 100f * Time.deltaTime);

        // 상하 회전 → 카메라 X축 (클램프)
        _pitch -= delta.y * sensitivityY * 100f * Time.deltaTime;
        _pitch  = Mathf.Clamp(_pitch, minPitch, maxPitch);
        transform.localRotation = Quaternion.Euler(_pitch, 0f, 0f);
    }

    // ─── 헤드 밥 ───────────────────────────────────
    void UpdateHeadBob()
    {
        if (_player == null || !_player.IsMoving)
        {
            // 멈추면 부드럽게 원위치 복귀
            _bobOffset = Vector3.Lerp(_bobOffset, Vector3.zero, Time.deltaTime * bobReturnSpd);
            _bobTimer  = 0f;
            return;
        }

        float freq, amp;
        switch (_player.State)
        {
            case FPSPlayerController.PlayerState.Sprinting:
                freq = sprintBobFreq; amp = sprintBobAmp; break;
            case FPSPlayerController.PlayerState.Crouching:
                freq = crouchBobFreq; amp = crouchBobAmp; break;
            default:
                freq = walkBobFreq;   amp = walkBobAmp;   break;
        }

        _bobTimer += Time.deltaTime * freq * Mathf.PI * 2f;

        // sin 파형으로 상하 흔들림, 0.5배 주파수로 좌우 흔들림
        float bobY = Mathf.Sin(_bobTimer)        * amp;
        float bobX = Mathf.Sin(_bobTimer * 0.5f) * amp * 0.5f;
        _bobOffset = new Vector3(bobX, bobY, 0f);
    }

    // ─── 숨소리 (달리기 또는 공포 상태) ────────────
    void UpdateBreathing()
    {
        if (breathingSource == null || heavyBreathing == null) return;

        if (!breathingSource.isPlaying && _targetBreathVolume > 0.05f)
        {
            breathingSource.clip = heavyBreathing;
            breathingSource.loop = true;
            breathingSource.Play();
        }

        breathingSource.volume = Mathf.MoveTowards(
            breathingSource.volume, _targetBreathVolume,
            Time.deltaTime * breathingFadeSpeed);

        if (breathingSource.volume < 0.01f && breathingSource.isPlaying)
            breathingSource.Stop();
    }

    // ─── 상태 변화 핸들러 ──────────────────────────
    void HandleStateChanged(FPSPlayerController.PlayerState state)
    {
        // 달리기 중 숨소리 페이드 인/아웃
        _targetBreathVolume = state == FPSPlayerController.PlayerState.Sprinting ? 0.8f : 0f;
    }

    // ─── 공개 API ──────────────────────────────────

    /// <summary>
    /// 카메라 흔들림 (jump_scare 이벤트 등에서 호출)
    /// </summary>
    public void ShakeCamera(float intensity = 0.05f, float duration = 0.4f)
    {
        if (_shakeRoutine != null) StopCoroutine(_shakeRoutine);
        _shakeRoutine = StartCoroutine(ShakeCoroutine(intensity, duration));
    }

    /// <summary>
    /// 공포 숨소리 강도 설정 (0~1) — 적 접근 시 외부에서 호출
    /// </summary>
    public void SetFearBreathing(float intensity)
    {
        _targetBreathVolume = Mathf.Clamp01(intensity);
    }

    /// <summary>
    /// 감도 런타임 변경 (설정 메뉴용)
    /// </summary>
    public void SetSensitivity(float x, float y)
    {
        sensitivityX = x;
        sensitivityY = y;
    }

    // ─── 내부 코루틴 ───────────────────────────────
    IEnumerator ShakeCoroutine(float intensity, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            // 시간이 지날수록 감쇠
            float damping = 1f - (elapsed / duration);
            _shakeOffset = new Vector3(
                Random.Range(-1f, 1f) * intensity * damping,
                Random.Range(-1f, 1f) * intensity * damping,
                0f);
            elapsed += Time.deltaTime;
            yield return null;
        }
        _shakeOffset = Vector3.zero;
    }

    void ToggleCursorLock()
    {
        if (Cursor.lockState == CursorLockMode.Locked)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible   = true;
        }
        else
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible   = false;
        }
    }

    // ─── 에디터 Gizmo ──────────────────────────────
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawRay(transform.position, transform.forward * 2f);
    }
}
