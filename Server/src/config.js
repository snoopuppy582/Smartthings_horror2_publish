// src/config.js
// 이벤트별 허용 동작 및 안전 제한 설정

const EVENT_CONFIG = {
  Enemy_hint: {
    label: "적 기척",
    cooldown: 5000,
    light: { action: "dim", level: 50 },
    plug:  { action: "off" },
  },
  Enemy_near: {
    label: "적 접근",
    cooldown: 5000,
    light: { action: "dim", level: 25 },
    plug:  { action: "on", duration: 4000 },
  },
  blackout: {
    label: "암전",
    cooldown: 8000,
    light: { action: "blackout", dimLevel: 20, duration: 1000, restoreLevel: 100 },
    plug:  { action: "off" },
  },
  chase: {
    label: "추격",
    cooldown: 5000,
    light: { action: "dim", level: 25 },
    plug:  { action: "on", duration: 4000 },
  },
  jump_scare: {
    label: "점프스케어",
    cooldown: 10000,
    light: { action: "flash", duration: 200 },
    plug:  { action: "cycle", interval: 300 },
  },
  recovery: {
    label: "복구",
    cooldown: 2000,
    light: { action: "restore" },
    plug:  { action: "off" },
  },
  plug_on: {
    label: "플러그 ON",
    cooldown: 100,
    plug: { action: "on" },
  },
  plug_off: {
    label: "플러그 OFF",
    cooldown: 100,
    plug: { action: "off" },
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
