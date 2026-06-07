require('dotenv').config();
const express = require('express');
const { PORT, SMARTTHINGS_TOKEN, DEVICE_ID_LIGHT, DEVICE_ID_FAN } = require('./src/config');
const { handleEvent } = require('./src/eventHandler');
const { restoreAll } = require('./src/smartthings');
const { resetCooldowns } = require('./src/safetyFilter');
const { loadPlans } = require('./src/planLibrary');
const { logInfo, logError, getCounters, counters } = require('./src/logger');

const app = express();
app.use(express.json());

// plan library 로드
loadPlans();

// GET /health — 서버·토큰·기기 상태 확인
app.get('/health', (req, res) => {
  const tokenOk = !!SMARTTHINGS_TOKEN && SMARTTHINGS_TOKEN.length > 10;
  const lightOk = !!DEVICE_ID_LIGHT;
  const fanOk = !!DEVICE_ID_FAN;
  res.json({
    status: tokenOk && lightOk && fanOk ? 'ok' : 'degraded',
    token_set: tokenOk,
    device_light_set: lightOk,
    device_fan_set: fanOk,
    uptime_sec: Math.floor(process.uptime()),
    counters: getCounters(),
  });
});

// POST /event — Unity → 안전 필터 → 기기 실행
app.post('/event', handleEvent);

// POST /emergency-stop — 모든 기기 즉시 복구
app.post('/emergency-stop', async (req, res) => {
  try {
    resetCooldowns();
    counters.emergency_stop_count++;
    await restoreAll();
    logInfo('emergency-stop 실행 완료');
    res.json({ status: 'ok', message: '모든 기기 복구 완료' });
  } catch (err) {
    logError('emergency-stop 실패', err);
    res.status(500).json({ status: 'error', message: err.message });
  }
});

// GET /stats — 실험 카운터 확인
app.get('/stats', (req, res) => {
  res.json(getCounters());
});

app.listen(PORT, () => {
  logInfo(`서버 시작: http://localhost:${PORT}`);
  logInfo(`/health /event /emergency-stop /stats 엔드포인트 활성`);
});
