const assert = require('assert');
const fs = require('fs');
const http = require('http');
const os = require('os');
const path = require('path');
const { spawn } = require('child_process');
const test = require('node:test');

const serverDir = path.resolve(__dirname, '..');
const scriptPath = path.join(__dirname, 'submissionStatus.js');

test('submission status local mode allows simulation and missing human/real-IoT evidence as warnings', async () => {
  const fixture = makeFixture();
  writeUnityReports(fixture.projectDir, { qaWarnings: 1 });
  const healthServer = await startHealthServer({
    status: 'degraded',
    simulation: true,
    token_set: false,
    device_light_set: false,
    device_fan_set: false,
  });

  try {
    const result = await runSubmissionStatus(fixture, healthServer.url, []);
    assert.strictEqual(result.code, 0, result.stderr || result.stdout);

    const evidence = readJson(path.join(fixture.logDir, 'submission-status-local-latest.json'));
    assert.strictEqual(evidence.mode, 'local');
    assert.strictEqual(evidence.summary.fail, 0);
    assert.ok(evidence.summary.warn >= 3);
  } finally {
    await healthServer.close();
  }
});

test('submission status final mode passes with clean Unity, real IoT, and manual evidence', async () => {
  const fixture = makeFixture();
  writeUnityReports(fixture.projectDir, { qaWarnings: 0 });
  writeRealIotEvidence(fixture.realIotEvidencePath, { success: true, simulation: false });
  writeManualEvidence(fixture.manualEvidencePath, { passed: true });
  const healthServer = await startHealthServer({
    status: 'ok',
    simulation: false,
    token_set: true,
    device_light_set: true,
    device_fan_set: true,
  });

  try {
    const result = await runSubmissionStatus(fixture, healthServer.url, ['--require-real-iot']);
    assert.strictEqual(result.code, 0, result.stderr || result.stdout);

    const evidence = readJson(path.join(fixture.logDir, 'submission-status-final-latest.json'));
    assert.strictEqual(evidence.mode, 'final');
    assert.deepStrictEqual(evidence.summary, { pass: 10, warn: 0, fail: 0 });
  } finally {
    await healthServer.close();
  }
});

test('submission status final mode fails on Unity QA warning', async () => {
  const fixture = makeFixture();
  writeUnityReports(fixture.projectDir, { qaWarnings: 1 });
  writeRealIotEvidence(fixture.realIotEvidencePath, { success: true, simulation: false });
  writeManualEvidence(fixture.manualEvidencePath, { passed: true });
  const healthServer = await startHealthServer({
    status: 'ok',
    simulation: false,
    token_set: true,
    device_light_set: true,
    device_fan_set: true,
  });

  try {
    const result = await runSubmissionStatus(fixture, healthServer.url, ['--require-real-iot']);
    assert.strictEqual(result.code, 1, result.stdout);
    assert.match(result.stdout, /\[FAIL\] Unity Submission QA/);
  } finally {
    await healthServer.close();
  }
});

function makeFixture() {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'submission-status-'));
  const projectDir = path.join(root, 'project');
  const serverFixtureDir = path.join(root, 'server');
  const logDir = path.join(root, 'logs');
  fs.mkdirSync(path.join(projectDir, 'Temp'), { recursive: true });
  fs.mkdirSync(serverFixtureDir, { recursive: true });
  fs.mkdirSync(logDir, { recursive: true });

  return {
    root,
    projectDir,
    serverDir: serverFixtureDir,
    logDir,
    realIotEvidencePath: path.join(root, 'real-iot-smoke-latest.json'),
    manualEvidencePath: path.join(root, 'manual-playtest-latest.json'),
  };
}

