const fs = require('fs');
const http = require('http');
const path = require('path');

const requireRealIot = process.argv.includes('--require-real-iot');
const serverDir = process.env.SUBMISSION_SERVER_DIR
  ? path.resolve(process.env.SUBMISSION_SERVER_DIR)
  : path.resolve(__dirname, '..');
const projectDir = process.env.SUBMISSION_PROJECT_DIR
  ? path.resolve(process.env.SUBMISSION_PROJECT_DIR)
  : path.resolve(serverDir, '..');
const baseUrl = process.env.IOT_SERVER_URL || 'http://127.0.0.1:3000';
const logDir = process.env.SUBMISSION_STATUS_LOG_DIR
  ? path.resolve(process.env.SUBMISSION_STATUS_LOG_DIR)
  : path.join(serverDir, 'logs');
const latestStatusPath = path.join(logDir, 'submission-status-latest.json');
const latestModeStatusPath = path.join(
  logDir,
  requireRealIot ? 'submission-status-final-latest.json' : 'submission-status-local-latest.json',
);
const realIotEvidencePath = process.env.REAL_IOT_EVIDENCE_PATH
  ? path.resolve(process.env.REAL_IOT_EVIDENCE_PATH)
  : path.join(serverDir, 'logs', 'real-iot-smoke-latest.json');
const manualPlaytestEvidencePath = process.env.MANUAL_PLAYTEST_EVIDENCE_PATH
  ? path.resolve(process.env.MANUAL_PLAYTEST_EVIDENCE_PATH)
  : path.join(serverDir, 'logs', 'manual-playtest-latest.json');
const maxReportAgeHours = Number(process.env.SUBMISSION_REPORT_MAX_AGE_HOURS || 24);
const maxReportAgeMs = maxReportAgeHours * 60 * 60 * 1000;
const maxIotEvidenceAgeHours = Number(process.env.REAL_IOT_EVIDENCE_MAX_AGE_HOURS || 24);
const maxIotEvidenceAgeMs = maxIotEvidenceAgeHours * 60 * 60 * 1000;
const maxManualPlaytestAgeHours = Number(process.env.MANUAL_PLAYTEST_MAX_AGE_HOURS || 24);
const maxManualPlaytestAgeMs = maxManualPlaytestAgeHours * 60 * 60 * 1000;
const requiredManualConditions = ['GameOnly', 'GameWithIoT'];
const requiredManualChecks = {
  GameOnly: [
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
  ],
  GameWithIoT: [
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
    'iotStimulusObserved',
  ],
};

const checks = [];

async function main() {
  const startedAtUtc = new Date().toISOString();
  checkUnityReports();
  await checkServerHealth();
  checkRealIotEvidence();
  checkManualPlaytestEvidence();

  const failed = checks.filter(check => check.status === 'fail');
  const warned = checks.filter(check => check.status === 'warn');
  const summary = {
    pass: checks.length - failed.length - warned.length,
    warn: warned.length,
    fail: failed.length,
  };

  for (const check of checks) {
    const marker = check.status.toUpperCase().padEnd(4);
    console.log(`[${marker}] ${check.name}: ${check.message}`);
  }

  console.log('');
  console.log(`Summary: ${summary.pass} pass, ${summary.warn} warn, ${summary.fail} fail`);

  const evidencePath = writeStatusEvidence({
    startedAtUtc,
    endedAtUtc: new Date().toISOString(),
    mode: requireRealIot ? 'final' : 'local',
    requireRealIot,
    baseUrl,
    projectDir,
    serverDir,
    maxReportAgeHours,
    maxIotEvidenceAgeHours,
    maxManualPlaytestAgeHours,
    summary,
    checks,
  });
  console.log(`Evidence: ${evidencePath}`);

  if (failed.length > 0) {
    process.exitCode = 1;
  }
}

