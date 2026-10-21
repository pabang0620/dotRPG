# dotRPG 개발 진행 현황

> 기준 브랜치: `wonho` (사용자 작업 브랜치. 브랜치는 main/jaein/wonho 3개)
> 전체 할 일·결정 기록: [MASTER_CHECKLIST.md](MASTER_CHECKLIST.md) · 기획: [PLAN_DUNGEON_RAID.md](PLAN_DUNGEON_RAID.md), [PLAN_ONLINE.md](PLAN_ONLINE.md) · 음악: [BGM_PROMPTS.md](BGM_PROMPTS.md)

## 완료된 것

| 영역 | 내용 | 커밋 |
|---|---|---|
| 강화 | 참고 게임 공개 확률(+20까지), +11·+12 실패 시 3단계 하락과 확률 보정, +13부터 파괴(방어구·장신구는 +11부터), 장비 보호권, 장비 개체별 강화, 세이브 v4, 리뷰 지적 15건 수정 | 218a759, 5392680 |
| 파티 | AI 용병 4명(브론·카이·엘린·세라), 최대 4인, 캐릭터별 스탯, 어그로, 딜 미터, 파티 창·프레임 | 453a9aa |
| 던전 | 방 단위 진행, 문, 코인식 부활, CLEAR 연출, 랭크 SSS~F, 보상 카드 4장, 던전 선택 창, 안내원 NPC, 06:00/목요일 초기화, 던전 안 저장 금지 | 0d44c05 |
| 몬스터·보스 | 몬스터 7종, 요일 보스 5종, 해골왕(3페이즈·토템·망자의 심판), 예고 장판, 슈퍼아머, 무력화, 보스 HP 바 | d1446e2 |
| 요일던전·레이드 연결 | 요일별 몬스터·보스 배치, 방 데이터 검증, 레이드 수치 조정 | 2f26632 |
| 배경음악 | Flow Music 13곡 생성·마스터링(-14 LUFS), 마을·사냥터·던전 테마·보스·레이드·광폭화·클리어·실패별 재생, 1초 크로스페이드 | 2da8ef6, ca691b4 |
| 다른 작업자 병합 | 32px 고해상도 맵·캐릭터·UI, 기모노 카타나 전사, 3연타 불꽃 베기 (양쪽 기능 모두 유지) | bd167aa, a4ed03f |
| 아트 통합 | 몬스터·보스·토템 고해상도화(1-A), 던전 방 32px 지형 + 광산·동굴 톤(3-A) | 24d00f5, f310549 |
| UI 개편(진행 중) | 전 화면 감사(UI_AUDIT.md), UiTheme 디자인 토큰, 버튼 피드백, HUD 중앙 배너, 최소 16px 글자, 어두운 불투명 메뉴 패널 | e2e2f51 ~ ce72947 |
| 온라인 클라이언트 구조(진행 중) | ActorId, NetCommand, NetworkInput, LoopbackTransport, IAuthority(강화 굴림·몬스터 드롭), OnlineSession, 파티 찾기·경매장 미리보기 창, PLAN_ONLINE/PLAN_AUCTION v1.0, `-dotrpgOnline` 캡처 | eb44a1c, 4621063 |
| 문서 | 마스터 체크리스트, 온라인·서버·경매장 기획(v0.1), 사용자 의도 기록 | adccf60 외 |
| 스킬 체계 v2 | 슬롯 역할 고정(단일·범위·제어·방어·각성), 범위 보너스 완화, 제어 감쇠, v1은 SkillGemsLegacy·태그 skills-v1로 보관 | ac135b9 |
| 게임 서버 1·2단계 | Node·TypeScript·PostgreSQL, 개발용 로그인, 온라인 캐릭터·상태 저장, 타이틀 온라인 모드 | c40f2f5 |
| 게임 서버 3단계 | 경제 판정 전부 서버로(처치·드롭·채집·퀘스트·상점·강화·창고·장착·솔로 던전), 원장 | b391f4e |
| 게임 서버 4단계 | 파티 모집·매칭·로비, 협동 던전(방장 계산 + 스냅샷), 방장 인계, 결과 대조, 레이드 | b172263 |
| 게임 서버 5·6단계 | WebSocket 채팅·친구·차단·신고·초대, 경매장·우편·시세 | 58beda7 |
| 게임 서버 7단계 | 관리자 CLI, 정리 작업, 백업·점검·배포 파일, 출시 전 수정 13건 | 9b38dc7 |
| 다국어 | `L` 번역 함수, 언어 설정, 서버 `X-Lang` 메시지 번역, 11개 언어 번역표(영어·일본어·중국어 간체/번체·스페인어·포르투갈어·독일어·프랑스어·러시아어·베트남어·인도네시아어) | d5a8f691, 1e97917a |
| 정책·동의 | 서버 정책 문서 6종(한국어/영어)과 자동 생성 확률 페이지, 약관 동의 화면, 설정의 약관 및 정책 메뉴, 이름 변경 제재, 다국어 금칙어 | b4f0ff36, a89ce253 |
| 정책 방어 보강 | 확률표 공개 기록(0040)과 30만 회 통계 시험, 이용 시간대 제한(0039), 제재 사유 코드 6종 추가, 약관 14조의2·14조의3 등 | 88e38921 |
| 결제 한도 제거 | 결제 일·월 한도, 시간당 주문 상한, 별조각 일일 소비 상한 제거(0036). 사기 방지 장치만 유지 | cbcbb1ac, cc387a7f |
| 골드 경제(무자본 조금 부족) | 강화 골드 단계별 5~35배, 잡화점 장비 판매가 40%, 퀘스트 골드 절반, 승급 유니크 10핵/12만·레전더리 24핵/40만, 레이드 상점 45/120/20/60: Lv40 도달 시 클리어 한 벌 값의 86%. 20명 에이전트 시뮬레이션 v3로 확인(`Docs/BALANCE_GOLD_2026-10-16.md`, `Tools/balance/theory_gold.py`, `analysis/player-sim/reports/report-dotrpg-v3.md`) | f6474cb9, 5851f7c9 |
| 실측 재조정 | 지금 빌드로 사냥·던전 재측정: Lv16+ 자동 사냥 분당 20~25(설계 15)라 경험치 1.9배(Lv16~20 램프), 왕·영웅 체력 4.8/6.7 -> 7.7/12(재측정 119/188초), 강화 골드 Lv20+ 1.33배. 레이드는 측정 드라이버가 장판을 못 피해 미사용(`Docs/BALANCE_MEASURE_2026-10-16.md`) | c07fcd6e |
| Lv20 이후·레이드 점검 | 레이드 정산 결과 창 누락 경로 3건(방장 보고 무효 시 90초 대기, 서버 대신 접수 뒤 409, RunEnd 유실·방장 포기 시 잔류), 즉사 기믹 3건(그로기 중 판정 지연·결계·포효 겹침·파티원 지연 위치), 체력 구간 패턴 중복 발동, 파티 경험치 상한, 중계 재접속 간격, 날리기 이동 제한 | f6474cb9 |
| UI 줄바꿈 | 한글 라벨이 글자 중간에서 꺾이지 않게(UIWordWrap: 짧은 라벨은 한 줄, 긴 글은 어절 단위), 경매·파티 찾기 열 한 줄, 버튼 글자 테두리 안 | 6b396a3e |
| AI 동료 부활 | 필드·던전·레이드 어디서든 4·8·16·32초(이후 64초) 뒤 스스로 일어남, 부활 코인 안 씀 | 43286cdd |
| 약한 점 개선 (2026-10-18) | 레이드 보상 혼자서도(최소 인원 1), 메인 퀘스트로 콘텐츠 해금 + 열릴 때 안내 의뢰 자동 수락(`ContentUnlocks`), "할 일" 바로가기 창(N, `TodayScreen`) (`PLAN_WEAKNESS_2026-10-18.md`) | 837a0cbd |
| 미니 던전 병합 (jaein) | 경험치 전용 1인 미니 던전 3구간(Lv10~39, 하루 30분, 자동 사냥). 마이그레이션 번호 겹침으로 0042 -> 0046, 성장 추천·할 일 창에 연결(`MiniDungeonGrowth`), 던전 창에 미니던전 탭. 처치당 경험치 막대 1/240 -> 1/2,000, Lv16+ 경험치 x1.9 -> x2.1(Lv40 42시간 이상 유지) | bfe4c2b5 |
| 미니 던전 따라잡기 | 다음 메인 퀘스트가 레벨 때문에 막혔을 때만 처치 경험치 x10(막대 1/800, 평소 1/8,000), 서버가 처치마다 판정. 메인 1-11로 열림, 따라잡기 때만 성장 추천·할 일 맨 위. Lv16+ 경험치 x2.2 | 5c25428a |
| 길드·월드보스·길드보스·랭킹 (2026-10-20) | 길드(Lv20·3만 골드, 30명, 직위, 바로/승인 가입, 탈퇴 뒤 24시간, 길드 채팅, 이름표 길드 이름), 월드보스(Lv40 하루 3회 3분, 서버 공유 체력 4,000만(10-21 하향), 주간 최고 1회 피해 랭킹, 순위 칭호·이름 색), 길드보스(점수 누적, 하루 2회, 주간 점수 랭킹), 랭킹 창(Y). 보스 방 실측으로 체력·상한·보스 공격력 조정, 전용 그림(심연의 거신 40프레임, 메뉴 아이콘 3종) (`PLAN_GUILD_WORLDBOSS_2026-10-20.md`) | 3e748fe8, fc4de05d, 572acd4f |
| 측정 봇 장판 회피 | 레이드·보스 방 측정 봇이 장판·낙인·섬멸을 0.25초 반응으로 피함(`-measureDodge`, 측정 전용). 일반 난이도 레이드를 잴 수 있게 됨 | 50cc880d |
| +11 무기 강화 빛 오류 | 맞을 때 흰색 번쩍임 스프라이트(읽기 불가)를 읽어 +11 이상 무기에서 매 타격 오류가 나던 결함(`WeaponEnhanceVfx`) | fc4de05d |
| 공유 빌드 (2026-10-20) | `games/dotRPG_online_20261020.zip`, `games/dotRPG_mobile_20261020.zip`, 시험 서버 데이터 버전 abf5257bb1ef274e (10-20 배포). 옛 zip은 삭제됨 | - |

