using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// 120초 실험 세션, GameOnly/GameWithIoT 조건, IoT 이벤트, 성공/실패 로그를 관리한다.
/// </summary>
[RequireComponent(typeof(ExperimentLogger))]
public class ExperimentDirector : MonoBehaviour
{
    private const string HudRootName = "ExperimentHUD_Auto";
    public const string DamageVignetteName = "ExperimentDamageVignette_Auto";

    public enum ExperimentCondition
    {
        GameOnly,
        GameWithIoT,
    }

    private enum DefaultSoundCue
    {
        SessionStart,
        KillerNear,
        Success,
        Failed,
    }

    public static ExperimentDirector Instance { get; private set; }

    [Header("실험 설정")]
    [SerializeField] private ExperimentCondition condition = ExperimentCondition.GameWithIoT;
    [SerializeField] private float sessionDurationSec = 120f;
    [SerializeField] private float localEventCooldownSec = 15f;
    [SerializeField] private float resultRecoveryDelaySec = 2.5f;
    [SerializeField] private bool startOnPlay = true;
    [SerializeField] private bool syncGameManagerIotFlag = true;
    [SerializeField] private string conditionPlayerPrefsKey = "ExperimentCondition";

    [Header("2-minute scenario")]
    [SerializeField] private bool enableTimedScenario = true;
    [SerializeField] private float scenarioTimeScale = 1f;
    [SerializeField] private string scenarioTimeScalePlayerPrefsKey = "ExperimentScenarioTimeScale";

    [Header("UI")]
    [SerializeField] private Text timerText;
    [SerializeField] private Text objectiveText;
    [SerializeField] private GameObject successPanel;
    [SerializeField] private GameObject failedPanel;
    [SerializeField] private string objectiveLabel = "Find the upstairs evidence and return alive.";

    [Header("오디오")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip sessionStartClip;
    [SerializeField] private AudioClip successClip;
    [SerializeField] private AudioClip failedClip;
    [SerializeField] private AudioClip killerNearClip;

    private readonly Dictionary<string, float> _lastEventTimes = new();
    private static readonly Dictionary<DefaultSoundCue, AudioClip> s_defaultClips = new();
    private ExperimentLogger _logger;
    private string _sessionId;
    private float _startTime = -1f;
    private bool _running;
    private bool _ended;
    private int _hitCount;
    private Coroutine _recoveryCoroutine;
    private bool _killerCueTriggered;
    private float _killerCueTime = -999f;
    private int _nextScenarioCueIndex;
    private static readonly ScenarioCue[] s_defaultScenarioCues =
    {
        new ScenarioCue(18f, "ghost_hint", false),
        new ScenarioCue(42f, "killer_near", true),
        new ScenarioCue(70f, "blackout", false),
        new ScenarioCue(90f, "chase", true),
        new ScenarioCue(104f, "killer_near", true),
    };

    public bool IsRunning => _running && !_ended;
    public int HitCount => _hitCount;
    public float ElapsedSec => _startTime >= 0f ? Time.time - _startTime : 0f;
    public string SessionId => _sessionId;
    public string ConditionName => condition.ToString();
    public bool IotEnabled => condition == ExperimentCondition.GameWithIoT;
    public bool KillerCueTriggered => _killerCueTriggered;
    public float SecondsSinceKillerCue => _killerCueTriggered ? Time.time - _killerCueTime : 0f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        _logger = GetComponent<ExperimentLogger>();
        EnsureDefaultUi();
    }

    private void Start()
    {
        if (startOnPlay && !_running)
            BeginSession();
    }

    private void Update()
    {
        if (!IsRunning) return;

        float remaining = Mathf.Max(0f, sessionDurationSec - ElapsedSec);
        if (timerText != null)
        {
            int seconds = Mathf.CeilToInt(remaining);
            timerText.text = $"{seconds / 60:00}:{seconds % 60:00}";
        }

        UpdateTimedScenario();

        if (remaining <= 0f)
            FailSession("timeout");
    }

