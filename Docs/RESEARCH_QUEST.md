# 퀘스트 시스템 조사 노트 (2026-10-02)
표기: [확인]=출처에서 확인, [추측]=확인 못 함, 설계 제안

## 1. 던전앤파이터 퀘스트 구조
- [확인] 2015-01 스토리 재정비로 일반/수련/반복 퀘스트가 사라지고 에픽 퀘스트가 세분화. 에픽 퀘스트는 스토리 진행을 맡고 진행도에 따라 에어리어 내 던전이 순차 해금.
  https://namu.wiki/w/던전앤파이터/퀘스트
- [확인] 에어리어 에피소드 전부 클리어 시 모험 퀘스트 해금. 일일 미션은 매일 첫 접속 시 자동 수락(2019-07 패치로 삭제). 2017 오리진 패치에서 업적/일일 등을 "미션"으로 통합.
  https://namu.wiki/w/던전앤파이터/퀘스트/모험 , https://namu.wiki/w/던전앤파이터/퀘스트/미션
- [확인] 분류: 에픽(Act) / 각성 / 서브(레이드 입장 해금, 특수 아이템, 캐릭터 서사) / 특수장비 퀘스트(필수, 슬롯 해금; 특정 보스+재료) / 헬파티 퀘스트(NPC 대화만으로 상위 난이도 개방).
- [확인] 퀘스트 완료 시 퀘스트북에서 노란색 [완료] 표시, 느낌표 알림 아이콘으로 완료 여부 확인. NPC 머리 위에 퀘스트 종류별 아이콘.
  https://df.nexon.com/guide?no=1534 (검색 요약 기준)
- [추측] 색 규칙(노랑 느낌표=수락 가능, 물음표=완료 가능)은 공식 문서에서 확인 못 함. MMO 관례로 가정.
- [확인] 레이드 입장 구조: 명성/퀘스트 조건, 요일 개방, 주간 횟수. 예) 안개신 무: 토~수 입장, 주 1회, 목 06시 초기화, 실패 시 횟수 복구, 입장 퀘스트 "깨어난 숲으로 갈 준비"를 NPC 카밀라에게. 보상=상위 장비 재료+레이드 상점 화폐.
  https://df.nexon.com/guide?no=1420 , https://namu.wiki/w/레이드(던전앤파이터)
- [확인] 아포칼립스 안티엔바이 주간 3회(명성 73,993), 안개신 채널 명성 58,087.
  https://df.nexon.com/guide?no=1534

## 2. 로스트아크 레이드 해금
- [확인] 군단장 레이드: 캐릭터별 주 1회, 수 06시 초기화. 6명 레이드, 관문 2~4개(발탄 2관문 8인, 쿠크 3관문 4인 등). 아이템레벨 관문별 상승(예 아브렐슈드 노말 1~2관문 1490, 3~4 1500, 5~6 1520).
  https://lostark.game.onstove.com/GameGuide/Pages/군단장 레이드 , https://www.gametoc.co.kr/news/articleView.html?idxno=71160
- [확인] 관문 저장: 관문 클리어 시 보상 획득과 함께 저장, 다음 주에 이어서 진행. 도중 중단해도 횟수 차감(어비스).
- [확인] 어비스 레이드 해금: 전투레벨 50 + 템렙 1370 + 해금 퀘스트 완료, 주 1회. 템렙 1475 이상이면 골드 보상 불가.
  https://lostark.game.onstove.com/GameGuide/Pages/어비스 레이드
- [확인] 아브렐슈드 하드는 템렙 1540~1560 관문별 요구, 입장권은 카오스 던전 "공허"에서 획득, 4관문은 2주 단위 도전 기간.
  https://lostark.game.onstove.com/News/GMNote/Views/1216
- [확인] 골드 획득은 엔드콘텐츠 전체에서 주 3회 한도.
  https://namu.wiki/w/군단장 레이드 (404/403 일부, 검색 요약 기준)
