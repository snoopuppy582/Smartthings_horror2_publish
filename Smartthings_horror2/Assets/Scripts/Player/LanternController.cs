using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F키로 랜턴 토글. 배터리 소모, 외부 강제 끄기 지원.
/// Player GameObject에 부착하고, Inspector에서 spotLight(카메라 하위 Spot Light)를 연결하세요.
/// </summary>
public class LanternController : MonoBehaviour
{
    [Header("조명")]
    [SerializeField] private Light spotLight;
    [SerializeField] private float maxIntensity = 3f;
    [SerializeField] private float transitionSpeed = 8f;

    [Header("배터리")]
    [SerializeField] private bool useBattery = true;
    [SerializeField] private float maxBattery = 100f;
    [SerializeField] private float drainRate = 2f;   // 초당 소모 (%)

    private InputSystem_Actions _input;
    private bool _isOn;
    private float _battery;
    private float _currentIntensity;

    // HUD 등 외부에서 읽는 프로퍼티
    public float BatteryPercent => useBattery ? _battery / maxBattery : 1f;
    public bool IsOn => _isOn;

    void Awake()
    {
        _input = new InputSystem_Actions();
        _battery = maxBattery;

        if (spotLight != null)
            spotLight.intensity = 0f;
    }

    void OnEnable()
    {
        _input.Enable();
        _input.Player.Lantern.performed += OnLanternToggle;
    }

    void OnDisable()
    {
        _input.Player.Lantern.performed -= OnLanternToggle;
        _input.Disable();
    }

    void Update()
    {
        if (IsGamePaused()) return;

        HandleBattery();
        UpdateLightIntensity();
    }

    // ── 입력 콜백 ────────────────────────────────────────────────

    private void OnLanternToggle(InputAction.CallbackContext ctx)
    {
        if (IsGamePaused()) return;

        // 배터리가 없으면 켤 수 없음
        if (!_isOn && useBattery && _battery <= 0f)
        {
            Debug.Log("[Lantern] 배터리 소진 — 랜턴을 켤 수 없습니다.");
            return;
        }

        _isOn = !_isOn;
        Debug.Log($"[Lantern] {(_isOn ? "ON" : "OFF")} | 배터리 {_battery:F1}%");
    }

    // ── 배터리 ───────────────────────────────────────────────────

    private void HandleBattery()
    {
        if (!useBattery || !_isOn) return;

        _battery = Mathf.Max(0f, _battery - drainRate * Time.deltaTime);

        if (_battery <= 0f)
        {
            _isOn = false;
            Debug.Log("[Lantern] 배터리 소진으로 자동 꺼짐");
        }
    }

    // ── 조명 강도 부드럽게 전환 ──────────────────────────────────

    private void UpdateLightIntensity()
    {
        if (spotLight == null) return;

        float target = _isOn ? maxIntensity : 0f;
        _currentIntensity = Mathf.Lerp(_currentIntensity, target, Time.deltaTime * transitionSpeed);
        spotLight.intensity = _currentIntensity;
    }

    // ── 외부 제어 (blackout, 게임오버 등) ────────────────────────

    /// <summary>blackout 이벤트 등에서 호출 — 부드럽게 꺼짐</summary>
    public void ForceOff()
    {
        _isOn = false;
    }

    /// <summary>컷씬·게임오버 등에서 호출 — 즉시 꺼짐</summary>
    public void ForceOffImmediate()
    {
        _isOn = false;
        _currentIntensity = 0f;
        if (spotLight != null)
            spotLight.intensity = 0f;
    }

    // ── 헬퍼 ─────────────────────────────────────────────────────

    private bool IsGamePaused()
    {
        if (GameManager.Instance == null) return false;
        var state = GameManager.Instance.CurrentState;
        return state == GameManager.GameState.GameOver ||
               state == GameManager.GameState.Paused;
    }
}
