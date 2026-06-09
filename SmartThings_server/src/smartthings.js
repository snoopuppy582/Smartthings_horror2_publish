const {
  SMARTTHINGS_BASE_URL,
  SMARTTHINGS_TOKEN,
  DEVICE_ID_LIGHT,
  DEVICE_ID_FAN,
  SMARTTHINGS_SIMULATION_MODE,
} = require('./config');
const { logError, logInfo } = require('./logger');

// node-fetch v3 는 ESM이므로 동적 import 사용
let _fetch;
async function getFetch() {
  if (!_fetch) {
    const mod = await import('node-fetch');
    _fetch = mod.default;
  }
  return _fetch;
}

const DEVICE_MAP = {
  smart_light: DEVICE_ID_LIGHT,
  fan_plug: DEVICE_ID_FAN,
};

// ── 전역 API 요청 직렬 큐 ──────────────────────────────────────────────────
// SmartThings rate limit(429) 방지: 모든 API 호출을 순차 실행하고 최소 간격 유지
const API_MIN_INTERVAL_MS = 600; // 호출 간 최소 간격
let _apiQueue = Promise.resolve();             // 직렬화 체인
let _lastApiCallTs = 0;                        // 마지막 호출 시각

function enqueueApiCall(fn) {
  _apiQueue = _apiQueue.then(async () => {
    const elapsed = Date.now() - _lastApiCallTs;
    if (elapsed < API_MIN_INTERVAL_MS) {
      await new Promise(r => setTimeout(r, API_MIN_INTERVAL_MS - elapsed));
    }
    _lastApiCallTs = Date.now();
    return fn();
  });
  return _apiQueue;
}
// ──────────────────────────────────────────────────────────────────────────

/**
 * SmartThings REST API로 commands 전송 (큐 경유, 429 시 1회 자동 재시도)
 * @param {string} deviceKey - 'smart_light' | 'fan_plug'
 * @param {Array}  commands  - SmartThings commands 배열
 */
async function sendCommands(deviceKey, commands) {
  const deviceId = DEVICE_MAP[deviceKey];
  if (SMARTTHINGS_SIMULATION_MODE) {
    logInfo(`[SIMULATION] SmartThings 명령 생략 [${deviceKey}] commands=${JSON.stringify(commands)}`);
    return 204;
  }

  if (!deviceId) {
    logError(`알 수 없는 device key: ${deviceKey}`);
    throw new Error(`알 수 없는 device: ${deviceKey}`);
  }

  return enqueueApiCall(() => _doSend(deviceKey, deviceId, commands));
}

async function _doSend(deviceKey, deviceId, commands, isRetry = false) {
  const url = `${SMARTTHINGS_BASE_URL}/devices/${deviceId}/commands`;
  const fetch = await getFetch();

  let res;
  try {
    res = await fetch(url, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${SMARTTHINGS_TOKEN}`,
        'Content-Type': 'application/json',
      },
      body: JSON.stringify({ commands }),
    });
  } catch (err) {
    logError(`SmartThings 네트워크 오류 [${deviceKey}]`, err);
    throw err;
  }

  if (res.status === 401) {
    logError(`SmartThings 401: 토큰 무효/만료 → .env SMARTTHINGS_TOKEN 재발급 필요`);
    throw new Error('SmartThings 401 Unauthorized');
  }
  if (res.status === 429) {
    if (!isRetry) {
      // 429 시 2초 대기 후 1회 재시도
      logInfo(`SmartThings 429 [${deviceKey}] → 2초 후 재시도`);
      await new Promise(r => setTimeout(r, 2000));
      return _doSend(deviceKey, deviceId, commands, true);
    }
    logError(`SmartThings 429: rate limit → 재시도 실패, 이 호출 스킵`);
    throw new Error('SmartThings 429 Rate Limit');
  }
  if (res.status >= 500) {
    const body = await res.text();
    logError(`SmartThings ${res.status}: 서버 오류`, body);
    throw new Error(`SmartThings ${res.status}`);
  }

  logInfo(`SmartThings 명령 전송 완료 [${deviceKey}] status=${res.status}`);
  return res.status;
}

/** 조명 밝기 설정 (0-100) */
async function setLightLevel(level) {
  return sendCommands('smart_light', [
    { component: 'main', capability: 'switchLevel', command: 'setLevel', arguments: [level, 0] },
  ]);
}

/** 조명 켜기/끄기 */
async function setLightSwitch(onOff) {
  return sendCommands('smart_light', [
    { component: 'main', capability: 'switch', command: onOff === 'on' ? 'on' : 'off', arguments: [] },
  ]);
}

/** 선풍기(플러그) 켜기/끄기 */
async function setFanSwitch(onOff) {
  return sendCommands('fan_plug', [
    { component: 'main', capability: 'switch', command: onOff === 'on' ? 'on' : 'off', arguments: [] },
  ]);
}

/** 전체 기본 상태 복구 — 조명은 switch+level을 단일 배치 호출로 묶어 API 횟수 절감 */
async function restoreAll() {
  const { DEFAULT_LIGHT_LEVEL, DEFAULT_LIGHT_SWITCH, DEFAULT_FAN_SWITCH } = require('./config');
  // 조명: switch on + setLevel을 commands 배열 1건으로 합침 (2 call → 1 call)
  await sendCommands('smart_light', [
    { component: 'main', capability: 'switch', command: DEFAULT_LIGHT_SWITCH === 'on' ? 'on' : 'off', arguments: [] },
    { component: 'main', capability: 'switchLevel', command: 'setLevel', arguments: [DEFAULT_LIGHT_LEVEL, 0] },
  ]);
  await setFanSwitch(DEFAULT_FAN_SWITCH);
}

module.exports = { sendCommands, setLightLevel, setLightSwitch, setFanSwitch, restoreAll };
