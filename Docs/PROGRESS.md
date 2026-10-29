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
| 표기 이름 볼론 (2026-10-21) | 게임 표기 이름 볼론(Bolon), 영문 로고, 보스·네임드 이름 전부 부여, 마야 모티프 떡밥(`STORY.md` 7절). productName·세이브 경로는 dotRPG | 6c11170b |
| 챕터 3 설계·구현 (2026-10-21~) | 만렙 50·특화·사얍 마을·레이드 2. 설계 `PLAN_CHAPTER3_2026-10-21.md` + `ch3/`, 0~3단계 완료(특화 48쌍, 사얍 마을·사냥터·이야기·그림), 4~7단계 남음(9절). 서버 `CHAPTER3_OPEN=0`이면 지금과 같은 Lv40 게임 | 336bcc8b~0a590311 |
| 공유 빌드 (2026-10-26) | `games/dotRPG_online_20261026.zip`, `games/dotRPG_mobile_20261026.zip`. 도적(전직 2종·스킨 67종·동료 2명), 스킨 등급 개편(전설·히든, 세 직업 67종, 0054), 일일·주간 완료 상자(0055), 챕터 3 코드 포함이지만 서버 `CHAPTER3_OPEN` 닫힘이라 만렙 40. 시험 서버 데이터 버전 6fd32cd2e649bfac(10-26 배포, 마이그레이션 0055) | 이 커밋 |

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
| M | 필드 자동 사냥과 손맛 (2026-10-13) | 현행 유지. 필드는 자동 사냥·자동 진행 허용, 손맛은 던전·요일 던전·레이드(직접만)가 맡는다. 직접 사냥 보상 강화안(M1~M4)은 하지 않는다. 사용자: "던전 요일던전 레이드 직접 싸우면 됐지 뭐" |

## 남은 일

### 출시 점검 작업 결과 (커밋 전)

후속 지시로 측정 실행이 허용돼 고정 스텝·1배속 비교를 진행했다. 이전의 실행 금지 상태와 구분하며 최신 숫자와 남은 기준은 `ch3/PROGRESSION_AND_GEAR.md` 1.10절이 기준이다. 그림 작업은 `ART_MISSING_2026-10-26.md`의 11개 생성·설치와 PC/모바일 아이콘 연결, 여관 초상 별칭 수정, Unity 내보내기·번역 검사까지 완료했다. 모음판·원본·백업·프롬프트는 `C:/Users/admin/Desktop/games/dotRPG_art_work/2026-10-26/`에 보관한다. 아래는 후속 실측 전 점검 기록이다.

이전 측정 기록(아래 2026-10-27 재측정으로 대체): Lv50 보스 방 어쌔신 82.8%·닌자 82.4%, Lv40 raid 닌자 103.5%로 미달 항목 남음. 지바크 6/12, 카마츠 5/12, 군주 10/10. 사냥은 초반 12/12·3장 33/42 유효, 복귀 실패 9행 제외. 닌자 3장 이론 시간 27.5로 상한 밖. 따라서 3장 전체 완료는 아니다. 동일 시드 배속 피해 차이 -4.8%/-7.5%, 게임 프로세스 0개 확인. 후속 변경은 위 측정 C# 4개와 문서 외에 `C:/Users/admin/Desktop/games/analysis/player-sim/tools/merge-measured.mjs`, `compare-measured.mjs`, `run-recheck-20261026.ps1`이다. 최종 합본은 `C:/Users/admin/Desktop/games/analysis/player-sim/data/measure_20261026_recheck_hero_ready/final-measured.json`.

2026-10-27 재측정: 60fps 고정 스텝에서 사냥 120/120행 유효, 사냥터 이탈과 레벨 변화 0. Lv50 보스 방 어쌔신 87.33%·닌자 96.67%, Lv40 3시드 합산도 목표 안이다. 지바크 7/12·카마츠 6/12, 골드 0.891 통과. 기본조건 Lv40 46.0시간은 유지했다. 초반 Lv12·14 자동 도적과 3장 도적 사냥 속도, 닌자 3장 28.1시간은 미달로 남아 있다. 별도 닌자 leap 빈범위 요청 수정은 원래 최종 사냥 로드아웃에 leap가 없으므로 이 미달 해결로 주장하지 않는다. 최종 유효 자료는 `../analysis/player-sim/data/measure_20261027_step60_final/`. 사용자 후속 요청에 따라 실제 메뉴 캡처 및 UI 개선도 진행 중이며 목록은 `UI_AUDIT.md`의 2026-10-27 절을 따른다.

