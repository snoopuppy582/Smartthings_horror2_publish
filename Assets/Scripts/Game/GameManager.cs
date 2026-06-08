using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// 게임 전체 상태 머신. SmartThings 이벤트 전송의 중앙 창구.
/// 씬에 하나만 존재하는 싱글턴.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    public enum GameState
    {
        Exploring,   // 평상시 탐색
        GhostHint,   // 먼 기척
        GhostNear,   // 추적자 접근
        Chase,       // 추격 중
        JumpScare,   // 근거리 조우
        GameOver,    // 플레이어 사망
        Paused,
    }

    public GameState CurrentState { get; private set; } = GameState.Exploring;

    [Header("게임오버 UI")]
    [SerializeField] private GameObject gameOverPanel;
    [SerializeField] private GameObject pausePanel;

    [Header("실험 조건")]
    [Tooltip("true = SmartThings 연동 실험 조건 / false = 제어 조건(IoT 없음)")]
    [SerializeField] public bool iotEnabled = true;

    // 게임오버 후 재시작 딜레이
    private const float RestartDelay = 3f;

    void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Escape))
            TogglePause();
    }

    // ── 상태 전환 ──────────────────────────────────────────────────

    public void SetState(GameState newState)
    {
        if (CurrentState == newState) return;
        if (CurrentState == GameState.GameOver) return; // 게임오버 후 전환 차단

        CurrentState = newState;

        switch (newState)
        {
            case GameState.GhostHint:  TriggerIoT("ghost_hint");  break;
            case GameState.GhostNear:  TriggerIoT("ghost_near");  break;
            case GameState.Chase:      TriggerIoT("chase");        break;
            case GameState.JumpScare:  TriggerIoT("jump_scare");   break;
            case GameState.Exploring:  TriggerIoT("recovery");     break;
            case GameState.GameOver:   OnGameOver();               break;
        }
    }

    // ── IoT 이벤트 전송 ──────────────────────────────────────────

    public void TriggerIoT(string eventId)
    {
        if (!iotEnabled) return;
        SmartThingsEventSender.Instance?.SendEvent(eventId);
    }

    // ── 게임오버 ────────────────────────────────────────────────

    private void OnGameOver()
    {
        TriggerIoT("recovery"); // 모든 기기 복구 최우선
        if (gameOverPanel) gameOverPanel.SetActive(true);
        Cursor.lockState = CursorLockMode.None;
        Invoke(nameof(ReloadScene), RestartDelay);
    }

    private void ReloadScene()
    {
        CurrentState = GameState.Exploring;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ── 일시정지 ────────────────────────────────────────────────

    public void TogglePause()
    {
        if (CurrentState == GameState.GameOver) return;

        bool paused = CurrentState == GameState.Paused;
        if (paused)
        {
            CurrentState = GameState.Exploring;
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.Locked;
            if (pausePanel) pausePanel.SetActive(false);
        }
        else
        {
            CurrentState = GameState.Paused;
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            if (pausePanel) pausePanel.SetActive(true);
        }
    }

    void OnApplicationQuit()
    {
        // 앱 종료 시 모든 기기 복구
        SmartThingsEventSender.Instance?.SendEmergencyStop();
    }
}
