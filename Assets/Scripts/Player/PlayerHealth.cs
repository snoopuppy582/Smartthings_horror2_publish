using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 플레이어 체력 및 적에게 잡혔을 때 처리.
/// 화면 비네트(붉은 테두리)로 위험 상황을 시각적으로 전달한다.
/// </summary>
public class PlayerHealth : MonoBehaviour
{
    [Header("체력")]
    [SerializeField] private float maxHealth = 100f;
    [SerializeField] private float currentHealth;

    [Header("피격 효과")]
    [SerializeField] private Image damageVignette;   // 붉은 비네트 UI Image
    [SerializeField] private float vignetteFadeSpeed = 3f;

    [Header("오디오")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip heartbeatClip;  // 위험 시 심장소리
    [SerializeField] private AudioClip deathClip;

    private bool _isDead;
    private float _vignetteAlpha;

    public float HealthRatio => currentHealth / maxHealth;
    public bool IsDead => _isDead;

    void Awake()
    {
        currentHealth = maxHealth;
        ResolveDamageVignette();
        if (damageVignette != null)
        {
            Color c = damageVignette.color;
            c.a = 0f;
            damageVignette.color = c;
        }
    }

    void Update()
    {
        UpdateVignette();
        UpdateHeartbeat();
    }

    // ── 피해 처리 ────────────────────────────────────────────────

    public void TakeDamage(float amount)
    {
        if (_isDead) return;
        currentHealth = Mathf.Max(0f, currentHealth - amount);
        _vignetteAlpha = 1f;

        if (currentHealth <= 0f) Die();
    }

    /// <summary>적이 즉사 처리(점프 스케어 후 잡힘) 시 호출.</summary>
    public void CaughtByEnemy()
    {
        if (_isDead) return;

        if (ExperimentDirector.Instance != null && ExperimentDirector.Instance.IsRunning)
        {
            ApplyExperimentHit();
            return;
        }

        StartCoroutine(CaughtSequence());
    }

    private void ApplyExperimentHit()
    {
        _vignetteAlpha = 1f;
        currentHealth = Mathf.Max(maxHealth * 0.35f, currentHealth - 15f);
        ExperimentDirector.Instance.ReportPlayerHit();
        GetComponent<NonLethalHitFeedback>()?.PlayFeedback();
    }

    private IEnumerator CaughtSequence()
    {
        _isDead = true;
        _vignetteAlpha = 1f;

        // jump_scare 이벤트 → 잠시 후 게임오버
        GameManager.Instance?.TriggerIoT("jump_scare");

        if (audioSource != null && deathClip != null)
            audioSource.PlayOneShot(deathClip);

        // 이동 잠금
        GetComponent<FirstPersonController>()?.LockMovement(true);

        yield return new WaitForSeconds(1.5f);
        GameManager.Instance?.SetState(GameManager.GameState.GameOver);
    }

    private void Die()
    {
        _isDead = true;
        GameManager.Instance?.SetState(GameManager.GameState.GameOver);
    }

    // ── UI 비네트 ────────────────────────────────────────────────

    private void UpdateVignette()
    {
        if (damageVignette == null)
            ResolveDamageVignette();

        // 체력이 낮을수록 비네트 기본값 높음
        float lowHealthAlpha = HealthRatio < 0.3f ? (1f - HealthRatio / 0.3f) * 0.5f : 0f;
        float target = Mathf.Max(lowHealthAlpha, _vignetteAlpha);

        _vignetteAlpha = Mathf.MoveTowards(_vignetteAlpha, lowHealthAlpha, Time.deltaTime * vignetteFadeSpeed);

        if (damageVignette != null)
        {
            Color c = damageVignette.color;
            c.a = target;
            damageVignette.color = c;
        }
    }

    private void UpdateHeartbeat()
    {
        if (audioSource == null || heartbeatClip == null) return;
        bool lowHealth = HealthRatio < 0.3f;

        if (lowHealth && !audioSource.isPlaying)
            audioSource.PlayOneShot(heartbeatClip);
    }

    // ── 체력 회복 (세이프 존 진입 등) ───────────────────────────

    public void Heal(float amount)
    {
        if (_isDead) return;
        currentHealth = Mathf.Min(maxHealth, currentHealth + amount);
    }

    public void FullHeal() => currentHealth = maxHealth;

    private void ResolveDamageVignette()
    {
        if (damageVignette != null) return;

        GameObject vignette = GameObject.Find(ExperimentDirector.DamageVignetteName);
        if (vignette != null)
            damageVignette = vignette.GetComponent<Image>();
    }
}