## 사용자 결정 기록

| # | 질문 | 결정 |
|---|---|---|
| D1 | 파티원 | 사람이 없으면 AI 용병, 사람을 구인하면 온라인 멀티 |
| 1 | 던전 몬스터 그림 | 고해상도로 다시 그림 (완료) |
| 2 | 용병 전사 외형 | 일반 고해상도 외형 유지 |
| 3 | 던전 방 바닥 | 새 32px 지형 적용 (완료) |
| 4 | 동료 전사 3연타 | 유지 |
| 5 | 보호권 아이콘 | 16px 그대로 |
| 6 | BGM 연결 병합 | 추가 테스트 없이 병합 |

## 남은 일

- 레이드 측정: 2026-10-20 측정 봇에 장판·낙인·섬멸 회피를 넣었다(`-measureDodge`). 일반 난이도는 잴 수 있게 됐고(클리어 절반 정도), 심화는 봇이 아직 사람만큼 못 피해 판단에 못 쓴다. 심화는 사람 실플레이로 확인한다(`TEST_LOG.md` 2026-10-20)
- Steam 연동(시험 앱 ID 480: SteamAuth·SteamP2PTransport, 서버 Web API 키), 실제 출시 서버 배포(`server/ops/README.md`)
- 출시 전 운영: 법률 검토(약관·개인정보처리방침), 번역 원어민 감수, `POLICY_CONSENT_ENFORCE`·`WITHDRAW_DESTROY_ENABLED` 켜기, 클라이언트 결제 구매 화면
- 다음 업데이트(출시 약 2달 뒤): 만렙 확장과 신규 레이드(바르가스·바위 심장은 데이터만 있고 닫혀 있음). 지금 끝판 할 거리를 따로 늘리지 않는다(`PLAN_WEAKNESS_2026-10-18.md` 4번)
- 출시 전 개선 계획: [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md), 전체 체크리스트: [MASTER_CHECKLIST.md](MASTER_CHECKLIST.md)
- 지금까지 돌린 테스트(실측·시뮬레이션·실플레이·서버 테스트)와 결과: [TEST_LOG.md](TEST_LOG.md)