function checkUnityReports() {
  const qaPath = path.join(projectDir, 'Temp', 'experiment_submission_qa.json');
  const qa = readJson(qaPath);
  if (!qa) {
    add('Unity Submission QA', 'fail', 'Temp/experiment_submission_qa.json is missing.');
  } else if (qa.errorCount !== 0) {
    add('Unity Submission QA', 'fail', `errorCount=${qa.errorCount}`, reportDetails(qaPath, qa));
  } else if ((qa.warningCount || 0) > 0) {
    add('Unity Submission QA', requireRealIot ? 'fail' : 'warn', `passed with warningCount=${qa.warningCount}`, reportDetails(qaPath, qa));
  } else {
    add('Unity Submission QA', 'pass', 'errorCount=0', reportDetails(qaPath, qa));
  }
  checkReportFreshness('Unity Submission QA Freshness', qaPath, qa);

  checkSmokeReport('Unity GameOnly Smoke', 'experiment_playmode_smoke.json');
  checkSmokeReport('Unity GameWithIoT Simulation Smoke', 'experiment_playmode_iot_smoke.json');
}

function checkSmokeReport(name, fileName) {
  const reportPath = path.join(projectDir, 'Temp', fileName);
  const report = readJson(reportPath);
  if (!report) {
    add(name, 'fail', `Temp/${fileName} is missing.`);
    return;
  }

  if (!report.success || report.errorCount !== 0) {
    add(name, 'fail', `success=${report.success}, errorCount=${report.errorCount}`, reportDetails(reportPath, report));
    checkReportFreshness(`${name} Freshness`, reportPath, report);
    return;
  }

  if ((report.warningCount || 0) > 0) {
    add(name, requireRealIot ? 'fail' : 'warn', `success=true with warningCount=${report.warningCount}`, reportDetails(reportPath, report));
    checkReportFreshness(`${name} Freshness`, reportPath, report);
    return;
  }

  add(name, 'pass', 'success=true, errorCount=0', reportDetails(reportPath, report));
  checkReportFreshness(`${name} Freshness`, reportPath, report);
}

async function checkServerHealth() {
  try {
    const health = await requestJson('GET', '/health');
    if (health.status === 'ok' && !health.simulation) {
      add('SmartThings Server Health', 'pass', 'real-device mode, credentials present.', health);
      return;
    }

    if (requireRealIot) {
      const details = {
        status: health.status,
        simulation: health.simulation,
        token_set: health.token_set,
        device_light_set: health.device_light_set,
        device_fan_set: health.device_fan_set,
      };
      add('SmartThings Server Health', 'fail', `not ready for final IoT gate: ${JSON.stringify(details)}`, details);
    } else {
      add('SmartThings Server Health', 'warn', `local mode only: simulation=${health.simulation}, status=${health.status}`, health);
    }
  } catch (err) {
    add('SmartThings Server Health', requireRealIot ? 'fail' : 'warn', `server not reachable at ${baseUrl}: ${err.message}`, {
      baseUrl,
      error: err.message,
    });
  }
}

function checkRealIotEvidence() {
  const evidencePath = realIotEvidencePath;
  const evidence = readJson(evidencePath);

  if (!evidence) {
    add('Real IoT Smoke Evidence', requireRealIot ? 'fail' : 'warn', 'logs/real-iot-smoke-latest.json is missing.');
    return;
  }
  checkIotEvidenceFreshness(evidencePath, evidence);

  if (!evidence.success) {
    add('Real IoT Smoke Evidence', requireRealIot ? 'fail' : 'warn', `latest run failed: ${evidence.error || 'unknown error'}`, summarizeIotEvidence(evidencePath, evidence));
    return;
  }

  if (!evidence.health || evidence.health.simulation) {
    add('Real IoT Smoke Evidence', requireRealIot ? 'fail' : 'warn', 'latest run was not proven in real-device mode.', summarizeIotEvidence(evidencePath, evidence));
    return;
  }

  add('Real IoT Smoke Evidence', 'pass', `session=${evidence.sessionId}, endedAtUtc=${evidence.endedAtUtc}`, summarizeIotEvidence(evidencePath, evidence));
}

