# SmartThings IoT Horror Experiment Game Plan

## 목적

이 프로젝트의 목적은 일반적인 공포 게임 완성보다, 2분 안에 통제된 공포 자극과 실제 IoT 반응을 제공해서 실험 가설을 검증하는 것이다.

핵심 실험 자극은 다음 두 가지다.

- 스마트 전등: killer 접근, 피격, 특정 이벤트에서 밝기/색/점멸 반응 제공
- 스마트 콘센트: 연결된 선풍기를 켜서 실제 바람 자극 제공

게임은 참가자가 짧은 시간 안에 목표를 이해하고, 이동하고, killer 압박을 받고, IoT 반응을 경험한 뒤 성공 또는 실패로 종료되도록 설계한다.

## 기본 방향

- 플레이 시간은 최대 120초.
- 목표 아이템을 빨리 먹으면 즉시 성공 종료.
- 플레이어 체력은 무제한.
- 피격은 사망이 아니라 강한 감각 피드백으로 처리한다.
- killer는 완전 랜덤 AI보다 반스크립트형 추격 구조로 통제한다.
- 3층 접근 수단이 불명확하므로 최종 목표 아이템은 2층에 배치한다.
- 기존 에셋은 재사용하되, 불필요한 멀티플레이/템플릿 구조는 제거 대상으로 본다.

## 플레이 목표

참가자는 1층에서 시작한다.

화면 목표는 단순하게 유지한다.

> 2층 방에서 목표 아이템을 회수하라.

성공 조건:

- 2층 목표 아이템 획득
- 즉시 `Mission Success` UI 출력
- 입력 잠금
- 결과 로그 저장

실패 조건:

- 120초 안에 목표 아이템 미획득
- `Time Over` 또는 `Mission Failed` UI 출력
- 결과 로그 저장

## 2분 플레이 시퀀스

아래 흐름은 모든 참가자가 비슷한 강도의 자극을 경험하도록 하기 위한 기준안이다.

| 시간 | 이벤트 | 의도 |
| --- | --- | --- |
| 0-15초 | 1층 시작, 낮은 조도, 목표 표시 | 조작 적응과 목표 이해 |
| 15-35초 | 원거리 이상 징후, 약한 소리/발소리 | 긴장 형성 |
| 35-60초 | killer 근접 힌트, 전등 약한 변화, 선풍기 짧게 ON 가능 | 실제 IoT 자극 첫 노출 |
| 60-85초 | 2층 진입 유도, 짧은 암전/소리 이벤트 | 이동 압박 |
| 85-110초 | killer 추격 또는 피격 가능 구간 | 핵심 공포 자극 |
| 110-120초 | 목표 아이템 획득 기회 또는 시간 압박 | 성공/실패 확정 |

목표 아이템을 중간에 획득하면 위 시퀀스를 기다리지 않고 즉시 성공 종료한다.

## IoT 이벤트 설계

IoT 이벤트는 게임 연출과 실험 로그 양쪽에서 동일한 이름으로 관리한다.

| 이벤트 | 전등 | 선풍기 | 사용 시점 |
| --- | --- | --- | --- |
| `game_start` | 어둡게 설정 | OFF | 시작 시 |
| `ghost_hint` | 약한 밝기 변화 | OFF 또는 짧은 ON | 초반 이상 징후 |
| `killer_near` | 밝기 흔들림/색 변화 | 2-3초 ON | killer 근접 |
| `player_hit` | flash로 확 밝아짐 | 4-6초 ON | 피격 시 |
| `blackout` | 짧게 OFF 후 복구 | OFF | 중반 공포 이벤트 |
| `mission_success` | 안정적인 밝기 | OFF | 성공 종료 |
| `mission_failed` | 낮은 밝기 또는 붉은 톤 | OFF | 실패 종료 |

### 선풍기 사용 원칙

선풍기를 killer 근접 단계에서 계속 켜두면 피격 이벤트의 효과가 약해질 수 있다.

따라서 선풍기는 다음처럼 제한한다.

- killer 근접: 2-3초 ON
- 피격: 4-6초 ON
- 선풍기 이벤트 쿨다운: 15-20초
- 같은 상태에서 반복 ON 금지

이렇게 해야 `killer_near`와 `player_hit`의 차이가 참가자에게 명확해진다.

## 피격 연출

피격은 사망이 아니라 감각 피드백이다.

필수 연출:

- 화면 붉은 flash 또는 vignette 0.4-0.8초
- 짧은 카메라 흔들림
- 타격음
- 플레이어 신음 효과음
- 짧은 이동속도 저하 또는 멈칫함
- 전등 flash
- 선풍기 ON

추가 후보:

- 심장 박동음 증가
- 숨소리 증가
- 화면 가장자리 노이즈
- killer 공격 직후 짧은 거리 벌림

