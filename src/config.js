// src/config.js
// 이벤트별 허용 동작 및 안전 제한 설정

const EVENT_CONFIG = {
  ghost_hint: {
    label: "귀신 기척",
    cooldown: 5000, // ms — 동일 이벤트 재요청 최소 간격
    light: { action: "dim", level: -15 },        // 현재 밝기에서 -15%
    ac: { action: "none" },
    appliance: { action: "notify_short" },        // 짧은 알림만
  },
  ghost_near: {
    label: "귀신 접근",
    cooldown: 5000,
    light: { action: "flicker", times: 1 },       // 1회 깜빡임
    ac: { action: "fan_up", step: 1 },            // 송풍 1단계 상승
    appliance: { action: "status_display" },
  },
  blackout: {
    label: "암전",
    cooldown: 8000,
    light: { action: "blackout", duration: 3000 }, // 최대 3초 후 자동 복구
    ac: { action: "none" },
    appliance: { action: "none" },                 // 작동 금지
  },
  chase: {
    label: "추격",
    cooldown: 5000,
    light: { action: "low", level: 20 },           // 20% 낮은 밝기 유지
    ac: { action: "cool_maintain" },               // 안전 범위 내 냉방 유지
    appliance: { action: "none" },                 // 반복 알림 금지
  },
  jump_scare: {
    label: "점프스케어",
    cooldown: 10000,
    light: { action: "flash", duration: 500 },    // 0.5초 플래시 후 복구
    ac: { action: "none" },
    appliance: { action: "status_short" },        // 짧은 상태 변화만
  },
  recovery: {
    label: "복구",
    cooldown: 2000,
    light: { action: "restore" },                  // 기본 밝기(100%) 복구
    ac: { action: "restore" },                     // 기본 설정 복구
    appliance: { action: "restore" },              // 모든 변화 종료
  },
};

// 안전 상수 — 절대 초과 불가
const SAFETY_LIMITS = {
  MAX_BLACKOUT_MS: 3000,      // 암전 최대 지속 시간
  AC_TEMP_DELTA_MAX: 2,        // 에어컨 온도 변화 최대 ±2°C
  DEFAULT_LIGHT_LEVEL: 100,    // 복구 시 조명 기본값
  FLICKER_INTERVAL_MS: 300,   // 깜빡임 간격
};

module.exports = { EVENT_CONFIG, SAFETY_LIMITS };
