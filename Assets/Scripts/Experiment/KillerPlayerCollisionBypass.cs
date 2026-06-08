using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps the killer visible and attack-capable without letting its colliders block the player route.
/// </summary>
[DisallowMultipleComponent]
public class KillerPlayerCollisionBypass : MonoBehaviour
{
    [SerializeField] private string playerTag = "Player";
    [SerializeField] private float refreshIntervalSec = 0.5f;

    private readonly List<Collider> _ignoredKillerColliders = new List<Collider>();
    private Collider _playerCollider;
    private float _nextRefreshTime;

    public bool IsConfigured => _playerCollider != null;
    public int IgnoredColliderCount => _ignoredKillerColliders.Count;

    private void Awake()
    {
        RefreshIgnorePairs();
    }

    private void OnEnable()
    {
        RefreshIgnorePairs();
    }

    private void Update()
    {
        if (Time.time < _nextRefreshTime)
            return;

        _nextRefreshTime = Time.time + Mathf.Max(0.1f, refreshIntervalSec);
        RefreshIgnorePairs();
    }

    private void OnDisable()
    {
        SetIgnored(false);
        _ignoredKillerColliders.Clear();
        _playerCollider = null;
    }

    public void Configure(string tagName)
    {
        if (!string.IsNullOrWhiteSpace(tagName))
            playerTag = tagName;

        RefreshIgnorePairs();
    }

    public bool IsIgnoringCollider(Collider collider)
    {
        return collider != null && _ignoredKillerColliders.Contains(collider);
    }

    private void RefreshIgnorePairs()
    {
        Collider playerCollider = ResolvePlayerCollider();
        if (playerCollider == null)
            return;

        if (_playerCollider != playerCollider)
        {
            SetIgnored(false);
            _playerCollider = playerCollider;
            _ignoredKillerColliders.Clear();
        }

        Collider[] killerColliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < killerColliders.Length; i++)
        {
            Collider killerCollider = killerColliders[i];
            if (killerCollider == null ||
                killerCollider == _playerCollider ||
                killerCollider.isTrigger ||
                _ignoredKillerColliders.Contains(killerCollider))
            {
                continue;
            }

            Physics.IgnoreCollision(_playerCollider, killerCollider, true);
            _ignoredKillerColliders.Add(killerCollider);
        }
    }

    private Collider ResolvePlayerCollider()
    {
        GameObject player = GameObject.FindGameObjectWithTag(playerTag);
        if (player == null)
            return null;

        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null)
            return controller;

        return player.GetComponent<Collider>();
    }

    private void SetIgnored(bool ignored)
    {
        if (_playerCollider == null)
            return;

        for (int i = 0; i < _ignoredKillerColliders.Count; i++)
        {
            Collider killerCollider = _ignoredKillerColliders[i];
            if (killerCollider != null)
                Physics.IgnoreCollision(_playerCollider, killerCollider, ignored);
        }
    }
}
