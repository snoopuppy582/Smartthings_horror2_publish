using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Player camera에 들고 있는 랜턴 리그를 자동 생성한다.
/// 맵 전체를 밝히지 않고 플레이어 시야만 확보하는 실험용 조명이다.
/// </summary>
public class LanternController : MonoBehaviour
{
    private const string RigRootName = "ExperimentPlayerLantern_Auto";
    private const string SpotLightName = "ExperimentPlayerLanternSpot_Auto";
    private const string FillLightName = "ExperimentPlayerLanternFill_Auto";
    private const string BodyName = "ExperimentPlayerLanternBody_Auto";
    private const string HandleName = "ExperimentPlayerLanternHandle_Auto";
    private const string GlassName = "ExperimentPlayerLanternGlass_Auto";

    [Header("조명")]
    [SerializeField] private Light spotLight;
    [SerializeField] private Light fillLight;
    [SerializeField] private bool startOn = true;
    [SerializeField] private float maxIntensity = 4.8f;
    [SerializeField] private float fillIntensity = 0.55f;
    [SerializeField] private float transitionSpeed = 10f;

    [Header("시야")]
    [SerializeField] private float spotRange = 18f;
    [SerializeField] private float spotAngle = 58f;
    [SerializeField] private Color lanternColor = new Color(1f, 0.82f, 0.55f, 1f);

    [Header("연출")]
    [SerializeField] private float idleFlickerStrength = 0.08f;
    [SerializeField] private float flickerSpeed = 9f;
    [SerializeField] private float heldSwayAmount = 0.012f;
    [SerializeField] private float heldSwaySpeed = 1.7f;

    [Header("배터리")]
    [SerializeField] private bool useBattery = false;
    [SerializeField] private float maxBattery = 100f;
    [SerializeField] private float drainRate = 2f;

    private InputSystem_Actions _input;
    private Transform _rigRoot;
    private Vector3 _rigBaseLocalPosition;
    private Quaternion _rigBaseLocalRotation;
    private bool _isOn;
    private bool _inputSubscribed;
    private float _battery;
    private float _currentIntensity;
    private float _eventFlickerUntil = -999f;
    private float _eventFlickerStart = -999f;
    private float _eventFlickerDuration = 0.1f;
    private float _eventFlickerStrength;
    private float _dimUntil = -999f;
    private float _dimMultiplier = 1f;
    private float _boostUntil = -999f;
    private float _boostMultiplier = 1f;

    public float BatteryPercent => useBattery ? Mathf.Clamp01(_battery / Mathf.Max(1f, maxBattery)) : 1f;
    public bool IsOn => _isOn;
    public bool HasUsableLight => spotLight != null && spotLight.type == LightType.Spot && spotLight.range >= 12f && maxIntensity >= 3f;

    private void Awake()
    {
        EnsureInput();
        ConfigureForExperimentDefaults();
        _battery = Mathf.Max(1f, maxBattery);
        _isOn = startOn && (!useBattery || _battery > 0f);
        _currentIntensity = _isOn ? maxIntensity : 0f;
        ApplyLightSettingsImmediate();
    }

    private void OnEnable()
    {
        EnsureInput();
        if (!_inputSubscribed)
        {
            _input.Player.Lantern.performed += OnLanternToggle;
            _inputSubscribed = true;
        }

        _input.Enable();
    }

    private void OnDisable()
    {
        if (_input == null) return;

        if (_inputSubscribed)
        {
            _input.Player.Lantern.performed -= OnLanternToggle;
            _inputSubscribed = false;
        }

        _input.Disable();
    }

    private void Update()
    {
        if (IsGamePaused()) return;

        HandleBattery();
        UpdateLightIntensity();
        UpdateHeldSway();
    }

    public void ConfigureForExperimentDefaults()
    {
        startOn = true;
        useBattery = false;
        maxIntensity = Mathf.Max(maxIntensity, 4.8f);
        fillIntensity = Mathf.Max(fillIntensity, 0.55f);
        transitionSpeed = Mathf.Max(transitionSpeed, 10f);
        spotRange = Mathf.Max(spotRange, 18f);
        spotAngle = Mathf.Clamp(Mathf.Max(spotAngle, 58f), 35f, 85f);

        Transform anchor = ResolveCameraAnchor();
        _rigRoot = GetOrCreateChild(anchor, RigRootName);
        _rigRoot.localPosition = new Vector3(0.32f, -0.26f, 0.58f);
        _rigRoot.localRotation = Quaternion.Euler(4f, -7f, 0f);
        _rigRoot.localScale = Vector3.one;
        _rigBaseLocalPosition = _rigRoot.localPosition;
        _rigBaseLocalRotation = _rigRoot.localRotation;

        spotLight = ConfigureSpotLight(GetOrCreateChild(_rigRoot, SpotLightName));
        fillLight = ConfigureFillLight(GetOrCreateChild(_rigRoot, FillLightName));
        ConfigureLanternVisuals(_rigRoot);
    }