function checkManualPlaytestEvidence() {
  const evidencePath = manualPlaytestEvidencePath;
  const evidence = readJson(evidencePath);

  if (!evidence) {
    add('Manual Playtest Evidence', requireRealIot ? 'fail' : 'warn', 'logs/manual-playtest-latest.json is missing.');
    return;
  }

  const entries = Array.isArray(evidence.entries) ? evidence.entries : [];
  let allConditionsPassed = true;
  const conditionSummaries = [];

  for (const condition of requiredManualConditions) {
    const entry = entries.find(item => item.condition === condition);
    if (!entry) {
      allConditionsPassed = false;
      conditionSummaries.push({ condition, passed: false, missing: ['entry'] });
      continue;
    }

    const requiredChecks = requiredManualChecks[condition];
    const missingChecks = requiredChecks.filter(check => !entry.checks || entry.checks[check] !== true);
    const timestampStatus = manualEntryFreshness(entry);
    const passed = entry.passed === true && entry.result === 'pass' && missingChecks.length === 0 && timestampStatus.fresh;
    if (!passed) allConditionsPassed = false;

    conditionSummaries.push({
      condition,
      passed,
      result: entry.result || null,
      playedAtUtc: entry.playedAtUtc || null,
      tester: entry.tester || null,
      missingChecks,
      stale: !timestampStatus.fresh,
      ageHours: timestampStatus.ageHours,
    });
  }

  const status = allConditionsPassed ? 'pass' : (requireRealIot ? 'fail' : 'warn');
  const message = allConditionsPassed
    ? 'GameOnly and GameWithIoT manual playtests passed.'
    : 'GameOnly and/or GameWithIoT manual playtest evidence is incomplete.';
  add('Manual Playtest Evidence', status, message, {
    path: evidencePath,
    maxManualPlaytestAgeHours,
    conditions: conditionSummaries,
  });
}

function readJson(filePath) {
  try {
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
  } catch (err) {
    return null;
  }
}

function requestJson(method, route) {
  const url = new URL(route, baseUrl);
  return new Promise((resolve, reject) => {
    const req = http.request(url, { method, timeout: 5000 }, res => {
      let raw = '';
      res.setEncoding('utf8');
      res.on('data', chunk => { raw += chunk; });
      res.on('end', () => {
        if (res.statusCode < 200 || res.statusCode >= 300) {
          reject(new Error(`${method} ${route} -> HTTP ${res.statusCode}: ${raw}`));
          return;
        }

        try {
          resolve(raw ? JSON.parse(raw) : {});
        } catch (err) {
          reject(new Error(`Invalid JSON from ${method} ${route}: ${raw}`));
        }
      });
    });

    req.on('timeout', () => req.destroy(new Error(`${method} ${route} timed out`)));
    req.on('error', reject);
    req.end();
  });
}

function checkReportFreshness(name, filePath, report) {
  if (!report || !report.timestampUtc) {
    add(name, requireRealIot ? 'fail' : 'warn', 'report timestampUtc is missing.', { path: filePath });
    return;
  }

  const timestampMs = Date.parse(report.timestampUtc);
  if (!Number.isFinite(timestampMs)) {
    add(name, requireRealIot ? 'fail' : 'warn', `invalid timestampUtc: ${report.timestampUtc}`, { path: filePath });
    return;
  }

  const ageHours = (Date.now() - timestampMs) / (60 * 60 * 1000);
  const details = {
    path: filePath,
    timestampUtc: report.timestampUtc,
    ageHours: Number(ageHours.toFixed(3)),
    maxReportAgeHours,
  };

  if (ageHours > maxReportAgeMs / (60 * 60 * 1000)) {
    add(name, requireRealIot ? 'fail' : 'warn', `report is stale: ${details.ageHours}h old`, details);
    return;
  }

  add(name, 'pass', `timestampUtc=${report.timestampUtc}`, details);
}