플레이어 체력은 감소하지 않는다. 피격 횟수는 로그에만 기록한다.

## Killer AI 설계

killer는 실험 자극을 안정적으로 발생시키기 위한 압박 장치다.

필수 기능:

- 플레이어 감지
- 추격
- 공격 거리 판정
- 공격 쿨다운
- 공격 애니메이션
- 피격 이벤트 호출
- 추격 음악 또는 숨소리 트리거

권장 구조:

- `Idle`: 시작 대기 또는 배치 상태
- `Patrol`: 제한된 구역 순찰
- `Investigate`: 소리/트리거 위치로 이동
- `Chase`: 플레이어 추격
- `Attack`: 근접 공격
- `Reset`: 공격 후 살짝 후퇴하거나 위치 재조정

실험 통제를 위해 완전 자율 순찰보다 트리거 구간 기반 추격을 우선한다.

## 플레이어 설계

필수 기능:

- WASD 이동
- 마우스 시점 조작
- 상호작용 키
- 목표 아이템 획득
- 피격 피드백 수신
- 결과 UI 표시 시 입력 잠금

불필요한 기능:

- 멀티플레이
- 인벤토리 복잡화
- 체력/사망 시스템
- 무기/전투 시스템
- 스킬 또는 성장 시스템

## 목표 아이템 후보

단순 코인보다 실험 맥락에 맞는 아이템이 좋다.

후보:

- 빛나는 열쇠
- 오래된 부적
- 이상한 스마트 장치 부품
- 작은 금속 토큰
- 연구용 회수 장치

추천은 `glowing token` 또는 `strange device part`다.

이유:

- 2층 방 안에서도 눈에 잘 띄게 만들 수 있다.
- 공포 분위기와 실험 장치 맥락을 모두 살릴 수 있다.
- 획득 시 `Mission Success`로 바로 연결하기 쉽다.

## 사운드/애니메이션 에셋 검색 후보

추후 개발 단계에서 검색 AI 또는 에셋 담당 에이전트가 확인할 사이트:

- Unity Asset Store: horror SFX, ambient, footsteps, hit sound
- Freesound: 숨소리, 신음, 타격음, 문소리, 발소리
- Pixabay Sound: 공포 배경음, impact, heartbeat
- OpenGameArt: 무료 효과음과 간단한 아이템 에셋
- Mixamo: killer 걷기, 달리기, 공격 모션

주의할 점:

- 라이선스 확인
- 상업/연구 발표 사용 가능 여부 확인
- 출처 기록
- 볼륨 정규화
- 너무 긴 BGM보다 2분 루프에 맞는 짧은 ambience 우선

## 개발 에이전트 분담안

다중 병렬 에이전트를 쓴다면 다음처럼 나눈다.

| 에이전트 | 역할 |
| --- | --- |
| Unity/Scene Agent | 씬 정리, 플레이어 시작 위치, killer 위치, 목표 아이템 배치 |
| Gameplay Agent | 플레이어 이동, 목표 획득, 성공/실패 상태, 타이머 |
| Killer AI Agent | 감지, 추격, 공격, 피격 이벤트 |
| IoT Agent | SmartThings REST API, 전등/콘센트 이벤트 큐, 실패 재시도, 로그 |
| Audio/Asset Agent | BGM, 숨소리, 피격음, 신음, 발소리, 라이선스 정리 |
| QA Reviewer Agent | 2분 플레이 검토, 어색한 구간, 자극 강도, 반복 플레이 확인 |
| Experiment Log Agent | 피격 횟수, IoT 발동 시각, 성공/실패, 클리어 시간 기록 |

## Unity MCP 사용 판단

Unity MCP는 필수는 아니지만, 후반 검증 단계에서 유용하다.

유용한 작업:

- Play Mode 실행
- Console 에러 확인
- 씬 오브젝트 상태 확인
- 스크린샷 기반 시야 검증
- 플레이어와 killer 위치 검증
- UI가 실제 Game View에 보이는지 확인

우선순위:

1. 코드와 씬 구조 정리
2. 컴파일 에러 제거
3. 플레이 가능한 2분 루프 구현
4. IoT 이벤트 연결
5. Unity MCP로 반복 플레이 검증

## 실험 로그

최소 로그 항목:

- session id
- start time
- clear time
- success/failure
- hit count
- killer_near event count
- player_hit event count
- light command timestamps
- fan command timestamps
- IoT command success/failure

로그는 CSV 또는 JSON Lines로 남긴다.

## QA 체크리스트

플레이 검증 시 반드시 확인할 항목:

