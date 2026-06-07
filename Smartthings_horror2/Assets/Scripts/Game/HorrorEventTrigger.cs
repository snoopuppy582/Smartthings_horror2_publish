using System.Collections;
using UnityEngine;

/// <summary>
/// 플레이어가 콜라이더에 진입할 때 SmartThings 이벤트를 발동하는 트리거.
/// 장소 기반 이벤트(blackout, ghost_hint 등 특정 복도/방 진입)에 사용.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HorrorEventTrigger : MonoBehaviour
{
    [Header("이벤트 설정")]
    [Tooltip("발동할 event_id (ghost_hint / ghost_near / blackout / chase / jump_scare / recovery)")]
    [SerializeField] private string eventId = "ghost_hint";

    [Tooltip("대응하는 GameState. None이면 IoT만 전송하고 게임 상태는 변경 안 함.")]
    [SerializeField] private GameStateOption targetGameState = GameStateOption.None;

    [Header("반복 설정")]
    [Tooltip("true = 1회만 발동 / false = 쿨다운마다 재발동")]
    [SerializeField] private bool triggerOnce = true;
    [Tooltip("triggerOnce=false일 때 재발동 쿨다운(초)")]
    [SerializeField] private float cooldownSec = 30f;

    [Header("블랙아웃 전용")]
    [Tooltip("blackout 이벤트일 때 점멸용 조명 레퍼런스 (선택)")]
    [SerializeField] private Light sceneLight;

    private bool _triggered;
    private float _lastTriggerTime = -999f;

    // GameState 선택을 None 포함해서 따로 열거
    public enum GameStateOption
    {
        None,
        GhostHint,
        GhostNear,
        Chase,
        JumpScare,
        Exploring,
    }

    void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag("Player")) return;
        if (_triggered && triggerOnce) return;
        if (Time.time - _lastTriggerTime < cooldownSec) return;

        _triggered = true;
        _lastTriggerTime = Time.time;

        // GameState 변경 (IoT 전송은 GameManager가 담당)
        if (targetGameState != GameStateOption.None && GameManager.Instance != null)
            GameManager.Instance.SetState(ToGameState(targetGameState));
        else
            // GameManager를 거치지 않고 IoT만 직접 전송 (상태 변경 없는 분위기 이벤트)
            SmartThingsEventSender.Instance?.SendEvent(eventId);

        // 블랙아웃 전용 로컬 조명 효과
        if (eventId == "blackout" && sceneLight != null)
            StartCoroutine(BlackoutEffect());
    }

    private IEnumerator BlackoutEffect()
    {
        sceneLight.enabled = false;
        yield return new WaitForSeconds(0.7f); // BLACKOUT_MAX_MS 이하
        sceneLight.enabled = true;
    }

    private GameManager.GameState ToGameState(GameStateOption opt) => opt switch
    {
        GameStateOption.GhostHint  => GameManager.GameState.GhostHint,
        GameStateOption.GhostNear  => GameManager.GameState.GhostNear,
        GameStateOption.Chase      => GameManager.GameState.Chase,
        GameStateOption.JumpScare  => GameManager.GameState.JumpScare,
        GameStateOption.Exploring  => GameManager.GameState.Exploring,
        _ => GameManager.GameState.Exploring,
    };

#if UNITY_EDITOR
    void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.25f);
        Collider col = GetComponent<Collider>();
        if (col is BoxCollider box)
            Gizmos.DrawCube(transform.position + box.center, box.size);
    }
#endif
}
