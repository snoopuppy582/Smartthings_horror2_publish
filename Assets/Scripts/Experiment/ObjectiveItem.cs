using UnityEngine;

/// <summary>
/// 2층 목표물을 수집하면 실험 성공으로 종료한다.
/// </summary>
[RequireComponent(typeof(Collider))]
public class ObjectiveItem : MonoBehaviour
{
    [SerializeField] private bool destroyOnCollect = true;
    [SerializeField] private bool rotate = true;
    [SerializeField] private float rotateSpeed = 45f;
    [SerializeField] private bool pulse = true;
    [SerializeField] private float pulseAmount = 0.08f;
    [SerializeField] private float pulseSpeed = 2f;

    private Vector3 _baseScale;
    private bool _collected;

    private void Awake()
    {
        _baseScale = transform.localScale;
    }

    private void Reset()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void Update()
    {
        if (_collected) return;

        if (rotate)
            transform.Rotate(Vector3.up, rotateSpeed * Time.deltaTime, Space.World);

        if (pulse)
        {
            float scale = 1f + Mathf.Sin(Time.time * pulseSpeed) * pulseAmount;
            transform.localScale = _baseScale * scale;
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        TryCollect(other);
    }

    private void OnTriggerStay(Collider other)
    {
        TryCollect(other);
    }

    public bool TryCollect(Collider other)
    {
        if (_collected) return false;
        if (other == null || !other.CompareTag("Player")) return false;

        _collected = true;
        ExperimentDirector.Instance?.CompleteObjective();

        if (destroyOnCollect)
            gameObject.SetActive(false);

        return true;
    }
}
