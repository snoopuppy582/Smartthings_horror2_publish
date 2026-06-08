using UnityEngine;

/// <summary>
/// Keeps the old-house mesh collider solid while letting the player pass through the tight doorway.
/// </summary>
[DisallowMultipleComponent]
public sealed class DoorwayHouseCollisionGate : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string houseColliderName = "Old_House_windows_separated_Collider";
    [SerializeField] private Vector3 gateSize = new Vector3(2.75f, 2.35f, 3.4f);
    [SerializeField] private Vector3 gateCenter = new Vector3(0f, 1.05f, 0f);
    [SerializeField] private float padding = 0.25f;
    [SerializeField] private float reacquireIntervalSec = 0.5f;

    private BoxCollider _gateCollider;
    private CharacterController _playerController;
    private Collider _houseCollider;
    private bool _isIgnoring;
    private float _nextReacquireTime;

    public bool IsConfigured => GetComponent<BoxCollider>() != null && GetComponent<BoxCollider>().isTrigger;
    public bool IsIgnoring => _isIgnoring;
    public string HouseColliderName => houseColliderName;
    public Vector3 GateSize => gateSize;
    public Vector3 GateCenter => gateCenter;

    private void Awake()
    {
        ConfigureForExperimentDefaults();
        ReacquireReferences(force: true);
    }

    private void Update()
    {
        ReacquireReferences(force: false);
        bool shouldIgnore = _playerController != null && _houseCollider != null && IsPlayerInsideGate();
        SetCollisionIgnored(shouldIgnore);
    }

    private void OnDisable()
    {
        SetCollisionIgnored(false);
    }

    public void Configure(string targetHouseColliderName, Vector3 localSize)
    {
        Configure(targetHouseColliderName, localSize, gateCenter);
    }

    public void Configure(string targetHouseColliderName, Vector3 localSize, Vector3 localCenter)
    {
        if (!string.IsNullOrEmpty(targetHouseColliderName))
            houseColliderName = targetHouseColliderName;

        gateSize = localSize;
        gateCenter = localCenter;
        ConfigureForExperimentDefaults();
    }

    public void ConfigureForExperimentDefaults()
    {
        _gateCollider = GetComponent<BoxCollider>();
        if (_gateCollider == null)
            _gateCollider = gameObject.AddComponent<BoxCollider>();

        _gateCollider.isTrigger = true;
        _gateCollider.center = gateCenter;
        _gateCollider.size = gateSize;
    }

    private void ReacquireReferences(bool force)
    {
        if (!force && Time.time < _nextReacquireTime && _playerController != null && _houseCollider != null)
            return;

        _nextReacquireTime = Time.time + Mathf.Max(0.1f, reacquireIntervalSec);

        if (_playerController == null)
        {
            GameObject player = GameObject.FindGameObjectWithTag(playerTag);
            if (player != null)
                _playerController = player.GetComponent<CharacterController>();
        }

        if (_houseCollider != null && _houseCollider.enabled)
            return;

        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (Collider collider in colliders)
        {
            if (collider == null || collider.gameObject.name != houseColliderName)
                continue;

            _houseCollider = collider;
            break;
        }
    }

    private bool IsPlayerInsideGate()
    {
        if (_gateCollider == null || _playerController == null)
            return false;

        Vector3 localPoint = transform.InverseTransformPoint(_playerController.bounds.center);
        Vector3 halfSize = gateSize * 0.5f + Vector3.one * Mathf.Max(0f, padding);
        Vector3 localFromCenter = localPoint - gateCenter;

        return Mathf.Abs(localFromCenter.x) <= halfSize.x &&
               Mathf.Abs(localFromCenter.y) <= halfSize.y &&
               Mathf.Abs(localFromCenter.z) <= halfSize.z;
    }

    private void SetCollisionIgnored(bool ignored)
    {
        if (_playerController != null && _houseCollider != null)
            Physics.IgnoreCollision(_playerController, _houseCollider, ignored);

        _isIgnoring = ignored;
    }
}
