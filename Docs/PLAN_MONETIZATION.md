# 외형 상점 적용 계획과 연동 계약

## 결정

Steam 선출시, 모바일 후속 출시. 조사 결과는 RESEARCH_MONETIZATION.md를 기준으로 한다.
첫 단계는 확정 외형 구매다. 새로운 외형 컬렉션으로 재방문할 이유를 만들고, 보유 상품의 중복 구매는 막는다.
이번 구현은 발밑 오라 3종(하늘빛 무료, 노을빛·별빛 판매 준비)과 기본 모습이다.
기존 전투, 강화 확률, 보상, 게임 골드, 세이브 버전은 바꾸지 않는다.
유료 뽑기·패스는 이번 출시 범위에 넣지 않는다. 서버·상품 운영 기반 없이 뽑기부터 열면 소유권과 비용 보장이 불완전해진다.

## 구현 범위

- 메뉴 → 외형 상점: 목록 선택, 현재 캐릭터와 오라 미리보기, 보유/착용 상태, 무료 외형 착용.
- 모든 직업 공통 영구 오라. 렌더러만 추가하며 능력치·충돌·난수에는 관여하지 않는다.
- 기본 모습이 최초 선택값이다. 외형 선택만 PlayerPrefs에 저장하고 게임 슬롯과 분리한다.
- 상품·금액을 확인한 뒤 플랫폼 결제에 진입한다. 실제 통화 가격은 플랫폼 제공자가 전달한다.
- 결제 중 중복 요청 차단, 보유 상품 재구매 차단, 취소/실패 안내, 구매 복원, 환불 후 소유권 재검증.
- 실제 제공자 미설정 상태에서는 판매 버튼을 비활성화한다. 임의 가격, 가짜 결제 성공, 클라이언트 소유권 지급은 없다.

## 구조

`CosmeticCatalog`는 외형 정의, `CosmeticStore`는 소유·선택·구매 상태, `ICommerceProvider`는 플랫폼 연동 경계다.
`CosmeticShopScreen`은 화면, `CosmeticAura`는 동일한 오라를 미리보기와 월드에 그린다.
기존 상점은 게임 골드 거래를 계속 담당한다. 기존 세이브의 마이그레이션은 필요 없다.
GameBootstrap의 UnavailableCommerceProvider를 실제 플랫폼 어댑터로 교체한다.
모든 어댑터 호출과 완료는 Unity 주 스레드 문맥에서 처리해야 한다.

## 다음 연동 작업

1. Steam App ID, 개발자 서버 주소와 인증 방법, 상품별 가격·판매 지역·연령 정책 확정.
2. Steam 클라이언트 인증 → 서버 사용자 확인 → 주문 생성 → Steam 결제 승인 → 서버 주문 검증·확정 → 소유권 조회.
3. 모바일은 Apple/Google 상품을 각각 등록하고 공식 SDK 구매·복원을 연결한다. 플랫폼 영수증/거래 검증은 서버가 담당한다.
4. 서버는 `(플랫폼, 거래 ID)`에 유일 제약을 두고 동일 거래의 재처리에도 한 번만 지급한다. 미완료 거래 재조회, 환불/취소 통지, 소유권 철회를 구현한다.
5. 상품 매핑은 `aura_sunset`, `aura_violet`. 무료 항목은 결제 상품으로 등록하지 않는다.
6. FetchAsync/RestoreAsync는 현재 로그인 계정의 **전체** 소유권 스냅샷을 반환한다. 부분 목록을 반환하면 기존 소유권이 제거된다. 로그아웃·계정 변경 시 새 CosmeticStore를 만들고 화면/오라 구독도 다시 연결한다.
7. Offer의 LocalizedPrice는 실제 통화·세금 포함 표시 가격, QuoteId는 계정·상품·가격·만료 시각에 묶인 서버 견적이다. 변경/만료 시 결제를 거절하고 다시 확인받는다.
8. PurchaseAsync는 네이티브 결제와 서버 검증까지 기다린다. 창 닫힘을 성공으로 취급하지 않는다. 취소는 OperationCanceledException, 오류는 예외로 전달한다.
9. 결제 후 응답 유실은 서버 주문 조회/복원으로 해결한다. UI의 중복 방지는 서버 멱등성의 대체가 아니다. 유효한 새 견적 발행 전 진행 중 주문부터 확인해야 한다.
10. 스토어 심사·샌드박스에서 취소, 네트워크 끊김, 재실행 복원, 환불을 검증한 뒤 판매 활성화한다. 현재는 실제 결제 SDK/서버가 없는 사전 구현이다.

유료 외형 소유권은 현재 프로세스에만 보관한다. 재시작 후 서버 확인 전에는 기본 모습으로 보이고, 상점 동기화 후 선택한 보유 외형이 돌아온다.
오프라인 유료 소유권 캐시는 실제 인증 설계 이후 결정한다. 플랫폼 간 구매 이전도 현재 보장하지 않는다.

## 조사 반영과 후속 지표

