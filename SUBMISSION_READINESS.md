# Submission Readiness

This project currently passes the automated GameOnly and simulated GameWithIoT checks, including the timed 120-second scenario cues, primary old-house collider, doorway/stair clearance gates, synthetic WASD traversal to 2F, visible KILLER placement, player lantern, procedural horror ambience, external template BGM/SFX, and second-floor support checks. Real SmartThings device verification still requires a local `.env` file with valid device credentials. IoT teammate setup is summarized in `IOT_TEAM_HANDOFF.md`.

## Current Completion Targets

The local build is considered submission-ready, excluding real-device evidence, when all of these are true:

- Unity compiles without project C# warnings from experiment code.
- Unity Submission QA reports `errorCount: 0`; the only allowed warning is missing real SmartThings credentials during simulation.
- GameOnly PlayMode smoke reports `success: true`, `errorCount: 0`, `warningCount: 0`.
- GameWithIoT simulation smoke reports `success: true`, `errorCount: 0`, `warningCount: 0`, and accepted server requests for timed scenario events.
- Doorway movement is verified by capsule probe, while the old-house mesh collider stays enabled for walls and floors.
- Stair movement is verified by capsule probe and synthetic FirstPersonController WASD movement over solid ramp colliders from the lower stair approach through the landing to the 2F objective route.
- 2F route and objective area have solid support colliders and do not fall through.
- Killer setup includes visible renderers, deterministic placement near the house, player collision bypass, slow chase pacing around 60% of player walk speed, and vertical relocation guard so it cannot physically block the stair route or relocate onto a different floor during forced chase.
- Player lantern, procedural ambience, external exploration BGM, and chase BGM are verified in smoke reports.
- Killer pathing reaches the player start and 2F objective route without NavMesh failure.
- Timeout failure, objective success, result UI, and JSONL experiment logs are verified.

Final submission still additionally requires physical SmartThings smoke evidence and manual playtest evidence for both conditions.

## Automated Gates

Run these from `Smartthing_server`:

```powershell
npm test --prefix Smartthings_horror2\SmartThings_server
npm run verify:submission --prefix Smartthings_horror2\SmartThings_server
git diff --check
```

If Unity MCP is needed for Edit Mode inspection, first run:

```powershell
powershell -ExecutionPolicy Bypass -File Smartthings_horror2\scripts\mcp-unity-health.ps1
```

Run these from Unity after opening `Assets/Scenes/MainScene.unity`:

```text
Tools > Experiment > Run Submission QA
Tools > Experiment > Run Play Mode Smoke Test (GameOnly)
Tools > Experiment > Run Play Mode Smoke Test (GameWithIoT Simulation)
```

Expected reports:

- `Temp/experiment_submission_qa.json`: `errorCount` is `0`.
- `Temp/experiment_playmode_smoke.json`: `success` is `true`, `errorCount` is `0`, and the report confirms doorway capsule clearance, stair route capsule clearance, synthetic WASD traversal to 2F, primary house mesh collider, visible KILLER placement, killer collision bypass, player lantern, procedural ambience playback/tension, external BGM switching to chase music, 2F support colliders, and timed events `ghost_hint`, `killer_near`, `blackout`, and `chase`.
- `Temp/experiment_playmode_iot_smoke.json`: `success` is `true`, `errorCount` is `0`, and the report confirms the same local traversal/audio/killer checks plus accepted server requests for the timed scenario.

The QA report may warn that the SmartThings server is in simulation mode until real device credentials are configured.

`npm run verify:submission` also writes a machine-readable evidence bundle to `SmartThings_server/logs/submission-status-local-latest.json`, `SmartThings_server/logs/submission-status-latest.json`, plus a timestamped `submission-status-*.json` file.

By default, submission verification treats Unity reports, real IoT smoke evidence, and manual playtest evidence as fresh for 24 hours. Override with `SUBMISSION_REPORT_MAX_AGE_HOURS`, `REAL_IOT_EVIDENCE_MAX_AGE_HOURS`, and `MANUAL_PLAYTEST_MAX_AGE_HOURS` when a stricter or looser local audit window is needed.

## Real SmartThings Device Gate

Create `SmartThings_server/.env` from `SmartThings_server/.env.example`:

```env
PORT=3000
IOT_SIMULATION=0
SMARTTHINGS_TOKEN=...
DEVICE_ID_LIGHT=...
DEVICE_ID_FAN=...
```

Restart the Node server, then run:

```powershell
npm run verify:iot --prefix Smartthings_horror2\SmartThings_server
npm run verify:submission:final --prefix Smartthings_horror2\SmartThings_server
```

This sends the controlled sequence `game_start`, `killer_near`, `player_hit`, `mission_success`, then calls `emergency-stop`. Confirm the physical smart light and fan respond safely and the script exits with `[real-iot-smoke] ok`.

The real IoT smoke writes evidence to `SmartThings_server/logs/real-iot-smoke-latest.json`. The final submission verifier fails until that evidence exists, was produced while `/health` reported `simulation=false`, and the latest smoke run succeeded.

`npm run verify:submission:final` writes `SmartThings_server/logs/submission-status-final-latest.json` and the common latest evidence file, but treats Unity QA warnings, stale Unity reports, stale real IoT smoke evidence, missing real-device credentials, failed real IoT smoke evidence, and missing manual playtest evidence as final gate failures.

## Manual Playtest

Before submission, play once in `GameOnly` and once in `GameWithIoT`:

- Start on 1F and understand the objective within 5 seconds.
- Enter the house without jumping or rubbing against the doorway.
- Try pushing into the house walls near the entrance and 1F/2F route; the old-house mesh collider should remain solid.
- Reach the 2F objective route and stand near the stair landing/objective without falling through the floor.
- Walk up the stair route normally with WASD; there should be no scripted lift or sudden Y-axis snap.
- Confirm the held lantern gives enough forward visibility without making the whole map bright.
- Confirm low horror ambience is audible and grows during ghost/killer/chase beats.
- Confirm external BGM/SFX starts during exploration and switches into chase music when the scenario escalates.
- Confirm killer pressure/chase is visible, slow enough to read, and not instant unfair contact.
- Confirm hit feedback is nonlethal and logs `player_hit`.
- Confirm the 120-second scenario exposes multiple IoT beats: ghost hint, killer pressure, blackout, and chase.
- Collect the 2F objective and see `Mission Success`.
- Let a run time out and see `Time Over`.
- Confirm JSONL logs are written under `Application.persistentDataPath/ExperimentLogs`.

Initialize the manual playtest evidence file:

```powershell
npm run manual:init --prefix Smartthings_horror2\SmartThings_server
```

After each real playthrough, record the result:

```powershell
npm run manual:record --prefix Smartthings_horror2\SmartThings_server -- --condition GameOnly --tester "name" --all-pass --notes "movement, killer chase, objective, UI, logs checked"
npm run manual:record --prefix Smartthings_horror2\SmartThings_server -- --condition GameWithIoT --tester "name" --all-pass --notes "IoT light/fan stimulus observed during play"
```

This writes `SmartThings_server/logs/manual-playtest-latest.json`. The final submission verifier requires both conditions to pass, and `GameWithIoT` requires `iotStimulusObserved`.
