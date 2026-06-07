// Node.js 내장 테스트 러너 사용 (node --test)
const { describe, it, before, beforeEach } = require('node:test');
const assert = require('node:assert/strict');

// 환경변수 Mock (테스트용)
process.env.SMARTTHINGS_TOKEN = 'test_token';
process.env.DEVICE_ID_LIGHT = 'light-id';
process.env.DEVICE_ID_FAN = 'fan-id';

const { filterPlan, resetCooldowns } = require('./safetyFilter');
const { counters } = require('./logger');

beforeEach(() => {
  resetCooldowns();
});

describe('safetyFilter - allowlist', () => {
  it('허용된 event_id는 통과', () => {
    const plan = { actions: [{ device: 'smart_light', type: 'setLevel', value: 50 }, { type: 'restore', device: 'all' }] };
    const result = filterPlan('ghost_hint', plan);
    assert.equal(result.allowed, true);
  });

  it('허용되지 않은 event_id는 차단', () => {
    const plan = { actions: [] };
    const result = filterPlan('unknown_event', plan);
    assert.equal(result.allowed, false);
  });

  it('허용되지 않은 device는 action에서 제거', () => {
    const plan = {
      actions: [
        { device: 'evil_device', type: 'setSwitch', value: 'on' },
        { device: 'smart_light', type: 'setLevel', value: 50 },
        { type: 'restore', device: 'all' },
      ],
    };
    const result = filterPlan('ghost_hint', plan);
    assert.equal(result.allowed, true);
    const hasEvilDevice = result.actions.some(a => a.device === 'evil_device');
    assert.equal(hasEvilDevice, false);
  });
});

describe('safetyFilter - duration clamp', () => {
  it('blackout duration이 800ms 초과시 800ms로 clamp', () => {
    const plan = {
      actions: [
        { device: 'smart_light', type: 'setSwitch', value: 'off', duration_ms: 3000 },
        { type: 'restore', device: 'all' },
      ],
    };
    const result = filterPlan('blackout', plan);
    const lightAction = result.actions.find(a => a.device === 'smart_light');
    assert.equal(lightAction.duration_ms, 800);
  });

  it('fan duration이 5000ms 초과시 5000ms로 clamp', () => {
    const plan = {
      actions: [
        { device: 'fan_plug', type: 'setSwitch', value: 'on', duration_ms: 10000 },
        { type: 'restore', device: 'all' },
      ],
    };
    const result = filterPlan('chase', plan);
    const fanAction = result.actions.find(a => a.device === 'fan_plug');
    assert.equal(fanAction.duration_ms, 5000);
  });

  it('허용 범위 내 duration은 그대로 통과', () => {
    const plan = {
      actions: [
        { device: 'fan_plug', type: 'setSwitch', value: 'on', duration_ms: 3000 },
        { type: 'restore', device: 'all' },
      ],
    };
    const result = filterPlan('ghost_near', plan);
    const fanAction = result.actions.find(a => a.device === 'fan_plug');
    assert.equal(fanAction.duration_ms, 3000);
  });
});

describe('safetyFilter - cooldown', () => {
  it('동일 event_id 연속 요청시 cooldown 차단', () => {
    const plan = { actions: [{ device: 'smart_light', type: 'setLevel', value: 50 }, { type: 'restore', device: 'all' }] };
    const first = filterPlan('ghost_near', plan);
    assert.equal(first.allowed, true);
    const second = filterPlan('ghost_near', plan);
    assert.equal(second.allowed, false);
  });

  it('recovery는 cooldown 면제', () => {
    const plan = { actions: [{ type: 'restore', device: 'all' }] };
    const first = filterPlan('recovery', plan);
    assert.equal(first.allowed, true);
    const second = filterPlan('recovery', plan);
    assert.equal(second.allowed, true);
  });
});

describe('safetyFilter - strobe 금지', () => {
  it('flash repeat>1 은 repeat=1로 강제', () => {
    const plan = {
      actions: [
        { device: 'smart_light', type: 'flash', repeat: 5, duration_ms: 100 },
        { type: 'restore', device: 'all' },
      ],
    };
    const result = filterPlan('jump_scare', plan);
    const flashAction = result.actions.find(a => a.type === 'flash');
    assert.equal(flashAction.repeat, 1);
  });
});

describe('safetyFilter - restore 보장', () => {
  it('restore action 없는 plan에 자동 추가', () => {
    const plan = {
      actions: [
        { device: 'smart_light', type: 'setLevel', value: 30 },
      ],
    };
    const result = filterPlan('ghost_hint', plan);
    const last = result.actions[result.actions.length - 1];
    assert.equal(last.type, 'restore');
  });
});
