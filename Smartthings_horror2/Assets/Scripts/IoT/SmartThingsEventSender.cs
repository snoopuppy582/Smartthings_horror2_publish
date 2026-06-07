using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Node.js 서버 /event 엔드포인트에 이벤트를 HTTP POST로 전송하는 싱글턴.
/// 서버 토큰은 이 스크립트가 보유하지 않는다 — 서버 측에서만 관리.
/// </summary>
public class SmartThingsEventSender : MonoBehaviour
{
    public static SmartThingsEventSender Instance { get; private set; }

    [Header("서버 설정")]
    [Tooltip("Node.js 서버 주소 (예: http://localhost:3000)")]
    [SerializeField] private string serverUrl = "http://localhost:3000";

    [Header("디버그")]
    [SerializeField] private bool logRequests = true;

    // 마지막 전송 이벤트 기록 (Unity 측 중복 전송 방지용 — 실제 쿨다운은 서버가 처리)
    private string _lastSentEventId;
    private float _lastSentTime = -999f;
    private const float ClientMinIntervalSec = 0.5f; // 동일 이벤트 0.5초 내 중복 전송 방지

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>이벤트 전송. 결과 콜백은 선택적.</summary>
    public void SendEvent(string eventId, System.Action<bool> onComplete = null)
    {
        if (string.IsNullOrEmpty(eventId)) return;

        // Unity 측 중복 억제
        if (eventId == _lastSentEventId && Time.time - _lastSentTime < ClientMinIntervalSec)
        {
            if (logRequests) Debug.Log($"[SmartThings] 중복 억제: {eventId}");
            return;
        }
        _lastSentEventId = eventId;
        _lastSentTime = Time.time;

        StartCoroutine(PostEvent(eventId, onComplete));
    }

    /// <summary>emergency-stop 전송 (쿨다운 무시).</summary>
    public void SendEmergencyStop(System.Action<bool> onComplete = null)
    {
        StartCoroutine(PostEmergencyStop(onComplete));
    }

    private IEnumerator PostEvent(string eventId, System.Action<bool> onComplete)
    {
        string json = $"{{\"event_id\":\"{eventId}\",\"timestamp\":{Time.time}}}";
        yield return SendPost($"{serverUrl}/event", json, eventId, onComplete);
    }

    private IEnumerator PostEmergencyStop(System.Action<bool> onComplete)
    {
        using var req = new UnityWebRequest($"{serverUrl}/emergency-stop", "POST");
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        yield return req.SendWebRequest();

        bool ok = req.result == UnityWebRequest.Result.Success;
        if (logRequests) Debug.Log($"[SmartThings] emergency-stop → {(ok ? "OK" : req.error)}");
        onComplete?.Invoke(ok);
    }

    private IEnumerator SendPost(string url, string json, string eventId, System.Action<bool> onComplete)
    {
        byte[] body = Encoding.UTF8.GetBytes(json);
        using var req = new UnityWebRequest(url, "POST");
        req.uploadHandler = new UploadHandlerRaw(body);
        req.downloadHandler = new DownloadHandlerBuffer();
        req.SetRequestHeader("Content-Type", "application/json");
        req.timeout = 5;

        yield return req.SendWebRequest();

        bool ok = req.result == UnityWebRequest.Result.Success
               || req.responseCode == 202
               || req.responseCode == 200;

        if (logRequests)
            Debug.Log($"[SmartThings] {eventId} → HTTP {req.responseCode} {(ok ? "OK" : req.error)}");

        onComplete?.Invoke(ok);
    }
}
