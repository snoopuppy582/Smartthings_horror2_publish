const fs = require('fs');
const path = require('path');
const { logError, logInfo } = require('./logger');

const PLANS_DIR = path.join(__dirname, '..', 'plans');

// 메모리 캐시
const planCache = new Map();

/** plans/ 폴더의 JSON 파일을 모두 로드해 캐시 */
function loadPlans() {
  if (!fs.existsSync(PLANS_DIR)) {
    logError('plans/ 디렉토리 없음. plan이 없어도 hardcoded fallback으로 동작합니다.');
    return;
  }
  const files = fs.readdirSync(PLANS_DIR).filter(f => f.endsWith('.json'));
  for (const file of files) {
    try {
      const raw = fs.readFileSync(path.join(PLANS_DIR, file), 'utf-8');
      const plan = JSON.parse(raw);
      if (plan.plan_id) {
        planCache.set(plan.plan_id, plan);
      }
    } catch (e) {
      logError(`plan 로드 실패: ${file}`, e);
    }
  }
  logInfo(`plan library 로드 완료: ${planCache.size}개`);
}

/** plan_id 또는 event_id로 plan 조회 */
function getPlan(id) {
  return planCache.get(id) || null;
}

/** 하드코딩 fallback plan (plans/ 에 JSON 없을 때 사용) */
const FALLBACK_PLANS = {
  ghost_hint: {
    plan_id: 'ghost_hint',
    actions: [
      { device: 'smart_light', type: 'setLevel', value: 45 },
      { device: 'fan_plug', type: 'setSwitch', value: 'off' },
      { type: 'restore', device: 'all' },
    ],
  },
  ghost_near: {
    plan_id: 'ghost_near',
    actions: [
      { device: 'smart_light', type: 'setLevel', value: 25 },
      { device: 'fan_plug', type: 'setSwitch', value: 'on', duration_ms: 3500 },
      { type: 'restore', device: 'all' },
    ],
  },
  blackout: {
    plan_id: 'blackout',
    actions: [
      { device: 'smart_light', type: 'setSwitch', value: 'off', duration_ms: 700 },
      { device: 'fan_plug', type: 'setSwitch', value: 'off' },
      { type: 'restore', device: 'all' },
    ],
  },
  chase: {
    plan_id: 'chase',
    actions: [
      { device: 'smart_light', type: 'setLevel', value: 20 },
      { device: 'fan_plug', type: 'setSwitch', value: 'on', duration_ms: 5000 },
      { type: 'restore', device: 'all' },
    ],
  },
  jump_scare: {
    plan_id: 'jump_scare',
    actions: [
      { device: 'smart_light', type: 'flash', repeat: 1, duration_ms: 200 },
      { device: 'fan_plug', type: 'setSwitch', value: 'on', duration_ms: 2000 },
      { type: 'restore', device: 'all' },
    ],
  },
  recovery: {
    plan_id: 'recovery',
    actions: [
      { type: 'restore', device: 'all' },
    ],
  },
};

/** plan_id 로 plan 반환 (캐시 → fallback 순) */
function resolvePlan(id) {
  return planCache.get(id) || FALLBACK_PLANS[id] || null;
}

module.exports = { loadPlans, getPlan, resolvePlan };