- [추측] "하위 레이드 보상이 상위 레이드 입장 재료" 구조는 던파(장비 재료)와 로아(입장권 드랍) 양쪽에서 부분 확인. 정확한 수치 공식은 확인 못 함.

## 3. 스토리 픽셀 RPG
- Stardew Valley [확인]: 스토리 퀘스트는 우편 수신, 기한 없음. Help Wanted는 게시판 일일 생성, 2일 기한. 목표 5종(채집, 몬스터, 낚시, 인사, 배달). 저널(F키)에서 확장 보기, 진행률 표시, 포기 가능(패널티 없음). 특별 주문 7~28일 기한, 자동 완료.
  https://stardewvalleywiki.com/Quests
- CrossCode [확인]: NPC와 게시판에서 수락, 캐릭터 메뉴 퀘스트 탭에서 즐겨찾기한 퀘스트만 HUD 표시. 맵 마커는 의도적으로 없고 후에 사용자 배치 "스탬프"만 추가.
  https://steamcommunity.com/app/368340/discussions/1/1291817837629635260/ , https://crosscode.fandom.com/wiki/Quests
- Sea of Stars [확인, 2차자료]: 저널 없음, 지도에 메인 퀘스트 마커, 목적 잊으면 컷신으로 재안내. 전투는 접촉 또는 컷신 직후 개시. 디스크1 최종보스 후 스토리 이벤트, 보스전 직전 파티 강제 교체.
  https://www.thereviewgeek.com/seaofstars-walkthrough-fleshmancerslair/
- Eastward [확인]: 컷신 비중 큼(연극식). 퀘스트 목표 UI 세부는 확인 못 함 [추측: 불필요].
  https://www.nintendolife.com/reviews/switch-eshop/eastward
- 강제 패배 연출 [확인]: 두 방식 = 컷신 보스(결과 조작 불가) / 절망적 보스전(피해 불가 또는 HP 하한). 플레이어가 "무의미"하다고 느끼면 조기 포기 유도라는 비판. Halo Reach처럼 점진적 인식이 잘된 사례.
  https://tvtropes.org/pmwiki/pmwiki.php/HopelessBossFight/RolePlayingGames , https://www.resetera.com/threads/scripted-losses-in-jrpgs.146858/
- 컷신 구성(대화+이동+페이드)은 일반 관례. 전용 출처 확인 못 함 [추측]. Cutscene 개요: https://en.wikipedia.org/wiki/Cutscene

## 4. 데이터 설계 관례
- 상태 [확인]: LOCKED, HIDDEN, AVAILABLE, OFFERED, ACCEPTED, ACTIVE, READY_TO_TURN_IN, COMPLETED, FAILED, ABANDONED, EXPIRED, SUSPENDED. 단순형은 pending/active/completed.
  https://github.com/The-Nexus-Decoded/The-Nexus/issues/503
- 데이터 [확인]: QuestDefinition(불변) / QuestInstance(플레이어 상태) / 이벤트 버스 / 조건 평가기 분리. ID, 표시정보, 선행조건(퀘스트 ID 배열, 레벨), 목표 배열, 보상(아이템, XP, 재화, 평판, 해금).
  https://betterprogramming.pub/implementing-a-scalable-quest-system-7f36ea4cfe22 , https://medium.com/object-oriented-worlds/implementing-quest-systems-in-ue5-blueprints-47ea0ac00599
- 목표 타입 [확인]: kill, collect, location(도달), interact, custom(이벤트); Quest Manager는 Deliver, Talk, Kill, Craft, Gather, Find, Discover Area, Use Item/Skill, Level Up, Complete Narrative 등 15+. 목표 순서: 순차/병렬/N중 M.
  https://gamesbyhyper.com/docs/exploration-and-narrative/quest-manager/
- Quest Machine 매뉴얼: https://www.pixelcrushers.com/quest_machine/Quest_Machine_Manual.pdf