- 자율성: 구매 전 실제 외형을 보여 주고 무료 선택지와 기본 모습 복귀를 제공한다.
- 비용 예측: 확정 상품 하나를 직접 구매하고 중간 유료 재화를 넣지 않는다.
- 수집 만족: 새 외형을 추가할 때 각 상품을 직접 선택하게 한다. 허위 한정·실패 복구 판매는 사용하지 않는다.
- 추후 집계는 결제 전환율뿐 아니라 복원 실패율, 환불률, 구매 후 만족도, 재방문을 함께 본다. 이번에는 개인 추적 SDK를 추가하지 않는다.
- 향후 유료 뽑기를 검토하면 목표 아이템 실효 확률, 확정 횟수, 최대 실결제액, 중복 처리, 천장 이월, 버전별 확률 고지를 먼저 확정한다.
  기대 횟수 1/p만으로 비용을 설명하지 않는다. 고정 확률 p의 n회 실패 확률 (1-p)^n과 상위 분위 비용을 같이 계산한다.

## 최소 검증

Unity 컴파일과 CosmeticStoreChecks의 결정적 검사만 수행한다. 결제 호출, 장시간 던전 순회, 실제 과금은 하지 않는다.
검사는 기본/무료 선택, 비소유 착용 거부, 선택 유지, 로컬 값으로 유료 지급 불가, 동시 구매 차단,
서버 검증 후 소유, 중복 구매 거부, 환불 반영, 오래된 견적 거부, 취소 처리를 확인한다.
실제 플랫폼별 결제 검증과 화면·모바일 터치 실기기 검증은 연동 이후 별도로 필요하다.

### 이번 검증 결과 (2026-10-03)

Unity 6000.5.9f1 컴파일 성공. 기존 API 폐기 예정 경고 9개 외 신규 컴파일 오류 없음.
CosmeticStoreChecks: 상태 검사 12개와 화면 생성·미연동 판매 차단·무료 항목 활성화 검사 통과.
UI 크기 설정이 커져도 두 패널이 겹치지 않도록 내용 영역에 맞춰 축소한다.
실제 결제, 전체 게임 회귀, 실기기 화면 검증은 실행하지 않았다.
변경은 기존 가독성 개선 커밋을 포함하는 feature/cosmetic-store 브랜치에 보관한다.

## 에셋 개선 (2026-10-03)

main에 초기 구현을 병합·push한 뒤 상점 전용 아이콘과 별 장식 오라를 추가했다.
이미지는 내장 image_gen으로 생성했고 원본 RGBA PNG를 그대로 프로젝트에 복사했다.
Unity 임포트에서 아이콘은 최대 128px, 오라는 최대 256px, 포인트 필터·무압축·밉맵 없음으로 설정한다.
오라 중심의 투명 영역을 유지하고, 월드 너비는 기존과 같은 약 1유닛이다.

- `Assets/Resources/Art/menuicon_cosmetics.png`: 사이드 메뉴와 상점 제목의 전용 아이콘.
- `Assets/Resources/Art/fx_cosmetic_stars.png`: 노을빛·별빛 오라에 공통 적용하고 상품 색으로 틴트한다.
- 무료 하늘빛은 기존 타원 형태를 유지한다. 목록 썸네일·상세 미리보기·월드 렌더러는 같은 상품별 스프라이트 선택 함수를 사용한다.
- 생성 에셋을 불러올 수 없는 경우 오라는 기존 타원으로 대체한다. 결제·능력치·세이브 로직은 변경하지 않는다.

검증: Unity 컴파일 및 기존 상태 검사 12개·화면 생성 검사를 통과했다. 추가로 Sprite 임포트,
텍스처 최대 크기, 약 1유닛의 월드 크기, 무료/판매 상품별 에셋 분기, 미리보기 스프라이트 일치를 확인했다.
Unity는 임포트 축소 시 스프라이트의 월드 크기를 보존하므로, 오라 PPU는 원본 너비인 1774를 사용한다.
실기기 화면·실제 결제 테스트는 이번 에셋 검증에 포함하지 않았다.

### 최종 생성 프롬프트 (내장 도구)

아이콘:
> Use case: stylized-concept. Asset type: transparent pixel-art game UI icon for a cosmetic wardrobe shop in a top-down fantasy RPG. Single centered small folded indigo-violet wizard cloak with a bright cyan diamond brooch and a tiny golden four-point sparkle, readable at 40x40 pixels. Match classic handcrafted 16-bit inventory icon aesthetic: chunky deliberate pixel clusters, dark brown-purple outline, warm golden edge highlights, limited palette, front three-quarter view. Square composition, silhouette occupies 85 percent of canvas, ample transparent margin. Actual transparent background. No text, no letters, no frame, no scene, no checkerboard, no blur, no photographic rendering. Produce one isolated icon only.

오라:
> Use case: stylized-concept. Asset type: one transparent pixel-art foot aura sprite for a top-down 2D fantasy RPG, recolored at runtime. Single flat horizontal elliptical celestial magic ring viewed from above at the game's 3/4 perspective, width about twice height. Pure neutral WHITE and SILVER grayscale only, no colored pixels. Thin double elliptical outline with eight small diamond-shaped star motifs arranged on the perimeter, clear open fully transparent center so ground remains visible. Crisp chunky 16-bit pixel clusters with no fuzzy bloom. Outer ellipse and all star motifs contained inside a centered 2:1 bounding box with generous transparent margin. Quiet elegant effect that does not obscure combat, no vertical flames, no pillars. Actual transparent background everywhere outside the thin ring and stars AND inside the ring. No text, letters, runic writing, character, scenery, solid disk, checkerboard or shadow. One isolated sprite only, landscape canvas.