    public void ReactToExperimentEvent(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
            return;

        switch (eventId)
        {
            case "ghost_hint":
                TriggerFlicker(1.8f, 0.22f);
                break;
            case "killer_near":
                TriggerFlicker(2.4f, 0.34f);
                break;
            case "blackout":
                _dimUntil = Time.time + 3.2f;
                _dimMultiplier = 0.48f;
                TriggerFlicker(3.2f, 0.48f);
                ForceOn();
                break;
            case "chase":
                _boostUntil = Time.time + 7.5f;
                _boostMultiplier = 1.18f;
                TriggerFlicker(4.0f, 0.28f);
                ForceOn();
                break;
            case "player_hit":
                TriggerFlicker(1.4f, 0.65f);
                break;
            case "mission_failed":
                TriggerFlicker(2.0f, 0.45f);
                break;
            case "mission_success":
                _boostUntil = Time.time + 2.5f;
                _boostMultiplier = 1.08f;
                break;
        }
    }

    public void ForceOn()
    {
        if (useBattery && _battery <= 0f)
            _battery = Mathf.Max(20f, maxBattery * 0.2f);

        _isOn = true;
    }

    public void ForceOff()
    {
        _isOn = false;
    }

    public void ForceOffImmediate()
    {
        _isOn = false;
        _currentIntensity = 0f;
        ApplyLightSettingsImmediate();
    }

    private void EnsureInput()
    {
        _input ??= new InputSystem_Actions();
    }

    private void OnLanternToggle(InputAction.CallbackContext ctx)
    {
        if (IsGamePaused()) return;

        if (!_isOn && useBattery && _battery <= 0f)
        {
            Debug.Log("[Lantern] Battery empty.");
            return;
        }

        _isOn = !_isOn;
        Debug.Log($"[Lantern] {(_isOn ? "ON" : "OFF")} | battery {_battery:0.0}%");
    }

    private void HandleBattery()
    {
        if (!useBattery || !_isOn) return;

        _battery = Mathf.Max(0f, _battery - drainRate * Time.deltaTime);
        if (_battery <= 0f)
        {
            _isOn = false;
            Debug.Log("[Lantern] Battery empty; lantern turned off.");
        }
    }

    private void UpdateLightIntensity()
    {
        if (spotLight == null)
            return;

        float scenarioMultiplier = ResolveScenarioMultiplier();
        float target = _isOn ? maxIntensity * scenarioMultiplier : 0f;
        _currentIntensity = Mathf.Lerp(_currentIntensity, target, Time.deltaTime * transitionSpeed);

        float flicker = ResolveFlickerMultiplier();
        spotLight.intensity = _currentIntensity * flicker;
        spotLight.enabled = spotLight.intensity > 0.03f;

        if (fillLight != null)
        {
            fillLight.intensity = (_isOn ? fillIntensity * scenarioMultiplier : 0f) * Mathf.Lerp(1f, flicker, 0.45f);
            fillLight.enabled = fillLight.intensity > 0.02f;
        }
    }

    private void ApplyLightSettingsImmediate()
    {
        float scenarioMultiplier = ResolveScenarioMultiplier();
        float spotIntensity = _isOn ? maxIntensity * scenarioMultiplier : 0f;

        if (spotLight != null)
        {
            spotLight.intensity = spotIntensity;
            spotLight.enabled = spotIntensity > 0.03f;
        }

        if (fillLight != null)
        {
            float pointIntensity = _isOn ? fillIntensity * scenarioMultiplier : 0f;
            fillLight.intensity = pointIntensity;
            fillLight.enabled = pointIntensity > 0.02f;
        }
    }

    private float ResolveScenarioMultiplier()
    {
        float multiplier = 1f;
        if (Time.time < _dimUntil)
            multiplier = Mathf.Min(multiplier, _dimMultiplier);
        if (Time.time < _boostUntil)
            multiplier = Mathf.Max(multiplier, _boostMultiplier);

        return multiplier;
    }

