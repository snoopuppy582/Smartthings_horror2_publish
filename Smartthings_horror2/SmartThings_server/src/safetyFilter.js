const {
  ALLOWED_EVENTS,
  ALLOWED_DEVICES,
  ALLOWED_ACTIONS,
  COOLDOWN_MS,
  BLACKOUT_MAX_MS,
  FAN_ON_MAX_MS,
} = require('./config');
const { logEvent, counters } = require('./logger');

// event_id별 마지막 실행 시각 (cooldown 추적)
const lastExecuted = new Map();

/**
 * plan의 actions 배열을 안전 필터로 검증·수정한다.
 * @returns {{ allowed: boolean, actions: Array, reason?: string }}
 */
function filterPlan(event_id, plan) {
  // 1. allowlist: event_id 검증
  if (!ALLOWED_EVENTS.includes(event_id)) {
    counters.blocked_event_count++;
    logEvent({ event_id, delayMs: null, status: 'blocked', detail: 'event_id not in allowlist' });
    return { allowed: false, reason: `허용되지 않은 event_id: ${event_id}` };
  }

  // 2. cooldown: 동일 event_id 15초 내 재요청 차단 (recovery/emergencyStop 제외)
  if (event_id !== 'recovery') {
    const last = lastExecuted.get(event_id);
    if (last && Date.now() - last < COOLDOWN_MS) {
      counters.cooldown_block_count++;
      logEvent({ event_id, delayMs: null, status: 'cooldown', detail: `남은 ${COOLDOWN_MS - (Date.now() - last)}ms` });
      return { allowed: false, reason: `cooldown 중: ${event_id}` };
    }
  }

  // 3. actions 검증 및 clamp
  const safeActions = [];
  for (const action of plan.actions) {
    // restore action은 device/type allowlist 검사 면제
    if (action.type === 'restore') {
      safeActions.push(action);
      continue;
    }

    // device allowlist
    if (!ALLOWED_DEVICES.includes(action.device)) {
      counters.blocked_event_count++;
      logEvent({ event_id, delayMs: null, status: 'blocked', detail: `device not allowed: ${action.device}` });
      continue; // 해당 action만 스킵
    }

    // action type allowlist
    if (!ALLOWED_ACTIONS.includes(action.type)) {
      counters.blocked_event_count++;
      logEvent({ event_id, delayMs: null, status: 'blocked', detail: `action type not allowed: ${action.type}` });
      continue;
    }

    // strobe 금지: flash는 반복 없이 1회만
    if (action.type === 'flash' && action.repeat && action.repeat > 1) {
      counters.blocked_event_count++;
      logEvent({ event_id, delayMs: null, status: 'blocked', detail: 'strobe(flash repeat>1) 금지' });
      action.repeat = 1;
    }

    // duration clamp
    let clamped = false;
    if (action.device === 'smart_light' && action.type === 'setSwitch' && action.value === 'off' && action.duration_ms) {
      if (action.duration_ms > BLACKOUT_MAX_MS) {
        action.duration_ms = BLACKOUT_MAX_MS;
        clamped = true;
      }
    }
    if (action.device === 'fan_plug' && action.type === 'setSwitch' && action.value === 'on' && action.duration_ms) {
      if (action.duration_ms > FAN_ON_MAX_MS) {
        action.duration_ms = FAN_ON_MAX_MS;
        clamped = true;
      }
    }
    if (clamped) {
      counters.clamped_duration_count++;
      logEvent({ event_id, delayMs: null, status: 'clamped', detail: `device=${action.device} duration clamped` });
    }

    safeActions.push(action);
  }

  // 4. restore action 보장: 마지막 action이 복구인지 확인
  const lastAction = safeActions[safeActions.length - 1];
  const hasRestore =
    lastAction &&
    lastAction.type === 'restore';
  if (!hasRestore) {
    // restore action 자동 추가
    safeActions.push({ type: 'restore', device: 'all' });
  }

  // cooldown 갱신 (통과한 경우에만)
  lastExecuted.set(event_id, Date.now());

  return { allowed: true, actions: safeActions };
}

/** emergencyStop 시 cooldown 맵 초기화 */
function resetCooldowns() {
  lastExecuted.clear();
}

module.exports = { filterPlan, resetCooldowns };
