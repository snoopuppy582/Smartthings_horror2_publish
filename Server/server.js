// server.js
// Unity 공포 게임 ↔ SmartThings 중계 서버
// 역할: 이벤트 수신 → 안전 필터 → 기기 제어 → 로그 기록

require("dotenv").config();
console.log('[DEBUG] Token loaded:', 
  process.env.SMARTTHINGS_TOKEN ? 
    `OK (${process.env.SMARTTHINGS_TOKEN.length} chars, prefix=${process.env.SMARTTHINGS_TOKEN.slice(0,8)}...)` : 
    'MISSING');
const express = require("express");
const { EVENT_CONFIG } = require("./src/config");
const { HANDLERS } = require("./src/eventHandler");
const st = require("./src/smartthings");

const app = express();
app.use(express.json());

// ── 쿨다운 관리 ─────────────────────────────────────────
// key: 이벤트 이름 / value: 마지막 실행 타임스탬프
const cooldownMap = {};

function isOnCooldown(eventName) {
  const config = EVENT_CONFIG[eventName];
  if (!config) return false;
  const lastTime = cooldownMap[eventName] || 0;
  return Date.now() - lastTime < config.cooldown;
}

function setCooldown(eventName) {
  cooldownMap[eventName] = Date.now();
}

// ── 로그 헬퍼 ────────────────────────────────────────────
function log(level, message, data = {}) {
  const timestamp = new Date().toISOString();
  const dataStr = Object.keys(data).length ? " | " + JSON.stringify(data) : "";
  console.log(`[${timestamp}] [${level}] ${message}${dataStr}`);
}

// ── 환경 변수 체크 ───────────────────────────────────────
function checkEnv() {
  const required = ["SMARTTHINGS_TOKEN", "DEVICE_ID_LIGHT", "DEVICE_ID_AC"];
  const missing = required.filter((k) => !process.env[k]);
  if (missing.length > 0) {
    console.warn(`\n⚠️  경고: 아래 환경 변수가 .env에 없습니다:`);
    missing.forEach((k) => console.warn(`   - ${k}`));
    console.warn(`   기기 ID는 조원에게 받은 뒤 .env에 채워넣으세요.\n`);
  }
}

// ── 라우트 1: 헬스 체크 ──────────────────────────────────
// 서버가 살아있는지 확인용 (Unity 연결 전 테스트)
app.get("/health", (req, res) => {
  res.json({
    status: "ok",
    time: new Date().toISOString(),
    token_set: !!process.env.SMARTTHINGS_TOKEN,
    devices: {
      light: process.env.DEVICE_ID_LIGHT || "미설정",
      ac: process.env.DEVICE_ID_AC || "미설정",
      appliance: process.env.DEVICE_ID_APPLIANCE || "미설정",
    },
  });
});

// ── 라우트 2: Unity 이벤트 수신 ──────────────────────────
// Unity C#에서 POST /event 로 호출
// Body: { "event": "ghost_near", "intensity": 0.8 }
app.post("/event", async (req, res) => {
  const { event, intensity = 1.0 } = req.body;
  const receiveTime = Date.now();

  // [안전 필터 1] 허용 목록에 없는 이벤트 차단
  if (!EVENT_CONFIG[event]) {
    log("BLOCK", `알 수 없는 이벤트 차단`, { event });
    return res.status(400).json({ error: "허용되지 않은 이벤트", event });
  }

  // [안전 필터 2] 쿨다운 체크 — 동일 이벤트 5초 이내 재요청 차단
  if (isOnCooldown(event)) {
    const remaining = EVENT_CONFIG[event].cooldown - (Date.now() - cooldownMap[event]);
    log("COOLDOWN", `쿨다운 중 차단`, { event, remaining_ms: remaining });
    return res.status(429).json({ error: "쿨다운 중", remaining_ms: remaining });
  }

  // [안전 필터 3] 기기 ID 미설정 시 경고 (시연 전 체크용)
  if (!process.env.DEVICE_ID_LIGHT) {
    log("WARN", "DEVICE_ID_LIGHT 미설정 — 기기 반응 생략", { event });
    return res.status(503).json({ error: "기기 ID 미설정. .env를 확인하세요." });
  }

  // 쿨다운 등록
  setCooldown(event);

  log("EVENT", `이벤트 수신`, { event, intensity });

  try {
    // 이벤트에 맞는 핸들러 실행
    const handler = HANDLERS[event];
    const result = await handler(intensity);

    const delay = Date.now() - receiveTime;
    log("SUCCESS", `기기 반응 완료`, { event, delay_ms: delay, result });

    return res.json({ ok: true, event, delay_ms: delay, result });
  } catch (err) {
    // SmartThings API 오류 처리
    const status = err.response?.status;
    const errMsg = err.response?.data || err.message;

    if (status === 401) {
      log("ERROR", "SmartThings 인증 실패 — 토큰을 확인하세요", { event });
    } else if (status === 429) {
      log("ERROR", "SmartThings rate limit 초과", { event });
    } else {
      log("ERROR", `기기 제어 실패`, { event, status, errMsg });
    }

    return res.status(500).json({ error: "기기 제어 실패", detail: errMsg });
  }
});

// ── 라우트 3: 긴급 중단 ───────────────────────────────────
// 사용자가 중단 요청 시 모든 기기를 즉시 기본값으로 복구
// POST /emergency-stop
app.post("/emergency-stop", async (req, res) => {
  log("EMERGENCY", "긴급 중단 요청 — 모든 기기 복구 시작");

  const results = {};

  try {
    // 조명 복구
    await st.setLightLevel(process.env.DEVICE_ID_LIGHT, 100);
    await st.setLightSwitch(process.env.DEVICE_ID_LIGHT, true);
    results.light = "복구 완료";
  } catch (e) {
    results.light = `복구 실패: ${e.message}`;
  }

  try {
    // 에어컨 복구
    await st.restoreAc(process.env.DEVICE_ID_AC);
    results.ac = "복구 완료";
  } catch (e) {
    results.ac = `복구 실패: ${e.message}`;
  }

  log("EMERGENCY", "긴급 중단 완료", results);
  return res.json({ ok: true, results });
});

// ── 서버 시작 ────────────────────────────────────────────
const PORT = process.env.PORT || 3000;
app.listen(PORT, () => {
  console.log("\n========================================");
  console.log("  🎮 SmartThings 공포 게임 중계 서버");
  console.log(`  http://localhost:${PORT}`);
  console.log("========================================");
  checkEnv();
  console.log("  엔드포인트:");
  console.log(`  GET  /health          — 서버 상태 확인`);
  console.log(`  POST /event           — Unity 이벤트 수신`);
  console.log(`  POST /emergency-stop  — 긴급 중단 (모든 기기 복구)`);
  console.log("========================================\n");
});
