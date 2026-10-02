# dotRPG 마법사·용병 4인 재제작 조사 노트 (2026-10-02)

## 0. 현재 전사 제작 방식 (로컬 파일 실측)
- 출처: Docs/SILVER_WARRIOR.txt, Docs/KIMONO_WARRIOR_PROMPTS.txt (dotRPG 레포)
- 몸체 시트: 8열x5행 (행=S/SW/E/NE/N, 열=idle2 + walk4 + 공격준비 + 피격). 나머지 3방향은 좌우 반전. 64x64 프레임, 제한 팔레트, Point 샘플링.
- 팔·소매·다리·무기는 시트에서 제외하고 코드 리그(WarriorRightHandRig, WarriorGait, WarriorAttackMotion)가 합성. 보행 8단계, 공격 24자세.
- 프롬프트 패턴: 템플릿 시트 + 컨셉 이미지 2장 입력(style-transfer), "head 45% height", "NO shadows/text/grid", "all sprites same body height, foot baseline", 투명 배경.
- 현재 마법사·용병은 CharacterLook.cs(코드 도트)로 생성: 마법사=보라 로브(116,70,190), 인디고 위저드햇(58,72,170), 은발 롱. 용병 4명은 hair/shirt/pants/hat 색 파라미터만 다르고 같은 체형 템플릿.
- MercenaryDatabase 현행: 브론(탱커, UI색 #E66E3C), 카이(근접딜러, #FFC446), 엘린(빙결 제어, #6EC8FF), 세라(번개 딜러, #FF78C8). 파티 프레임/이름 색은 이 UI색을 유지하는 것이 안전.

## 1. 마법사 디자인 사례와 원칙
- 로브가 몸선을 가려 사다리꼴(삼각) 실루엣이 되고, 모자+지팡이가 있으면 즉시 마법사로 읽힌다. 팔레트는 3~4색 고정, 지팡이 끝 보석/불꽃만 대비색으로 튀게. 천 접힘은 디더링 또는 2톤 음영. [pixelartgg wizard gallery](https://www.pixelartgg.com/gallery/wizard), [pixnote character](https://pixnote.net/en/learn/character/)
- 작은 스프라이트는 "읽히는 실루엣 1개"가 우선(마법사 모자 vs 기사 투구). 정지 컷에서 좋은 디테일이 움직이면 노이즈가 되므로 단순화. [Slynyrd Pixelblog 22](https://www.slynyrd.com/blog/2019/10/21/pixelblog-22-top-down-character-sprites), [Pixelblog 55](https://www.slynyrd.com/blog/2025/3/24/pixelblog-55-top-down-character-animation)
- Sea of Stars: 90년대 RPG 비율을 HD로, 확장된 팔레트+캐릭터 컬러 하이라이트 조명, 적은 프레임으로 표정 전달, 타격 시점이 읽히는 동작. [Megavisions 분석](https://www.megavisions.net/the-art-of-sea-of-stars-a-sea-of-pixels/)
- 확인 한계: Eastward, CrossCode, Children of Morta, Hyper Light Drifter, 던파, 메이플의 마법사 디자인을 직접 다룬 신뢰 가능한 1차 출처는 검색에서 못 찾았다. 아래 "일반 지식" 표시는 출처 없는 관례임.
  - (일반 지식) Hyper Light Drifter: 망토+단순 실루엣+고채도 한 색 포인트. Eastward: 굵은 외곽, 낮은 채도 바탕에 의상 한 곳만 고채도. 메이플/던파: 큰 머리(2~3등신), 모자와 지팡이로 직업 구분.

## 2. 마법사 애니메이션
- 걷기 4프레임(접지-통과-접지-통과), 프레임당 100~150ms, 상하 보브 1~2px. 6프레임이면 작은 스프라이트에 경제성과 부드러움의 균형. 8방향은 대칭 반전으로 5방향만 그리면 되나 비대칭 장비(한 손 지팡이)는 8방향 전부 필요. [sprite-ai animate guide](https://www.sprite-ai.art/guides/how-to-animate-pixel-art), [Slynyrd 55](https://www.slynyrd.com/blog/2025/3/24/pixelblog-55-top-down-character-animation)
- 이차 모션: 머리카락/망토는 2·4프레임만 1px 이동, 대기 호흡 시 반대 방향 1px. 로브 밑단은 몸보다 1프레임 늦게. [Slynyrd 55](https://www.slynyrd.com/blog/2025/3/24/pixelblog-55-top-down-character-animation), [sprite-ai](https://www.sprite-ai.art/guides/how-to-animate-pixel-art)
- 공격 3막: 준비 1~2 / 타격 1~2(임팩트 150~200ms 유지) / 회복 1~2. 준비는 느리게, 발사는 빠르게, 회복은 느리게. 시전 충전 사례: 6프레임x80ms. [sprite-ai principles](https://www.sprite-ai.art/guides/animation-principles), [Stormbound spells devlog](https://nogitsunegirl.itch.io/stormbound/devlog/1674125/using-pixel-spells-directions-loops-pivots-and-sprite-sheets)
- 지팡이 끝 발광: 몸 시트에 굽지 말고 별도 이펙트 레이어(스프라이트 보석 + 파티클/가산 글로우)로 분리하면 시전 중에만 밝기 변화를 줄 수 있음(전사 리그 방식과 동일 철학). 출처 없는 권고.
- 권고(전사 리그와 맞춤): 마법사도 "몸체+로브 시트 / 손·지팡이는 코드 리그" 분리. 시전 포즈는 양손 앞으로 모으기 -> 지팡이 들어올림 -> 발사 -> 회수.

## 3. 파티 동료 구분
- 고유 실루엣(키, 자세, 부피)이 기본, 색은 보조. 색만으로 구분되면 형태 체계가 약한 것. 육중한 블록형=탱커, 둥근형=힐러/주인공, 날렵=딜러. [Medium TF2/Overwatch readability](https://medium.com/@xavierck/character-readability-in-team-fortress-2-and-overwatch-68c41d454465), [Shape Language, RocketBrush](https://rocketbrush.com/blog/shape-language-in-game-character-design-how-to-make-characters-readable-and-consistent), [Power of Silhouette](https://binus.ac.id/bandung/dkv/2025/11/04/the-power-of-silhouette-designing-readable-characters-in-motion/)
- 피부·옷·머리 사이 명도 대비를 크게 해 윤곽을 선명하게. [같은 RocketBrush/Lemmasoft 팁](https://lemmasoft.renai.us/forums/viewtopic.php?t=60675)
- 같은 세계관 유지: 공통 요소 3개 고정(굵기 같은 어두운 외곽선, 머리 45% 비율, 동일 팔레트 채도 범위) + 직업별 무기 실루엣만 차별화. 주인공(검정+은색+붉은 눈)은 "저명도 저채도 몸+소수 포인트", 동료는 "중명도 고유색 1+포인트 1"로 위계.

## 4. AI 픽셀 시트 일관성 팁
- 시트 복잡도 제한: 행당 3~6개, 시트당 2~3행. 복잡할수록 일관성 급락. 정확한 해상도 지정. [Robotic Ape: Nano Banana Pro 스프라이트 교훈](https://roboticape.com/2026/03/07/generating-game-sprites-with-gemini-image-generation-nano-banana-pro-lessons-learned/)
- 배경색은 캐릭터 팔레트와 겹치지 않게(검정 외곽선+검정 배경은 분리 불가). 마젠타 사용 시 마젠타·분홍 계열 의상 금지(세라 팔레트 주의). 흰 외곽선 버퍼는 안티앨리어싱 오염 방지. HSV 크로마키 + bbox 자동 트림 + 즉시 리사이즈. [Robotic Ape](https://roboticape.com/2026/03/07/generating-game-sprites-with-gemini-image-generation-nano-banana-pro-lessons-learned/)
- Nano Banana 출력은 픽셀 그리드에 스냅되지 않음(1024px에 도트풍). 생성 후 그리드 검출, 스냅, 색 수 축소 필수. 디자인 확정을 먼저, 기술 보정은 후처리. [SpriteCook](https://www.spritecook.ai/blog/nanobanana-pixel-art-for-games)
- Nano Banana Pro는 참조 이미지 최대 14장(고충실도 6장), 레퍼런스 시트 구조를 "exactly" 따르라고 지시. 정체성 고정에 전사 시트+컨셉 이미지를 같이 첨부. [Nano-Banana Pro prompting guide](https://dev.to/googleai/nano-banana-pro-prompting-guide-strategies-1h9n), [Rosebud 가이드](https://lab.rosebud.ai/blog/how-to-create-a-sprite-sheet-with-ai-using-google-gemini-and-nano-banana-easy-guide)
- 프레임 정렬: 모든 프레임에서 실루엣 높이 정규화, 발 기준선(피벗) 고정, 셀 간격 균일. 안 하면 재생 시 흔들림. [seeles 가이드](https://www.seeles.ai/resources/blogs/how-to-animate-sprite-sheets-ai-game-development)
- 실행 체크리스트(전사 파이프라인 재사용): 컨셉 1장 확정 -> 5방향x(idle2, walk4, 시전준비, 피격) 템플릿에 style-transfer -> 마젠타 크로마키 -> 그리드 스냅/팔레트 고정(캐릭터당 12~16색) -> 발 기준선 정렬 -> 무기/손은 별도 시트.

## 5. 디자인 확정안 (전사: 검정 기모노, 붉은 눈, 은색 카타나, 흰 오비와 비교)
공통: 머리 약 45% 신장, 어두운 외곽선, 마젠타 배경 생성이므로 의상에 마젠타/핑크 계열 사용 금지(세라는 자주-버건디로 대체). 전사 실루엣=세로로 가는 직선 + 짧은 치마.

| 캐릭터 | 외형 키워드 | 실루엣 포인트 | 팔레트 HEX | 무기 |
|---|---|---|---|---|
| 마법사(주인공) | 은발 긴 머리 반묶음, 남색 야간 로브 + 금 자수 테두리, 챙 넓은 구부러진 뾰족 모자, 청록 눈, 별무늬 망토 | 넓은 사다리꼴 로브(밑단 퍼짐) + 큰 챙 모자 + 머리 위로 솟은 지팡이. 전사의 가는 직선과 정반대의 삼각 실루엣 | #1F2A5A 로브, #3B4FA8 하이라이트, #E2B84A 금, #D8DEEA 은발, #5FE0E6 보석/눈, #0E1230 외곽 | 키보다 높은 나무 지팡이, 끝에 청록 구슬+금 고리 (구슬 발광은 이펙트 레이어) |
| 브론(전사 탱커) | 단단한 체격, 짧은 갈색 머리+붉은 반다나 유지, 철 판금 어깨/가슴 갑옷, 갈색 가죽 | 정사각 블록 몸통, 과장된 큰 견갑, 가장 낮고 넓은 체형. 방패로 한쪽이 두꺼움 | #5A5F6B 철, #8E96A3 철 하이라이트, #E66E3C 포인트(UI색), #6B4A2E 가죽, #AA2828 반다나, #1A1A22 외곽 | 타워형 방패 + 짧은 도끼(둥근 방패면이 실루엣 핵심) |
| 카이(전사 딜러) | 마른 근육형, 뾰족한 검은 머리, 녹청 가죽 경갑, 허리 천 띠와 긴 목도리 | 날렵한 역삼각, 뒤로 휘날리는 긴 목도리가 이동 방향 표시. 무기가 몸 길이보다 긴 장창 | #1F8F6B 청록, #2A5E54 음영, #FFC446 포인트(UI색), #28344A 바지, #E8EEF2 붕대, #101418 외곽 | 장창(사선 보유, 창끝 금색 삼각 날, 전사 카타나와 다른 직선 장병) |
| 엘린(마법사 빙결) | 단정한 쪽머리, 연하늘 머리, 흰색 + 하늘색 로브, 눈꽃 장식 모자, 작은 체구 | 짧고 둥근 종형 로브, 챙 없는 작은 뾰족 모자 + 눈꽃 브로치. 주인공 마법사보다 작고 밝음 | #DDF0FA 모자/머리, #6EC8FF 포인트(UI색), #468CD2 로브, #284A86 음영, #FFFFFF 서리, #14264A 외곽 | 짧은 크리스털 완드(끝이 눈결정 모양 얼음 결정, 발광 하늘색) |
| 세라(마법사 번개) | 긴 금발 포니테일, 깊은 자주-버건디 로브(마젠타 피함), 날카로운 지그재그 밑단, 큰 챙 없는 앞코 뾰족 모자 | 찢어진 지그재그 로브 밑단 + 한쪽으로 기울어진 포니테일 + 번개 모양 지팡이 머리. 엘린(둥글고 밝음)과 대비되는 각지고 어두운 실루엣 | #5A1F3C 로브, #8E2F5E 하이라이트, #F0C850 금발/번개, #2A0F26 모자, #FFE96A 번개 포인트, #14080F 외곽 | 지그재그 번개형 금속 로드, 끝에 노란 스파크 구슬 |

### 확정 근거 요약
- 색: 전사(검정+흰+은+붉은 눈)는 무채색이라 동료의 유채색과 겹치지 않음. 파티 UI색을 각 캐릭터 포인트색에 그대로 사용해 프레임 줄무늬와 몸이 연결됨.
- 실루엣: 전사=세로 직선, 브론=블록, 카이=역삼각+장창, 마법사=대사다리꼴, 엘린=작은 종형, 세라=각진 지그재그. 무기도 카타나/방패+도끼/장창/수정 완드/번개 로드/긴 지팡이로 전부 다름.
- 마젠타 크로마키 충돌: 세라 로브를 분홍이 아니라 어두운 버건디(#5A1F3C)로 잡았음. 생성 시 마젠타 배경 키를 #FF00FF에서 의상색과 거리 확인.