- 최초 실행 금지 상태에서는 게임 실행·화면 확인 없이 코드·저장 로그·원본 그림으로 검토했다. 어쌔신 배율은 0.95로 복원했고 원인을 확정하기 전 추가 배율 조정은 하지 않는다. 보스 방 41.2%의 새 검증과 3장 사냥·레이드 재측정은 미완료다. 근거와 측정 로그 확장은 `ch3/PROGRESSION_AND_GEAR.md` 1.8절.
- 전사 그림: `w_whitetwin/attack.png` 30칸 머리를 몸 시트와 맞췄다. `w_stripe`, `w_pajama`, `w_starnight`, `w_blossom`, `w_festival`의 body/attack 무늬를 확대했다(10장 알파 불변). 스킨별 hands.json을 지원하고 직접 확인한 9칸을 수정했다: greybun 3, bluecoat·valkyrie·violet·noir·blackbob·bluebraids 각 1. 기존 사용자가 지적한 9칸 목록과 동일하다고 단정하지 않는다. 미사용 자산 포함 전수 알파 검사 318칸은 손 검수 완료 수가 아니다. 자동 최근접 픽셀 보정은 손 대신 몸통을 선택해 폐기했다.
- 도적 확정 수정: 비행 중 취소된 어쌔신 쿠나이 정리, 파티 멤버의 Puppet 몬스터에서 누락되던 닌자 기본 스킬 분신 타격 연출 복구(피해 없는 원격 연출). 닌자 즉시 기본 투척·기본 스킬 투척판, 분신 60%, 어쌔신 쿠나이 10m·착지 후 3초·쿨 5.5초, 연쇄 도약 0.5초, 그림자 습격 2.99, 허수아비와 두 각성기를 코드로 확인했다. 원격 순간이동의 실제 지연·패킷 순서 동작은 실행 검증 전이다. 기본 스킬 분신은 문서대로 적중 echo이며 이동·설치·각성까지 복제하도록 임의 확대하지 않았다. 추적 투척의 벽 관통 허용 여부는 설계 질문으로 남긴다.
- 서버 확정 수정: 스킨 1,011/히든 3,011 게이지에서 +10 교환 시 기존 값이 1,000/3,000으로 깎이던 문제, 스킨 게이지 1,000 초과 시 주간 상자의 음수 지급량으로 수령 실패하던 문제, 일반 히든도 제외된다고 잘못 설명하던 보너스 확률 공시. 확률 자체는 유지하고 공시의 중복 방지 단위를 등급+히든 칸으로 명확히 했다. 회귀 테스트를 먼저 추가하고 수정했다.
- 서버 검토 범위: 재전송 요청 해시·응답 재생, 계정 지갑/캐릭터 잠금, 부캐 동시 교환·상자, 서버 정의 가격·수량·확률·천장, 합성 재료·결과의 히든 제외. 0054 이전 스킨 ID 99개 누락 0개, 기존 보유·여분·게이지 유지 확인. 마이그레이션 0050~0055는 수정하지 않았다. 가득 찬 게이지 교환은 기존처럼 비용·주간 재고를 쓰고 추가 0칸이므로 UI 예상량 안내는 별도 정책 판단이 필요하다. 결제·구매 제한은 추가하지 않았다.
- 4장 0단계는 시작하지 않았다. 1~40 수치·XP는 불변이며 미니 미사용 이론 결과 46.0시간이다.

변경 파일(이번 작업, 기존 작업 파일은 덮어쓰지 않고 필요한 부분만 추가):