function checkIotEvidenceFreshness(filePath, evidence) {
  const timestampUtc = evidence.endedAtUtc || evidence.startedAtUtc;
  if (!timestampUtc) {
    add('Real IoT Smoke Evidence Freshness', requireRealIot ? 'fail' : 'warn', 'evidence timestamp is missing.', { path: filePath });
    return;
  }

  const timestampMs = Date.parse(timestampUtc);
  if (!Number.isFinite(timestampMs)) {
    add('Real IoT Smoke Evidence Freshness', requireRealIot ? 'fail' : 'warn', `invalid evidence timestamp: ${timestampUtc}`, { path: filePath });
    return;
  }

  const ageHours = (Date.now() - timestampMs) / (60 * 60 * 1000);
  const details = {
    path: filePath,
    timestampUtc,
    ageHours: Number(ageHours.toFixed(3)),
    maxIotEvidenceAgeHours,
  };

  if (ageHours > maxIotEvidenceAgeMs / (60 * 60 * 1000)) {
    add('Real IoT Smoke Evidence Freshness', requireRealIot ? 'fail' : 'warn', `evidence is stale: ${details.ageHours}h old`, details);
    return;
  }

  add('Real IoT Smoke Evidence Freshness', 'pass', `timestampUtc=${timestampUtc}`, details);
}

function manualEntryFreshness(entry) {
  const timestampUtc = entry.playedAtUtc;
  if (!timestampUtc) {
    return { fresh: false, ageHours: null };
  }

  const timestampMs = Date.parse(timestampUtc);
  if (!Number.isFinite(timestampMs)) {
    return { fresh: false, ageHours: null };
  }

  const ageHours = (Date.now() - timestampMs) / (60 * 60 * 1000);
  return {
    fresh: ageHours <= maxManualPlaytestAgeMs / (60 * 60 * 1000),
    ageHours: Number(ageHours.toFixed(3)),
  };
}

function reportDetails(filePath, report) {
  return {
    path: filePath,
    timestampUtc: report.timestampUtc || null,
    sceneName: report.sceneName || null,
    scenePath: report.scenePath || null,
    condition: report.condition || null,
    sessionId: report.sessionId || null,
    logPath: report.logPath || null,
    errorCount: report.errorCount ?? null,
    warningCount: report.warningCount ?? null,
    success: report.success ?? null,
  };
}

function summarizeIotEvidence(filePath, evidence) {
  return {
    path: filePath,
    sessionId: evidence.sessionId || null,
    success: evidence.success,
    startedAtUtc: evidence.startedAtUtc || null,
    endedAtUtc: evidence.endedAtUtc || null,
    health: evidence.health
      ? {
        status: evidence.health.status,
        simulation: evidence.health.simulation,
        token_set: evidence.health.token_set,
        device_light_set: evidence.health.device_light_set,
        device_fan_set: evidence.health.device_fan_set,
      }
      : null,
    error: evidence.error || null,
  };
}

function writeStatusEvidence(evidence) {
  fs.mkdirSync(logDir, { recursive: true });
  const safeTimestamp = evidence.startedAtUtc.replace(/[:.]/g, '-');
  const timestampedPath = path.join(logDir, `submission-status-${safeTimestamp}.json`);
  evidence.evidencePath = timestampedPath;
  evidence.latestEvidencePath = latestStatusPath;
  evidence.latestModeEvidencePath = latestModeStatusPath;
  const payload = JSON.stringify(evidence, null, 2);
  fs.writeFileSync(timestampedPath, payload);
  fs.writeFileSync(latestStatusPath, payload);
  fs.writeFileSync(latestModeStatusPath, payload);
  return timestampedPath;
}

function add(name, status, message, details = null) {
  checks.push({ name, status, message, details });
}

main().catch(err => {
  console.error(`[submission-status] failed: ${err.stack || err.message}`);
  process.exitCode = 1;
});