## 현재 상태 (2026-10-20)

| 항목 | 내용 |
|---|---|
| 작업 브랜치 | `wonho` (사용자 작업 브랜치). 브랜치는 `main`·`jaein`·`wonho` 3개, 푸시는 `wonho`와 `main`에 같이 |
| 엔진 | Unity 6000.5.9f1 (`ProjectSettings/ProjectVersion.txt`) |
| 표기 이름 | **볼론**(Bolon, 2026-10-21 사용자 결정, 마왕 이름). 타이틀 로고는 영문 "BOLON" 하나(`Art/ui_logo_bolon`, 모든 언어 공통), 화면·문서·정책·홈페이지만 바꾼다. Unity productName·세이브 경로·레포·서버 이름은 dotRPG 유지(세이브 유실 방지). 고유 이름·떡밥은 `STORY.md` 7절 |
| 게임 서버 | `server/`(Node·TypeScript). 마이그레이션 0001~0047, 서버 테스트 117개 파일. 단계별 현황은 `PLAN_SERVER.md`와 `Docs/server/` |
| 시험 서버 | 구글 클라우드 35.216.2.121(`https://35-216-2-121.sslip.io`), DB는 Supabase `dotrpg` 스키마. 0047(길드·보스)까지 적용, 데이터 버전 abf5257bb1ef274e(2026-10-20 배포). 절차는 `server/ops/README.md` 10절 |
| 정책 공개 사본 | https://pabang0620.github.io/dotrpg-policy/ (저장소 pabang0620/dotrpg-policy) |
| 홈페이지 | 로컬 `C:\Users\admin\Desktop\games\sites\dotrpg-home`(저장소 pabang0620/dotrpg-home, 비공개). OpenAI Sites 프로젝트(Next/vinext + Cloudflare), 게임 소개·스크린샷·캐릭터·스킨 화면과 로그인·가입·캐릭터 화면. 계정·OAuth 실제 연결은 아직 꺼져 있다(미설정 API는 503). 그림은 `public/assets/`(로고·배경·캐릭터·스킨·게임 캡처), 생성 기록 `docs/GENERATED_ASSETS.md`, 연결 방법 `docs/DELIVERY.md` |
| 밸런스 기준 | 총 플레이 Lv40 약 48시간, 미니 던전을 매일 하면 42.8~43.6시간(`Tools/balance/theory_progress.py --measured --mini`), 무자본 골드는 Lv40에서 한 벌 값의 약 88%(`Tools/balance/theory_gold.py`), 20명 에이전트 시뮬레이션 `analysis/player-sim` v3 |
| 검증 | 컴파일과 관련 서버 테스트는 작업 묶음 끝에 확인한다. 게임 실행·빌드·배포는 요청이 있을 때만 |

