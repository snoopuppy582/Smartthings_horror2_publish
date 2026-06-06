// scripts/list-locations.js
// SmartThings 계정에 등록된 Location 목록을 조회한다.
// 이 스크립트는 한 번만 실행해서 locationId를 얻은 뒤,
// 결과를 .env의 SMARTTHINGS_LOCATION_ID에 저장하기 위한 셋업용 도구다.
//
// 실행 위치: 레포 루트
// 실행 명령: node scripts/list-locations.js

require("dotenv").config();
const axios = require("axios");

const TOKEN = process.env.SMARTTHINGS_TOKEN;
const API_BASE = "https://api.smartthings.com/v1";

// ─── 사전 점검 ──────────────────────────────────────────────
if (!TOKEN) {
  console.error("❌ .env 파일에 SMARTTHINGS_TOKEN이 없습니다.");
  console.error("   레포 루트에 .env가 있는지, 토큰이 채워졌는지 확인하세요.");
  process.exit(1);
}

if (TOKEN.length < 20) {
  console.error("❌ SMARTTHINGS_TOKEN이 너무 짧습니다. PAT가 제대로 복사됐는지 확인하세요.");
  process.exit(1);
}

// ─── Location 조회 ─────────────────────────────────────────
(async () => {
  try {
    console.log("🔍 SmartThings Location 목록 조회 중...\n");

    const res = await axios.get(`${API_BASE}/locations`, {
      headers: {
        Authorization: `Bearer ${TOKEN}`,
        Accept: "application/json",
      },
      timeout: 10000,
    });

    const items = res.data.items || [];

    if (items.length === 0) {
      console.log("⚠️  등록된 Location이 없습니다.");
      console.log("   Note 8의 SmartThings 앱에서 '집(Home)'을 먼저 생성하세요.");
      return;
    }

    console.log(`✅ ${items.length}개의 Location을 찾았습니다.\n`);
    console.log("─".repeat(70));

    items.forEach((loc, i) => {
      console.log(`[${i + 1}] ${loc.name}`);
      console.log(`    locationId : ${loc.locationId}`);
      console.log(`    국가코드   : ${loc.countryCode || "-"}`);
      console.log(`    타임존     : ${loc.timeZoneId || "-"}`);
      console.log("");
    });

    console.log("─".repeat(70));
    console.log("👉 위 locationId 중 사용할 항목을 .env에 추가하세요:");
    console.log("");
    console.log(`    SMARTTHINGS_LOCATION_ID=${items[0].locationId}`);
    console.log("");
    console.log("   (보통 Location은 1개입니다. 여러 개라면 Note 8이 등록된 쪽 선택)");
  } catch (err) {
    // axios 에러 디테일하게 처리
    if (err.response) {
      const { status, statusText, data } = err.response;
      console.error(`❌ HTTP ${status} ${statusText}`);
      console.error("   응답:", JSON.stringify(data, null, 2));

      if (status === 401) {
        console.error("\n💡 401 = 토큰이 유효하지 않거나 만료됨.");
        console.error("   https://account.smartthings.com/tokens 에서 PAT 재확인.");
      } else if (status === 403) {
        console.error("\n💡 403 = 권한 부족. PAT 발급 시 r:locations:* 스코프가 빠졌을 수 있음.");
      } else if (status === 429) {
        console.error("\n💡 429 = Rate limit. 1분 후 재시도.");
      }
    } else if (err.code === "ENOTFOUND") {
      console.error("❌ 네트워크 오류: api.smartthings.com에 접근할 수 없습니다.");
    } else {
      console.error("❌ 요청 실패:", err.message);
    }
    process.exit(1);
  }
})();
