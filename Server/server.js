// server.js — Express 진입점 + 안전 필터 (허용목록 + 쿨다운 + 긴급정지)
require("dotenv").config();
const express = require("express");
const { HANDLERS } = require("./src/eventHandler");
const { EVENT_CONFIG } = require("./src/config");

const app = express();
app.use(express.json());

const PORT = process.env.PORT || 3000;

// 이벤트별 마지막 실행 시각 (안전 규칙 5: 쿨다운 추적)
const lastFired = {};

// 안전 규칙 1: 허용 이벤트 = HANDLERS에 등록된 것만
const ALLOWED = new Set(Object.keys(HANDLERS));

// ── 헬스 체크 ──
app.get("/health", (req, res) => {
  res.json({ ok: true, uptime: process.uptime(), allowed: [...ALLOWED] });
});

// ── 이벤트 수신 ──
app.post("/event", async (req, res) => {
  const { event, timestamp } = req.body || {};
  const recvAt = Date.now();

  // 안전 규칙 1: 허용 목록 외 차단 + 로그
  if (!event || !ALLOWED.has(event)) {
    console.warn(`[차단] 허용되지 않은 이벤트: ${event}`);
    return res.status(200).json({ ok: false, reason: "event_not_allowed", event });
  }

  // 안전 규칙 5: 쿨다운 (config의 이벤트별 cooldown 적용)
  const cd = EVENT_CONFIG[event]?.cooldown ?? 5000;
  if (lastFired[event] && recvAt - lastFired[event] < cd) {
    const wait = cd - (recvAt - lastFired[event]);
    console.log(`[쿨다운] ${event} 무시 (${wait}ms 남음)`);
    return res.status(200).json({ ok: false, reason: "cooldown", remaining_ms: wait });
  }
  lastFired[event] = recvAt;

  // 핸들러 실행
  try {
    const t0 = Date.now();
    const result = await HANDLERS[event]();
    const procMs = Date.now() - t0;
    // 검증 항목 4: 지연 측정 (Unity 타임스탬프 → 서버 수신)
    const e2e = timestamp ? recvAt - Number(timestamp) : null;
    console.log(`[${event}] 완료 | 처리 ${procMs}ms | Unity→서버 ${e2e ?? "-"}ms`, result);
    return res.status(200).json({ ok: true, event, result, procMs, e2eMs: e2e });
  } catch (err) {
    console.error(`[${event}] 처리 오류:`, err.message);
    // SmartThings 에러 코드 해석
    const stStatus = err.response?.status;
    if (stStatus === 401) console.error("→ 토큰 만료/무효. PAT 재발급 후 .env 갱신 필요");
    if (stStatus === 429) console.error("→ rate limit 초과. 쿨다운 간격 확인 필요");
    return res.status(500).json({ ok: false, event, error: err.message, st_status: stStatus });
  }
});

// ── 긴급 정지 (안전 규칙 7: 쿨다운 무시하고 최우선 복구) ──
app.post("/emergency-stop", async (req, res) => {
  console.warn("[EMERGENCY STOP] 모든 기기 복구 명령 즉시 실행");
  try {
    await HANDLERS.recovery(); // 조명/에어컨 기본값 복구
    return res.status(200).json({ ok: true, action: "emergency_recovery" });
  } catch (err) {
    console.error("[EMERGENCY STOP] 복구 실패:", err.message);
    return res.status(500).json({ ok: false, error: err.message });
  }
});

app.listen(PORT, () => {
  console.log(`중계 서버 실행 → http://localhost:${PORT}`);
  console.log(`허용 이벤트: ${[...ALLOWED].join(", ")}`);
});