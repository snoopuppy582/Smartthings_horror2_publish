# Codex Next Session Handoff

Date: 2026-06-08
Project: `Smartthing_server/Smartthings_horror2`
Branch: `main`

## Context

The project target is a 120-second experimental Unity horror loop, not a general-purpose horror game. It must let a participant start on 1F, understand the goal quickly, move to a 2F objective, experience controlled killer pressure, and end in success or timeout failure while logging experiment data.

The two experiment conditions are:

- `GameOnly`: no physical IoT stimulus required.
- `GameWithIoT`: SmartThings smart light plus smart plug fan.

The user wants the unnecessary multiplayer/template/server bloat kept out. Do not restore removed Mirror/MasterServer/multiplayer/template systems unless a specific required asset depends on one and there is no smaller fix.

## Current State

The automated Unity loop is currently green in local/simulated mode:

- Submission QA: `Temp/experiment_submission_qa.json`
  - latest timestamp: `2026-06-07T22:36:07.3143343Z`
  - `errorCount: 0`
  - `warningCount: 1`
  - Only warning: SmartThings server is in simulation mode because `.env` is missing.
- GameOnly PlayMode smoke: `Temp/experiment_playmode_smoke.json`
  - latest timestamp: `2026-06-07T22:36:28.0774213Z`
  - `success: true`
  - `errorCount: 0`
  - `warningCount: 0`
  - doorway capsule probe clear
  - stair route capsule probe clear from lower stair approach through 2F route
  - player lantern has a usable forward Spot Light
  - procedural horror ambience is playing from generated clip and reacts to timed scenario tension
  - external horror BGM/SFX layer starts on `MenuMusic01` and switches to `Hunter01Chase01Loop` during chase
  - primary old-house `MeshCollider` is enabled, and doorway clearance is handled by a collision gate instead of disabling the house collider
  - stair clearance is handled by `StairHouseCollisionGate_Auto` plus a converted legacy `Cube (1)` trigger instead of disabling the house collider
  - second-floor support raycasts hit solid walkable colliders
  - NavMesh path to player/objective OK
  - `KillerPlayerCollisionBypass` is active; latest runtime had no blocking killer colliders to ignore
  - timed 2-minute scenario log contains `ghost_hint`, `killer_near`, `blackout`, and `chase`
  - timeout failure and objective success paths verified
- GameWithIoT simulation smoke: `Temp/experiment_playmode_iot_smoke.json`
  - latest timestamp: `2026-06-07T22:36:49.1062158Z`
  - `success: true`
  - `errorCount: 0`
  - `warningCount: 0`
  - accepted Unity-to-server event requests verified for the timed scenario
  - IoT cooldown reset path verified

The previous `LanternController.DestroyObject(Object)` CS0108 compile warning has been removed by renaming the helper to `DestroyGeneratedObject(Object)`. `Assembly-CSharp.dll` was rebuilt after the source edit.

The remaining hard completion gap is real-device SmartThings verification:

- `SmartThings_server/.env` is not present.
- `/health` reports `simulation: true`, `status: degraded`, token/device IDs false.
- `npm run verify:iot` intentionally fails and writes failure evidence to `SmartThings_server/logs/real-iot-smoke-latest.json`.
- `npm run verify:submission:final` intentionally fails until real `.env` credentials and successful physical device smoke evidence exist.

Do not mark the overall goal complete until physical smart light/fan response is verified and `verify:submission:final` passes.

## Important Added Files

- `EXPERIMENT_GAME_PLAN.md`: experiment plan and current implementation notes.
- `SUBMISSION_READINESS.md`: canonical submission gates and manual playtest checklist.
- `CODEX_NEXT_SESSION_HANDOFF.md`: this file.
- `SmartThings_server/.env.example`: safe real-device configuration template.
- `SmartThings_server/src/realIotSmoke.js`: real IoT smoke runner with JSON evidence output.
- `SmartThings_server/scripts/manualPlaytestEvidence.js`: records manual GameOnly/GameWithIoT playtest evidence.
- `SmartThings_server/scripts/submissionStatus.js`: aggregates Unity reports, server health, real IoT evidence, report/evidence freshness, and writes local/final submission-status evidence.
- `IOT_TEAM_HANDOFF.md`: short setup sheet for the teammate connecting SmartThings tokens/device IDs and final evidence commands.

