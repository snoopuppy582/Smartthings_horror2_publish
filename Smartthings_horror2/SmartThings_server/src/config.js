require('dotenv').config();

// 허용 이벤트 목록
const ALLOWED_EVENTS = [
  'ghost_hint',
  'ghost_near',
  'blackout',
  'chase',
  'jump_scare',
  'recovery',
];

// 허용 기기 목록
const ALLOWED_DEVICES = ['smart_light', 'fan_plug'];

// 허용 액션 목록
const ALLOWED_ACTIONS = ['setLevel', 'setSwitch', 'flash'];

// 안전 제한값 (ms)
const COOLDOWN_MS = 15000;
const BLACKOUT_MAX_MS = 800;
const FAN_ON_MAX_MS = 5000;

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

if (!SMARTTHINGS_TOKEN || !DEVICE_ID_LIGHT || !DEVICE_ID_FAN) {
  console.error('[CONFIG] 필수 환경변수 누락: SMARTTHINGS_TOKEN, DEVICE_ID_LIGHT, DEVICE_ID_FAN 을 .env에 설정하세요.');
}

module.exports = {
  ALLOWED_EVENTS,
  ALLOWED_DEVICES,
  ALLOWED_ACTIONS,
  COOLDOWN_MS,
  BLACKOUT_MAX_MS,
  FAN_ON_MAX_MS,
  DEFAULT_LIGHT_LEVEL,
  DEFAULT_LIGHT_SWITCH,
  DEFAULT_FAN_SWITCH,
  SMARTTHINGS_BASE_URL,
  SMARTTHINGS_TOKEN,
  DEVICE_ID_LIGHT,
  DEVICE_ID_FAN,
  PORT,
};
