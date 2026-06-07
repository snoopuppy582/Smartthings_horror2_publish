const assert = require('assert');
const fs = require('fs');
const os = require('os');
const path = require('path');
const { spawnSync } = require('child_process');
const test = require('node:test');

const serverDir = path.resolve(__dirname, '..');
const scriptPath = path.join(__dirname, 'manualPlaytestEvidence.js');

test('manual playtest evidence initializes pending GameOnly and GameWithIoT entries', () => {
  const tempDir = makeTempDir();
  runManual(tempDir, ['--init']);

  const evidence = readLatest(tempDir);
  assert.strictEqual(evidence.entries.length, 2);
  assertEntry(evidence, 'GameOnly', false);
  assertEntry(evidence, 'GameWithIoT', false);
});

test('manual playtest evidence records both conditions as passed only after both are complete', () => {
  const tempDir = makeTempDir();
  runManual(tempDir, ['--init']);
  runManual(tempDir, ['--condition', 'GameOnly', '--tester', 'qa', '--all-pass', '--notes', 'game only checked']);

  let evidence = readLatest(tempDir);
  assertEntry(evidence, 'GameOnly', true);
  assertEntry(evidence, 'GameWithIoT', false);

  runManual(tempDir, ['--condition', 'GameWithIoT', '--tester', 'qa', '--all-pass', '--notes', 'iot checked']);
  evidence = readLatest(tempDir);
  assertEntry(evidence, 'GameOnly', true);
  assertEntry(evidence, 'GameWithIoT', true);
  assert.strictEqual(evidence.entries.find(entry => entry.condition === 'GameWithIoT').checks.iotStimulusObserved, true);
});

test('manual playtest evidence leaves condition incomplete when a required check is false', () => {
  const tempDir = makeTempDir();
  runManual(tempDir, ['--init']);
  runManual(tempDir, [
    '--condition',
    'GameOnly',
    '--tester',
    'qa',
    '--all-pass',
    '--doorway',
    'false',
    '--notes',
    'doorway failed',
  ]);

  const evidence = readLatest(tempDir);
  const gameOnly = evidence.entries.find(entry => entry.condition === 'GameOnly');
  assert.strictEqual(gameOnly.passed, false);
  assert.ok(gameOnly.missingChecks.includes('enteredHouseWithoutJumpingOrRubbing'));
});

function runManual(tempDir, args) {
  const result = spawnSync(process.execPath, [scriptPath, ...args], {
    cwd: serverDir,
    env: {
      ...process.env,
      MANUAL_PLAYTEST_LOG_DIR: tempDir,
    },
    encoding: 'utf8',
  });

  assert.strictEqual(result.status, 0, `${result.stdout}\n${result.stderr}`);
}

function readLatest(tempDir) {
  return JSON.parse(fs.readFileSync(path.join(tempDir, 'manual-playtest-latest.json'), 'utf8'));
}

function assertEntry(evidence, condition, passed) {
  const entry = evidence.entries.find(item => item.condition === condition);
  assert.ok(entry, `${condition} entry missing`);
  assert.strictEqual(entry.passed, passed);
}

function makeTempDir() {
  return fs.mkdtempSync(path.join(os.tmpdir(), 'manual-playtest-'));
}