## Implemented Unity Runtime

- `ExperimentDirector`: 120-second session, `GameOnly`/`GameWithIoT`, timed scenario cues, success/failure, result UI, input lock/unlock, event dispatch, JSONL logging.
- `ExperimentLogger`: per-session JSON Lines logs under `Application.persistentDataPath/ExperimentLogs`.
- `ExperimentBootstrapper`: creates missing experiment runtime objects during Play.
- `ObjectiveItem`: 2F objective pickup immediately triggers `Mission Success`.
- `ExperimentProgressMarker`: route/progress event markers.
- `NonLethalHitFeedback`: red flash/vignette, camera shake, brief stun/audio hooks.
- `PlayerHealth`: experiment sessions use nonlethal `player_hit` logging instead of death.
- `SmartThingsEventSender`: sends `session_id`, `condition`, `elapsed_sec`, `hit_count`.
- `KillerAI`/`EnemyAI`: report `killer_near`, chase/attack, nonlethal hit, resume chase.
- `FirstPersonController`: tuned movement, input lock, step/doorway assist.
- `FootstepAnimationEventReceiver`: absorbs footstep animation events to reduce console noise.
- `LanternController`: auto-generates a held player lantern rig with forward Spot Light, fill Light, flicker, and scenario-event reactions.
- `ProceduralHorrorAmbience`: generates low horror drone/noise/heartbeat ambience without external audio assets and reacts to scenario events.
- `AmbientAudioManager`: layers local template BGM/SFX/heartbeat over the procedural bed, repairs missing child `AudioSource` objects, and switches chase/jump-scare music immediately on scenario events.
- `DoorwayHouseCollisionGate`: keeps `Old_House_windows_separated_Collider` enabled for old-house wall/floor collision, but temporarily ignores collision between the player and that mesh collider inside the doorway gate to prevent body-rubbing at the entrance.
- `StairHouseCollisionGate_Auto`: same collision-gate pattern for the stair route where the old-house mesh collider overlaps the player capsule path. The runtime/bootstrap/editor config now uses an explicit larger stair gate size/center.
- `KillerPlayerCollisionBypass`: keeps the killer visible and attack-capable while preventing any killer child collider from physically blocking the player route.
- `SecondFloorWalkableFloor_Auto` / `SecondFloorStairLanding_Auto`: invisible solid support colliders preventing 2F fall-through near the objective route.
- Legacy root `Cube (1)` stair blocker near `-27.6,2.0,-16.0` is converted to a trigger by scene prep/runtime bootstrap; it was a large vertical BoxCollider blocking the lower stair capsule route.

## Implemented Unity Editor/QA Tools

- `Tools > Experiment > Prepare Active Scene`
  - cleans missing scripts and blocking scene issues
  - ensures experiment manager/sender/director/UI/helpers
  - sets player/killer tuning
  - prepares objective route and rebuilds NavMesh
- `Tools > Validate Horror Scene Setup`
  - current QA report has no hard errors.
- `Tools > Experiment > Run Submission QA`
  - writes `Temp/experiment_submission_qa.json`.
- `Tools > Experiment > Run Play Mode Smoke Test (GameOnly)`
  - writes `Temp/experiment_playmode_smoke.json`.
  - verifies doorway clearance, 2F solid support, player lantern, procedural ambience playback/tension, killer route, timeout failure, objective success, and timed scenario cues.
- `Tools > Experiment > Run Play Mode Smoke Test (GameWithIoT Simulation)`
  - writes `Temp/experiment_playmode_iot_smoke.json`.
  - verifies the same scenario and requires accepted server requests for `ghost_hint`, `killer_near`, `blackout`, `chase`, `player_hit`, and `mission_success`.

## Implemented SmartThings Server

- Allowed/handled experiment events:
  - `game_start`
  - `ghost_hint`
  - `killer_near`
  - `player_hit`
  - `blackout`
  - `mission_success`
  - `mission_failed`
  - `recovery`