    public void BeginSession()
    {
        ApplyConditionOverride();
        ApplyScenarioTimeScaleOverride();

        _sessionId = BuildSessionId();
        _startTime = Time.time;
        _hitCount = 0;
        _running = true;
        _ended = false;
        _killerCueTriggered = false;
        _killerCueTime = -999f;
        _nextScenarioCueIndex = 0;
        _lastEventTimes.Clear();
        if (_recoveryCoroutine != null)
        {
            StopCoroutine(_recoveryCoroutine);
            _recoveryCoroutine = null;
        }

        if (syncGameManagerIotFlag && GameManager.Instance != null)
            GameManager.Instance.iotEnabled = IotEnabled;

        SetPlayerInputLocked(false);
        if (successPanel != null) successPanel.SetActive(false);
        if (failedPanel != null) failedPanel.SetActive(false);
        if (objectiveText != null) objectiveText.text = objectiveLabel;

        _logger.BeginSession(_sessionId);
        _logger.LogEvent("session_config", "system", 0f, ConditionName, _hitCount, null, "scene", SceneManager.GetActiveScene().name);
        PlayClip(sessionStartClip, DefaultSoundCue.SessionStart);
        SendExperimentEvent("game_start", "director", "session_begin", true);
    }

    public void ReportProgress(ExperimentProgressMarker.MarkerType markerType, string optionalEventId = null)
    {
        if (!IsRunning) return;

        string markerName = markerType.ToString();
        _logger.LogEvent("progress_marker", "trigger", ElapsedSec, ConditionName, _hitCount, null, "marker", markerName);

        if (markerType == ExperimentProgressMarker.MarkerType.KillerCueArea)
        {
            _killerCueTriggered = true;
            _killerCueTime = Time.time;
            FindFirstObjectByType<KillerAI>()?.ForceChase(12f);
        }

        if (!string.IsNullOrEmpty(optionalEventId))
        {
            NotifySensorySystems(optionalEventId);
            SendExperimentEvent(optionalEventId, "trigger", markerName);
        }
    }

    public void ReportKillerNear(float distance)
    {
        if (!IsRunning) return;

        PlayClip(killerNearClip, DefaultSoundCue.KillerNear);
        NotifySensorySystems("killer_near");
        SendExperimentEvent("killer_near", "enemy", $"distance={distance:0.0}");
    }

    public void ReportPlayerHit()
    {
        if (!IsRunning) return;

        _hitCount++;
        _logger.LogEvent("player_hit_local", "enemy", ElapsedSec, ConditionName, _hitCount, null, "detail", "nonlethal_hit");
        NotifySensorySystems("player_hit");
        SendExperimentEvent("player_hit", "enemy", "nonlethal_hit");
    }

    public void CompleteObjective()
    {
        if (!IsRunning) return;

        NotifySensorySystems("mission_success");
        SendExperimentEvent("mission_success", "objective", "objective_collected", true);
        FinishSession(true, "objective_collected");
    }

    public void FailSession(string reason)
    {
        if (!IsRunning) return;

        NotifySensorySystems("mission_failed");
        SendExperimentEvent("mission_failed", "director", reason, true);
        FinishSession(false, reason);
    }

    private void FinishSession(bool success, string reason)
    {
        _running = false;
        _ended = true;

        if (syncGameManagerIotFlag && GameManager.Instance != null)
            _recoveryCoroutine = StartCoroutine(TriggerRecoveryAfterResultDelay());

        SetPlayerInputLocked(true);
        SetResultPanelText(successPanel, "Mission Success");
        SetResultPanelText(failedPanel, reason == "timeout" ? "Time Over" : "Mission Failed");

        if (successPanel != null) successPanel.SetActive(success);
        if (failedPanel != null) failedPanel.SetActive(!success);
        PlayClip(success ? successClip : failedClip, success ? DefaultSoundCue.Success : DefaultSoundCue.Failed);

        _logger.LogEvent(success ? "mission_success_local" : "mission_failed_local", "director", ElapsedSec, ConditionName, _hitCount, null, "reason", reason);
        _logger.LogSummary(success, ElapsedSec, ConditionName, _hitCount);
    }

