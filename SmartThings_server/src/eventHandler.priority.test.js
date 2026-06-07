const test = require('node:test');
const assert = require('node:assert/strict');

const { _test } = require('./eventHandler');

test('player_hit prevents killer_near cleanup from cutting off fan feedback', () => {
  _test.resetEffectPriorityForTest();

  const killerNear = _test.beginEffectContext('killer_near', [
    { device: 'fan_plug', type: 'setSwitch', value: 'on', duration_ms: 3000 },
  ]);
  const playerHit = _test.beginEffectContext('player_hit', [
    { device: 'fan_plug', type: 'setSwitch', value: 'on', duration_ms: 5000 },
  ]);

  assert.equal(_test.shouldSuppressAction(playerHit), false);
  assert.equal(_test.shouldSkipCleanup(killerNear, 'fan_off'), true);
  assert.equal(_test.shouldSuppressAction(killerNear), true);
});

test('mission_success can supersede active player_hit effects for final recovery', () => {
  _test.resetEffectPriorityForTest();

  const playerHit = _test.beginEffectContext('player_hit', [
    { device: 'fan_plug', type: 'setSwitch', value: 'on', duration_ms: 5000 },
  ]);
  const missionSuccess = _test.beginEffectContext('mission_success', [
    { device: 'fan_plug', type: 'setSwitch', value: 'off' },
  ]);

  assert.equal(_test.shouldSuppressAction(missionSuccess), false);
  assert.equal(_test.shouldSkipCleanup(playerHit, 'fan_off'), true);
  assert.equal(_test.shouldSuppressAction(playerHit), true);
});
