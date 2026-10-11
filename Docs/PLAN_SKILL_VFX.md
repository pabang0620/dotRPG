# 전직 스킬 이펙트 점검과 그림 생성 계획 (2026-10-11)

읽기 전용 점검 결과다. 코드는 바꾸지 않았다. 숫자(피해·범위)는 밸런스 작업이 따로 맡고, 이 문서는 "보이는 것"만 다룬다.

## 1. 지금 이펙트가 만들어지는 방식

| 종류 | 위치 | 설명 |
|---|---|---|
| 클립(flipbook) | `Runtime/Art/VfxArt.cs`, `VfxArt.{Fighter,Guardian,Arcanist,Bishop}.cs` | 코드로 픽셀을 그려 만든 프레임 묶음. Density 4(타일당 64픽셀), point 필터. `CareerFx.Clip("이름")`이 `VfxLibrary.Get`으로 불러 재생한다. 그림 파일이 아니다. |
| 그림 한 장 | `Resources/Art/FxImg/fxi_*.png` (11장) | 생성 그림. aegis, shield_small, bigsword, bigsword_dark, blackhole, crack, holy_sigil, life_lotus, bell, wings, meteor. `SkillFx.Spawn(SkillFx.Pick("fxi_x", 대체))`로 쓰고 없으면 코드 그림으로 대체. |
| 코드 도형 | `SkillFx`(fx_ring, fx_glow, fx_spark, fx_streak, fx_scorch...), `SkillVisuals`(Flash, Sparks, ArcBolt, MeteorFall, FrostOrbHead), `GlowLineFx`, `CareerFx.Spear/Orb/OrbTrail` | 점·원·선을 크기·색만 바꿔 쓴다. |

결론: 메이지와 비숍 이펙트 대부분이 "코드로 그린 원·선 + 몇 개의 클립"이라 모양이 비슷하고(특히 성운 폭발과 천체 붕괴가 같은 m_collapse·m_starburst를 공유), 상용 게임 같은 덩어리감(불덩이 결, 얼음 결정, 번개 갈래)이 없다.

## 2. 스킬별 판정 (29개)

판정: **OK**(그대로) / **다듬기**(코드만 손보면 된다) / **새 그림**(생성 그림이 필요하다)

### 메이지 (7개 전부 새 그림)

| 스킬 | 지금 | 판정 | 문제 |
|---|---|---|---|
| 홍련구 | m_fireball 클립 + m_explode + fx_scorch | 새 그림 | 코드 불덩이가 작고 납작해 연사기의 주인공으로 약하다. 폭발 결이 없다 |
| 빙결삼창 | CareerFx.Spear(코드 창) + fx_snow 꼬리 + m_icebloom | 새 그림 | 얼음창이 선 하나로 보인다. 결정 질감·관통 파편 없음 |
| 연쇄전격 | SkillVisuals.ArcBolt(코드 선) + m_spark | 새 그림 | 이제 연사기인데 번개가 가는 선 하나다. 갈래·두께·적중 섬광 부족 |
| 성운 폭발(차징) | 모으기: fx_ring·fx_sparkle / 폭발: m_collapse·m_starburst·impact | 새 그림 | 차징 중 손에 모이는 구슬이 없어 차징인지 모른다. 폭발이 천체 붕괴와 같은 클립 |
| 차원도약 | m_portal 2개 + m_starburst | 새 그림 | 출발·도착 표시가 작다. 잔상 폭발이 성운과 같은 클립 |
| 중력 균열 | fxi_blackhole 회전 + fx_glow | 새 그림 | 블랙홀 한 장이 돌기만 한다(사용자가 회전 연출을 싫어함). 바닥에 열린 균열로 안 보인다 |
| 천체 붕괴(각성) | MeteorFall(코드) + FrostOrbHead + CareerFx.Orb + m_collapse | 새 그림 | 세 원소 운석이 코드 도형이라 각성기다운 무게가 없다. 붕괴가 성운과 같다 |

### 비숍 (OK 2, 다듬기 1, 새 그림 4)