- 시작 후 5초 안에 목표를 이해할 수 있는가
- 플레이어가 2층 목표까지 이동할 수 있는가
- 목표 아이템이 충분히 눈에 띄는가
- killer가 아예 안 보이거나 너무 빨리 붙지 않는가
- 피격 시 화면/소리/IoT 반응이 동시에 인지되는가
- 선풍기 이벤트가 너무 자주 겹치지 않는가
- 2분 안에 성공과 실패가 모두 가능한가
- Mission Success UI가 명확히 보이는가
- Time Over UI가 명확히 보이는가
- 실험 로그가 누락 없이 남는가

## 1차 구현 범위

가장 먼저 구현할 최소 완성본:

- 단일 씬
- 1층 시작
- 2층 목표 아이템
- 120초 타이머
- 목표 획득 성공 종료
- 시간 초과 실패 종료
- killer 추격/공격
- 피격 피드백
- 전등/선풍기 이벤트 호출부
- 실험 로그

이후 완성도 개선:

- 공격 애니메이션 교체
- 공포 BGM 적용
- 숨소리/발소리/문소리 추가
- 목표 아이템 에셋 교체
- 조명 연출 튜닝
- QA 에이전트 반복 검토

## 현재 구현 기준

2026-06-08 기준으로 코드 레벨에서 우선 반영한 범위:

- `ExperimentDirector`: 120초 세션, `GameOnly`/`GameWithIoT` 조건, 성공/실패 종료, 실험 이벤트 전송
- `ExperimentLogger`: 세션별 JSON Lines 로그 저장
- `ExperimentBootstrapper`: 씬에 `GameManager`, `SmartThingsEventSender`, `ExperimentDirector`가 없으면 Play 시 자동 생성
- `ObjectiveItem`: 2층 목표물 획득 시 `mission_success`
- `ExperimentProgressMarker`: 계단/2층/이벤트 구간 트리거 기록
- `NonLethalHitFeedback`: 피격 시 붉은 비네트, 카메라 흔들림, 짧은 스턴, 효과음
- `PlayerHealth`: 실험 중에는 사망 대신 `player_hit` 기록
- `KillerAI`/`EnemyAI`: 근접 시 `killer_near`, 공격 시 비치명 피격 후 추격 복귀
- `SmartThingsEventSender`: `session_id`, `condition`, `elapsed_sec`, `hit_count` 포함 전송
- Node 서버: `game_start`, `killer_near`, `player_hit`, `mission_success`, `mission_failed` plan과 안전 clamp 반영

조건 전환:

- 기본값은 `GameWithIoT`
- 자동 생성된 `ExperimentDirector`의 조건을 바꾸려면 PlayerPrefs `ExperimentCondition` 값을 `GameOnly` 또는 `GameWithIoT`로 설정
- 최종 실험 씬에서는 `ExperimentDirector`를 직접 배치하고 Inspector에서 조건을 지정하는 방식이 더 명확함

## Unity 씬 배치 체크

코드가 있어도 아래 배치가 끝나야 실제 실험용으로 볼 수 있다.

- `Assets/Scenes/MainScene.unity`를 열고 `Tools > Validate Horror Scene Setup` 실행
- 2층 목표물 GameObject 생성 후 Collider를 Trigger로 두고 `ObjectiveItem` 추가
- Player에 `NonLethalHitFeedback` 추가, vignette Image와 camera Transform, 피격음 연결
- Killer 프리팹 또는 씬 오브젝트의 `KillerAI`에서 detect/catch/hit cooldown 값 확인
- 계단 입구, 2층 입구, 목표 방 앞에 `ExperimentProgressMarker` 배치
- `Mission Success`, `Mission Failed`, timer, objective UI가 있으면 `ExperimentDirector`에 연결
- Missing Script 컴포넌트는 제거
- NavMesh를 다시 Bake하고 Killer가 2층까지 이동 가능한지 확인
- `SmartThings_server/.env`에 `SMARTTHINGS_TOKEN`, `DEVICE_ID_LIGHT`, `DEVICE_ID_FAN` 설정
- 서버 실행 후 GameWithIoT 조건에서 `game_start`, `killer_near`, `player_hit`, `mission_success/failed`, `recovery` 로그 확인

## MCP 재연결 체크

현재 작업 세션에서는 Unity MCP 도구가 Codex tool registry에 노출되지 않을 수 있다. 이 경우 코드 수정은 가능하지만 Game View 캡처와 Scene 오브젝트 조작은 제한된다.

확인 순서:

- Unity에서 `Smartthings_horror2` 프로젝트 열기
- Play Mode가 아닌 상태에서 `Tools > MCP Unity > Server Window`의 서버 Start 확인
- `127.0.0.1:8090` 포트가 열려 있는지 확인
- Codex를 `겜인텍실험` 또는 `Smartthing_server` 루트에서 새 세션으로 시작
- `.mcp.json`을 읽은 뒤 `mcp-unity` 도구가 노출되는지 확인
- MCP가 붙으면 Console 로그, scene hierarchy, screenshot을 JSON 요약 형태로만 주고받아 메인 대화 context를 아낀다
