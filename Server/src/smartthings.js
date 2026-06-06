// src/smartthings.js
// SmartThings REST API 호출 담당 모듈
// 토큰은 환경 변수에서만 읽고, Unity 클라이언트에는 절대 노출하지 않음

const axios = require("axios");

const BASE_URL = "https://api.smartthings.com/v1";

// 공통 헤더 생성
function getHeaders() {
  return {
    Authorization: `Bearer ${process.env.SMARTTHINGS_TOKEN}`,
    "Content-Type": "application/json",
  };
}

// 기기에 명령 전송하는 기본 함수
async function sendCommand(deviceId, capability, command, args = []) {
  const url = `${BASE_URL}/devices/${deviceId}/commands`;
  const body = {
    commands: [
      {
        component: "main",
        capability,
        command,
        arguments: args,
      },
    ],
  };

  const start = Date.now();
  const response = await axios.post(url, body, { headers: getHeaders() });
  const delay = Date.now() - start;

  return { status: response.status, delay };
}

// ── 조명 제어 함수들 ──────────────────────────────────────

// 조명 켜기/끄기
async function setLightSwitch(deviceId, onOff) {
  return sendCommand(deviceId, "switch", onOff ? "on" : "off");
}

// 조명 밝기 설정 (0~100)
async function setLightLevel(deviceId, level) {
  const safeLevel = Math.max(0, Math.min(100, level)); // 0~100 범위 강제
  return sendCommand(deviceId, "switchLevel", "setLevel", [safeLevel]);
}

// 조명 깜빡임 (on→off→on, 1회)
async function flickerLight(deviceId, intervalMs = 300) {
  await setLightSwitch(deviceId, false);
  await new Promise((r) => setTimeout(r, intervalMs));
  await setLightSwitch(deviceId, true);
}

// 암전 후 자동 복구 (최대 3초 강제 제한)
async function blackoutWithRestore(deviceId, durationMs, defaultLevel = 100) {
  const safeDuration = Math.min(durationMs, 3000); // 안전 필터: 최대 3초
  await setLightSwitch(deviceId, false);
  await new Promise((r) => setTimeout(r, safeDuration));
  await setLightLevel(deviceId, defaultLevel);
  await setLightSwitch(deviceId, true);
}

// 짧은 플래시 (켰다가 바로 원상 복구)
async function flashLight(deviceId, durationMs = 500) {
  await setLightSwitch(deviceId, true);
  await setLightLevel(deviceId, 100);
  await new Promise((r) => setTimeout(r, durationMs));
  await setLightLevel(deviceId, 100); // 복구
}

// ── 에어컨 제어 함수들 ──────────────────────────────────

// 에어컨 모드 설정 (cool / fanOnly)
async function setAcMode(deviceId, mode) {
  return sendCommand(deviceId, "thermostatMode", "setThermostatMode", [mode]);
}

// 에어컨 팬 속도 설정 (low / medium / high)
async function setFanSpeed(deviceId, speed) {
  return sendCommand(deviceId, "thermostatFanMode", "setThermostatFanMode", [speed]);
}

// 에어컨 설정 온도 변경 (안전 필터: ±2°C 이내만 허용)
async function setAcTemperature(deviceId, currentTemp, delta) {
  const safeDelta = Math.max(-2, Math.min(2, delta)); // 안전 필터
  const newTemp = currentTemp + safeDelta;
  return sendCommand(deviceId, "thermostatCoolingSetpoint", "setCoolingSetpoint", [newTemp]);
}

// 에어컨 기본 설정으로 복구
async function restoreAc(deviceId) {
  await setAcMode(deviceId, "cool");
  await setFanSpeed(deviceId, "low");
}

module.exports = {
  setLightSwitch,
  setLightLevel,
  flickerLight,
  blackoutWithRestore,
  flashLight,
  setAcMode,
  setFanSpeed,
  setAcTemperature,
  restoreAc,
};
