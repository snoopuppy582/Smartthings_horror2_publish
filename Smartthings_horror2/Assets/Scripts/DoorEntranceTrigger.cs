using UnityEngine;

// 문 개구부에 두는 트리거 콜라이더 — 플레이어 통과 시 door_entrance 발생
[RequireComponent(typeof(Collider))]
public class DoorEntranceTrigger : MonoBehaviour
{
    [SerializeField] private HouseLightController houseLights; // 집 안 조명 컨트롤러
    [SerializeField] private string playerTag = "Player";

    private bool triggered = false; // 중복 발동 방지

    private void Reset() => GetComponent<Collider>().isTrigger = true;

    private void OnTriggerEnter(Collider other)
    {
        if (triggered || !other.CompareTag(playerTag)) return;
        triggered = true;

        // 1) 게임 내 집 안 암전 (지속)
        if (houseLights != null) houseLights.Blackout();

        // 2) 실제 SmartThings 조명 암전 요청 (서버 안전 필터 통과)
        STInteracter.Instance.SendEvent("blackout");

        Debug.Log("[DoorEntrance] 플레이어 진입 — door_entrance 전송");
    }
}