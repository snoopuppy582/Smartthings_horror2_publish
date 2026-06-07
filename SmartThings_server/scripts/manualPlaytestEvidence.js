const fs = require('fs');
const os = require('os');
const path = require('path');

const serverDir = path.resolve(__dirname, '..');
const logDir = process.env.MANUAL_PLAYTEST_LOG_DIR
  ? path.resolve(process.env.MANUAL_PLAYTEST_LOG_DIR)
  : path.join(serverDir, 'logs');
const latestEvidencePath = path.join(logDir, 'manual-playtest-latest.json');

const CONDITIONS = ['GameOnly', 'GameWithIoT'];
const COMMON_CHECKS = [
  'objectiveUnderstoodWithin5Sec',
  'playerMovementFeelsNatural',
  'enteredHouseWithoutJumpingOrRubbing',
  'reachedSecondFloorObjectiveRoute',
  'mapTraversalNormal',
  'killerPressureVisibleAndFair',
  'killerMotionFeelsNatural',
  'nonlethalHitFeedbackObserved',
  'successUiObserved',
  'timeoutUiObserved',
  'experimentLogsWritten',
];
const CONDITION_CHECKS = {
  GameOnly: [],
  GameWithIoT: ['iotStimulusObserved'],
};
const FLAG_TO_CHECK = {
  'objective-understood': 'objectiveUnderstoodWithin5Sec',
  'player-movement': 'playerMovementFeelsNatural',
  doorway: 'enteredHouseWithoutJumpingOrRubbing',
  'second-floor': 'reachedSecondFloorObjectiveRoute',
  'map-traversal': 'mapTraversalNormal',
  'killer-pressure': 'killerPressureVisibleAndFair',
  'killer-motion': 'killerMotionFeelsNatural',
  'hit-feedback': 'nonlethalHitFeedbackObserved',
  'success-ui': 'successUiObserved',
  'timeout-ui': 'timeoutUiObserved',
  logs: 'experimentLogsWritten',
  'iot-stimulus': 'iotStimulusObserved',
};

const args = parseArgs(process.argv.slice(2));

if (args.help) {
  printHelp();
  process.exit(0);
}

fs.mkdirSync(logDir, { recursive: true });

if (args.init) {
  const evidence = createEmptyEvidence();
  writeEvidence(evidence);
  console.log(`[manual-playtest] initialized ${latestEvidencePath}`);
  process.exit(0);
}

if (!args.condition) {
  printHelp();
  process.exitCode = 1;
} else {
  recordCondition(args);
}

function recordCondition(options) {
  const condition = options.condition;
  if (!CONDITIONS.includes(condition)) {
    throw new Error(`Invalid condition: ${condition}. Use ${CONDITIONS.join(' or ')}.`);
  }

  const evidence = readEvidence() || createEmptyEvidence();
  const entry = ensureEntry(evidence, condition);
  entry.playedAtUtc = new Date().toISOString();
  entry.tester = options.tester || os.userInfo().username || 'unknown';
  entry.notes = options.notes || '';
  entry.result = options.result || (options['all-pass'] ? 'pass' : 'pending');

  const requiredChecks = requiredChecksFor(condition);
  if (options['all-pass']) {
    for (const check of requiredChecks) {
      entry.checks[check] = true;
    }
  }

  for (const [flag, check] of Object.entries(FLAG_TO_CHECK)) {
    if (Object.prototype.hasOwnProperty.call(options, flag)) {
      entry.checks[check] = booleanValue(options[flag]);
    }
  }

  entry.missingChecks = requiredChecks.filter(check => entry.checks[check] !== true);
  entry.passed = entry.result === 'pass' && entry.missingChecks.length === 0;
  evidence.updatedAtUtc = new Date().toISOString();
  writeEvidence(evidence);

  const status = entry.passed ? 'pass' : 'incomplete';
  console.log(`[manual-playtest] ${condition} ${status}; evidence=${latestEvidencePath}`);
  if (!entry.passed) {
    console.log(`[manual-playtest] missing=${entry.missingChecks.join(',') || 'none'} result=${entry.result}`);
  }
}

function createEmptyEvidence() {
  return {
    version: 1,
    updatedAtUtc: new Date().toISOString(),
    entries: CONDITIONS.map(condition => ({
      condition,
      playedAtUtc: null,
      tester: null,
      result: 'pending',
      passed: false,
      notes: '',
      checks: Object.fromEntries(requiredChecksFor(condition).map(check => [check, false])),
      missingChecks: requiredChecksFor(condition),
    })),
  };
}

function readEvidence() {
  try {
    return JSON.parse(fs.readFileSync(latestEvidencePath, 'utf8'));
  } catch (err) {
    return null;
  }
}

function ensureEntry(evidence, condition) {
  if (!Array.isArray(evidence.entries)) {
    evidence.entries = [];
  }

  let entry = evidence.entries.find(item => item.condition === condition);
  if (!entry) {
    entry = createEmptyEvidence().entries.find(item => item.condition === condition);
    evidence.entries.push(entry);
  }

  entry.checks = entry.checks || {};
  for (const check of requiredChecksFor(condition)) {
    if (!Object.prototype.hasOwnProperty.call(entry.checks, check)) {
      entry.checks[check] = false;
    }
  }

  return entry;
}

function writeEvidence(evidence) {
  const safeTimestamp = new Date().toISOString().replace(/[:.]/g, '-');
  const timestampedPath = path.join(logDir, `manual-playtest-${safeTimestamp}.json`);
  evidence.evidencePath = timestampedPath;
  evidence.latestEvidencePath = latestEvidencePath;
  const payload = JSON.stringify(evidence, null, 2);
  fs.writeFileSync(timestampedPath, payload);
  fs.writeFileSync(latestEvidencePath, payload);
}

function requiredChecksFor(condition) {
  return COMMON_CHECKS.concat(CONDITION_CHECKS[condition] || []);
}

function parseArgs(rawArgs) {
  const parsed = {};
  for (let i = 0; i < rawArgs.length; i++) {
    const raw = rawArgs[i];
    if (!raw.startsWith('--')) continue;

    const key = raw.slice(2);
    const next = rawArgs[i + 1];
    if (!next || next.startsWith('--')) {
      parsed[key] = true;
    } else {
      parsed[key] = next;
      i++;
    }
  }
  return parsed;
}

function booleanValue(value) {
  if (value === true) return true;
  if (value === false) return false;
  const normalized = String(value).trim().toLowerCase();
  if (['1', 'true', 'yes', 'y', 'pass', 'passed'].includes(normalized)) return true;
  if (['0', 'false', 'no', 'n', 'fail', 'failed'].includes(normalized)) return false;
  return Boolean(value);
}

function printHelp() {
  console.log(`Manual playtest evidence

Initialize:
  node scripts/manualPlaytestEvidence.js --init

Record a passed condition after real play:
  node scripts/manualPlaytestEvidence.js --condition GameOnly --tester "name" --all-pass --notes "short note"
  node scripts/manualPlaytestEvidence.js --condition GameWithIoT --tester "name" --all-pass --notes "short note"

Record individual checks:
  --objective-understood --player-movement --doorway --second-floor --map-traversal
  --killer-pressure --killer-motion --hit-feedback --success-ui --timeout-ui --logs
  --iot-stimulus  (required only for GameWithIoT)

Set --result pass when all required checks are true.`);
}
