const { resolvePlan } = require('./planLibrary');
const { filterPlan } = require('./safetyFilter');
const { setLightLevel, setLightSwitch, setFanSwitch, restoreAll } = require('./smartthings');
const { logEvent, logError, recordLatency, counters } = require('./logger');

/**
 * action 배열을 순서대로 실행한다.
 * duration_ms 가 있으면 해당 시간만큼 기다린 뒤 복구한다.
 */
async function executeActions(event_id, actions) {
  const startTs = Date.now();

  for (const action of actions) {
    try {
      if (action.type === 'restore' || action.device === 'all') {
        counters.restore_total_count++;
        await restoreAll();
        counters.restore_success_count++;
        continue;
      }

      if (action.device === 'smart_light') {
        if (action.type === 'setLevel') {
          await setLightLevel(action.value);
        } else if (action.type === 'setSwitch') {
          await setLightSwitch(action.value);
          if (action.duration_ms) {
            await delay(action.duration_ms);
            // 암전 후 복구
            await setLightSwitch('on');
          }
        } else if (action.type === 'flash') {
          // flash 1회: 껐다 켜기
          await setLightSwitch('off');
          await delay(action.duration_ms || 200);
          await setLightSwitch('on');
        }
      } else if (action.device === 'fan_plug') {
        if (action.type === 'setSwitch') {
          await setFanSwitch(action.value);
          if (action.value === 'on' && action.duration_ms) {
            await delay(action.duration_ms);
            await setFanSwitch('off');
          }
        }
      }
    } catch (err) {
      logError(`action 실행 실패 [${event_id}]`, err);
    }
  }

  const latencyMs = Date.now() - startTs;
  recordLatency(event_id, latencyMs);
  logEvent({ event_id, delayMs: latencyMs, status: 'ok' });
}

/** POST /event 처리 */
async function handleEvent(req, res) {
  const { event_id, plan_id, timestamp } = req.body || {};
  const id = event_id || plan_id;

  if (!id) {
    return res.status(400).json({ error: 'event_id 또는 plan_id 필요' });
  }

  // plan 조회
  const plan = resolvePlan(id);
  if (!plan) {
    counters.blocked_event_count++;
    logEvent({ event_id: id, delayMs: null, status: 'blocked', detail: 'plan 없음' });
    return res.status(404).json({ error: `plan 없음: ${id}` });
  }

  // 안전 필터
  const { allowed, actions, reason } = filterPlan(id, plan);
  if (!allowed) {
    return res.status(429).json({ error: reason });
  }

  // 즉시 202 응답 후 비동기 실행 (Unity가 블로킹되지 않도록)
  res.status(202).json({ status: 'accepted', event_id: id });
  executeActions(id, actions).catch(err => logError('executeActions 비동기 오류', err));
}

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

module.exports = { handleEvent };
