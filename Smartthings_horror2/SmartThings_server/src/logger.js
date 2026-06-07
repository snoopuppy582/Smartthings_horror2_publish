const fs = require('fs');
const path = require('path');

// 안전 필터 카운터
const counters = {
  blocked_event_count: 0,
  clamped_duration_count: 0,
  cooldown_block_count: 0,
  restore_success_count: 0,
  restore_total_count: 0,
  emergency_stop_count: 0,
  latency_records: [], // ms 단위 raw 기록
};

const LOG_DIR = path.join(__dirname, '..', 'logs');
const LOG_FILE = path.join(LOG_DIR, `server_${new Date().toISOString().slice(0, 10)}.log`);

function ensureLogDir() {
  if (!fs.existsSync(LOG_DIR)) fs.mkdirSync(LOG_DIR, { recursive: true });
}

// [ISO] EVENT: xxx | DELAY: xms | STATUS: xxx
function logEvent({ event_id, delayMs, status, detail = '' }) {
  const line = `[${new Date().toISOString()}] EVENT: ${event_id} | DELAY: ${delayMs ?? '-'}ms | STATUS: ${status}${detail ? ' | ' + detail : ''}`;
  console.log(line);
  try {
    ensureLogDir();
    fs.appendFileSync(LOG_FILE, line + '\n');
  } catch (e) {
    console.error('[LOGGER] 파일 기록 실패:', e.message);
  }
}

function logInfo(message) {
  const line = `[${new Date().toISOString()}] INFO: ${message}`;
  console.log(line);
  try {
    ensureLogDir();
    fs.appendFileSync(LOG_FILE, line + '\n');
  } catch (e) {
    // 로그 실패는 무시
  }
}

function logError(message, err) {
  const line = `[${new Date().toISOString()}] ERROR: ${message}${err ? ' | ' + (err.message || err) : ''}`;
  console.error(line);
  try {
    ensureLogDir();
    fs.appendFileSync(LOG_FILE, line + '\n');
  } catch (e) {
    // 로그 실패는 무시
  }
}

function recordLatency(event_id, latencyMs) {
  counters.latency_records.push({ event_id, latencyMs, ts: Date.now() });
}

function getCounters() {
  const total = counters.restore_total_count;
  const success = counters.restore_success_count;
  return {
    ...counters,
    restore_success_rate: total > 0 ? (success / total).toFixed(3) : 'N/A',
    latency_records: counters.latency_records,
  };
}

module.exports = { logEvent, logInfo, logError, recordLatency, getCounters, counters };
