# SmartThings Horror Experiment

Unity 기반 2분 공포 체험과 SmartThings 전등/콘센트 반응을 연결한 실험용 게임 프로젝트입니다. 목표는 상업용 게임 완성이 아니라, 초심자가 짧은 시간 안에 이동, 추격, 피격, 암전, 목표 회수, IoT 자극을 안정적으로 경험하게 하는 것입니다.

## Current State

- Unity project: `Smartthings_horror2`
- Main scene: `Assets/Scenes/MainScene.unity`
- Unity version: `6000.3.17f1`
- Node server: `SmartThings_server`
- Play length: 120 seconds
- Objective: recover the 2F device part
- Conditions: `GameOnly`, `GameWithIoT`
- Current local status: automated Unity QA and PlayMode smokes pass in both conditions. The reports verify synthetic WASD traversal from the lower stair route to 2F on solid ramp colliders, visible `KILLER` placement, lantern visibility, horror ambience/BGM, and timed scenario events. Real SmartThings evidence still requires `.env` credentials and a physical-device smoke run.

## Core Runtime Flow

1. Unity starts `ExperimentDirector` on Play Mode.
2. `BeginSession()` starts the 120-second timer and emits `game_start`.
3. Timed scenario cues fire at fixed offsets: `ghost_hint`, `killer_near`, `blackout`, `chase`, `killer_near`.
4. The player moves through the house to the 2F objective.
5. Killer hits are nonlethal. They trigger screen/audio feedback and `player_hit`, then the killer backs off before re-engaging.
6. In `GameWithIoT`, Unity posts events to the local Node server.
7. The Node server safety-filter maps events to SmartThings light/fan actions.
8. Collecting the objective ends with `Mission Success`; timeout ends with `Time Over`.

## Important Scripts

- `Assets/Scripts/Experiment/ExperimentDirector.cs`: session timer, condition, scenario cues, success/fail logs.
- `Assets/Scripts/KillerAI.cs`: NavMesh chase, attack validation, hit cooldown, post-hit backoff.
- `Assets/Scripts/Player/FirstPersonController.cs`: first-person movement, step/doorway assist, current move direction.
- `Assets/Scripts/Experiment/StairTraversalAssistZone.cs`: stair-route house-mesh collision bypass only; it does not lift or move the player.
- `Assets/Scripts/Game/ExperimentSceneTools.cs`: Unity editor tools for scene preparation and QA.
- `Assets/Scripts/Experiment/ExperimentBootstrapper.cs`: runtime fallback setup if required scene helpers are missing.
- `Assets/Scripts/Experiment/ExperimentPlayModeSmokeRunner.cs`: automated PlayMode smoke checks.
- `SmartThings_server/src/planLibrary.js`: event-to-device action plans.
- `SmartThings_server/src/safetyFilter.js`: allowlist, duration clamps, cooldowns, restore actions.

## Open And Prepare The Unity Scene

1. Open this folder in Unity Hub: `Smartthings_horror2`.
2. Open `Assets/Scenes/MainScene.unity`.
3. In Unity, run:

```text
Tools > Experiment > Prepare Active Scene
Tools > Validate Horror Scene Setup
Tools > Experiment > Run Submission QA
Tools > Experiment > Run Play Mode Smoke Test (GameOnly)
Tools > Experiment > Run Play Mode Smoke Test (GameWithIoT Simulation)
```

Expected Unity reports:

- `Temp/experiment_submission_qa.json`: `errorCount = 0`
- `Temp/experiment_playmode_smoke.json`: `success = true`, including `Synthetic WASD player route reached 2F using FirstPersonController`
- `Temp/experiment_playmode_iot_smoke.json`: `success = true`, including accepted Unity-to-server timed scenario requests

The submission QA may warn that SmartThings is in simulation mode until real device credentials are configured.

## Run The SmartThings Server

From the `Smartthings_horror2` folder:

```powershell
npm install --prefix SmartThings_server
npm start --prefix SmartThings_server
```

Health check:

```powershell
Invoke-RestMethod http://127.0.0.1:3000/health
```

For development without devices, use simulation mode in `SmartThings_server/.env`:

```env
PORT=3000
IOT_SIMULATION=1
SMARTTHINGS_TOKEN=
DEVICE_ID_LIGHT=
DEVICE_ID_FAN=
```

For real devices, copy `SmartThings_server/.env.example` to `.env` and set:

```env
PORT=3000
IOT_SIMULATION=0
SMARTTHINGS_TOKEN=...
DEVICE_ID_LIGHT=...
DEVICE_ID_FAN=...
```

Never commit `.env`.

## Validation Commands

From `Smartthings_horror2`:

```powershell
npm test --prefix SmartThings_server
npm run verify:submission --prefix SmartThings_server
git diff --check
```

Real-device gate:

```powershell
npm run verify:iot --prefix SmartThings_server
npm run verify:submission:final --prefix SmartThings_server
```

Manual playtest evidence:

```powershell
npm run manual:init --prefix SmartThings_server
npm run manual:record --prefix SmartThings_server -- --condition GameOnly --tester "name" --all-pass --notes "movement, killer chase, objective, UI, logs checked"
npm run manual:record --prefix SmartThings_server -- --condition GameWithIoT --tester "name" --all-pass --notes "IoT light/fan stimulus observed during play"
```

## Killer And IoT Pacing

The killer is tuned for readable IoT feedback instead of constant punishment:

- Hit cooldown: 8 seconds
- Attack recovery: 1.6 seconds
- Post-hit backoff: 3.2 meters for at least 1.8 seconds
- Killer-near report interval: 18 seconds
- Walk/chase speed: `1.05m/s` patrol, `1.75m/s` chase, about 60% of the player's `2.9m/s` walk speed.
- Hits are nonlethal during the experiment and are logged as `player_hit`.

This gives the player enough time to feel Smart LED/fan responses before the next attack can occur.

## Collision And 2F Route

The old-house mesh collider is kept enabled for walls/floors, but the doorway and stair route use narrow runtime gates so the player does not need to jump or rub against invisible geometry. `Prepare Active Scene` and `ExperimentBootstrapper` both create/maintain:

- `OldHouseInterior*_Auto` shell colliders for the visible house interior.
- `SecondFloorAccessRamp_Auto` and `SecondFloorAccessRamp_Landing_Auto` as solid walkable stair-ramp colliders.
- `SecondFloorWalkableFloor_Auto` and `SecondFloorStairBridge_Auto` for the 2F route.
- `StairTraversalAssistZone_Auto` to bypass only the broad old-house mesh collider while the player is already moving through the stair route.
- `KILLER` placement near the house with visible renderers and player collision bypass.

## Current Known Warnings

- Local submission verification warns if SmartThings is in simulation mode.
- Final submission verification fails until real IoT smoke evidence and manual playtest evidence are recorded.
- Unity MCP direct transport may be unstable in this environment. The project includes flag-based editor tools as a reliable fallback.

## Continue Work

Recommended next steps:

1. Run the five Unity menu actions listed above after opening `MainScene`.
2. Play one manual `GameOnly` run and confirm stair/door movement, lantern visibility, killer pacing, objective success, and timeout failure.
3. Start the Node server in simulation mode and play one `GameWithIoT` run.
4. Configure real SmartThings credentials and run `verify:iot`.
5. Record manual playtest evidence for both conditions.
6. If the killer feels too aggressive, adjust only `KillerAI.ConfigureForExperimentDefaults()` and rerun both PlayMode smoke tests.

More detailed docs:

- `EXPERIMENT_GAME_PLAN.md`
- `IOT_TEAM_HANDOFF.md`
- `SUBMISSION_READINESS.md`
- `CODEX_NEXT_SESSION_HANDOFF.md`