    private IEnumerator TriggerRecoveryAfterResultDelay()
    {
        float delay = Mathf.Max(0f, resultRecoveryDelaySec);
        if (delay > 0f)
            yield return new WaitForSeconds(delay);

        GameManager.Instance?.TriggerIoT("recovery");
        _recoveryCoroutine = null;
    }

    private void UpdateTimedScenario()
    {
        if (!enableTimedScenario || s_defaultScenarioCues.Length == 0)
            return;

        float scaledElapsed = ElapsedSec * Mathf.Max(0.01f, scenarioTimeScale);
        while (_nextScenarioCueIndex < s_defaultScenarioCues.Length &&
               scaledElapsed >= s_defaultScenarioCues[_nextScenarioCueIndex].timeSec)
        {
            TriggerScenarioCue(s_defaultScenarioCues[_nextScenarioCueIndex]);
            _nextScenarioCueIndex++;
        }
    }

    private void TriggerScenarioCue(ScenarioCue cue)
    {
        _logger.LogEvent(
            "scenario_cue",
            "scenario",
            ElapsedSec,
            ConditionName,
            _hitCount,
            null,
            "event_id",
            cue.eventId);

        if (cue.forceChase)
        {
            _killerCueTriggered = true;
            _killerCueTime = Time.time;
            FindFirstObjectByType<KillerAI>()?.ForceChase(12f);
        }

        if (cue.eventId == "killer_near" || cue.eventId == "chase")
            PlayClip(killerNearClip, DefaultSoundCue.KillerNear);

        NotifySensorySystems(cue.eventId);
        SendExperimentEvent(cue.eventId, "scenario", $"scenario_t={cue.timeSec:0}");
    }

    private static void NotifySensorySystems(string eventId)
    {
        FindFirstObjectByType<LanternController>()?.ReactToExperimentEvent(eventId);
        FindFirstObjectByType<ProceduralHorrorAmbience>()?.ReactToExperimentEvent(eventId);
        FindFirstObjectByType<AmbientAudioManager>()?.ReactToExperimentEvent(eventId);
        FindFirstObjectByType<HouseLightController>()?.ReactToEvent(eventId);
    }

    private void SendExperimentEvent(string eventId, string source, string detail, bool bypassLocalCooldown = false)
    {
        float elapsed = ElapsedSec;
        if (!bypassLocalCooldown && _lastEventTimes.TryGetValue(eventId, out float lastTime) &&
            Time.time - lastTime < localEventCooldownSec)
        {
            _logger.LogEvent(eventId, source, elapsed, ConditionName, _hitCount, null, "skip_reason", "local_cooldown");
            return;
        }

        _lastEventTimes[eventId] = Time.time;

        if (!IotEnabled)
        {
            _logger.LogEvent(eventId, source, elapsed, ConditionName, _hitCount, null, "skip_reason", "game_only_condition");
            return;
        }

        if (SmartThingsEventSender.Instance == null)
        {
            _logger.LogEvent(eventId, source, elapsed, ConditionName, _hitCount, false, "skip_reason", "sender_missing");
            return;
        }

        SmartThingsEventSender.Instance.SendExperimentEvent(
            eventId,
            _sessionId,
            ConditionName,
            elapsed,
            _hitCount,
            ok => _logger.LogEvent(eventId, source, elapsed, ConditionName, _hitCount, ok, "detail", detail));
    }

    private static string BuildSessionId()
    {
        string scene = SceneManager.GetActiveScene().name;
        string suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        return $"{scene}_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{suffix}";
    }

    private void ApplyConditionOverride()
    {
        if (string.IsNullOrEmpty(conditionPlayerPrefsKey)) return;
        if (!PlayerPrefs.HasKey(conditionPlayerPrefsKey)) return;

        string raw = PlayerPrefs.GetString(conditionPlayerPrefsKey, string.Empty);
        if (Enum.TryParse(raw, true, out ExperimentCondition parsed))
            condition = parsed;
        else
            Debug.LogWarning($"[ExperimentDirector] Unknown condition override: {raw}");
    }

