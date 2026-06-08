// src/eventHandler.js
// Unity 이벤트 → 기기 반응 변환 담당
// 각 이벤트 함수는 server.js의 안전 필터를 통과한 뒤 여기서 실행됨

const st = require("./smartthings");
const { SAFETY_LIMITS } = require("./config");

const LIGHT_ID = () => process.env.DEVICE_ID_LIGHT;
const PLUG_ID  = () => process.env.DEVICE_ID_PLUG;

// 현재 조명 밝기 추적 (jump_scare 복귀에 사용)
let currentLightLevel = 100;

// jump_scare 사이클 타이머 (recovery 시 취소)
let jumpScareCycleTimer = null;

// ── 이벤트별 처리 함수 ──────────────────────────────────

// Enemy_hint: 밝기 50%, 플러그 OFF
async function handleEnemyHint() {
  await st.setLightLevel(LIGHT_ID(), 50);
  currentLightLevel = 50;
  return { light: 50, plug: false };
}

// Enemy_near: 밝기 25%, 플러그 4초 ON → OFF
async function handleEnemyNear() {
  await st.setLightLevel(LIGHT_ID(), 25);
  currentLightLevel = 25;

  await st.setPlugSwitch(PLUG_ID(), true);
  setTimeout(async () => {
    await st.setPlugSwitch(PLUG_ID(), false);
  }, 4000);

  return { light: 25, plug: "on_4s" };
}

// blackout: 즉시 20% → 1초 → 100% 복귀, 플러그 OFF
async function handleBlackout() {
  await st.setLightLevel(LIGHT_ID(), 20);

  setTimeout(async () => {
    await st.setLightLevel(LIGHT_ID(), 100);
    currentLightLevel = 100;
  }, 1000);

  return { light: "20%_1s_then_100%", plug: false };
}

// chase: 밝기 25%, 플러그 4초 ON → OFF
async function handleChase() {
  await st.setLightLevel(LIGHT_ID(), 25);
  currentLightLevel = 25;

  await st.setPlugSwitch(PLUG_ID(), true);
  setTimeout(async () => {
    await st.setPlugSwitch(PLUG_ID(), false);
  }, 4000);

  return { light: 25, plug: "on_4s" };
}

// jump_scare: 밝기 100% 플래시 → 0.2초 → 원 밝기 복귀, 플러그 T/F/T 0.3초 사이클
async function handleJumpScare() {
  // 기존 사이클 취소
  stopJumpScareCycle();

  const savedLevel = currentLightLevel;

  // 조명 플래시
  await st.setLightLevel(LIGHT_ID(), 100);
  setTimeout(async () => {
    await st.setLightLevel(LIGHT_ID(), savedLevel);
    currentLightLevel = savedLevel;
  }, 200);

  // 플러그 T/F/T 0.3초 사이클 시작
  let plugState = true;
  jumpScareCycleTimer = setInterval(async () => {
    await st.setPlugSwitch(PLUG_ID(), plugState);
    plugState = !plugState;
  }, 300);

  return { light: "flash_100%_200ms", plug: "cycle_300ms" };
}

// recovery: 밝기 100% 복구, 플러그 OFF, 사이클 중단
async function handleRecovery() {
  stopJumpScareCycle();

  await st.setLightLevel(LIGHT_ID(), 100);
  await st.setLightSwitch(LIGHT_ID(), true);
  currentLightLevel = SAFETY_LIMITS.DEFAULT_LIGHT_LEVEL;

  await st.setPlugSwitch(PLUG_ID(), false);

  return { light: 100, plug: false };
}

// ── 플러그 개별 명령 (FanPlugController에서 직접 호출) ──────────

async function handlePlugOn() {
  await st.setPlugSwitch(PLUG_ID(), true);
  return { plug: true };
}

async function handlePlugOff() {
  await st.setPlugSwitch(PLUG_ID(), false);
  return { plug: false };
}

// ── 헬퍼 ────────────────────────────────────────────────

function stopJumpScareCycle() {
  if (jumpScareCycleTimer) {
    clearInterval(jumpScareCycleTimer);
    jumpScareCycleTimer = null;
  }
}

// ── 이벤트 이름 → 처리 함수 매핑 ──────────────────────────

const HANDLERS = {
  Enemy_hint:  handleEnemyHint,
  Enemy_near:  handleEnemyNear,
  blackout:    handleBlackout,
  chase:       handleChase,
  jump_scare:  handleJumpScare,
  recovery:    handleRecovery,
  plug_on:     handlePlugOn,
  plug_off:    handlePlugOff,
};

module.exports = { HANDLERS };
