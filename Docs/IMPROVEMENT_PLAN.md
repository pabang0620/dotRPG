# 전체 개선 계획 (2026-10-08)

전체 분석(클라이언트 게임플레이, 클라이언트 온라인·UI·아트, 서버, 기획·데이터)에서 나온 결함 중 **코드로 고칠 수 있는 것**을 병렬 작업으로 처리한다.
사업·법무 결정이 필요한 항목은 마지막 절에 모아 두고 이번 작업에서 건드리지 않는다.

## 진행 원칙

- 브랜치는 `wonho` 하나. 워크트리를 만들지 않는다. 대신 **작업마다 담당 파일을 겹치지 않게** 나눈다(아래 "담당 파일" 열).
- 에이전트는 커밋하지 않는다. 커밋·Unity 배치 컴파일·서버 전체 테스트는 오케스트레이터가 묶음 끝에 1회 한다.
- Unity 프로젝트는 하나라서 에이전트는 Unity 컴파일을 돌리지 않는다. 서버 에이전트는 `npx tsc --noEmit`과 자기 영역 테스트만 돌린다.
- 기능 보존: 게임 동작·수치·저장 포맷을 바꾸지 않는다(명시한 항목 제외).
- 빌드·게임 실행은 하지 않는다.

## 에이전트

| 에이전트 | 신규/기존 | 역할 |
|---|---|---|
| `dotrpg-unity-splitter` | 신규 | 500줄 초과 C# 파일을 partial class로 동작 변경 없이 분할 |
| `dotrpg-client-net-engineer` | 신규 | Unity 클라이언트 온라인 계층(ApiClient·OnlineSession·OnlineEconomy·세이브) 견고화 |
| `dotrpg-doc-syncer` | 신규 | 기획 문서를 코드·데이터(SSOT)에 맞춰 갱신, 문서 간 충돌 보고 |
| `dotrpg-server-architect` | 기존 | 회원 탈퇴 설계 |
| `dotrpg-backend-coder` | 기존 | 서버 수정·테스트 작성 |
| `dotrpg-economy-auditor` | 기존 | 재화 경로 정적 점검(opus low) |

## 1차 (병렬)

| ID | 작업 | 에이전트 | 담당 파일 |
|---|---|---|---|
| S1 | 서버 기동 시 `data_version.json`의 files 해시를 실제 `server/data/*.json` 내용과 대조, 불일치면 기동 거부. 해시 방식은 `Assets/Scripts/Editor/GameDataExport.cs`와 같게 | backend-coder | `server/src/gamedata/**`, 관련 신규 테스트 |
| S2 | J8: "전직의 길" 퀘스트 완료를 클라이언트가 올린 전직 상태가 아니라 서버 전직 기록(`character_career`)으로 확인 | backend-coder (S1과 같은 인스턴스) | `server/src/domains/quests/**`, 관련 테스트 |
| S3 | 테스트 공백 채우기: achievements, versionCheck 미들웨어(426 경로), promote, transport fallback | backend-coder (별도 인스턴스) | `server/test/` 신규 파일만 |
| S4 | J9: 경매·소탕·레이드 보상·우편 수령의 함수 단위 재화 점검 | economy-auditor | 읽기 전용 |
| S5 | 회원 탈퇴(L5) 설계: 탈퇴 요청·로그인 차단·세션 폐기·유예 후 개인정보 익명화·원장 보관 | server-architect | `Docs/server/phase12_withdrawal.md` 신규 |
| C1 | J1 refresh 토큰 DPAPI 암호화 저장(기존 평문 값은 읽어서 이전), J2 타임아웃 시 같은 request_id로 재조회, J3 온라인 상태 업로드 실패 시 최신 상태 재전송, 세이브 v6 마이그레이션 분기 점검 | client-net-engineer | `Runtime/Net/ApiClient.cs`, `OnlineSession.cs`, `OnlineEconomy.cs`, `Runtime/Save/**` |
| C2 | `OnlineEconomy.On` 분기 누락 전수 점검: 온라인에서 로컬 지급이 실행되는 경로 목록화(수정 없이 보고) | client-net-engineer (C1과 같은 인스턴스) | 읽기 전용 |
| R1 | 파일 분할: `Runtime/Art/**`, `Runtime/World/**` | unity-splitter | 해당 폴더 |
| R2 | 파일 분할: `Runtime/UI/**`, `Runtime/Core/**`, `Runtime/Quest/**`, `Runtime/Combat/**`, `Runtime/Dungeon/**`, `Runtime/Net/PartyNet.Host.cs` | unity-splitter | 해당 파일 |
| R3 | 파일 분할: `Runtime/Player/**`, `Runtime/Input/**`, `Runtime/Progression/**`, `Runtime/Enemies/**`, `Runtime/Items/**`, `Runtime/Party/**`, `Editor/**` | unity-splitter | 해당 파일 |
| D1 | 문서 동기화: 장비 등급 배율(BALANCE_PLAN vs GEAR_RENEWAL), 전직 스킬 수치(PLAN_CAREER_SKILLS), PLAN_MONETIZATION 맨 위 결정, STORE.md 기능 설명, MASTER_CHECKLIST 구버전 수치, 시계 기준(PLAN_DUNGEON_RAID D3), 경매 수수료·가격 제한 서술, PROGRESS 스냅샷 | doc-syncer | `Docs/*.md`(이 파일과 PRELAUNCH_REVIEW 제외), `README.md` |

