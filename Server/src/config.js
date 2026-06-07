// src/config.js
// 이벤트별 허용 동작 및 안전 제한 설정

const EVENT_CONFIG = {
  // 🆕 문 진입 — 집안 암전 (게임 입장 연출)
  door_entrance: {
    label: "귀신의 집 입장",
    cooldown: 8000,
    light: { action: "blackout", duration: 3000, restoreLevel: 25 }, // 3초 암전 후 25%로 복귀
    ac: { action: "none" },
    appliance: { action: "none" },
  },
  ghost_hint: {
    label: "귀신 기척",
    cooldown: 5000,
    light: { action: "dim", level: -15 },
    ac: { action: "none" },
    appliance: { action: "notify_short" },
  },
  ghost_near: {
    label: "귀신 접근",
    cooldown: 5000,
    light: { action: "flicker", times: 1 },
    ac: { action: "fan_up", step: 1 },
    appliance: { action: "status_display" },
  },
  blackout: {
    label: "암전",
    cooldown: 8000,
    light: { action: "blackout", duration: 3000 },
    ac: { action: "none" },
    appliance: { action: "none" },
  },
  chase: {
    label: "추격",
    cooldown: 5000,
    light: { action: "low", level: 20 },
    ac: { action: "cool_maintain" },
    appliance: { action: "none" },
  },
  jump_scare: {
    label: "점프스케어",
    cooldown: 10000,
    light: { action: "flash", duration: 500 },
    ac: { action: "none" },
    appliance: { action: "status_short" },
  },
  recovery: {
    label: "복구",
    cooldown: 2000,
    light: { action: "restore" },
    ac: { action: "restore" },
    appliance: { action: "restore" },
  },
};

// 안전 상수 — 절대 초과 불가
const SAFETY_LIMITS = {
  MAX_BLACKOUT_MS: 3000,
  AC_TEMP_DELTA_MAX: 2,
  DEFAULT_LIGHT_LEVEL: 100,
  FLICKER_INTERVAL_MS: 300,
};

module.exports = { EVENT_CONFIG, SAFETY_LIMITS };