    private float ResolveFlickerMultiplier()
    {
        if (!_isOn)
            return 1f;

        float noise = Mathf.PerlinNoise(Time.time * flickerSpeed, 0.37f) - 0.5f;
        float flicker = noise * idleFlickerStrength;

        if (Time.time < _eventFlickerUntil)
        {
            float elapsed = Time.time - _eventFlickerStart;
            float falloff = 1f - Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _eventFlickerDuration));
            float pulse = Mathf.Sin(Time.time * 39f) * _eventFlickerStrength * falloff;
            flicker += pulse;
        }

        return Mathf.Clamp(1f + flicker, 0.18f, 1.32f);
    }

    private void TriggerFlicker(float durationSec, float strength)
    {
        _eventFlickerStart = Time.time;
        _eventFlickerDuration = Mathf.Max(0.05f, durationSec);
        _eventFlickerUntil = Time.time + _eventFlickerDuration;
        _eventFlickerStrength = Mathf.Clamp01(strength);
    }

    private void UpdateHeldSway()
    {
        if (_rigRoot == null)
            return;

        float t = Time.time * heldSwaySpeed;
        Vector3 offset = new Vector3(
            Mathf.Sin(t * 0.7f) * heldSwayAmount,
            Mathf.Sin(t) * heldSwayAmount,
            Mathf.Cos(t * 0.55f) * heldSwayAmount * 0.45f);

        _rigRoot.localPosition = _rigBaseLocalPosition + offset;
        _rigRoot.localRotation = _rigBaseLocalRotation * Quaternion.Euler(
            Mathf.Sin(t * 0.8f) * 0.6f,
            Mathf.Cos(t * 0.55f) * 0.7f,
            Mathf.Sin(t * 0.45f) * 0.5f);
    }

    private Transform ResolveCameraAnchor()
    {
        Camera camera = GetComponentInChildren<Camera>(true);
        return camera != null ? camera.transform : transform;
    }

    private Light ConfigureSpotLight(Transform lightTransform)
    {
        lightTransform.localPosition = new Vector3(0.02f, 0.07f, 0.08f);
        lightTransform.localRotation = Quaternion.identity;
        lightTransform.localScale = Vector3.one;

        Light light = lightTransform.GetComponent<Light>();
        if (light == null)
            light = lightTransform.gameObject.AddComponent<Light>();

        light.type = LightType.Spot;
        light.color = lanternColor;
        light.range = spotRange;
        light.spotAngle = spotAngle;
        light.intensity = maxIntensity;
        light.shadows = LightShadows.Soft;
        light.shadowStrength = 0.55f;
        light.renderMode = LightRenderMode.ForcePixel;
        return light;
    }

    private Light ConfigureFillLight(Transform lightTransform)
    {
        lightTransform.localPosition = new Vector3(0.06f, -0.02f, 0.0f);
        lightTransform.localRotation = Quaternion.identity;
        lightTransform.localScale = Vector3.one;

        Light light = lightTransform.GetComponent<Light>();
        if (light == null)
            light = lightTransform.gameObject.AddComponent<Light>();

        light.type = LightType.Point;
        light.color = lanternColor;
        light.range = 4.5f;
        light.intensity = fillIntensity;
        light.shadows = LightShadows.None;
        light.renderMode = LightRenderMode.ForcePixel;
        return light;
    }

    private void ConfigureLanternVisuals(Transform parent)
    {
        GameObject body = GetOrCreatePrimitive(parent, BodyName, PrimitiveType.Cylinder);
        body.transform.localPosition = new Vector3(0.2f, -0.13f, 0.12f);
        body.transform.localRotation = Quaternion.Euler(0f, 0f, 0f);
        body.transform.localScale = new Vector3(0.08f, 0.11f, 0.08f);
        SetRendererMaterial(body, new Color(0.09f, 0.075f, 0.055f, 1f), 0f);

        GameObject glass = GetOrCreatePrimitive(parent, GlassName, PrimitiveType.Cube);
        glass.transform.localPosition = new Vector3(0.2f, -0.09f, 0.13f);
        glass.transform.localRotation = Quaternion.identity;
        glass.transform.localScale = new Vector3(0.11f, 0.08f, 0.055f);
        SetRendererMaterial(glass, new Color(1f, 0.72f, 0.34f, 0.72f), 1.2f);

        GameObject handle = GetOrCreatePrimitive(parent, HandleName, PrimitiveType.Cube);
        handle.transform.localPosition = new Vector3(0.2f, 0.01f, 0.12f);
        handle.transform.localRotation = Quaternion.identity;
        handle.transform.localScale = new Vector3(0.13f, 0.025f, 0.04f);
        SetRendererMaterial(handle, new Color(0.05f, 0.045f, 0.035f, 1f), 0f);
    }

    private static Transform GetOrCreateChild(Transform parent, string objectName)
    {
        Transform existing = parent.Find(objectName);
        if (existing != null)
            return existing;

        GameObject go = new GameObject(objectName);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static GameObject GetOrCreatePrimitive(Transform parent, string objectName, PrimitiveType primitiveType)
    {
        Transform existing = parent.Find(objectName);
        GameObject go = existing != null ? existing.gameObject : GameObject.CreatePrimitive(primitiveType);
        go.name = objectName;
        go.transform.SetParent(parent, false);

        Collider collider = go.GetComponent<Collider>();
        if (collider != null)
            DestroyGeneratedObject(collider);

        return go;
    }

    private static void SetRendererMaterial(GameObject go, Color color, float emission)
    {
        Renderer renderer = go.GetComponent<Renderer>();
        if (renderer == null)
            return;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            return;

        Material material = new Material(shader)
        {
            name = go.name + "_Material",
            color = color,
        };

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (emission > 0f && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * emission);
        }

        renderer.sharedMaterial = material;
    }

    private static void DestroyGeneratedObject(Object obj)
    {
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    private bool IsGamePaused()
    {
        if (GameManager.Instance == null) return false;
        var state = GameManager.Instance.CurrentState;
        return state == GameManager.GameState.GameOver ||
               state == GameManager.GameState.Paused;
    }
}