- Safety clamps:
  - blackout max `800ms`
  - fan-on max `5000ms`
  - light effect max `5000ms`
  - light level `20-100`
  - cooldown protection for repeated effects
  - automatic restore action where needed
- Priority handling prevents lower-priority cleanup from cutting off `player_hit` or final recovery.
- `/health`, `/event`, `/emergency-stop`, `/stats` are available.
- `realIotSmoke.js` sends `game_start`, `killer_near`, `player_hit`, `mission_success`, then `emergency-stop`.
- `manualPlaytestEvidence.js` records explicit GameOnly/GameWithIoT manual playtest evidence.
- `submissionStatus.js` separates local readiness from final readiness and requires manual playtest evidence for final submission.

## Current Validation

Run from workspace/repo context:

```powershell
node --check Smartthing_server\Smartthings_horror2\SmartThings_server\src\realIotSmoke.js
node --check Smartthing_server\Smartthings_horror2\SmartThings_server\scripts\submissionStatus.js
npm test --prefix Smartthing_server\Smartthings_horror2\SmartThings_server
npm run verify:submission --prefix Smartthing_server\Smartthings_horror2\SmartThings_server
git -C Smartthing_server diff --check
```

Latest observed results:

- `node --check` passed for both new scripts.
- `npm test`: latest observed `23 pass / 0 fail`.
- `Temp/experiment_submission_qa.json`: latest observed `errorCount: 0`, `warningCount: 1`; the only warning is SmartThings simulation mode.
- `Temp/experiment_playmode_smoke.json`: latest observed `success: true`, `errorCount: 0`, `warningCount: 0`; includes doorway capsule clearance, primary house mesh collider proof, player lantern, procedural ambience playback/tension, external BGM chase switch to `Hunter01Chase01Loop`, and 2F support proof.
- `Temp/experiment_playmode_iot_smoke.json`: latest observed `success: true`, `errorCount: 0`, `warningCount: 0`; includes the same local/audio proofs plus accepted timed-scenario server requests.
- `npm run verify:submission`: expected local status is warning-only while `.env` and manual evidence are missing. Latest observed: `6 pass, 4 warn, 0 fail`.
- `npm run verify:submission:final`: expected to fail until real `.env`, fresh no-warning Unity QA, fresh real IoT smoke evidence, and fresh manual playtest evidence exist. Latest observed: `6 pass, 0 warn, 4 fail`.
- `git diff --check`: no whitespace errors, only CRLF conversion warnings.

`verify:submission` and `verify:submission:final` write evidence to:

- `SmartThings_server/logs/submission-status-latest.json`
- `SmartThings_server/logs/submission-status-local-latest.json`
- `SmartThings_server/logs/submission-status-final-latest.json`
- `SmartThings_server/logs/submission-status-*.json`

Manual playtest evidence is stored in:

- `SmartThings_server/logs/manual-playtest-latest.json`
- `SmartThings_server/logs/manual-playtest-*.json`

## MCP Unity Status

Unity MCP configuration:

- `ProjectSettings/McpUnitySettings.json`
  - port `8090`
  - `RequestTimeoutSeconds: 120`
  - `AutoStartServer: true`
  - `EnableInfoLogs: false`
  - `AllowRemoteConnections: true`
- Unity process is listening on `127.0.0.1:8090`.
- Editor log shows the MCP WebSocket server starts successfully.
- `.codex/config.toml`, project `.mcp.json`, workspace `.mcp.json`, and the user global Codex config now include `cwd` pointing at the Unity project root plus longer MCP startup/tool timeouts.
- `scripts/mcp-unity-health.ps1` checks Node, npm, MCP settings, config `cwd`, package path, and port `8090`.

Current issue:

- Codex MCP tool calls return `Transport closed`.
- Editor log shows MCP WebSocket clients disconnecting.
- Treat this as MCP transport instability, not a gameplay code failure.
- MCP Unity stops its Unity WebSocket server when Unity exits Edit Mode to enter PlayMode, so direct MCP use during PlayMode is not a reliable validation path for this package.

Recommended MCP recovery:

1. Keep Unity out of Play Mode.
2. In Unity, open `Tools > MCP Unity > Server Window`.
3. Stop/start the MCP server.
4. Run `powershell -ExecutionPolicy Bypass -File Smartthings_horror2\scripts\mcp-unity-health.ps1` from `Smartthing_server`.
5. Start a fresh Codex session from the Unity project root or `Smartthing_server`.
6. First call `get_console_logs` in Edit Mode only.

Do not rely on MCP to drive PlayMode. Use `Tools > Experiment > Run Play Mode Smoke Test (...)` and the generated JSON reports for PlayMode verification.

## Remaining Issues

1. Real SmartThings physical verification
   - Add `SmartThings_server/.env` from `.env.example`.
   - Restart the Node server.
   - Run `npm run verify:iot --prefix Smartthings_horror2\SmartThings_server`.
   - Confirm the actual smart light and fan respond safely.
   - Run `npm run verify:submission:final --prefix Smartthings_horror2\SmartThings_server`.

2. Manual feel pass
   - Automated smoke verifies route/path/collision/UI/log conditions, but a human should still play once in each condition.
   - Check that movement through doors and stairs feels natural.
   - Check killer pressure is visible but not unfair.
   - Check objective visibility and final UI readability.
   - Record evidence with:
     - `npm run manual:init --prefix Smartthings_horror2\SmartThings_server`
     - `npm run manual:record --prefix Smartthings_horror2\SmartThings_server -- --condition GameOnly --tester "name" --all-pass --notes "checked"`
     - `npm run manual:record --prefix Smartthings_horror2\SmartThings_server -- --condition GameWithIoT --tester "name" --all-pass --notes "checked"`

3. MCP transport
   - Useful for Edit Mode hierarchy/console inspection, but currently unstable from Codex.
   - Do not block gameplay verification on MCP if automated Unity reports and manual playtest are available.

4. Dirty worktree
   - The tree has large intentional deletions and many Unity-generated modifications.
   - Do not revert unrelated deletions or generated assets without explicit user instruction.
   - `Assets/Scenes/MainScene.unity` currently shows as binary in git diff, so use Unity reports and manual playtest as scene evidence.

## Next Actions

1. If real devices are available, create `.env`, restart server, run `verify:iot`, then `verify:submission:final`.
2. Re-run Unity menus if the scene changes:
   - `Tools > Experiment > Run Submission QA`
   - `Tools > Experiment > Run Play Mode Smoke Test (GameOnly)`
   - `Tools > Experiment > Run Play Mode Smoke Test (GameWithIoT Simulation)`
3. Do one manual GameOnly playthrough and one GameWithIoT playthrough.
4. If MCP is needed, fix the transport in Edit Mode first; otherwise rely on generated Unity JSON reports.
5. After real IoT and manual pass, update `SUBMISSION_READINESS.md` with final evidence and then the goal can be marked complete.

## Useful Commands

From `Smartthing_server`:

```powershell
npm test --prefix Smartthings_horror2\SmartThings_server
npm run verify:submission --prefix Smartthings_horror2\SmartThings_server
npm run verify:iot --prefix Smartthings_horror2\SmartThings_server
npm run manual:init --prefix Smartthings_horror2\SmartThings_server
npm run manual:record --prefix Smartthings_horror2\SmartThings_server -- --condition GameOnly --tester "name" --all-pass --notes "checked"
npm run manual:record --prefix Smartthings_horror2\SmartThings_server -- --condition GameWithIoT --tester "name" --all-pass --notes "checked"
npm run verify:submission:final --prefix Smartthings_horror2\SmartThings_server
git diff --check
```

Unity menus:

```text
Tools > Experiment > Prepare Active Scene
Tools > Validate Horror Scene Setup
Tools > Experiment > Run Submission QA
Tools > Experiment > Run Play Mode Smoke Test (GameOnly)
Tools > Experiment > Run Play Mode Smoke Test (GameWithIoT Simulation)
```

## Do Not Forget

- Keep SmartThings token out of committed docs and Unity scripts.
- If the deleted plaintext token in the old root `CLAUDE.md` was real, revoke it.
- The experiment device scope is smart light plus smart plug fan only.
- Final completion requires real physical IoT evidence, not just simulation.