    private void ApplyScenarioTimeScaleOverride()
    {
        scenarioTimeScale = Mathf.Max(0.01f, scenarioTimeScale);
        if (string.IsNullOrEmpty(scenarioTimeScalePlayerPrefsKey))
            return;
        if (!PlayerPrefs.HasKey(scenarioTimeScalePlayerPrefsKey))
            return;

        float overrideValue = PlayerPrefs.GetFloat(scenarioTimeScalePlayerPrefsKey, scenarioTimeScale);
        if (overrideValue > 0f)
            scenarioTimeScale = overrideValue;
        else
            Debug.LogWarning($"[ExperimentDirector] Ignoring invalid scenario time scale override: {overrideValue}");
    }

    private readonly struct ScenarioCue
    {
        public readonly float timeSec;
        public readonly string eventId;
        public readonly bool forceChase;

        public ScenarioCue(float timeSec, string eventId, bool forceChase)
        {
            this.timeSec = timeSec;
            this.eventId = eventId;
            this.forceChase = forceChase;
        }
    }

    private void PlayClip(AudioClip clip, DefaultSoundCue fallbackCue)
    {
        if (audioSource == null) return;

        AudioClip resolvedClip = clip != null ? clip : GetDefaultClip(fallbackCue);
        if (resolvedClip != null)
            audioSource.PlayOneShot(resolvedClip);
    }

    private static AudioClip GetDefaultClip(DefaultSoundCue cue)
    {
        if (s_defaultClips.TryGetValue(cue, out AudioClip clip) && clip != null)
            return clip;

        clip = cue switch
        {
            DefaultSoundCue.SessionStart => CreateToneClip("DefaultExperimentStart", 0.18f, 440f, 0.18f, 0.08f),
            DefaultSoundCue.KillerNear => CreateRumbleClip("DefaultExperimentKillerNear", 0.42f, 58f, 0.28f),
            DefaultSoundCue.Success => CreateToneClip("DefaultExperimentSuccess", 0.32f, 660f, 0.22f, 0.1f),
            DefaultSoundCue.Failed => CreateRumbleClip("DefaultExperimentFailed", 0.5f, 46f, 0.32f),
            _ => null,
        };

        if (clip != null)
            s_defaultClips[cue] = clip;
        return clip;
    }

