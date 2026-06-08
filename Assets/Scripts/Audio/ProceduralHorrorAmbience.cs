using System;
using UnityEngine;

/// <summary>
/// 저작권 외부 음원 없이 120초 공포 실험용 드론, 저주파, 노이즈, 심박 계열 앰비언스를 생성한다.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(AudioSource))]
public class ProceduralHorrorAmbience : MonoBehaviour
{
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private bool playOnStart = true;
    [SerializeField] private float masterVolume = 0.28f;
    [SerializeField] private float baseTension = 0.22f;
    [SerializeField] private float transitionSpeed = 0.75f;
    [SerializeField] private float maxTension = 1f;

    private AudioClip _clip;
    private int _sampleRate = 48000;
    private long _sampleCursor;
    private uint _noiseState = 0x1234567u;
    private float _noiseLow;
    private float _noiseLower;
    private float _currentTension;
    private float _audioTension;
    private float _audioAccent;
    private float _eventTensionUntil = -999f;
    private float _eventTensionValue;
    private float _accentUntil = -999f;
    private float _accentStart = -999f;
    private float _accentDuration = 0.1f;
    private float _accentStrength;

    public bool HasUsableOutput => audioSource != null && masterVolume > 0.05f && maxTension > 0.5f;
    public bool IsPlaying => audioSource != null && audioSource.isPlaying;
    public bool UsesProceduralClip => audioSource != null && audioSource.clip != null && audioSource.clip.name == "ProceduralHorrorAmbience";
    public string RuntimeClipName => audioSource != null && audioSource.clip != null ? audioSource.clip.name : string.Empty;
    public float CurrentTension => _currentTension;
    public float ActiveEventTension => Time.time < _eventTensionUntil ? _eventTensionValue : 0f;

    private void Awake()
    {
        ConfigureForExperimentDefaults();
    }

    private void Start()
    {
        if (playOnStart)
            BeginPlayback();
    }

    private void Update()
    {
        float desired = baseTension;

        ExperimentDirector director = ExperimentDirector.Instance;
        if (director != null && director.IsRunning)
        {
            desired = Mathf.Max(desired, Mathf.InverseLerp(0f, 120f, director.ElapsedSec) * 0.34f);
            if (director.KillerCueTriggered)
                desired = Mathf.Max(desired, Mathf.Clamp01(0.72f - director.SecondsSinceKillerCue * 0.035f));
            if (director.HitCount > 0)
                desired = Mathf.Max(desired, 0.52f);
        }

        if (Time.time < _eventTensionUntil)
            desired = Mathf.Max(desired, _eventTensionValue);

        if (GameManager.Instance != null)
        {
            desired = GameManager.Instance.CurrentState switch
            {
                GameManager.GameState.GhostHint => Mathf.Max(desired, 0.48f),
                GameManager.GameState.GhostNear => Mathf.Max(desired, 0.65f),
                GameManager.GameState.Chase => Mathf.Max(desired, 0.92f),
                GameManager.GameState.JumpScare => Mathf.Max(desired, 1f),
                GameManager.GameState.GameOver => Mathf.Max(desired, 0.35f),
                _ => desired,
            };
        }

        _currentTension = Mathf.MoveTowards(_currentTension, Mathf.Clamp(desired, 0f, maxTension), transitionSpeed * Time.deltaTime);
        _audioTension = _currentTension;
        _audioAccent = ResolveAccent();

        if (!IsPlaying && playOnStart)
            BeginPlayback();
    }

    public void ConfigureForExperimentDefaults()
    {
        audioSource = GetComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = true;
        audioSource.spatialBlend = 0f;
        audioSource.dopplerLevel = 0f;
        audioSource.priority = 96;
        audioSource.volume = Mathf.Clamp01(masterVolume);

        baseTension = Mathf.Clamp(baseTension, 0.08f, 0.45f);
        transitionSpeed = Mathf.Max(0.25f, transitionSpeed);
        maxTension = Mathf.Clamp(maxTension, 0.65f, 1f);
    }

    public void BeginPlayback()
    {
        ConfigureForExperimentDefaults();

        _sampleRate = Mathf.Max(22050, AudioSettings.outputSampleRate);
        if (_clip == null || _clip.frequency != _sampleRate)
            _clip = AudioClip.Create("ProceduralHorrorAmbience", _sampleRate * 2, 2, _sampleRate, true, FillAudio);

        audioSource.clip = _clip;
        audioSource.loop = true;
        if (!audioSource.isPlaying)
            audioSource.Play();
    }