| 스킬 | 지금 | 판정 | 문제 |
|---|---|---|---|
| 치유의 깃 | b_feather 클립(코드 깃털) | 새 그림 | 깃털이 작은 점처럼 보인다 |
| 생명의 파문 | fxi_life_lotus + fx_ring | OK | 생성 그림이 있고 이번에 회전도 뺐다 |
| 정화의 종 | fxi_bell + b_bell | OK | |
| 심판의 광창 | b_spear 클립 | 새 그림 | 연사기인데 창이 가는 선 |
| 신의 가호 | b_pillar + b_wings + 범위 원 + 머리 위 글자 | 다듬기 | 이번에 범위 원·글자를 넣었다. 기둥 클립만 조금 크게 |
| 축복의 연결 | b_cross + fx_glow | 새 그림 | 아군 사이를 잇는 빛 사슬이 없다 |
| 천상의 행진(각성) | b_pillar + fxi_wings + fxi_holy_sigil | 새 그림 | 하늘에서 내려오는 빛기둥이 코드 클립이라 약하다 |

### 파이터 (OK 4, 다듬기 2, 새 그림 2)

| 스킬 | 지금 | 판정 | 문제 |
|---|---|---|---|
| 십자참 | f_arc, f_x | OK | 픽셀 베기 클립이 화풍과 맞는다 |
| 섬광보(Shift) | Ghost 잔상, f_line, f_cut | OK | |
| 검귀 해방 | PowerAura 2초, f_arc 4방향 | 다듬기 | 이제 60초 버프라 켤 때 한 번 크게: 몸에서 위로 솟는 붉은 기운 클립 정도 |
| 파쇄 검기 | f_wave(초승달) | 다듬기 | 연사기라 괜찮지만 검기 태세도 같은 f_wave라 구분이 안 된다(색만 다름) |
| 일섬 | f_line, f_cut | OK | |
| 단죄 | f_vslash, impact, Crack | OK | |
| 천검귀일(각성) | fxi_bigsword, fxi_bigsword_dark | OK | 생성 그림 있음 |
| 검기 태세 | f_wave 재사용 | 새 그림 | 기본 공격이 바뀌는 태세인데 파쇄 검기와 같은 그림 |

### 수호자 (OK 5, 새 그림 2) - 이번에 코드 개편 완료

| 스킬 | 판정 | 비고 |
|---|---|---|
| 강철의 보루 | 새 그림 | 코드 돔(g_dome)이 납작하다. 육각 방벽 돔 그림이 있으면 탱커 느낌이 확 산다 |
| 회귀의 방패 | OK | fxi_aegis 회전 비행 |
| 수호의 맹세 | OK | g_ward 반경 원 |
| 대지의 호령 | OK | 충격 링 |
| 방패 강타 | OK | |
| 응보의 방진 | OK | |
| 천쇄방패(각성) | 새 그림 | 사슬이 코드 클립(g_chain). 금빛 빛사슬 그림 필요 |

**합계: OK 11, 다듬기 3, 새 그림 15** (메이지 7, 비숍 4, 파이터 2, 수호자 2)

## 3. 생성할 그림 목록

### 3.1 배경 규칙 (중요)

