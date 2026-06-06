// scripts/test-notification.js
// SmartThings Notification API로 Galaxy Note 8 (같은 Location에 등록된 SmartThings 앱)에
// 푸시 알림을 1회 전송하여 통신 경로가 정상인지 확인한다.
//
// 사전 조건:
//   - .env에 SMARTTHINGS_TOKEN, SMARTTHINGS_LOCATION_ID 모두 설정
//   - Note 8에 SmartThings 앱 설치 + 동일 Samsung 계정 로그인
//   - Note 8의 알림 권한 ON, 무음모드 해제 권장
//
// 실행: node scripts/test-notification.js [강도]
//   강도 인자(선택): hint | near | chase  (기본: near)

require("dotenv").config();
const axios = require("axios");

const TOKEN = process.env.SMARTTHINGS_TOKEN;
const LOCATION_ID = process.env.SMARTTHINGS_LOCATION_ID;
const API_BASE = "https://api.smartthings.com/v1";

// ─── 사전 점검 ──────────────────────────────────────────────
if (!TOKEN || !LOCATION_ID) {
  console.error("❌ .env에 SMARTTHINGS_TOKEN과 SMARTTHINGS_LOCATION_ID가 모두 필요합니다.");
  process.exit(1);
}

// ─── 강도별 메시지 (보고서 이벤트 매핑) ─────────────────────
const MESSAGES = {
  hint: {
    title: "👻 무언가 다가오는 기척",
    body: "어둠 속에서 발자국 소리가 들린다...",
  },
  near: {
    title: "⚠️ 귀신이 가까워졌다",
    body: "차가운 기운이 등 뒤에서 다가온다...",
  },
  chase: {
    title: "🏃 도망쳐!",
    body: "Undead Horse Knight가 당신을 쫓고 있다!",
  },
};

// CLI 인자로 강도 선택
const level = (process.argv[2] || "near").toLowerCase();
const msg = MESSAGES[level];
if (!msg) {
  console.error(`❌ 알 수 없는 강도: '${level}'. 사용 가능: hint | near | chase`);
  process.exit(1);
}

// ─── SmartThings Notification API 페이로드 ─────────────────
// type: ALERT | SUGGESTED_ACTION | EVENT_LOGGING | AUTOMATION_INFO
// messages 배열의 각 객체는 locale별 메시지를 담는다.
const payload = {
  locationId: LOCATION_ID,
  type: "ALERT",
  messages: [
    {
      default: {
        title: msg.title,
        body: msg.body,
      },
      ko_KR: {
        title: msg.title,
        body: msg.body,
      },
    },
  ],
};

// ─── 전송 ─────────────────────────────────────────────────
(async () => {
  console.log(`📨 테스트 알림 전송: [${level.toUpperCase()}]`);
  console.log(`   제목: ${msg.title}`);
  console.log(`   본문: ${msg.body}\n`);

  const startTime = Date.now();
  try {
    const res = await axios.post(`${API_BASE}/notification`, payload, {
      headers: {
        Authorization: `Bearer ${TOKEN}`,
        "Content-Type": "application/json",
        Accept: "application/json",
      },
      timeout: 10000,
    });
    const elapsed = Date.now() - startTime;

    console.log(`✅ HTTP ${res.status} (${elapsed}ms)`);
    if (res.data && Object.keys(res.data).length > 0) {
      console.log("   응답:", JSON.stringify(res.data, null, 2));
    }
    console.log("");
    console.log("👉 Note 8을 확인하세요. 보통 1~5초 안에 알림이 뜹니다.");
    console.log("   ▸ 잠금화면 / 상단바 / SmartThings 앱 알림 탭에서 확인 가능");
    console.log("   ▸ 안 뜨면: 무음모드 해제, 알림 권한 ON, 앱 강제종료 안 되었는지 확인");
  } catch (err) {
    const elapsed = Date.now() - startTime;

    if (err.response) {
      const { status, statusText, data } = err.response;
      console.error(`❌ HTTP ${status} ${statusText} (${elapsed}ms)`);
      console.error("   응답:", JSON.stringify(data, null, 2));

      if (status === 401) {
        console.error("\n💡 401 = 토큰 유효하지 않음. PAT 재확인.");
      } else if (status === 403) {
        console.error("\n💡 403 = 권한 부족. PAT에 w:notification 스코프가 있는지 확인.");
        console.error("   https://account.smartthings.com/tokens 에서 토큰 정보 확인 가능.");
      } else if (status === 422) {
        console.error("\n💡 422 = 페이로드 형식 오류. locationId가 정확한지 확인.");
      } else if (status === 429) {
        console.error("\n💡 429 = Rate limit 초과. 1분 후 재시도.");
      }
    } else {
      console.error("❌ 요청 실패:", err.message);
    }
    process.exit(1);
  }
})();
