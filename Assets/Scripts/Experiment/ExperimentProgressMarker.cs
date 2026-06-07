using UnityEngine;

/// <summary>
/// 플레이어가 특정 실험 구간에 진입했음을 ExperimentDirector에 보고한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ExperimentProgressMarker : MonoBehaviour
{
    public enum MarkerType
    {
        StairsReached,
        SecondFloorReached,
        ObjectiveAreaReached,
        KillerCueArea,
        Custom,
    }

    [SerializeField] private MarkerType markerType = MarkerType.Custom;
    [SerializeField] private string optionalEventId;
    [SerializeField] private bool oneShot = true;

    private bool _triggered;

    public void Configure(MarkerType type, string eventId = null, bool triggerOnce = true)
    {
        markerType = type;
        optionalEventId = eventId;
        oneShot = triggerOnce;
    }

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered && oneShot) return;
        if (!other.CompareTag("Player")) return;

        _triggered = true;
        ExperimentDirector.Instance?.ReportProgress(markerType, optionalEventId);
    }
}