function writeUnityReports(projectDir, { qaWarnings }) {
  const now = new Date().toISOString();
  writeJson(path.join(projectDir, 'Temp', 'experiment_submission_qa.json'), {
    timestampUtc: now,
    sceneName: 'MainScene',
    scenePath: 'Assets/Scenes/MainScene.unity',
    errorCount: 0,
    warningCount: qaWarnings,
  });
  writeJson(path.join(projectDir, 'Temp', 'experiment_playmode_smoke.json'), {
    timestampUtc: now,
    sceneName: 'MainScene',
    scenePath: 'Assets/Scenes/MainScene.unity',
    condition: 'GameOnly',
    sessionId: 'game_only_session',
    logPath: 'game_only.jsonl',
    success: true,
    errorCount: 0,
    warningCount: 0,
  });
  writeJson(path.join(projectDir, 'Temp', 'experiment_playmode_iot_smoke.json'), {
    timestampUtc: now,
    sceneName: 'MainScene',
    scenePath: 'Assets/Scenes/MainScene.unity',
    condition: 'GameWithIoT',
    sessionId: 'iot_session',
    logPath: 'iot.jsonl',
    success: true,
    errorCount: 0,
    warningCount: 0,
  });
}

function writeRealIotEvidence(filePath, { success, simulation }) {
  const now = new Date().toISOString();
  writeJson(filePath, {
    startedAtUtc: now,
    endedAtUtc: now,
    sessionId: 'real_iot_test',
    success,
    health: {
      status: simulation ? 'degraded' : 'ok',
      simulation,
      token_set: !simulation,
      device_light_set: !simulation,
      device_fan_set: !simulation,
    },
    error: success ? null : 'not ready',
  });
}

function writeManualEvidence(filePath, { passed }) {
  const now = new Date().toISOString();
  writeJson(filePath, {
    version: 1,
    updatedAtUtc: now,
    entries: [
      manualEntry('GameOnly', now, passed, false),
      manualEntry('GameWithIoT', now, passed, true),
    ],
  });
}

function manualEntry(condition, playedAtUtc, passed, includeIot) {
  const checks = {
    objectiveUnderstoodWithin5Sec: passed,
    playerMovementFeelsNatural: passed,
    enteredHouseWithoutJumpingOrRubbing: passed,
    reachedSecondFloorObjectiveRoute: passed,
    mapTraversalNormal: passed,
    killerPressureVisibleAndFair: passed,
    killerMotionFeelsNatural: passed,
    nonlethalHitFeedbackObserved: passed,
    successUiObserved: passed,
    timeoutUiObserved: passed,
    experimentLogsWritten: passed,
  };
  if (includeIot) checks.iotStimulusObserved = passed;

  return {
    condition,
    playedAtUtc,
    tester: 'qa',
    result: passed ? 'pass' : 'pending',
    passed,
    checks,
  };
}

async function startHealthServer(health) {
  const server = http.createServer((req, res) => {
    if (req.url === '/health') {
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify(health));
      return;
    }

    res.writeHead(404, { 'Content-Type': 'application/json' });
    res.end(JSON.stringify({ error: 'not found' }));
  });

  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  const address = server.address();
  return {
    url: `http://127.0.0.1:${address.port}`,
    close: () => new Promise(resolve => server.close(resolve)),
  };
}

function runSubmissionStatus(fixture, baseUrl, args) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [scriptPath, ...args], {
      cwd: serverDir,
      env: {
        ...process.env,
        IOT_SERVER_URL: baseUrl,
        SUBMISSION_PROJECT_DIR: fixture.projectDir,
        SUBMISSION_SERVER_DIR: fixture.serverDir,
        SUBMISSION_STATUS_LOG_DIR: fixture.logDir,
        REAL_IOT_EVIDENCE_PATH: fixture.realIotEvidencePath,
        MANUAL_PLAYTEST_EVIDENCE_PATH: fixture.manualEvidencePath,
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

function writeJson(filePath, value) {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, JSON.stringify(value, null, 2));
}

function readJson(filePath) {
  return JSON.parse(fs.readFileSync(filePath, 'utf8'));
}