## 개발·검증 방법

- 작업 방식: 브랜치는 `main`·`jaein`·`wonho` 3개뿐이고 사용자는 `wonho`에서만 작업한다. 에이전트는 파일 소유를 나눠 같은 브랜치에서 일하고, 컴파일·테스트·커밋은 묶음 끝에 메인이 한다
- 컴파일과 서버 데이터 내보내기: `Unity.exe -batchmode -nographics -quit -projectPath <경로> -executeMethod DotRPG.EditorTools.GameDataExport.Export -logFile <로그>`
- 공유 빌드: 빌드 전용 복사본 `C:\Users\admin\Desktop\games\dotRPG_apkbuild`에 `Assets`·`ProjectSettings`·`Packages`를 `robocopy /MIR`로 맞춘 뒤 `BuildScript.BuildWindowsOnline`(PC, 시험 서버 주소 기본값)과 `BuildScript.BuildAndroidDev`(APK). 공유 파일은 `games/dotRPG_online_<날짜>.zip`, `games/dotRPG_mobile_<날짜>.zip`, 바로 켤 폴더는 `games/dotRPG_play`(바탕화면 바로가기)
- 측정 빌드: `BuildScript.BuildWindowsQA` -> `Builds/WindowsQA/dotRPG.exe -dotrpgMeasure <폴더> -measureOnly hunting|dungeons|raids|bossrush` (`-measureDodge 0`이면 장판 회피 없는 옛 봇. 절차·주의는 `Docs/BALANCE_MEASURE_2026-10-16.md`)
- 자동 검증 모드: `dotRPG.exe <모드> <결과폴더>` -> `report.txt`의 `summary` 줄. `-dotrpgCapture`(전체·강화·BGM), `-dotrpgParty`, `-dotrpgDungeon`, `-dotrpgMonster`, `-dotrpgBalance`, 다른 작업자 쪽 `-dotrpgCanyon`, `-dotrpgWinter`, `-dotrpgChars`, `-dotrpgUi`, `-dotrpgDepth`, `-dotrpgStairs`, `-dotrpgSilver`
- 미리보기(에디터): `DotRPG.EditorTools.MonsterSheet.Render`(몬스터 시트), `DotRPG.EditorTools.DungeonRoomPreview.RenderAll`(던전 방)
- BGM: Flow Music 후보 생성 후 `Tools/audio/bgm_pick.py --apply`로 검증·루프·선정 (절차는 `Docs/BGM_PROMPTS.md`)
