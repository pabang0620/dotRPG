# 정리 계획 (2026-10-30)

dotRPG 레포의 안 쓰는 그림과 레포 밖 작업 폴더(아트 작업본, 빌드 폴더, 측정 데이터)를 지운다.
기준: 코드·데이터·이름 규칙 어느 것으로도 만들어지지 않는 파일만 지운다. 애매하면 남기고 아래 "남김" 표에 적는다.
커밋은 하지 않는다(레포 삭제는 `git rm`으로 스테이징만).

## 확인 방법

- 레포 그림 7,295장 전부를 스크립트로 대조했다(스크래치 `refscan.py`, `rt.py`)
  - 말뭉치: `Assets/Scripts`(Editor 포함/제외 두 번), `Assets/Resources` 텍스트, `Assets/StreamingAssets`, `server/data`, `server/src`, `Tools`
  - 인정 규칙: 파일 이름 그대로 등장 / 장면·프리팹 GUID 참조 / `char_<id>_<방향>_<프레임>`의 id 등장(장비 접미사 `_tN_bN` 제거 포함) / 접두어 + id 조합(`eqicon_`, `gem_`, `career_`, `cutin_`, `banner_`, `ach_`, `node_`, `pad_`, `fboss_icon_`, `quest_mark_`, `maticon_`, `icon_` 등)의 id 등장
  - 스킨 시트 폴더는 `SkinCatalog`이 `skin_` 뒤를 시트 이름으로 쓰는 규칙(`id.Substring("skin_".Length)`)까지 반영
  - 런타임 코드에 없는 이름은 로더를 직접 읽어 다시 판정했다(아래 A, B)
- 아트 작업본은 MD5로 레포 파일과 대조했다. 레포와 다르면 다른 세트 staging과도 대조해 "더 새 세트로 교체됨"인지, `git cat-file -e`로 옛 판이 git 이력에 있는지 확인했다
- 측정 폴더는 `dotRPG/Docs`·`README.md`에서 폴더 이름을 검색했다

## A. 레포: 안 쓰는 그림 (실행, `git rm`, .meta 포함)

| 파일 | 크기 | 안 쓰는 이유 |
|---|---|---|
| Assets/StreamingAssets/HuntingScenery/forest_relic.png | 2.3 MB | `HuntingScenery.Dress`가 숲 필드는 건너뛰고 `winter_relic`/`canyon_relic`만 불러 옴 |
| Assets/Resources/Art/worldmap_atlas.png | 1.4 MB | 월드 지도는 `WorldAtlasArt`가 `StreamingAssets/WorldAtlas/*.png`를 씀. 런타임 참조 없음(Editor 임포트 설정의 이름 비교만 남음, 파일 없어도 문제 없음) |
| Assets/Resources/Art/icon_carrot.png | 5 KB | 당근 아이템 삭제(d214eb6a, DB 보유분 정리 마이그레이션 0032) |
| Assets/Resources/Art/icon_wood.png | 7 KB | 목재 아이템 삭제(같은 커밋) |
| Assets/Resources/Art/maticon_grey_seal.png | 6 KB | 3장 재료 아이콘, 키를 쓰는 아이템 없음(잿빛 인장은 `maticon_raid_kamatz`) |
| Assets/Resources/Art/maticon_pebble.png | 6 KB | 3장 재료 아이콘, 키를 쓰는 아이템 없음(조약돌은 `c3_pebble` 오브젝트) |
| Assets/Resources/Art/maticon_record_piece.png | 8 KB | 같음(기록 조각은 `c3_record_piece` 오브젝트) |

합계 7장, 약 3.7 MB. `icon_carrot`/`icon_wood`의 .meta는 작업 트리에 수정분이 있어 `git rm -f`로 지운다.

## B. 레포: 남김 (사용자 판단 필요)

| 파일 | 남긴 이유 |
|---|---|
| Art/town_ccrate.png, Art/wnt_crate.png | 2026-10-12에 상자를 모든 맵에서 뺐지만 Editor 메뉴 `dotRPG/Art/Import Generated Village Art`(GeneratedArtImport.Props)가 두 파일을 목록에 두고, 없으면 예외를 던진다. 지우려면 그 목록과 `Tools/art/process_generated.py`도 함께 고쳐야 한다 |
| SilverWarrior/katanas.png | `katanas_v2`가 없을 때 쓰는 코드상 대체 경로 |
| SilverWarrior/Skins/w_snowblade, w_voidblade, w_phoenix | `legacySheet`로 지정됨(새 시트가 없을 때 대체) |
| Rogue/Skins/r_butterfly, r_teal, r_ivory, r_plum, r_slate, r_sand (v2가 따로 있음, 합계 약 0.9 MB) | 게임 코드는 `_v2` 시트만 부른다(`SkinCatalog.Tiers` Def). 다만 다시 칠하기 도구가 옛 판을 기준으로 쓸 수 있어 지울지 판단 필요 |
| Rogue/Skins/r_navy | 게임은 `r_navy_v2`를 쓰지만 `Tools/art/rogue_skin_recolor.py`가 머리 마스크 기준으로 읽는다 |

그 외 7,280여 장은 모두 코드·데이터·규칙으로 이름이 만들어져 남긴다(장비 `eqicon_band89_*`는 `GearCatalog` 접두어 규칙, `career_arcanist`/`bishop` 프레임은 `"career_" + 직업` 규칙 등).

## C. 아트 작업본 `dotRPG_art_work/` (git 아님)