## 2차 (1차 결과를 받아서)

| ID | 작업 | 에이전트 |
|---|---|---|
| S6 | S4 점검 결과 CRITICAL·HIGH 수정 | backend-coder |
| S7 | S5 설계대로 회원 탈퇴 구현(마이그레이션 0027, API, 익명화 작업, 테스트) | backend-coder |
| C3 | C2 보고 중 실제 누락 경로 수정 | client-net-engineer |

## 3차 (오케스트레이터)

1. Unity 배치 컴파일 1회(`error CS` 0 확인)
2. 서버 `npm run build` + `npm test` 1회
3. 변경 파일 목록 확인 후 영역별 커밋, `wonho` 푸시
4. 이 문서 결과 절 갱신

## 이번에 하지 않는 것 (사용자 결정 필요)

| 항목 | 이유 |
|---|---|
| 운영 서버 업체·도메인(O1~O4), WAL(O6) | 업체 선택 |
| 파티 저레벨 경험치 감쇠 | 밸런스 결정 |
| 미성년자 결제 한도(L9), 약관·개인정보처리방침·등급분류(L1~L8) | 법무 |
| 레포 비공개 전환(P1) | 운영 결정 |
| jaein 브랜치 커밋 2개 병합 | 병합 시점 결정 |
| 매칭 대기열·속도 제한 Redis 이전 | 서버 1대·100명 미만 전제에서는 불필요. 확장 시점에 |
| 온라인 분기 구조 통합(`IAuthority` 완성) | 31곳 동시 수정이라 위험 대비 효과가 작다. C2 점검으로 누락만 막는다 |

## 결과

| ID | 결과 |
|---|---|
| S1 | 서버 기동 시 `data_version.json` 해시 대조(`gamedata/loader.ts` `verifyDataHashes`). 데이터를 고치는 시험은 `rehashDataDir`로 해시를 다시 계산한다 |
| S2 | "전직의 길" 완료를 서버 전직 기록(`careerGrantRepository.getGrant`)으로 확인 |
| S3 | 시험 추가: achievements, versionCheck, promote, transportFallback |
| S4 | 재화 점검 CRITICAL·HIGH·MEDIUM 0건, LOW 4건 |
| S6 | LOW 4건 수정: 최종 레이드 열쇠 차감 실패 시 409, 주기 경계를 넘은 레이드 재판정, 캐릭터 단위 캠페인의 소탕권 첨부 거절, 경매 정산 골드 0 싱크 기록 생략 |
| S5·S7 | 회원 탈퇴 설계(`Docs/server/phase12_withdrawal.md`)와 구현(마이그레이션 0027, W1~W3, WD1~WD8, 익명화·파기 작업 2개). D1~D11은 설계서 추천 기본값. 보안 점검 후 파기 전용 역할 권한을 표별로 좁히고, 관리자 tombstone 경로를 uuid로 바꾸고, 철회 Steam 경로에 로그인과 같은 속도 제한을 걸었다 |
| C1 | refresh 토큰 DPAPI 저장(`TokenVault.cs`), 같은 request_id 재시도, 상태 업로드 재전송 |
| C2·C3 | 분기 누락 점검 후 장비 착용 서버 거절 시 되돌리기(`OnlineEconomy.Equipment.cs`) 등 수정 |
| R1~R3 | 500줄 초과 C# 파일 40개 partial 분할 |
| D1 | 기획 문서 11개를 코드 기준으로 갱신 |

검증: Unity 배치 컴파일 `error CS` 0, 서버 `npm run build` 통과, `npm test` 88개 묶음 1,103개 통과.

남은 위험
- 클라이언트: 앱 종료 시점 상태 업로드 불가, 재시도 중 캐릭터 전환 시 대기 상태 유실, `OnlineEconomy`를 거치지 않는 재화 클라이언트(StarShop, Cash, ReviveCoins, LevelReward, Sweep, Career, ServerAuction, Party)는 재시도 미적용
- 운영 배포 전: `WITHDRAW_ID_HMAC_KEY` 설정, 5년 실삭제를 켜려면 `dotrpg_purge` 역할 생성과 `PURGE_DATABASE_URL`, 법무 확인
- Unity 탈퇴 화면은 아직 없다
