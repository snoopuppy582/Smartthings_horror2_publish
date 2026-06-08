/**
 * 실험 전 1회 실행: Gemini structured output으로 effect plan 생성 → safetyFilter 검증 → plans/ 저장
 * 실행: node authoring/generatePlans.js
 * 런타임 경로에서 절대 import 하지 않는다.
 */
require('dotenv').config({ path: require('path').join(__dirname, '..', '.env') });
const fs = require('fs');
const path = require('path');

const PLANS_DIR = path.join(__dirname, '..', 'plans');
const GEMINI_API_KEY = process.env.GEMINI_API_KEY;

if (!GEMINI_API_KEY) {
  console.error('[authoring] GEMINI_API_KEY 없음 → .env에 설정 후 실행하세요.');
  process.exit(1);
}

// scene context 목록 (구조화된 입력, 이미지 아님)
const SCENE_CONTEXTS = [
  {
    scene_id: 'ghost_hint',
    event_type: 'ambient',
    tension_level: 2,
    player_state: 'exploring',
    recent_effects: [],
    available_devices: ['smart_light', 'fan_plug'],
    safety_rules: { max_blackout_ms: 800, max_fan_on_ms: 5000, no_strobe: true, restore_required: true },
  },
  {
    scene_id: 'ghost_near',
    event_type: 'proximity',
    tension_level: 4,
    player_state: 'threatened',
    recent_effects: ['ghost_hint'],
    available_devices: ['smart_light', 'fan_plug'],
    safety_rules: { max_blackout_ms: 800, max_fan_on_ms: 5000, no_strobe: true, restore_required: true },
  },
  {
    scene_id: 'blackout',
    event_type: 'environment',
    tension_level: 3,
    player_state: 'disoriented',
    recent_effects: [],
    available_devices: ['smart_light', 'fan_plug'],
    safety_rules: { max_blackout_ms: 800, max_fan_on_ms: 5000, no_strobe: true, restore_required: true },
  },
  {
    scene_id: 'chase',
    event_type: 'chase',
    tension_level: 5,
    player_state: 'fleeing',
    recent_effects: ['ghost_near'],
    available_devices: ['smart_light', 'fan_plug'],
    safety_rules: { max_blackout_ms: 800, max_fan_on_ms: 5000, no_strobe: true, restore_required: true },
  },
  {
    scene_id: 'jump_scare',
    event_type: 'jumpscare',
    tension_level: 5,
    player_state: 'shocked',
    recent_effects: [],
    available_devices: ['smart_light', 'fan_plug'],
    safety_rules: { max_blackout_ms: 800, max_fan_on_ms: 5000, no_strobe: true, restore_required: true },
  },
  {
    scene_id: 'recovery',
    event_type: 'recovery',
    tension_level: 0,
    player_state: 'safe',
    recent_effects: [],
    available_devices: ['smart_light', 'fan_plug'],
    safety_rules: { max_blackout_ms: 800, max_fan_on_ms: 5000, no_strobe: true, restore_required: true },
  },
];

// Gemini JSON Schema (structured output)
const RESPONSE_SCHEMA = {
  type: 'object',
  properties: {
    plan_id: { type: 'string' },
    description: { type: 'string' },
    actions: {
      type: 'array',
      items: {
        type: 'object',
        properties: {
          device: { type: 'string', enum: ['smart_light', 'fan_plug', 'all'] },
          type: { type: 'string', enum: ['setLevel', 'setSwitch', 'flash', 'restore'] },
          value: { type: 'string' },
          level: { type: 'number' },
          duration_ms: { type: 'number' },
          repeat: { type: 'number' },
        },
        required: ['type'],
      },
    },
  },
  required: ['plan_id', 'actions'],
};

async function callGemini(sceneContext) {
  const fetch = (await import('node-fetch')).default;
  const prompt = `당신은 공포 게임 IoT 연출 설계자입니다.
아래 scene context를 보고, SmartThings 기기(스마트 조명, 선풍기 플러그)를 제어하는 effect plan을 JSON으로 생성하세요.
반드시 safety_rules를 준수하고, 마지막 action은 반드시 restore여야 합니다.

Scene Context:
${JSON.stringify(sceneContext, null, 2)}`;

  const res = await fetch(
    `https://generativelanguage.googleapis.com/v1beta/models/gemini-1.5-flash:generateContent?key=${GEMINI_API_KEY}`,
    {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({
        contents: [{ parts: [{ text: prompt }] }],
        generationConfig: {
          responseMimeType: 'application/json',
          responseSchema: RESPONSE_SCHEMA,
        },
      }),
    }
  );

  if (!res.ok) {
    const err = await res.text();
    throw new Error(`Gemini API 오류 ${res.status}: ${err}`);
  }

  const data = await res.json();
  const text = data?.candidates?.[0]?.content?.parts?.[0]?.text;
  return JSON.parse(text);
}

async function validateWithSafetyFilter(plan, sceneId) {
  // safetyFilter를 직접 호출해 검증
  process.env.SMARTTHINGS_TOKEN = process.env.SMARTTHINGS_TOKEN || 'authoring_dummy';
  process.env.DEVICE_ID_LIGHT = process.env.DEVICE_ID_LIGHT || 'authoring_dummy';
  process.env.DEVICE_ID_FAN = process.env.DEVICE_ID_FAN || 'authoring_dummy';

  const { filterPlan, resetCooldowns } = require('../src/safetyFilter');
  resetCooldowns();
  const result = filterPlan(sceneId, plan);
  return result;
}

async function main() {
  if (!fs.existsSync(PLANS_DIR)) fs.mkdirSync(PLANS_DIR, { recursive: true });

  let saved = 0;
  let failed = 0;

  for (const ctx of SCENE_CONTEXTS) {
    console.log(`[authoring] 생성 중: ${ctx.scene_id}`);
    try {
      const plan = await callGemini(ctx);
      plan.plan_id = ctx.scene_id; // scene_id를 plan_id로 통일

      const { allowed, actions, reason } = await validateWithSafetyFilter(plan, ctx.scene_id);
      if (!allowed) {
        console.warn(`[authoring] 검증 실패 (${ctx.scene_id}): ${reason} → fallback 사용`);
        failed++;
        continue;
      }

      plan.actions = actions;
      const outPath = path.join(PLANS_DIR, `${ctx.scene_id}.json`);
      fs.writeFileSync(outPath, JSON.stringify(plan, null, 2));
      console.log(`[authoring] 저장 완료: ${outPath}`);
      saved++;

      // rate limit 방지
      await new Promise(r => setTimeout(r, 1000));
    } catch (err) {
      console.error(`[authoring] 오류 (${ctx.scene_id}):`, err.message);
      failed++;
    }
  }

  console.log(`\n[authoring] 완료: 저장 ${saved}개 / 실패 ${failed}개`);
}

main();
