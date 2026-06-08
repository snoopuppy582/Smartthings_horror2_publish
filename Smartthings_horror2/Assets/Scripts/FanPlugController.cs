using System.Collections;
using UnityEngine;

// 선풍기 플러그 제어
// 로컬 wind 파티클 이펙트 재생 + SmartThings 서버에 plug_on / plug_off 이벤트 전송
public class FanPlugController : MonoBehaviour
{
    [Header("로컬 바람 이펙트 (선택)")]
    [SerializeField] private ParticleSystem windParticle;

    private Coroutine _routine;

    // ── 6가지 이벤트 반응 (CLAUDE.md 기준) ──────────────────────────

    public void OnEnemyHint()  => SetPlug(false);      // 플러그 OFF
    public void OnEnemyNear()  => RunTimedOn(4f);      // 4초 ON → OFF
    public void OnBlackout()   => SetPlug(false);      // 플러그 OFF
    public void OnChase()      => RunTimedOn(4f);      // 4초 ON → OFF
    public void OnJumpScare()  => RunCycle(0.3f);      // T/F/T 0.3초 사이클
    public void OnRecovery()   => SetPlug(false);      // 플러그 OFF

    // ── 내부 구현 ─────────────────────────────────────────────────

    private void SetPlug(bool on)
    {
        StopRoutine();
        SendPlug(on);
        ApplyWind(on);
    }

    private void RunTimedOn(float duration)
    {
        StopRoutine();
        _routine = StartCoroutine(TimedOnRoutine(duration));
    }

    private void RunCycle(float interval)
    {
        StopRoutine();
        _routine = StartCoroutine(CycleRoutine(interval));
    }

    private IEnumerator TimedOnRoutine(float duration)
    {
        SendPlug(true);
        ApplyWind(true);
        yield return new WaitForSeconds(duration);
        SendPlug(false);
        ApplyWind(false);
        _routine = null;
    }

    private IEnumerator CycleRoutine(float interval)
    {
        bool state = true;
        while (true)
        {
            SendPlug(state);
            ApplyWind(state);
            state = !state;
            yield return new WaitForSeconds(interval);
        }
    }

    private void StopRoutine()
    {
        if (_routine == null) return;
        StopCoroutine(_routine);
        _routine = null;
    }

    private void SendPlug(bool on)
    {
        SmartThingsEventSender.Instance?.SendEvent(on ? "plug_on" : "plug_off");
    }

    private void ApplyWind(bool on)
    {
        if (windParticle == null) return;
        if (on) windParticle.Play();
        else    windParticle.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }
}