- `Assets/Scripts/Runtime/BossRush/BossRushCatalog.cs`
- `Assets/Scripts/Runtime/Core/DevCapture.Measure.cs`, `DevCapture.Measure.BossRush.cs`
- `Assets/Scripts/Runtime/Quest/QuestAutoPilot.Actions.cs`, `QuestAutoPilot.Dodge.cs`
- `Assets/Scripts/Runtime/Combat/CareerCombat.Assassin.cs`, `CareerCombat.Ninja.cs`
- `Assets/Scripts/Runtime/Player/SkillCaster.cs`, `Assets/Scripts/Runtime/Art/SilverWarriorArt.cs`
- `Assets/Resources/SilverWarrior/Skins/w_whitetwin/attack.png`
- `Assets/Resources/SilverWarrior/Skins/w_{stripe,pajama,starnight,blossom,festival}/{body,attack}.png`
- `Assets/Resources/SilverWarrior/Skins/{w_greybun,w_bluecoat,w_valkyrie,w_violet,noir,w_blackbob,w_bluebraids}/hands.json`
- `Tools/art/refine_warrior_skins.py`
- `server/src/domains/starshop/cashValueService.ts`, `server/src/domains/dailies/dailyChest.ts`, `server/src/domains/policies/ratesDisclosure.ts`
- `server/test/{cashValue,dailyChest,skinTiers}.test.ts`
- `Assets/Resources/Data/Lang/{de,en,es,fr,id,ja,pt-BR,ru,vi,zh-Hans,zh-Hant}/policy.json`, `server/data/lang.<언어>.json`
- `Docs/PROGRESS.md`, `Docs/PLAN_CHAPTER3_2026-10-21.md`, `Docs/ch3/PROGRESSION_AND_GEAR.md`

검증: Unity `GameDataExport.Export` 성공(종료 0, 21개 파일, 데이터 버전 `6fd32cd2e649bfac` 유지). `i18n.py check`는 11언어 미번역·형식 오류 0, `sync-server` 완료. 서버는 `npm test -- cashValue dailyChest skinTiers ratesStatistical ratesPublication starWalletOnly rogueClass chapter3Lock sealExchange raidsChapter3 bossRush` 한 묶음으로 11개 파일 127개 테스트 통과했다. 내보내기 기록 `server/data/data_version.json`의 exportedAt도 갱신됐다.

Unity가 갱신한 `.meta`는 그대로 둔다. 커밋·푸시·배포는 하지 않았다.

- 레이드 측정: 2026-10-20 측정 봇에 장판·낙인·섬멸 회피를 넣었다(`-measureDodge`). 일반 난이도는 잴 수 있게 됐고(클리어 절반 정도), 심화는 봇이 아직 사람만큼 못 피해 판단에 못 쓴다. 심화는 사람 실플레이로 확인한다(`TEST_LOG.md` 2026-10-20)
- Steam 연동(시험 앱 ID 480: SteamAuth·SteamP2PTransport, 서버 Web API 키), 실제 출시 서버 배포(`server/ops/README.md`)
- 출시 전 운영: 법률 검토(약관·개인정보처리방침), 번역 원어민 감수, `POLICY_CONSENT_ENFORCE`·`WITHDRAW_DESTROY_ENABLED` 켜기, 클라이언트 결제 구매 화면
- 다음 업데이트(출시 약 2달 뒤): 만렙 확장과 신규 레이드(바르가스·바위 심장은 데이터만 있고 닫혀 있음). 지금 끝판 할 거리를 따로 늘리지 않는다(`PLAN_WEAKNESS_2026-10-18.md` 4번)
- 출시 전 개선 계획: [IMPROVEMENT_PLAN.md](IMPROVEMENT_PLAN.md), 전체 체크리스트: [MASTER_CHECKLIST.md](MASTER_CHECKLIST.md)
- 지금까지 돌린 테스트(실측·시뮬레이션·실플레이·서버 테스트)와 결과: [TEST_LOG.md](TEST_LOG.md)

## 현재 상태 (2026-10-22)