| 대상 | 파일 수 / 크기 | 확인 | 조치 |
|---|---|---|---|
| skill_icons_2026-10-30/staging | 87 / 2.7 MB | 87개 모두 레포와 바이트 동일 | 삭제 |
| quality_2026-10-30/staging | 78 / 3.1 MB | 41개 레포와 동일, 37개(gem_*, career_a/n_*)는 skill_icons 판으로 교체되어 레포 = skill_icons staging | 삭제 |
| icons_2026-10-29/staging | 5개 동일 | 레포와 동일 | 5개 삭제 |
| icons_2026-10-29/staging 나머지 7개 | career_card_* 6(장당 약 2.6 MB 고해상도), quest_mark_turnin_check 1 | career_card는 레포의 작은 판(약 230 KB)과 다르고 어느 작업본과도 일치하지 않고 옛 판은 git 이력에 없음. quest_mark_turnin_check는 레포에 없음(설치 안 됨) | 남김 |
| guardian_shield_2026-10-30/set2/staging | 3 / 56 KB | fxi_shield_small 동일, buff_shield·fxi_aegis는 quality 판으로 교체됨 | 삭제 |
| skins_redo/staging 그림 | 302장 / 약 6.3 MB | 302장 모두 레포와 동일 | 그림만 삭제(DEST.txt 37개는 남김) |
| 2026-10-26/final, backups | 17 / 5.8 MB | 2장은 레포와 동일, 나머지는 이후 세트로 교체됨. 17개 모두 옛 판이 git 이력에 있음 | 삭제 |
| 생성 원본·중간본: quality/raw, quality/tmp, skill_icons/raw, raw_codex, raw_codex_v1, raw_codex_v2, tmp, guardian_shield/set2/raw, 2026-10-26/generated | 약 280 MB | 설치된 세트의 생성 원본·잘라내기 전 중간본. 결과물은 레포에 있음 | 삭제 |
| 남기는 것 | | AUDIT.md, jobs*.json, codex_jobs*.json, meta.json, manifest.json, validation.json, targets.json, gen*.log, tools/, review/(모음판), refs/, contact-sheet.png, references.png, skins_redo의 TODO·status·board·logs | 남김 |
| gacha_vfx_2026-10-30 전체 | 14 MB | final·upscale mp4가 레포 `Resources/Video/summon_*.mp4`와 바이트가 다름(설치 때 다시 인코딩된 것으로 보임). 레포 판과 같다는 확인이 안 됨 | 남김 (판단 필요) |

## D. 빌드 폴더 `dotRPG_apkbuild/` (git 아님)

| 대상 | 크기 | 조치 |
|---|---|---|
| Builds/WindowsQA | 약 1.0 GB | 삭제. `ninja_1030c_build.ps1`(BuildWindowsQA)로 다시 만들 수 있음 |
| Builds/WindowsOnline | 약 1.0 GB | 남김(최신) |
| 루트 로그 build_pc_1030*.log, export_final*.log | 216 KB | 삭제 |
| Logs/ 안 Unity 로그 22개 | 11 MB | 삭제(Unity가 다시 만듦) |
| build_pc_1030.ps1, build_pc_1030b.ps1, measure_1030.ps1 | 작음 | 삭제(c판·b판이 같은 내용의 최신본) |
| build_pc_1030c.ps1, measure_1030b.ps1 | | 남김(템플릿) |
| ninja_1030c_build.ps1, ninja_1030c_measure.ps1 | | 남김. WindowsQA 재빌드용이고 measure_1030b보다 새것이라 어느 쪽을 템플릿으로 둘지 판단 필요 |
| export_enh.done | | 남김(다른 스크립트의 완료 표시일 수 있음) |

## E. 측정 데이터 `analysis/player-sim/data/` (git 아님)

남김: measure_20261030, measure_20261030b, measure_20261030c, measure_20261027_step60_final, dotrpg-measured.json, Docs에 이름이 나오는 폴더(measure_20261026_recheck*, measure_20261027_ninja_leap_fixture, measure_20261027_reach_final, measure_ch3_20261024, measure_run4, measure_run5), json 파일 전부.

삭제(Docs 참조 0건): measure_20261027_consumption_final, measure_20261027_final, measure_20261027_ninja_leap60, measure_20261027_ninja_leap60_fixed, measure_20261027_portal_probe, measure_20261027_recovery_full, measure_20261027_recovery_probe, measure_20261027_recovery_smoke, measure_20261027_step60_probe. 9개 폴더, 122개 파일, 약 0.9 MB. consumption_final·final은 `analysis/player-sim/tools/run-*-20261027.ps1`의 출력 위치일 뿐이라 다시 돌리면 생긴다.

## 손대지 않음

dotRPG_play/dotRPG_1030c, dotRPG_online_20261030c.zip, bonghwa, geodraw, 그 밖의 프로젝트 폴더, dotRPG_apkbuild/Library·Assets(빌드 때 robocopy /MIR로 레포와 맞춰짐).

## 실행 결과 (2026-10-30)

| 묶음 | 지운 것 | 확보 |
|---|---|---|
| A 레포 그림 | 7장 + .meta 7개 (`git rm`, 스테이징만, 커밋 안 함) | 약 3.7 MB |
| C 아트 작업본 staging·final·backups | 492개(검증 스크립트가 동일·교체·git 이력 확인한 파일만) | 약 17.9 MB |
| C 아트 작업본 raw·tmp·generated | 9개 폴더, 220개 | 약 288 MB |
| D 빌드 폴더 | WindowsQA, 로그 27개, 옛 스크립트 3개 | 약 1.05 GB |
| E 측정 폴더 | 9개 폴더, 122개 | 약 0.9 MB |

아트 작업본은 340 MB에서 47 MB가 됐다(남은 것의 대부분은 gacha_vfx 14 MB와 career_card 원본 16 MB).
