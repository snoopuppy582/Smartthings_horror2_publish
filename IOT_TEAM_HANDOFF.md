# IoT Team Handoff

This project uses Unity only as an event producer. SmartThings tokens and device IDs stay on the Node server.

## Runtime Flow

1. Unity `SmartThingsEventSender` posts JSON to `http://localhost:3000/event`.
2. The Node server validates the event name and maps it to safe light/fan actions.
3. In simulation mode, the server logs accepted plans without controlling devices.
4. In real-device mode, the server sends commands to SmartThings and writes evidence logs.

## Required Local Server Setup

Create `SmartThings_server/.env` from `.env.example`:

```env
PORT=3000
IOT_SIMULATION=0
SMARTTHINGS_TOKEN=...
DEVICE_ID_LIGHT=...
DEVICE_ID_FAN=...
```

Use `IOT_SIMULATION=1` when developing without physical devices.

Run from `Smartthings_horror2`:

```powershell
npm install --prefix SmartThings_server
npm start --prefix SmartThings_server
```

Health check:

```powershell
Invoke-RestMethod http://127.0.0.1:3000/health
```

Final real-device smoke:

```powershell
npm run verify:iot --prefix SmartThings_server
npm run verify:submission:final --prefix SmartThings_server
```

## Unity Events

Unity currently emits these experiment events:

- `game_start`
- `ghost_hint`
- `killer_near`
- `blackout`
- `chase`
- `player_hit`
- `mission_success`
- `mission_failed`
- `recovery`

Payload fields sent by Unity:

```json
{
  "event_id": "killer_near",
  "timestamp": 42.5,
  "session_id": "session-id",
  "condition": "GameWithIoT",
  "elapsed_sec": 42.5,
  "hit_count": 0
}
```

## Safety Limits

Current server clamps are in `SmartThings_server/src/config.js`:

- blackout max: `800ms`
- fan-on max: `5000ms`
- light effect max: `5000ms`
- light level range: `20-100`
- repeated effect cooldown: `15000ms`

Use `/emergency-stop` after manual or automated real-device tests.

```powershell
Invoke-RestMethod -Method Post http://127.0.0.1:3000/emergency-stop
```

## Evidence Files

Real IoT smoke writes:

- `SmartThings_server/logs/real-iot-smoke-latest.json`
- `SmartThings_server/logs/real-iot-smoke-*.json`

Final submission status writes:

- `SmartThings_server/logs/submission-status-final-latest.json`
- `SmartThings_server/logs/submission-status-latest.json`

Do not commit `.env` or any copied token/device secret.
