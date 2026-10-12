# 스킬 영상 촬영과 스킬 미리보기 (2026-10-12)

스킬 이펙트를 영상으로 뽑아, 나중에 게임 안 "스킬 영상 미리보기"에 넣기 위한 절차다. 화면 녹화 프로그램 없이 게임 자체가 매 프레임을 저장하고, ffmpeg로 묶는다.

## 1. 한 번에 보는 순서
```bash
# 0) 빌드 복사본을 최신 코드로 맞추고 PC 빌드 (평소 빌드 절차와 같다. 출시용 DOTRPG_RELEASE 빌드에는 촬영 모드가 없다)
# 1) 촬영: 게임이 스스로 켜져서 찍고 꺼진다
cd /mnt/c/Users/admin/Desktop/games/dotRPG_apkbuild/Builds/WindowsSteamTest
cmd.exe /c "dotRPG.exe -dotrpgSkillClip C:\Users\admin\Desktop\games\skill_capture -skill all -screen-width 1280 -screen-height 720 -screen-fullscreen 0"
# 2) 영상으로 묶기 (원본 크기 + 미리보기용 작은 크기)
cd /mnt/c/Users/admin/Desktop/games/dotRPG
Tools/video/encode_skill_clips.sh /mnt/c/Users/admin/Desktop/games/skill_capture 640
```

## 2. 촬영 모드 `-dotrpgSkillClip <저장 폴더>`
- 코드: `Assets/Scripts/Runtime/Core/DevCapture.SkillClip.cs`
- 하는 일
  - 스킬의 직업으로 오프라인 새 캐릭터를 만들고 Lv.40, 전직, 모든 노드 습득, 각성 완료 상태로 만든다.
  - 마을 광장에 피해를 주지 않고 움직이지 않는 허수아비 해골 8마리를 둥글게 세운다. HUD는 숨긴다.
  - 스킬마다 같은 시작 장면(체력 가득, 오른쪽을 봄, 허수아비 원위치)에서 한 번 쓴다.
  - 30fps 고정 시간으로 매 프레임을 PNG로 저장한다. 렉이 있어도 영상 속도는 일정하다.
- 저장 위치: `<저장 폴더>/clips/<스킬 id>/f0000.png ...`, 진행 기록은 `<저장 폴더>/report.txt`
- 세이브는 `<저장 폴더>/saves`에 따로 생겨서 실제 세이브를 건드리지 않는다.

| 옵션 | 뜻 |
|---|---|
| `-skill f_awake` | 스킬 하나 (id는 `Progression/CareerData.cs`의 S(...) 두 번째 값) |
| `-skill f_focus,m_storm,g_awake` | 여러 개 (직업이 섞여도 된다) |
| `-skill all` | 네 직업의 액티브·각성 스킬 전부 (패시브는 건너뜀) |
| 생략 | 파이터 각성(f_awake)만 |
| `-screen-width 1280 -screen-height 720` | 촬영 해상도. 미리보기용이면 1280x720이면 충분하다 |

- 영상 길이: 시전 시간 + 지속 시간(최소 1.5초, 단계가 있는 각성기는 `ScriptedLength`의 값: 천검귀일·천쇄방패 3.2초, 천체 붕괴 4.2초) + 1.2초, 2.5~8초 사이. 새 각성기를 만들면 여기에 길이를 추가한다. 시전 0.33초 전부터 찍는다. 길이를 바꾸려면 `RecordClip`의 `seconds`를 고친다.
- 소요 시간: 스킬 하나에 약 15~20초(PNG 저장 때문). `all`은 30개 안팎이라 10분 정도.

## 3. 영상 묶기 `Tools/video/encode_skill_clips.sh <저장 폴더> [미리보기 가로폭]`
- `videos/<스킬 id>.mp4`: 원본 해상도, 고화질(H.264, crf 18). 홍보·확인용.
- `videos/preview/<스킬 id>.mp4`: 미리보기용(기본 가로 640, 소리 없음, crf 26, faststart). 3초 영상이 약 110KB라 스킬 30개여도 3~4MB.
- WSL의 ffmpeg를 쓴다(이 PC에는 Windows용 화면 녹화 도구가 없다).

## 4. 스킬 미리보기에 넣는 방법 (구현 예정 메모)
- 파일 위치: `Assets/StreamingAssets/SkillVideos/<스킬 id>.mp4` (미리보기용 작은 파일만 넣는다)
- 재생: Unity `VideoPlayer`
  - `source = VideoSource.Url`, `url = Path.Combine(Application.streamingAssetsPath, "SkillVideos", id + ".mp4")`
  - `renderMode = RenderTexture`로 작은 RenderTexture(640x360)에 그리고 UI `RawImage`로 보여 준다.
  - `isLooping = true`, `audioOutputMode = None`.
  - Android에서도 StreamingAssets 경로를 URL로 그대로 재생할 수 있다.
- 보여 줄 곳(후보): 전직 스킬 창의 스킬 상세 패널, 스킬 칸 마우스 오버 툴팁, 스킬 범위 미리보기 옆.
- 파일이 없는 스킬은 미리보기 칸을 숨긴다(영상이 없어도 게임이 동작해야 한다).
- 화면을 열 때만 VideoPlayer를 만들고 닫으면 멈추고 해제한다. 여러 개를 동시에 재생하지 않는다.

## 5. 다시 찍어야 할 때와 주의점
- 스킬 이펙트·범위·시간을 바꾸면 그 스킬만 `-skill <id>`로 다시 찍고 다시 묶어서 StreamingAssets 파일을 교체한다.
- 그대로는 잘 안 보이는 스킬
  - Shift 이동기 섬광보(f_rush): Cast로 돌진은 나오지만 실제로는 Shift로 나간다.
  - 켜고 끄는 검기 태세(f_stance): 켜는 순간만 찍힌다. 기본 공격으로 검기가 나가는 장면은 따로 공격 입력을 넣어야 한다.
  - 버프류(매직 배리어, 축복 등): 대상이 플레이어 자신이라 변화가 작다.
  - 이런 스킬은 필요하면 `RecordClip`에 스킬별 연출(공격 입력, 아군 더미 등)을 추가한다.
- 촬영 폴더(PNG 수천 장)는 영상으로 묶은 뒤 지워도 된다. 저장소 안에 두지 않는다.
- 각성기 쿨타임은 현재 테스트용으로 꺼져 있다(`SkillCaster.AwakeningNoCooldown`). 촬영은 쿨타임과 무관하게 Cast를 직접 부른다.
