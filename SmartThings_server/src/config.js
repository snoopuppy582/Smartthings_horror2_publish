require('dotenv').config();

// 허용 이벤트 목록
const ALLOWED_EVENTS = [
  'game_start',
  'ghost_hint',
  'ghost_near',
  'killer_near',
  'blackout',
  'chase',
  'jump_scare',
  'player_hit',
  'mission_success',
  'mission_failed',
  'recovery',
];

// 허용 기기 목록
const ALLOWED_DEVICES = ['smart_light', 'fan_plug'];

// 허용 액션 목록
const ALLOWED_ACTIONS = ['setLevel', 'setSwitch', 'flash'];

// 안전 제한값 (ms)
const COOLDOWN_MS = 15000; // 기본값
// 이벤트별 cooldown 재정의 — 자주 발생하는 이벤트는 더 길게
const COOLDOWN_OVERRIDES = {
  player_hit: 20000,  // 피격은 20초 (API 호출 많음)
  killer_near: 12000, // 추격자 접근은 12초
  ghost_hint: 10000,
  blackout: 20000,    // 암전은 복구 포함 API 부하 큼
};
const BLACKOUT_MAX_MS = 800;
const FAN_ON_MAX_MS = 5000;
const LIGHT_EFFECT_MAX_MS = 5000;
const LIGHT_LEVEL_MIN = 20;
const LIGHT_LEVEL_MAX = 100;

// 기본 복구 상태
const DEFAULT_LIGHT_LEVEL = 70; // %
const DEFAULT_LIGHT_SWITCH = 'on';
const DEFAULT_FAN_SWITCH = 'off';

// SmartThings API
const SMARTTHINGS_BASE_URL = 'https://api.smartthings.com/v1';
const SMARTTHINGS_TOKEN = process.env.SMARTTHINGS_TOKEN;
const DEVICE_ID_LIGHT = process.env.DEVICE_ID_LIGHT;
const DEVICE_ID_FAN = process.env.DEVICE_ID_FAN;
const PORT = process.env.PORT || 3000;
const SMARTTHINGS_SIMULATION_MODE =
  process.env.IOT_SIMULATION === '1' || !SMARTTHINGS_TOKEN || !DEVICE_ID_LIGHT || !DEVICE_ID_FAN;

if (!SMARTTHINGS_TOKEN || !DEVICE_ID_LIGHT || !DEVICE_ID_FAN) {
  console.warn('[CONFIG] SmartThings 환경변수 누락. 실제 기기 명령 대신 시뮬레이션 모드로 실행합니다.');
}

module.exports = {
  ALLOWED_EVENTS,
  ALLOWED_DEVICES,
  ALLOWED_ACTIONS,
  COOLDOWN_MS,
  COOLDOWN_OVERRIDES,
  BLACKOUT_MAX_MS,
  FAN_ON_MAX_MS,
  LIGHT_EFFECT_MAX_MS,
  LIGHT_LEVEL_MIN,
  LIGHT_LEVEL_MAX,
  DEFAULT_LIGHT_LEVEL,
  DEFAULT_LIGHT_SWITCH,
  DEFAULT_FAN_SWITCH,
  SMARTTHINGS_BASE_URL,
  SMARTTHINGS_TOKEN,
  DEVICE_ID_LIGHT,
  DEVICE_ID_FAN,
  SMARTTHINGS_SIMULATION_MODE,
  PORT,
};