    private static AudioClip CreateToneClip(string name, float durationSec, float frequency, float volume, float noiseAmount)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * durationSec);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float fade = Mathf.Sin(Mathf.Clamp01(t / durationSec) * Mathf.PI);
            float tone = Mathf.Sin(2f * Mathf.PI * frequency * t);
            float noise = PseudoNoise(i) * noiseAmount;
            samples[i] = (tone + noise) * fade * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static AudioClip CreateRumbleClip(string name, float durationSec, float frequency, float volume)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * durationSec);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float fade = 1f - Mathf.Clamp01(t / durationSec);
            float tone = Mathf.Sin(2f * Mathf.PI * frequency * t) * 0.7f;
            float harmonic = Mathf.Sin(2f * Mathf.PI * frequency * 1.5f * t) * 0.25f;
            float noise = PseudoNoise(i) * 0.18f;
            samples[i] = (tone + harmonic + noise) * fade * volume;
        }

        AudioClip clip = AudioClip.Create(name, sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static float PseudoNoise(int seed)
    {
        float value = Mathf.Sin(seed * 12.9898f) * 43758.5453f;
        return (value - Mathf.Floor(value)) * 2f - 1f;
    }

    private void SetPlayerInputLocked(bool locked)
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player == null) return;

        player.GetComponent<FirstPersonController>()?.LockMovement(locked, locked);
    }

    private void EnsureDefaultUi()
    {
        Canvas canvas = GetOrCreateHudCanvas();
        Transform root = canvas.transform;

        if (timerText == null)
        {
            timerText = GetOrCreateText(
                root,
                "ExperimentTimerText_Auto",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -24f),
                new Vector2(180f, 44f),
                28,
                TextAnchor.MiddleLeft,
                Color.white,
                "02:00");
        }

        if (objectiveText == null)
        {
            objectiveText = GetOrCreateText(
                root,
                "ExperimentObjectiveText_Auto",
                new Vector2(0f, 1f),
                new Vector2(0f, 1f),
                new Vector2(24f, -70f),
                new Vector2(520f, 54f),
                22,
                TextAnchor.MiddleLeft,
                new Color(0.85f, 0.95f, 1f, 1f),
                objectiveLabel);
        }

        if (successPanel == null)
            successPanel = GetOrCreateResultPanel(root, "ExperimentSuccessPanel_Auto", "Mission Success", new Color(0.03f, 0.32f, 0.2f, 0.84f));

        if (failedPanel == null)
            failedPanel = GetOrCreateResultPanel(root, "ExperimentFailedPanel_Auto", "Mission Failed", new Color(0.35f, 0.04f, 0.04f, 0.84f));

        GetOrCreateDamageVignette(root);

        if (successPanel != null) successPanel.SetActive(false);
        if (failedPanel != null) failedPanel.SetActive(false);

        if (audioSource == null)
            audioSource = GetComponent<AudioSource>() ?? gameObject.AddComponent<AudioSource>();
    }

    private Canvas GetOrCreateHudCanvas()
    {
        GameObject existing = FindGameObjectIncludingInactive(HudRootName);
        if (existing != null && existing.TryGetComponent(out Canvas foundCanvas))
        {
            if (!existing.activeSelf)
                existing.SetActive(true);
            return foundCanvas;
        }

        GameObject go = new GameObject(HudRootName, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Canvas canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 200;

        CanvasScaler scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        return canvas;
    }

    private Text GetOrCreateText(
        Transform parent,
        string objectName,
        Vector2 anchorMin,
        Vector2 anchorMax,
        Vector2 anchoredPosition,
        Vector2 sizeDelta,
        int fontSize,
        TextAnchor alignment,
        Color color,
        string initialText)
    {
        GameObject existing = FindGameObjectIncludingInactive(objectName);
        Text text = existing != null ? existing.GetComponent<Text>() : null;
        if (text == null)
        {
            GameObject go = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);
            text = go.GetComponent<Text>();
        }
        else if (text.transform.parent != parent)
        {
            text.transform.SetParent(parent, false);
        }

        RectTransform rect = text.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = anchorMin;
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = sizeDelta;

        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.alignment = alignment;
        text.color = color;
        text.raycastTarget = false;
        text.text = initialText;
        return text;
    }

    private GameObject GetOrCreateResultPanel(Transform parent, string objectName, string label, Color background)
    {
        GameObject panel = FindGameObjectIncludingInactive(objectName);
        if (panel == null)
        {
            panel = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(parent, false);
        }
        else if (panel.transform.parent != parent)
        {
            panel.transform.SetParent(parent, false);
        }

        RectTransform rect = panel.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0f, 0f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image image = panel.GetComponent<Image>();
        image.color = background;
        image.raycastTarget = false;

        GetOrCreateText(
            panel.transform,
            objectName + "_Text",
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            Vector2.zero,
            new Vector2(620f, 96f),
            44,
            TextAnchor.MiddleCenter,
            Color.white,
            label);

        return panel;
    }

    private Image GetOrCreateDamageVignette(Transform parent)
    {
        GameObject existing = FindGameObjectIncludingInactive(DamageVignetteName);
        Image image = existing != null ? existing.GetComponent<Image>() : null;
        if (image == null)
        {
            GameObject go = new GameObject(DamageVignetteName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);
            image = go.GetComponent<Image>();
        }
        else if (image.transform.parent != parent)
        {
            image.transform.SetParent(parent, false);
        }

        RectTransform rect = image.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        image.color = new Color(0.85f, 0f, 0f, 0f);
        image.raycastTarget = false;
        image.transform.SetAsLastSibling();
        return image;
    }

    private static GameObject FindGameObjectIncludingInactive(string objectName)
    {
        GameObject[] objects = FindObjectsByType<GameObject>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < objects.Length; i++)
        {
            if (objects[i].name == objectName)
                return objects[i];
        }

        return null;
    }

    private static Font ResolveFont()
    {
        Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (font == null)
            font = Resources.GetBuiltinResource<Font>("Arial.ttf");
        return font;
    }

    private static void SetResultPanelText(GameObject panel, string label)
    {
        if (panel == null) return;
        Text text = panel.GetComponentInChildren<Text>(true);
        if (text != null)
            text.text = label;
    }
}
