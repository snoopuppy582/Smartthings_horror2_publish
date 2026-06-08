using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

/// <summary>
/// 실험 세션별 JSON Lines 로그를 Application.persistentDataPath에 기록한다.
/// </summary>
public class ExperimentLogger : MonoBehaviour
{
    [SerializeField] private string logFolderName = "ExperimentLogs";

    private string _sessionId;
    private string _filePath;
    private readonly Dictionary<string, int> _eventCounts = new();

    public string CurrentLogPath => _filePath;

    public void BeginSession(string sessionId)
    {
        _sessionId = string.IsNullOrEmpty(sessionId) ? "session_unknown" : sessionId;
        _eventCounts.Clear();
        string dir = Path.Combine(Application.persistentDataPath, logFolderName);
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, $"{_sessionId}.jsonl");
        LogRaw("session_begin", "system", 0f, null, 0, null, "log_path", _filePath);
    }

    public void LogEvent(
        string eventName,
        string source,
        float elapsedSec,
        string condition,
        int hitCount,
        bool? iotRequestOk = null,
        string detailKey = null,
        string detailValue = null)
    {
        EnsureSession();
        LogRaw(eventName, source, elapsedSec, condition, hitCount, iotRequestOk, detailKey, detailValue);
    }

    public void LogSummary(bool success, float elapsedSec, string condition, int hitCount)
    {
        EnsureSession();
        string extraJson = $",\"success\":{success.ToString().ToLowerInvariant()},\"event_counts\":{BuildEventCountsJson()}";
        LogRaw(success ? "session_success" : "session_failed", "system", elapsedSec, condition, hitCount, null, null, null, extraJson);
    }

    private void EnsureSession()
    {
        if (!string.IsNullOrEmpty(_filePath)) return;
        BeginSession($"session_{DateTime.UtcNow:yyyyMMdd_HHmmss}");
    }

    private void LogRaw(
        string eventName,
        string source,
        float elapsedSec,
        string condition,
        int hitCount,
        bool? iotRequestOk,
        string detailKey,
        string detailValue)
    {
        LogRaw(eventName, source, elapsedSec, condition, hitCount, iotRequestOk, detailKey, detailValue, null);
    }

    private void LogRaw(
        string eventName,
        string source,
        float elapsedSec,
        string condition,
        int hitCount,
        bool? iotRequestOk,
        string detailKey,
        string detailValue,
        string extraJson)
    {
        string line =
            "{" +
            $"\"utc\":\"{DateTime.UtcNow:o}\"," +
            $"\"session_id\":{JsonString(_sessionId)}," +
            $"\"event_name\":{JsonString(eventName)}," +
            $"\"source\":{JsonString(source)}," +
            $"\"condition\":{JsonString(condition)}," +
            $"\"elapsed_sec\":{FormatFloat(elapsedSec)}," +
            $"\"hit_count\":{hitCount}," +
            $"\"iot_request_ok\":{JsonBool(iotRequestOk)}" +
            DetailJson(detailKey, detailValue) +
            (extraJson ?? string.Empty) +
            "}";

        try
        {
            CountEvent(eventName);
            File.AppendAllText(_filePath, line + Environment.NewLine);
        }
        catch (Exception ex)
        {
            Debug.LogError($"[ExperimentLogger] Log write failed: {ex.Message}");
        }
    }

    private void CountEvent(string eventName)
    {
        if (string.IsNullOrEmpty(eventName)) return;
        _eventCounts.TryGetValue(eventName, out int count);
        _eventCounts[eventName] = count + 1;
    }

    private string BuildEventCountsJson()
    {
        bool first = true;
        string json = "{";
        foreach (KeyValuePair<string, int> pair in _eventCounts)
        {
            if (!first)
                json += ",";
            json += $"\"{Escape(pair.Key)}\":{pair.Value}";
            first = false;
        }

        return json + "}";
    }

    private static string DetailJson(string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return string.Empty;
        return $",\"{Escape(key)}\":{JsonString(value)}";
    }

    private static string JsonString(string value)
    {
        return value == null ? "null" : $"\"{Escape(value)}\"";
    }

    private static string JsonBool(bool? value)
    {
        return value.HasValue ? value.Value.ToString().ToLowerInvariant() : "null";
    }

    private static string FormatFloat(float value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string Escape(string value)
    {
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
