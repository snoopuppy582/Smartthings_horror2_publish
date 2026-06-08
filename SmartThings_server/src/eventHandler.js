const { resolvePlan } = require('./planLibrary');
const { filterPlan } = require('./safetyFilter');
const { setLightLevel, setLightSwitch, setFanSwitch, restoreAll } = require('./smartthings');
const { DEFAULT_LIGHT_LEVEL } = require('./config');
const { logEvent, logError, recordLatency, counters } = require('./logger');

const EVENT_PRIORITIES = {
  recovery: 100,
  mission_success: 90,
  mission_failed: 90,
  player_hit: 80,
  jump_scare: 80,
  blackout: 70,
  killer_near: 60,
  chase: 60,
  ghost_near: 50,
  ghost_hint: 40,
  game_start: 30,
};

let activeEffect = null;
let nextEffectToken = 1;

/**
 * action 배열을 순서대로 실행한다.
 * duration_ms 가 있으면 해당 시간만큼 기다린 뒤 복구한다.
 */
async function executeActions(event_id, actions, metadata = null) {
  const startTs = Date.now();
  const effectContext = beginEffectContext(event_id, actions);

  for (const action of actions) {
    try {
      if (shouldSuppressAction(effectContext)) {
        logEvent({ event_id, delayMs: null, status: 'superseded', detail: `active=${activeEffect.event_id}` });
        continue;
      }

      if (action.type === 'restore' || action.device === 'all') {
        if (shouldSkipCleanup(effectContext, 'restore')) continue;
        counters.restore_total_count++;
        await restoreAll();
        counters.restore_success_count++;
        continue;
      }

      if (action.device === 'smart_light') {
        if (action.type === 'setLevel') {
          await setLightLevel(action.value);
          if (action.duration_ms) {
            await delay(action.duration_ms);
            if (!shouldSkipCleanup(effectContext, 'light_level_restore')) {
              await setLightLevel(DEFAULT_LIGHT_LEVEL);
            }
          }
        } else if (action.type === 'setSwitch') {
          await setLightSwitch(action.value);
          if (action.duration_ms) {
            await delay(action.duration_ms);
            // 암전 후 복구
            if (!shouldSkipCleanup(effectContext, 'light_switch_restore')) {
              await setLightSwitch('on');
            }
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
            if (!shouldSkipCleanup(effectContext, 'fan_off')) {
              await setFanSwitch('off');
            }
          }
        }
      }
    } catch (err) {
      logError(`action 실행 실패 [${event_id}]`, err);
    }
  }

  const latencyMs = Date.now() - startTs;
  recordLatency(event_id, latencyMs);
  logEvent({ event_id, delayMs: latencyMs, status: 'ok', metadata });
}

function beginEffectContext(event_id, actions) {
  const priority = EVENT_PRIORITIES[event_id] || 10;
  const token = nextEffectToken++;
  const maxDurationMs = actions.reduce((max, action) => Math.max(max, Number(action.duration_ms) || 0), 0);
  const until = Date.now() + Math.max(maxDurationMs, 250);

  if (!activeEffect || activeEffect.until <= Date.now() || priority >= activeEffect.priority) {
    activeEffect = { event_id, priority, token, until };
  }

  return { event_id, priority, token };
}

function shouldSuppressAction(context) {
  if (!activeEffect || activeEffect.until <= Date.now()) return false;
  if (activeEffect.token === context.token) return false;
  return activeEffect.priority > context.priority;
}

function shouldSkipCleanup(context, cleanupType) {
  if (!activeEffect || activeEffect.until <= Date.now()) return false;
  if (activeEffect.token === context.token) return false;
  if (activeEffect.priority < context.priority) return false;

  logEvent({
    event_id: context.event_id,
    delayMs: null,
    status: 'cleanup_skipped',
    detail: `${cleanupType} superseded by ${activeEffect.event_id}`,
  });
  return true;
}

function resetEffectPriorityForTest() {
  activeEffect = null;
  nextEffectToken = 1;
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
  const metadata = pickEventMetadata(req.body, timestamp);
  res.status(202).json({ status: 'accepted', event_id: id, session_id: metadata.session_id });
  executeActions(id, actions, metadata).catch(err => logError('executeActions 비동기 오류', err));
}

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function pickEventMetadata(body = {}, timestamp) {
  return {
    session_id: body.session_id || null,
    condition: body.condition || null,
    elapsed_sec: typeof body.elapsed_sec === 'number' ? body.elapsed_sec : null,
    hit_count: typeof body.hit_count === 'number' ? body.hit_count : null,
    client_time: timestamp ?? null,
  };
}

module.exports = {
  handleEvent,
  _test: {
    beginEffectContext,
    resetEffectPriorityForTest,
    shouldSkipCleanup,
    shouldSuppressAction,
  },
};
