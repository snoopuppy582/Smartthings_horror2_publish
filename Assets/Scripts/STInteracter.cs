using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

public class STInteracter : MonoBehaviour
{
    public static STInteracter Instance { get; private set; }

    [SerializeField] private string serverUrl = "http://localhost:3000/event";
    [SerializeField] private float cooldownSeconds = 5f;
    [SerializeField] private float unavailableRetryCooldownSec = 10f;

    // 서버 HANDLERS와 동일하게 유지 (안전 규칙 1, 클라이언트 1차 방어)
    private static readonly HashSet<string> Allowed = new HashSet<string>
    {
        "game_start",
        "ghost_hint",
        "ghost_near",
        "killer_near",
        "blackout",
        "chase",
        "jump_scare",
        "player_hit",
        "mission_success",
        "mission_failed",
        "recovery"
    };

    private readonly Dictionary<string, float> lastSent = new Dictionary<string, float>();
    private float _serverUnavailableUntil = -999f;
    private float _lastUnavailableWarningTime = -999f;

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
    }

    public void SendEvent(string eventName, int intensity = 1)
    {
        if (!Allowed.Contains(eventName))
        { Debug.LogWarning($"[STInteracter] 차단된 이벤트: {eventName}"); return; }
        if (Time.realtimeSinceStartup < _serverUnavailableUntil)
            return;
        if (lastSent.TryGetValue(eventName, out float last) && Time.time - last < cooldownSeconds)
        { Debug.Log($"[STInteracter] 쿨다운 중 — {eventName} 생략"); return; }

        lastSent[eventName] = Time.time;
        StartCoroutine(Post(eventName, intensity));
    }

    private IEnumerator Post(string eventName, int intensity)
    {
        string json = $"{{\"event_id\":\"{eventName}\",\"intensity\":{intensity}," +
                      $"\"timestamp\":{System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}";
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(serverUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.timeout = 3;

            float t0 = Time.realtimeSinceStartup;
            yield return req.SendWebRequest();
            float ms = (Time.realtimeSinceStartup - t0) * 1000f;

            if (req.result == UnityWebRequest.Result.Success)
                Debug.Log($"[STInteracter] {eventName} 성공 ({ms:F0}ms): {req.downloadHandler.text}");
            else
                TrackServerUnavailable(eventName, req.error);
        }
    }

    private void TrackServerUnavailable(string eventName, string error)
    {
        _serverUnavailableUntil = Time.realtimeSinceStartup + unavailableRetryCooldownSec;
        if (Time.realtimeSinceStartup - _lastUnavailableWarningTime < unavailableRetryCooldownSec)
            return;

        _lastUnavailableWarningTime = Time.realtimeSinceStartup;
        Debug.LogWarning($"[STInteracter] 서버 연결 실패; {unavailableRetryCooldownSec:0.#}s 동안 fallback 요청 억제. event={eventName}, error={error}");
    }
}