    public void ReactToExperimentEvent(string eventId)
    {
        if (string.IsNullOrEmpty(eventId))
            return;

        switch (eventId)
        {
            case "game_start":
                PushTension(4f, 0.34f);
                TriggerAccent(1.2f, 0.18f);
                break;
            case "ghost_hint":
                PushTension(9f, 0.52f);
                TriggerAccent(2.2f, 0.24f);
                break;
            case "killer_near":
                PushTension(10f, 0.78f);
                TriggerAccent(2.8f, 0.42f);
                break;
            case "blackout":
                PushTension(8f, 0.68f);
                TriggerAccent(3.4f, 0.34f);
                break;
            case "chase":
                PushTension(12f, 0.96f);
                TriggerAccent(5.5f, 0.46f);
                break;
            case "player_hit":
                PushTension(7f, 1f);
                TriggerAccent(1.6f, 0.9f);
                break;
            case "mission_failed":
                PushTension(4f, 0.58f);
                TriggerAccent(2.4f, 0.55f);
                break;
            case "mission_success":
                PushTension(2.2f, 0.28f);
                TriggerAccent(1.0f, 0.16f);
                break;
        }
    }

    private void PushTension(float durationSec, float tension)
    {
        float until = Time.time + Mathf.Max(0.1f, durationSec);
        float value = Mathf.Clamp01(tension);
        if (Time.time < _eventTensionUntil && value < _eventTensionValue)
        {
            _eventTensionUntil = Mathf.Max(_eventTensionUntil, until);
            return;
        }

        _eventTensionUntil = until;
        _eventTensionValue = value;
    }

    private void TriggerAccent(float durationSec, float strength)
    {
        _accentStart = Time.time;
        _accentDuration = Mathf.Max(0.05f, durationSec);
        _accentUntil = Time.time + _accentDuration;
        _accentStrength = Mathf.Clamp01(strength);
    }

    private float ResolveAccent()
    {
        if (Time.time >= _accentUntil)
            return 0f;

        float elapsed = Time.time - _accentStart;
        float falloff = 1f - Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _accentDuration));
        return _accentStrength * falloff;
    }

    private void FillAudio(float[] data)
    {
        int channels = 2;
        double invSampleRate = 1.0 / Math.Max(1, _sampleRate);
        float tension = _audioTension;
        float accent = _audioAccent;
        float volume = Mathf.Clamp01(masterVolume);

        for (int i = 0; i < data.Length; i += channels)
        {
            double t = _sampleCursor * invSampleRate;
            float drone =
                Sine(t, 39.5) * 0.28f +
                Sine(t, 54.7) * 0.18f +
                Sine(t, 82.3 + tension * 7.0) * 0.08f;

            float rawNoise = NextNoise();
            _noiseLow = _noiseLow * 0.985f + rawNoise * 0.015f;
            _noiseLower = _noiseLower * 0.997f + rawNoise * 0.003f;
            float air = _noiseLow * (0.12f + tension * 0.13f) + _noiseLower * 0.16f;

            float pulseRate = 0.85f + tension * 0.75f;
            double pulsePhase = t * pulseRate;
            pulsePhase -= Math.Floor(pulsePhase);
            float heartbeat = (float)Math.Exp(-pulsePhase * (18.0 - tension * 4.0)) * Sine(pulsePhase, 4.5);
            heartbeat *= Mathf.Clamp01((tension - 0.35f) / 0.65f) * 0.24f;

            float shimmer = Sine(t, 3.2 + tension * 2.0) * Sine(t, 147.0 + tension * 11.0) * 0.03f * tension;
            float accentPulse = Sine(t, 29.0) * accent * 0.22f + rawNoise * accent * 0.08f;

            float sample = (drone + air + heartbeat + shimmer + accentPulse) * volume;
            sample = SoftClip(sample);

            float pan = Sine(t, 0.08) * 0.08f;
            data[i] = sample * (1f - pan);
            if (i + 1 < data.Length)
                data[i + 1] = sample * (1f + pan);

            _sampleCursor++;
        }
    }

    private static float Sine(double time, double frequency)
    {
        return (float)Math.Sin(time * frequency * Math.PI * 2.0);
    }

    private float NextNoise()
    {
        _noiseState = _noiseState * 1664525u + 1013904223u;
        return ((_noiseState >> 8) / 16777215f) * 2f - 1f;
    }

    private static float SoftClip(float value)
    {
        return value / (1f + Mathf.Abs(value));
    }
}
