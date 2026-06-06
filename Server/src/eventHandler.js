// src/eventHandler.js
// Unity 이벤트 → 기기 반응 변환 담당
// 각 이벤트 함수는 안전 필터를 통과한 뒤 여기서 실행됨

const st = require("./smartthings");
const { SAFETY_LIMITS } = require("./config");

// 기기 ID는 환경 변수에서 읽음
const LIGHT_ID = () => process.env.DEVICE_ID_LIGHT;
const AC_ID = () => process.env.DEVICE_ID_AC;

// 현재 조명 밝기 추적 (복구 시 사용)
let currentLightLevel = 100;

// ── 이벤트별 처리 함수 ──────────────────────────────────

async function handleGhostHint() {
  // 조명: 현재 밝기에서 15% 감소
  const newLevel = Math.max(10, currentLightLevel - 15);
  await st.setLightLevel(LIGHT_ID(), newLevel);
  currentLightLevel = newLevel;

  // 에어컨: 변화 없음
  // 가전: 짧은 알림 (별도 구현 시 추가)
  return { light: newLevel, ac: "none" };
}

async function handleGhostNear() {
  // 조명: 1회 깜빡임
  await st.flickerLight(LIGHT_ID(), SAFETY_LIMITS.FLICKER_INTERVAL_MS);

  // 에어컨: 송풍 1단계 상승
  await st.setFanSpeed(AC_ID(), "medium");

  return { light: "flicker_1", ac: "fan_medium" };
}

async function handleBlackout() {
  // 조명: 최대 3초 암전 후 자동 복구 (안전 필터 내장)
  // await를 걸지 않아 비동기로 실행 → 서버가 응답을 먼저 반환
  st.blackoutWithRestore(LIGHT_ID(), SAFETY_LIMITS.MAX_BLACKOUT_MS, currentLightLevel);

  // 에어컨: 변화 없음 / 가전: 작동 금지
  return { light: `blackout_${SAFETY_LIMITS.MAX_BLACKOUT_MS}ms_then_restore`, ac: "none" };
}

async function handleChase() {
  // 조명: 20% 낮은 밝기로 유지
  await st.setLightLevel(LIGHT_ID(), 20);
  currentLightLevel = 20;

  // 에어컨: 냉방 모드 유지
  await st.setAcMode(AC_ID(), "cool");

  return { light: 20, ac: "cool_maintain" };
}

async function handleJumpScare() {
  // 조명: 0.5초 플래시 후 현재 밝기로 복구
  st.flashLight(LIGHT_ID(), 500); // 비동기 — 응답 먼저 반환

  // 에어컨: 추가 변화 없음
  return { light: "flash_500ms", ac: "none" };
}

async function handleRecovery() {
  // 조명: 기본 밝기(100%) 복구
  await st.setLightLevel(LIGHT_ID(), SAFETY_LIMITS.DEFAULT_LIGHT_LEVEL);
  await st.setLightSwitch(LIGHT_ID(), true);
  currentLightLevel = SAFETY_LIMITS.DEFAULT_LIGHT_LEVEL;

  // 에어컨: 기본 설정 복구
  await st.restoreAc(AC_ID());

  return { light: 100, ac: "restored" };
}

// ── 이벤트 이름 → 처리 함수 매핑 ──────────────────────

const HANDLERS = {
  ghost_hint: handleGhostHint,
  ghost_near: handleGhostNear,
  blackout: handleBlackout,
  chase: handleChase,
  jump_scare: handleJumpScare,
  recovery: handleRecovery,
};

module.exports = { HANDLERS };
