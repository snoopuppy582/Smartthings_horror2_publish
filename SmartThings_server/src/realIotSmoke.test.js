const assert = require('assert');
const fs = require('fs');
const http = require('http');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');
const test = require('node:test');

const serverDir = path.resolve(__dirname, '..');
const scriptPath = path.join(__dirname, 'realIotSmoke.js');

test('real IoT smoke writes failure evidence when server is in simulation mode', async () => {
  const logDir = makeTempDir();
  const testServer = await startSmokeServer({ simulation: true });

  try {
    const result = await runSmoke(logDir, testServer.url);
    assert.strictEqual(result.code, 1, result.stdout + result.stderr);

    const evidence = readLatest(logDir);
    assert.strictEqual(evidence.success, false);
    assert.strictEqual(evidence.health.simulation, true);
    assert.match(evidence.error, /simulation mode/);
    assert.strictEqual(evidence.events.length, 0);
  } finally {
    await testServer.close();
  }
});

test('real IoT smoke sends expected event sequence and writes success evidence', async () => {
  const logDir = makeTempDir();
  const testServer = await startSmokeServer({ simulation: false });

  try {
    const result = await runSmoke(logDir, testServer.url);
    assert.strictEqual(result.code, 0, result.stdout + result.stderr);

    const evidence = readLatest(logDir);
    assert.strictEqual(evidence.success, true);
    assert.strictEqual(evidence.health.simulation, false);
    assert.strictEqual(evidence.emergencyStop.status, 'ok');
    assert.deepStrictEqual(
      evidence.events.map(event => event.event_id),
      ['game_start', 'killer_near', 'player_hit', 'mission_success'],
    );
    assert.deepStrictEqual(
      testServer.requests.map(request => request.event_id),
      ['game_start', 'killer_near', 'player_hit', 'mission_success'],
    );
  } finally {
    await testServer.close();
  }
});

async function startSmokeServer({ simulation }) {
  const requests = [];
  const server = http.createServer((req, res) => {
    if (req.url === '/health') {
      writeJson(res, 200, {
        status: simulation ? 'degraded' : 'ok',
        simulation,
        token_set: !simulation,
        device_light_set: !simulation,
        device_fan_set: !simulation,
      });
      return;
    }

    if (req.url === '/event' && req.method === 'POST') {
      readBody(req).then(body => {
        requests.push(body);
        writeJson(res, 202, {
          status: 'accepted',
          event_id: body.event_id,
          session_id: body.session_id,
        });
      });
      return;
    }

    if (req.url === '/emergency-stop' && req.method === 'POST') {
      writeJson(res, 200, { status: 'ok' });
      return;
    }

    writeJson(res, 404, { error: 'not found' });
  });

  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  return {
    requests,
    url: `http://127.0.0.1:${address.port}`,
    close: () => new Promise(resolve => server.close(resolve)),
  };
}

function runSmoke(logDir, baseUrl) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [scriptPath], {
      cwd: serverDir,
      env: {
        ...process.env,
        IOT_SERVER_URL: baseUrl,
        REAL_IOT_SMOKE_LOG_DIR: logDir,
        REAL_IOT_SMOKE_EVENT_DELAY_MS: '0',
        REAL_IOT_SMOKE_HIT_DELAY_MS: '0',
      },
      encoding: 'utf8',
    });

    let stdout = '';
    let stderr = '';
    child.stdout.on('data', chunk => { stdout += chunk; });
    child.stderr.on('data', chunk => { stderr += chunk; });
    child.on('error', reject);
    child.on('close', code => resolve({ code, stdout, stderr }));
  });
}

function readLatest(logDir) {
  return JSON.parse(fs.readFileSync(path.join(logDir, 'real-iot-smoke-latest.json'), 'utf8'));
}

function readBody(req) {
  return new Promise((resolve, reject) => {
    let raw = '';
    req.setEncoding('utf8');
    req.on('data', chunk => { raw += chunk; });
    req.on('end', () => {
      try {
        resolve(raw ? JSON.parse(raw) : {});
      } catch (err) {
        reject(err);
      }
    });
    req.on('error', reject);
  });
}

function writeJson(res, statusCode, value) {
  res.writeHead(statusCode, { 'Content-Type': 'application/json' });
  res.end(JSON.stringify(value));
}

function makeTempDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'real-iot-smoke-'));
}
