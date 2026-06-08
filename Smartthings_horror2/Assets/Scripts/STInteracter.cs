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

    // 서버 HANDLERS와 동일하게 유지 (안전 규칙 1, 클라이언트 1차 방어)
    private static readonly HashSet<string> Allowed = new HashSet<string>
    { "Enemy_hint", "Enemy_near", "blackout", "chase", "jump_scare", "recovery", "plug_on", "plug_off" };

    private readonly Dictionary<string, float> lastSent = new Dictionary<string, float>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this; DontDestroyOnLoad(gameObject);
    }

    public void SendEvent(string eventName, int intensity = 1)
    {
        if (!Allowed.Contains(eventName))
        { Debug.LogWarning($"[STInteracter] 차단된 이벤트: {eventName}"); return; }
        if (lastSent.TryGetValue(eventName, out float last) && Time.time - last < cooldownSeconds)
        { Debug.Log($"[STInteracter] 쿨다운 중 — {eventName} 생략"); return; }

        lastSent[eventName] = Time.time;
        StartCoroutine(Post(eventName, intensity));
    }

    private IEnumerator Post(string eventName, int intensity)
    {
        string json = $"{{\"event\":\"{eventName}\",\"intensity\":{intensity}," +
                      $"\"timestamp\":{System.DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}}}";
        byte[] body = Encoding.UTF8.GetBytes(json);

        using (var req = new UnityWebRequest(serverUrl, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(body);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");

            float t0 = Time.realtimeSinceStartup;
            yield return req.SendWebRequest();
            float ms = (Time.realtimeSinceStartup - t0) * 1000f;

            if (req.result == UnityWebRequest.Result.Success)
                Debug.Log($"[STInteracter] {eventName} 성공 ({ms:F0}ms): {req.downloadHandler.text}");
            else
                Debug.LogError($"[STInteracter] {eventName} 실패: {req.error}");
        }
    }
}