- **빛·불·번개·마법처럼 빛나는 것**은 **순수 검정(#000000) 배경**으로 뽑고 게임에서 Additive(더하기) 재질로 그린다. 검정은 더하면 사라지므로 키잉이 필요 없고, 가장자리 번짐이 자연스럽다. 마젠타 키로 뽑으면 빛 번짐에 분홍 테두리가 남는다.
- **얼음 결정·바위 운석·깃털·사슬처럼 단단한 물체**는 **마젠타(#FF00FF) 배경**으로 뽑고 `process_generated.py`의 `key()`로 알파를 만든다.
- 시트는 칸 사이에 넓은 여백(검정 또는 마젠타)을 두고 "N columns x M rows, evenly spaced, same size frames, no frame borders, no text"를 꼭 넣는다. Flow 결과는 1376x768 또는 1024x1024 JPEG다.
- 화풍: 캐릭터는 64px 2등신 도트(ppu36), 이펙트는 Density 4 HD 도트. 프롬프트 공통 꼬리: `pixel art, crisp hard pixel edges, limited palette, stepped shading, no anti-aliasing blur, 16-bit action RPG skill effect, game asset`.

### 3.2 메이지 (우선)

| id | 쓰는 곳 | 종류 | 배경 |
|---|---|---|---|
| `m2_fireball` | 홍련구 비행 | 시트 6프레임 4x2 칸(2칸 비움), 프레임 128px, 오른쪽을 향함 | 검정 |
| `m2_fire_explosion` | 홍련구 폭발, 천체 붕괴 불 운석 착지 | 시트 8프레임 4x2, 192px | 검정 |
| `m2_ice_spear` | 빙결삼창 창 | 한 장 256x64, 오른쪽을 향함 | 마젠타 |
| `m2_ice_shatter` | 빙결 적중·빙결 | 시트 6프레임 3x2, 128px | 마젠타 |
| `m2_lightning_bolt` | 연쇄전격 줄기(늘려 쓴다) | 시트 4프레임 1x4 세로 쌓기, 256x64 | 검정 |
| `m2_lightning_hit` | 연쇄전격 적중 섬광 | 시트 6프레임 3x2, 128px | 검정 |
| `m2_charge_orb` | 성운 폭발 차징(손에 모이는 구슬, 반복) | 시트 8프레임 4x2, 128px | 검정 |
| `m2_nebula_burst` | 성운 폭발 터짐 | 시트 10프레임 5x2, 256px | 검정 |
| `m2_blink` | 차원도약 출발·도착 | 시트 8프레임 4x2, 128px | 검정 |
| `m2_rift` | 중력 균열(바닥에 열린 균열, 반복) | 시트 8프레임 4x2, 256x160(위에서 본 눌린 타원) | 마젠타 |
| `m2_meteor_fire` / `m2_meteor_ice` / `m2_meteor_storm` | 천체 붕괴 세 운석 | 각 한 장 128x192, 왼쪽 위에서 오른쪽 아래로 떨어지는 대각선, 꼬리 포함 | 마젠타 |
| `m2_cataclysm_collapse` | 천체 붕괴 마지막 붕괴 | 시트 10프레임 5x2, 384px | 검정 |

프롬프트(영어, 그대로 붙여 넣기):

- **m2_fireball**: `Sprite sheet of a flaming fireball projectile flying to the right, 6 frames in a 4 columns x 2 rows grid (last two cells empty), each frame 128x128, evenly spaced with wide black gutters, pure solid black #000000 background. Bright yellow-white core, orange and crimson flames streaming backward into a short tail, small embers. pixel art, crisp hard pixel edges, limited palette, stepped shading, 16-bit action RPG skill effect, game asset, no text, no frame borders.`
- **m2_fire_explosion**: `Sprite sheet of a fiery explosion, 8 frames in a 4x2 grid, each 192x192, evenly spaced, pure black #000000 background. Frame 1 small white flash, frames 2-4 expanding orange fireball with rolling flame petals, frames 5-8 dark red smoke ring and fading embers. pixel art, crisp hard pixel edges, limited palette, stepped shading, 16-bit action RPG skill effect, game asset, no text, no frame borders.`
- **m2_ice_spear**: `A single ice spear projectile pointing right, 256x64, on a solid magenta #FF00FF background. Long translucent cyan crystal shard with a sharp faceted tip, inner white highlights, small frost crystals trailing behind. pixel art, crisp hard pixel edges, limited palette (white, pale cyan, cyan, deep blue), stepped shading, 16-bit action RPG, game asset, no text.`
- **m2_ice_shatter**: `Sprite sheet of an ice impact shattering, 6 frames in a 3x2 grid, each 128x128, solid magenta #FF00FF background. Cyan crystal spikes burst outward then break into flying shards and frost mist. pixel art, crisp hard pixel edges, limited cold palette, 16-bit action RPG skill effect, game asset, no text, no frame borders.`
- **m2_lightning_bolt**: `Sprite sheet of a horizontal lightning bolt segment, 4 frames stacked vertically (1 column x 4 rows), each 256x64, pure black #000000 background. Jagged branching electric arc from left edge to right edge, white-hot core with electric blue and violet glow, each frame a different branch pattern for flicker. pixel art, crisp hard pixel edges, limited palette, 16-bit action RPG skill effect, game asset, no text.`
- **m2_lightning_hit**: `Sprite sheet of an electric impact burst, 6 frames in a 3x2 grid, each 128x128, pure black background. White flash with radiating blue-violet lightning forks that crackle and fade. pixel art, crisp hard pixel edges, 16-bit action RPG skill effect, game asset, no text, no frame borders.`
- **m2_charge_orb**: `Sprite sheet of a magic energy orb gathering power (looping), 8 frames in a 4x2 grid, each 128x128, pure black background. Violet and magenta arcane sphere with a bright white core, small star particles spiralling inward, pulsing larger each frame then looping. pixel art, crisp hard pixel edges, limited palette, 16-bit action RPG skill effect, game asset, no text, no frame borders.`
- **m2_nebula_burst**: `Sprite sheet of a cosmic nebula explosion, 10 frames in a 5x2 grid, each 256x256, pure black background. A violet-magenta nebula cloud bursts outward from a white star core, swirling gas, scattered tiny stars, expanding ring, then fading. pixel art, crisp hard pixel edges, limited palette (white, pink, magenta, violet, deep indigo), 16-bit action RPG skill effect, game asset, no text, no frame borders.`
- **m2_blink**: `Sprite sheet of a teleport blink effect, 8 frames in a 4x2 grid, each 128x128, pure black background. A vertical violet rune portal opens, a silhouette-shaped flash of arcane light, particles collapse inward and the portal closes. pixel art, crisp hard pixel edges, 16-bit action RPG skill effect, game asset, no text, no frame borders.`
- **m2_rift**: `Sprite sheet of a gravity rift torn open on the ground seen from a top-down 3/4 view (flattened ellipse), 8 looping frames in a 4x2 grid, each 256x160, solid magenta #FF00FF background. Dark void crack in the floor with a violet glowing rim, debris and pebbles being pulled toward the center, faint purple lightning along the edge. No spinning spiral. pixel art, crisp hard pixel edges, limited palette, 16-bit action RPG skill effect, game asset, no text, no frame borders.`
- **m2_meteor_fire / ice / storm** (각각 따로 생성): `A single falling meteor, 128x192, falling diagonally from the top-left toward the bottom-right, solid magenta #FF00FF background. [FIRE: a molten rock wrapped in orange flames with a long fire tail] [ICE: a jagged cyan ice boulder with a frosty white vapour tail] [STORM: a dark stone crackling with blue-violet lightning and a sparking tail]. pixel art, crisp hard pixel edges, limited palette, 16-bit action RPG, game asset, no text.`
- **m2_cataclysm_collapse**: `Sprite sheet of a celestial collapse finisher, 10 frames in a 5x2 grid, each 384x384, pure black background. Fire, ice and lightning energies spiral into a violet singularity, implode to a white point, then explode in a huge shockwave ring with star shards. pixel art, crisp hard pixel edges, limited palette, 16-bit action RPG ultimate skill effect, game asset, no text, no frame borders.`

### 3.3 비숍

| id | 쓰는 곳 | 종류 | 배경 |
|---|---|---|---|
| `b2_feather` | 치유의 깃 | 한 장 96x48, 오른쪽을 향함, 빛 꼬리 | 마젠타 |
| `b2_light_spear` | 심판의 광창 | 한 장 256x48 | 검정 |
| `b2_bless_link` | 축복의 연결(아군 사이 빛 사슬, 늘려 씀) | 시트 4프레임 1x4, 256x48 | 검정 |
| `b2_dawn_pillar` | 천상의 행진 빛기둥 | 시트 8프레임 4x2, 128x384 | 검정 |

- **b2_feather**: `A single glowing white angel feather flying to the right, 96x48, solid magenta #FF00FF background, soft gold edge light, short sparkling gold trail behind. pixel art, crisp hard pixel edges, limited palette (white, cream, gold), 16-bit action RPG, game asset, no text.`
- **b2_light_spear**: `A single holy light spear pointing right, 256x48, pure black background, white-gold blade of light with a bright tip and radiant streaks, faint halo. pixel art, crisp hard pixel edges, limited palette (white, pale gold, gold), 16-bit action RPG skill effect, game asset, no text.`
- **b2_bless_link**: `Sprite sheet of a horizontal holy light chain beam, 4 frames stacked vertically (1x4), each 256x48, pure black background, glowing golden links of light with small cross sparkles flowing left to right. pixel art, crisp hard pixel edges, 16-bit action RPG skill effect, game asset, no text.`
- **b2_dawn_pillar**: `Sprite sheet of a divine light pillar descending from the sky, 8 frames in a 4x2 grid, each 128x384 (tall), pure black background. A thin beam appears, widens into a radiant white-gold column with falling feathers and sparkles, then fades. pixel art, crisp hard pixel edges, limited palette, 16-bit action RPG ultimate skill effect, game asset, no text, no frame borders.`

### 3.4 파이터

| id | 쓰는 곳 | 종류 | 배경 |
|---|---|---|---|
| `f2_break_crescent` | 파쇄 검기 | 시트 6프레임 3x2, 192px, 오른쪽을 향함 | 검정 |
| `f2_stance_wave` | 검기 태세 기본 공격 검기 | 시트 6프레임 3x2, 160px | 검정 |

- **f2_break_crescent**: `Sprite sheet of a sword energy crescent wave flying right, 6 frames in a 3x2 grid, each 192x192, pure black background. A large steel-blue and white crescent blade of wind with sharp edges and speed lines, slight shimmer per frame. pixel art, crisp hard pixel edges, limited palette (white, ice blue, steel blue, navy), 16-bit action RPG skill effect, game asset, no text, no frame borders.`
- **f2_stance_wave**: `Sprite sheet of a thin fast azure sword-qi slash projectile flying right, 6 frames in a 3x2 grid, each 160x160, pure black background. A narrow glowing azure arc with a bright white edge and trailing blue sparks, lighter and quicker looking than a heavy crescent. pixel art, crisp hard pixel edges, 16-bit action RPG skill effect, game asset, no text, no frame borders.`

### 3.5 수호자

| id | 쓰는 곳 | 종류 | 배경 |
|---|---|---|---|
| `g2_barrier_dome` | 강철의 보루 방벽 | 시트 8프레임 4x2, 192px | 검정 |
| `g2_gold_chain` | 천쇄방패 속박 사슬 | 한 장 256x32(가로, 늘려 씀) | 마젠타 |

- **g2_barrier_dome**: `Sprite sheet of a protective hexagon barrier dome forming around a character (character not drawn), 8 frames in a 4x2 grid, each 192x192, pure black background. Teal hexagonal energy tiles assemble from the ground up into a translucent dome with gold edges, flash, then hold. pixel art, crisp hard pixel edges, limited palette (teal, cyan, gold, white), 16-bit action RPG skill effect, game asset, no text, no frame borders.`
- **g2_gold_chain**: `A single horizontal chain of golden light, 256x32, solid magenta #FF00FF background, heavy glowing gold links with teal inner glow. pixel art, crisp hard pixel edges, 16-bit action RPG, game asset, no text.`

## 4. 넣는 방법 (구현 단계, 이번 점검에서는 하지 않음)

1. **생성**: game-asset-artist(flow-nanobanana) 한 세션에서 순서대로. 원본은 `.playwright-mcp/dotrpg_refs/fx/`에 보관. 연속 제출 간격 규칙을 지킨다.
2. **처리 스크립트 새로 만들기 `Tools/art/process_fx.py`**:
   - 검정 배경: 알파 = RGB 최댓값, 색 = RGB / 알파(미리 곱한 색 풀기), Additive 표시.
   - 마젠타 배경: `process_generated.py`의 `key()` 재사용.
   - 격자 자르기: 시트를 열x행으로 나누되 칸마다 실제 그림 경계로 다시 잘라 같은 크기 캔버스 가운데에 놓는다(Flow는 칸 간격이 고르지 않다). 지정한 프레임 수만 쓴다.
   - 저장: `Assets/Resources/Art/VfxImg/<id>.png`(가로 한 줄 스트립) + `<id>.json`(frames, frameW, frameH, pivot, additive).
3. **불러오기**: `VfxLibrary.Get(name)`이 먼저 `Art/VfxImg/<name>` 스트립을 찾고, 없으면 지금 코드 그림(VfxArt)으로 대체. 생성 그림 클립은 point 필터 + HD 재질, additive면 `FxMaterials.Additive`. 기존 `CareerFx.Clip` 호출부는 이름만 바꾸면 된다(예: `m_fireball` -> `m2_fireball`, 없으면 옛 클립).
4. **배선**: 스킬별로 클립 이름 교체, 줄기형(번개·사슬·빛 사슬)은 길이에 맞춰 가로로 늘린다. 운석 3종은 `MeteorFall`·`FrostOrbHead`·`CareerFx.Orb` 대신 그림 한 장을 낙하시킨다.
5. **크기 기준**: 생성 그림 1프레임 128px = 2타일(64 art px/타일). 범위 스킬은 실제 반경에 맞춰 Scale.

## 5. 우선순위

1. 메이지 연사기 3종(홍련구, 연쇄전격, 빙결삼창) - 가장 자주 보인다
2. 메이지 성운 폭발 차징 구슬 + 폭발(차징인지 모른다는 사용자 지적)
3. 천체 붕괴 운석 3 + 붕괴, 중력 균열, 차원도약
4. 비숍 광창(연사기) -> 행진 빛기둥 -> 깃털 -> 빛 사슬
5. 파이터 검기 2종, 수호자 방벽 돔·사슬