| 항목 | 내용 |
|---|---|
| 작업 브랜치 | `wonho` (출시용, 사용자 작업 브랜치). 푸시는 `wonho`와 `main`에 같이. 챕터 3(Lv40→50)은 `ch3` 브랜치에서만(2026-10-22 분리, 백업 태그 `backup/pre-ch3-split-20261022`). `jaein`은 다른 작업자 |
| 엔진 | Unity 6000.5.9f1 (`ProjectSettings/ProjectVersion.txt`) |
| 표기 이름 | **볼론**(Bolon, 2026-10-21 사용자 결정, 마왕 이름). 타이틀 로고는 영문 "BOLON" 하나(`Art/ui_logo_bolon`, 모든 언어 공통), 화면·문서·정책·홈페이지만 바꾼다. Unity productName·세이브 경로·레포·서버 이름은 dotRPG 유지(세이브 유실 방지). 고유 이름·떡밥은 `STORY.md` 7절 |
| 게임 서버 | `server/`(Node·TypeScript). 마이그레이션 0001~0052 커밋됨(시험 서버는 0048까지). 0049_gear_inherit는 출시용 장비 계승(40→50도 같은 사유), 0050_rogue_class는 도적, 0051_chapter3_seal_exchange·0052_lord_difficulty는 챕터 3, 0053 보스 시즌 레벨은 5단계 계획, 서버 테스트 122개 파일. 단계별 현황은 `PLAN_SERVER.md`와 `Docs/server/` |
| 시험 서버 | 구글 클라우드 35.216.2.121(`https://35-216-2-121.sslip.io`), DB는 Supabase `dotrpg` 스키마. 0055까지 적용, 데이터 버전 6fd32cd2e649bfac(2026-10-26 배포). `CHAPTER3_OPEN` 미설정(닫힘). 여닫기는 `server/ops/chapter.sh open 3`. 절차는 `server/ops/README.md` 10절 |
| 정책 공개 사본 | https://pabang0620.github.io/dotrpg-policy/ (저장소 pabang0620/dotrpg-policy) |
| 홈페이지 | 로컬 `C:\Users\admin\Desktop\games\sites\dotrpg-home`(저장소 pabang0620/dotrpg-home, 비공개). OpenAI Sites 프로젝트(Next/vinext + Cloudflare), 게임 소개·스크린샷·캐릭터·스킨 화면과 로그인·가입·캐릭터 화면. 계정·OAuth 실제 연결은 아직 꺼져 있다(미설정 API는 503). 그림은 `public/assets/`(로고·배경·캐릭터·스킨·게임 캡처), 생성 기록 `docs/GENERATED_ASSETS.md`, 연결 방법 `docs/DELIVERY.md` |
| 밸런스 기준 | 총 플레이 Lv40 약 46시간, 미니 던전을 매일 하면 약 44시간(10-21 날짜 모델 수정 뒤)(`Tools/balance/theory_progress.py --measured --mini`), 무자본 골드는 Lv40에서 한 벌 값의 약 88%(`Tools/balance/theory_gold.py`), 20명 에이전트 시뮬레이션 `analysis/player-sim` v3 |
| 검증 | 컴파일과 관련 서버 테스트는 작업 묶음 끝에 확인한다. 게임 실행·빌드·배포는 요청이 있을 때만 |

## 개발·검증 방법

- 작업 방식: 브랜치는 `main`·`jaein`·`wonho` 3개뿐이고 사용자는 `wonho`에서만 작업한다. 에이전트는 파일 소유를 나눠 같은 브랜치에서 일하고, 컴파일·테스트·커밋은 묶음 끝에 메인이 한다
- 컴파일과 서버 데이터 내보내기: `Unity.exe -batchmode -nographics -quit -projectPath <경로> -executeMethod DotRPG.EditorTools.GameDataExport.Export -logFile <로그>`
- 공유 빌드: 빌드 전용 복사본 `C:\Users\admin\Desktop\games\dotRPG_apkbuild`에 `Assets`·`ProjectSettings`·`Packages`를 `robocopy /MIR`로 맞춘 뒤 `BuildScript.BuildWindowsOnline`(PC, 시험 서버 주소 기본값)과 `BuildScript.BuildAndroidDev`(APK). 공유 파일은 `games/dotRPG_online_<날짜>.zip`, `games/dotRPG_mobile_<날짜>.zip`, 바로 켤 폴더는 `games/dotRPG_play`(바탕화면 바로가기)
- 측정 빌드: `BuildScript.BuildWindowsQA` -> `Builds/WindowsQA/dotRPG.exe -dotrpgMeasure <폴더> -measureOnly hunting|dungeons|raids|bossrush` (`-measureDodge 0`이면 장판 회피 없는 옛 봇. 절차·주의는 `Docs/BALANCE_MEASURE_2026-10-16.md`)
- 자동 검증 모드: `dotRPG.exe <모드> <결과폴더>` -> `report.txt`의 `summary` 줄. `-dotrpgCapture`(전체·강화·BGM), `-dotrpgParty`, `-dotrpgDungeon`, `-dotrpgMonster`, `-dotrpgBalance`, 다른 작업자 쪽 `-dotrpgCanyon`, `-dotrpgWinter`, `-dotrpgChars`, `-dotrpgUi`, `-dotrpgDepth`, `-dotrpgStairs`, `-dotrpgSilver`
- 미리보기(에디터): `DotRPG.EditorTools.MonsterSheet.Render`(몬스터 시트), `DotRPG.EditorTools.DungeonRoomPreview.RenderAll`(던전 방)
- BGM: Flow Music 후보 생성 후 `Tools/audio/bgm_pick.py --apply`로 검증·루프·선정 (절차는 `Docs/BGM_PROMPTS.md`)
