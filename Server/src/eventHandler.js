// src/eventHandler.js
// Unity 이벤트 → 기기 반응 변환 담당
// 각 이벤트 함수는 server.js의 안전 필터를 통과한 뒤 여기서 실행됨

const st = require("./smartthings");
const { SAFETY_LIMITS } = require("./config");

const LIGHT_ID = () => process.env.DEVICE_ID_LIGHT;
const AC_ID = () => process.env.DEVICE_ID_AC;

// 현재 조명 밝기 추적 (복구 시 사용)
let currentLightLevel = 100;

// ── 이벤트별 처리 함수 ──────────────────────────────────

// 🆕 문 진입: 암전 → 안전 규칙 3에 따라 3초 내 자동 복구(낮은 밝기로 분위기 유지)
async function handleDoorEntrance() {
  const restoreLevel = 25; // 암전 후 복귀 밝기 — 어둑한 분위기 유지
  // 비동기 실행 → 서버가 응답을 먼저 반환 (지연 최소화)
  st.blackoutWithRestore(LIGHT_ID(), SAFETY_LIMITS.MAX_BLACKOUT_MS, restoreLevel);
  currentLightLevel = restoreLevel;
  return { light: `blackout_${SAFETY_LIMITS.MAX_BLACKOUT_MS}ms_then_${restoreLevel}`, ac: "none" };
}

async function handleGhostHint() {
  const newLevel = Math.max(10, currentLightLevel - 15);
  await st.setLightLevel(LIGHT_ID(), newLevel);
  currentLightLevel = newLevel;
  return { light: newLevel, ac: "none" };
}

async function handleGhostNear() {
  await st.flickerLight(LIGHT_ID(), SAFETY_LIMITS.FLICKER_INTERVAL_MS);
  await st.setFanSpeed(AC_ID(), "medium");
  return { light: "flicker_1", ac: "fan_medium" };
}

async function handleBlackout() {
  st.blackoutWithRestore(LIGHT_ID(), SAFETY_LIMITS.MAX_BLACKOUT_MS, currentLightLevel);
  return { light: `blackout_${SAFETY_LIMITS.MAX_BLACKOUT_MS}ms_then_restore`, ac: "none" };
}

async function handleChase() {
  await st.setLightLevel(LIGHT_ID(), 20);
  currentLightLevel = 20;
  await st.setAcMode(AC_ID(), "cool");
  return { light: 20, ac: "cool_maintain" };
}

async function handleJumpScare() {
  st.flashLight(LIGHT_ID(), 500);
  return { light: "flash_500ms", ac: "none" };
}

async function handleRecovery() {
  await st.setLightLevel(LIGHT_ID(), SAFETY_LIMITS.DEFAULT_LIGHT_LEVEL);
  await st.setLightSwitch(LIGHT_ID(), true);
  currentLightLevel = SAFETY_LIMITS.DEFAULT_LIGHT_LEVEL;
  await st.restoreAc(AC_ID());
  return { light: 100, ac: "restored" };
}

// ── 이벤트 이름 → 처리 함수 매핑 ──────────────────────

const HANDLERS = {
  door_entrance: handleDoorEntrance, // 🆕
  ghost_hint: handleGhostHint,
  ghost_near: handleGhostNear,
  blackout: handleBlackout,
  chase: handleChase,
  jump_scare: handleJumpScare,
  recovery: handleRecovery,
};

module.exports = { HANDLERS };