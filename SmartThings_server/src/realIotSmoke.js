const http = require('http');
const fs = require('fs');
const path = require('path');

const BASE_URL = process.env.IOT_SERVER_URL || 'http://127.0.0.1:3000';
const EVENTS = ['game_start', 'killer_near', 'player_hit', 'mission_success'];
const SERVER_DIR = path.resolve(__dirname, '..');
const LOG_DIR = process.env.REAL_IOT_SMOKE_LOG_DIR
  ? path.resolve(process.env.REAL_IOT_SMOKE_LOG_DIR)
  : path.join(SERVER_DIR, 'logs');
const LATEST_EVIDENCE_PATH = path.join(LOG_DIR, 'real-iot-smoke-latest.json');
const INTER_EVENT_DELAY_MS = Number(process.env.REAL_IOT_SMOKE_EVENT_DELAY_MS || 1200);
const HIT_EVENT_DELAY_MS = Number(process.env.REAL_IOT_SMOKE_HIT_DELAY_MS || 5500);

async function main() {
  const evidence = {
    startedAtUtc: new Date().toISOString(),
    baseUrl: BASE_URL,
    sessionId: `real_iot_smoke_${Date.now()}`,
    success: false,
    health: null,
    events: [],
    emergencyStop: null,
    evidencePath: null,
  };

  try {
    const health = await requestJson('GET', '/health');
    evidence.health = health;
    if (health.simulation) {
      throw new Error('Server is in simulation mode. Fill .env and restart the server before real IoT smoke.');
    }
    if (health.status !== 'ok' || !health.token_set || !health.device_light_set || !health.device_fan_set) {
      throw new Error(`Server health is not ready for real IoT: ${JSON.stringify(health)}`);
    }

    for (let i = 0; i < EVENTS.length; i++) {
      const eventId = EVENTS[i];
      const payload = {
        event_id: eventId,
        session_id: evidence.sessionId,
        condition: 'GameWithIoT',
        elapsed_sec: i,
        hit_count: eventId === 'player_hit' ? 1 : 0,
        timestamp: Date.now(),
      };
      const response = await requestJson('POST', '/event', payload);
      evidence.events.push({
        event_id: eventId,
        sentAtUtc: new Date().toISOString(),
        payload,
        response,
      });

      if (response.status !== 'accepted') {
        throw new Error(`Event was not accepted: ${eventId} ${JSON.stringify(response)}`);
      }

      await delay(eventId === 'player_hit' ? HIT_EVENT_DELAY_MS : INTER_EVENT_DELAY_MS);
    }

    evidence.emergencyStop = await requestJson('POST', '/emergency-stop');
    evidence.success = true;
    evidence.endedAtUtc = new Date().toISOString();
    evidence.evidencePath = writeEvidence(evidence);
    console.log(`[real-iot-smoke] ok session=${evidence.sessionId} events=${EVENTS.join(',')} evidence=${evidence.evidencePath}`);
  } catch (err) {
    evidence.error = err.message;
    evidence.endedAtUtc = new Date().toISOString();
    evidence.evidencePath = writeEvidence(evidence);
    console.error(`[real-iot-smoke] failed: ${err.message}`);
    console.error(`[real-iot-smoke] evidence=${evidence.evidencePath}`);
    process.exitCode = 1;
  }
}

function requestJson(method, path, body = null) {
  const url = new URL(path, BASE_URL);
  const payload = body ? Buffer.from(JSON.stringify(body), 'utf8') : null;

  return new Promise((resolve, reject) => {
    const req = http.request(
      url,
      {
        method,
        headers: payload
          ? { 'Content-Type': 'application/json', 'Content-Length': payload.length }
          : undefined,
        timeout: 5000,
      },
      res => {
        let raw = '';
        res.setEncoding('utf8');
        res.on('data', chunk => { raw += chunk; });
        res.on('end', () => {
          let parsed = {};
          if (raw) {
            try {
              parsed = JSON.parse(raw);
            } catch (err) {
              reject(new Error(`Invalid JSON from ${method} ${path}: ${raw}`));
              return;
            }
          }

          if (res.statusCode < 200 || res.statusCode >= 300) {
            reject(new Error(`${method} ${path} -> HTTP ${res.statusCode}: ${raw}`));
            return;
          }

          resolve(parsed);
        });
      });

    req.on('timeout', () => {
      req.destroy(new Error(`${method} ${path} timed out`));
    });
    req.on('error', reject);
    if (payload) req.write(payload);
    req.end();
  });
}

function delay(ms) {
  return new Promise(resolve => setTimeout(resolve, ms));
}

function writeEvidence(evidence) {
  fs.mkdirSync(LOG_DIR, { recursive: true });
  const safeTimestamp = evidence.startedAtUtc.replace(/[:.]/g, '-');
  const timestampedPath = path.join(LOG_DIR, `real-iot-smoke-${safeTimestamp}.json`);
  evidence.evidencePath = timestampedPath;
  evidence.latestEvidencePath = LATEST_EVIDENCE_PATH;
  const payload = JSON.stringify(evidence, null, 2);
  fs.writeFileSync(timestampedPath, payload);
  fs.writeFileSync(LATEST_EVIDENCE_PATH, payload);
  return timestampedPath;
}

main();
