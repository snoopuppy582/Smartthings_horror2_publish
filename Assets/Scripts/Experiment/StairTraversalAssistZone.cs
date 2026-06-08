using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(BoxCollider))]
[DefaultExecutionOrder(-100)]
public sealed class StairTraversalAssistZone : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private string houseColliderName = "Old_House_windows_separated_Collider";
    [SerializeField] private Vector3 lowerPoint = new Vector3(-29.15f, 0.22f, -18.75f);
    [SerializeField] private Vector3 upperPoint = new Vector3(-27.35f, 2.94f, -17.25f);
    [SerializeField] private Vector3 zoneSize = new Vector3(5.4f, 4.4f, 5.2f);
    [SerializeField] private Vector3 zoneCenter = new Vector3(0f, 1.15f, 0f);
    [SerializeField] private float insidePadding = 0.35f;
    [SerializeField] private float activationDot = 0.12f;
    [SerializeField] private float reacquireIntervalSec = 0.5f;

    private BoxCollider _zoneCollider;
    private CharacterController _playerController;
    private FirstPersonController _firstPersonController;
    private Collider _houseCollider;
    private bool _isIgnoringHouseCollider;
    private bool _houseColliderOriginalEnabled;
    private bool _houseColliderSuppressed;
    private float _nextReacquireTime;

    public Vector3 LowerPoint => lowerPoint;
    public Vector3 UpperPoint => upperPoint;
    public Vector3 ZoneSize => zoneSize;
    public bool IsIgnoringHouseCollider => _isIgnoringHouseCollider;

    private void Awake()
    {
        ConfigureZoneCollider();
        ReacquirePlayer(force: true);
    }

    private void Update()
    {
        ReacquirePlayer(force: false);
        bool controllerInside = _playerController != null && _playerController.enabled && IsControllerInside(_playerController);
        bool movingInZone = controllerInside &&
            _firstPersonController != null &&
            _firstPersonController.CurrentMoveInput.sqrMagnitude >= 0.01f;
        SetHouseCollisionIgnored(movingInZone);
        if (!movingInZone)
            return;

        TryAssist(_playerController, _firstPersonController.CurrentMoveWorldDirection, Time.deltaTime);
    }

    public void Configure(Vector3 routeLowerPoint, Vector3 routeUpperPoint, Vector3 localSize, Vector3 localCenter)
    {
        lowerPoint = routeLowerPoint;
        upperPoint = routeUpperPoint;
        zoneSize = localSize;
        zoneCenter = localCenter;
        ConfigureZoneCollider();
    }

    public bool TryAssist(CharacterController controller, Vector3 desiredMoveWorld, float deltaTime)
    {
        if (controller == null || !controller.enabled || deltaTime <= 0f)
            return false;

        bool isPlayerController = controller.CompareTag(playerTag);
        if (isPlayerController)
        {
            _playerController = controller;
            if (_firstPersonController == null)
                _firstPersonController = controller.GetComponent<FirstPersonController>();
        }

        Vector3 path = upperPoint - lowerPoint;
        Vector3 flatPath = new Vector3(path.x, 0f, path.z);
        float pathLength = flatPath.magnitude;
        if (pathLength < 0.1f)
            return false;

        Vector3 pathDirection = flatPath / pathLength;
        Vector3 flatMove = new Vector3(desiredMoveWorld.x, 0f, desiredMoveWorld.z);
        if (flatMove.sqrMagnitude < 0.0001f)
            return false;

        if (Vector3.Dot(flatMove.normalized, pathDirection) < activationDot)
            return false;

        if (!IsControllerInside(controller))
            return false;

        if (isPlayerController)
            SetHouseCollisionIgnored(true);

        return false;
    }

    private void OnDisable()
    {
        SetHouseCollisionIgnored(false);
    }

    public void ReleaseHouseCollider()
    {
        SetHouseCollisionIgnored(false);
    }

    private void ConfigureZoneCollider()
    {
        _zoneCollider = GetComponent<BoxCollider>();
        if (_zoneCollider == null)
            _zoneCollider = gameObject.AddComponent<BoxCollider>();

        _zoneCollider.isTrigger = true;
        _zoneCollider.center = zoneCenter;
        _zoneCollider.size = zoneSize;
    }

    private void ReacquirePlayer(bool force)
    {
        if (!force && Time.time < _nextReacquireTime && _playerController != null)
            return;

        _nextReacquireTime = Time.time + Mathf.Max(0.1f, reacquireIntervalSec);
        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        if (player == null)
            return;

        _playerController = player.GetComponent<CharacterController>();
        _firstPersonController = player.GetComponent<FirstPersonController>();
        ResolveHouseCollider();
    }

    private bool IsControllerInside(CharacterController controller)
    {
        if (_zoneCollider == null || controller == null)
            return false;

        Vector3 localPoint = transform.InverseTransformPoint(controller.bounds.center);
        Vector3 halfSize = zoneSize * 0.5f + Vector3.one * Mathf.Max(0f, insidePadding);
        Vector3 localFromCenter = localPoint - zoneCenter;

        return Mathf.Abs(localFromCenter.x) <= halfSize.x &&
               Mathf.Abs(localFromCenter.y) <= halfSize.y &&
               Mathf.Abs(localFromCenter.z) <= halfSize.z;
    }

    private void SetHouseCollisionIgnored(bool ignored)
    {
        ResolveHouseCollider();

        if (_houseCollider != null)
        {
            if (ignored && !_houseColliderSuppressed)
            {
                _houseColliderOriginalEnabled = _houseCollider.enabled;
                _houseCollider.enabled = false;
                _houseColliderSuppressed = true;
            }
            else if (!ignored && _houseColliderSuppressed)
            {
                _houseCollider.enabled = _houseColliderOriginalEnabled;
                _houseColliderSuppressed = false;
            }
        }

        if (_playerController != null && _playerController.enabled && _houseCollider != null && _houseCollider.enabled)
            Physics.IgnoreCollision(_playerController, _houseCollider, ignored);

        _isIgnoringHouseCollider = ignored;
    }

    private void ResolveHouseCollider()
    {
        if (_houseCollider != null && _houseCollider.enabled)
            return;

        Collider[] colliders = FindObjectsByType<Collider>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < colliders.Length; i++)
        {
            Collider collider = colliders[i];
            if (collider == null || collider.gameObject.name != houseColliderName)
                continue;

            _houseCollider = collider;
            return;
        }
    }
}
