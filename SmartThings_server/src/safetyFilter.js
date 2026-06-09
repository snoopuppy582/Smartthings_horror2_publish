const {
  ALLOWED_EVENTS,
  ALLOWED_DEVICES,
  ALLOWED_ACTIONS,
  COOLDOWN_MS,
  COOLDOWN_OVERRIDES,
  BLACKOUT_MAX_MS,
  FAN_ON_MAX_MS,
  LIGHT_EFFECT_MAX_MS,
  LIGHT_LEVEL_MIN,
  LIGHT_LEVEL_MAX,
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

  // 2. cooldown: 동일 event_id 재요청 차단 (recovery 제외, 이벤트별 재정의 적용)
  if (event_id !== 'recovery') {
    const cooldown = COOLDOWN_OVERRIDES[event_id] ?? COOLDOWN_MS;
    const last = lastExecuted.get(event_id);
    if (last && Date.now() - last < cooldown) {
      counters.cooldown_block_count++;
      logEvent({ event_id, delayMs: null, status: 'cooldown', detail: `남은 ${cooldown - (Date.now() - last)}ms` });
      return { allowed: false, reason: `cooldown 중: ${event_id}` };
    }
  }

  // 3. actions 검증 및 clamp
  const safeActions = [];
  for (const action of plan.actions) {
    const safeAction = { ...action };

    // restore action은 device/type allowlist 검사 면제
    if (safeAction.type === 'restore') {
      safeActions.push(safeAction);
      continue;
    }

    // device allowlist
    if (!ALLOWED_DEVICES.includes(safeAction.device)) {
      counters.blocked_event_count++;
      logEvent({ event_id, delayMs: null, status: 'blocked', detail: `device not allowed: ${safeAction.device}` });
      continue; // 해당 action만 스킵
    }

    // action type allowlist
    if (!ALLOWED_ACTIONS.includes(safeAction.type)) {
      counters.blocked_event_count++;
      logEvent({ event_id, delayMs: null, status: 'blocked', detail: `action type not allowed: ${safeAction.type}` });
      continue;
    }

    // strobe 금지: flash는 반복 없이 1회만
    if (safeAction.type === 'flash' && safeAction.repeat && safeAction.repeat > 1) {
      counters.blocked_event_count++;
      logEvent({ event_id, delayMs: null, status: 'blocked', detail: 'strobe(flash repeat>1) 금지' });
      safeAction.repeat = 1;
    }

    // duration clamp
    let clamped = false;
    if (safeAction.device === 'smart_light' && safeAction.type === 'setSwitch' && safeAction.value === 'off' && safeAction.duration_ms) {
      if (safeAction.duration_ms > BLACKOUT_MAX_MS) {
        safeAction.duration_ms = BLACKOUT_MAX_MS;
        clamped = true;
      }
    }
    if (safeAction.device === 'fan_plug' && safeAction.type === 'setSwitch' && safeAction.value === 'on' && safeAction.duration_ms) {
      if (safeAction.duration_ms > FAN_ON_MAX_MS) {
        safeAction.duration_ms = FAN_ON_MAX_MS;
        clamped = true;
      }
    }
    if (safeAction.device === 'smart_light' && (safeAction.type === 'setLevel' || safeAction.type === 'flash') && safeAction.duration_ms) {
      if (safeAction.duration_ms > LIGHT_EFFECT_MAX_MS) {
        safeAction.duration_ms = LIGHT_EFFECT_MAX_MS;
        clamped = true;
      }
    }
    if (safeAction.device === 'smart_light' && safeAction.type === 'setLevel' && typeof safeAction.value === 'number') {
      const clampedValue = Math.min(LIGHT_LEVEL_MAX, Math.max(LIGHT_LEVEL_MIN, safeAction.value));
      if (clampedValue !== safeAction.value) {
        safeAction.value = clampedValue;
        clamped = true;
      }
    }
    if (clamped) {
      counters.clamped_duration_count++;
      logEvent({ event_id, delayMs: null, status: 'clamped', detail: `device=${safeAction.device} safety clamp applied` });
    }

    safeActions.push(safeAction);
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
