const { SMARTTHINGS_BASE_URL, SMARTTHINGS_TOKEN, DEVICE_ID_LIGHT, DEVICE_ID_FAN } = require('./config');
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

/**
 * SmartThings REST API로 commands 전송
 * @param {string} deviceKey - 'smart_light' | 'fan_plug'
 * @param {Array}  commands  - SmartThings commands 배열
 */
async function sendCommands(deviceKey, commands) {
  const deviceId = DEVICE_MAP[deviceKey];
  if (!deviceId) {
    logError(`알 수 없는 device key: ${deviceKey}`);
    throw new Error(`알 수 없는 device: ${deviceKey}`);
  }

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
    logError(`SmartThings 429: rate limit → 백오프 후 재시도 필요`);
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

/** 전체 기본 상태 복구 */
async function restoreAll() {
  const { DEFAULT_LIGHT_LEVEL, DEFAULT_LIGHT_SWITCH, DEFAULT_FAN_SWITCH } = require('./config');
  await Promise.all([
    setLightSwitch(DEFAULT_LIGHT_SWITCH),
    setLightLevel(DEFAULT_LIGHT_LEVEL),
    setFanSwitch(DEFAULT_FAN_SWITCH),
  ]);
}

module.exports = { sendCommands, setLightLevel, setLightSwitch, setFanSwitch, restoreAll };
