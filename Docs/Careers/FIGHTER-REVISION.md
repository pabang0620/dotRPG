> 이 문서는 이전 구성의 기록입니다. 최신 파이터 구현은 [FIGHTER-REFORGE.md](FIGHTER-REFORGE.md)를 참조하세요.

# 파이터 3스킬 동작 변경

2026-10-05 · jaein. 최신 요청의 쌍참·회천검무·삼중검파를 게임의 실제 Cast에 연결했다. 이후 컨셉 이미지에 맞춘 파이터 전체 그래픽은 [FIGHTER-CONCEPT.md](FIGHTER-CONCEPT.md)를 참고한다.

| 스킬 | 현재 동작 | 실제 판정 |
|---|---|---|
| 쌍참 | 제자리에서 전방을 크게 한 번 베기 | 전방 160도, 도달 거리 2.8m. 지나가는 검날에 맞은 대상당 1회 |
| 회천검무 | 캐릭터를 중심으로 큰 원형을 한 번 베기. 몸과 오른손의 장착 검도 8방향으로 회전 | 주변 반경 3.2m. 한 바퀴 동안 같은 대상은 1회만 적중 |
| 삼중검파 | 검을 들어 바닥을 내려찍고, 내려찍은 지점에서 세 갈래 검기를 동시에 방출 | 정면 및 좌우 24도, 최대 7m 관통. 각 검기마다 대상당 1회 |

쌍참의 초승달 검흔은 캐릭터의 바라보는 방향을 고정 기준으로 삼아 전방에만 놓인다. 모든 렌더링 파츠의 경계와 실제 판정이 전방에 있는지 8방향에서 검사했다. 회천검무는 마지막 요청에 따라 원형 1회 베기로 적용했다. 베기 후 잔상이 짧게 사라지게 했다. 삼중검파는 준비 동작과 지면 충격을 검기가 나오는 순간에 맞췄다. 이동 위치와 스킬 종료 후 바라보는 방향은 유지된다.

쌍참의 기존 2타, 회천검무의 기존 3타 합산 기본 피해를 각각 한 타로 모았다. 카탈로그의 피해 배율·MP·쿨다운·도달 거리·시전 시간·저장 ID·노드와 각성 조건은 그대로다. 카탈로그 `hits`는 기존 피해 단위 수로 유지하고, 스킬창에는 실제 동작에 맞는 합산 피해와 **대상당 1회**를 표시한다. 삼중검파의 각 검기 피해는 기존처럼 기본 피해의 1/3이다.

다단 공격을 한 타로 합쳤으므로 치명타·중첩 발동 횟수와 피격 무적에 따른 실제 결과는 이전과 달라질 수 있다. 삼중검파도 평행한 검기에서 퍼지는 검기로 바뀌어 유효 대상 분포가 달라진다. 입력 회복 시간은 쌍참 0.38초, 회천검무 0.5초, 삼중검파 0.35초로 각 동작 길이에 맞췄다.

## 이미지와 코드

현재 검흔은 컨셉 분석 후 새로 제작한 투명 원화 `Assets/StreamingAssets/CareerRenewal/FighterConcept.png`를 사용한다. 기존 `Fighter.png`는 이전 리소스로 보존되어 있다. 새 원화의 행별 모티프와 생성 프롬프트는 FIGHTER-CONCEPT 문서에 기록했다.

- `Assets/Scripts/Runtime/Combat/CareerCombat.Renewal.cs`: 단일 전방/원형 베기, 회전 중 1회 판정, 지면에서 분기하는 검기.
- `Assets/Scripts/Runtime/Combat/CareerCombat.cs`: 준비 동작 및 초기화 시 준비 중 공격 취소.
- `Assets/Scripts/Runtime/Player/CharacterAnimator.cs`, `PlayerCombat.cs`: 준비/타격 프레임 구간과 원형 베기의 몸·오른손·장착 검 방향 정렬.
- `Assets/Scripts/Runtime/Art/CareerRenewalFx.cs`: 잔상 소멸, 새 delivery ID와 입력 회복 시간.
- `Assets/Scripts/Runtime/Progression/CareerData.cs`, `CareerNumbers.cs`: 최신 이름·설명·단일 합산 피해 안내.
- `Assets/Scripts/Runtime/Core/DevCapture.Renewal.cs`, `DevCapture.Career.cs`, `DevCapture.Pulse.cs`, `DevCapture.Reborn.cs`: 새 스킬 검사와 캡처, 변경된 판정에 맞는 기존 검사 갱신.
- `server/data/careers.json`, `data_version.json` 및 클라이언트 DataVersion: 카탈로그 동기화.

## 실제 화면과 검증

[대표 동작 GIF](FIGHTER-REVISION/gameplay.gif) · [직전 버전과 비교](FIGHTER-REVISION/comparison.gif) · [실행 검사 원문](FIGHTER-REVISION/runtime-results.txt) · [수치·기본 스킬 비교](FIGHTER-REVISION/invariants.json)

무음 실행 검사 **215 통과 / 0 실패**. 8방향에서 전방 검흔 경계, 전방/원형 베기의 단일 명중, 몸·장비의 8방향 회전, 원래 방향 복귀, 내려찍기 준비 시점과 세 갈래 관통, 범위 밖 대상 제외, MP 중복 소모 방지, 쿨다운, 초기화·맵 이동·사망 시 효과 정리를 확인했다. 새 원화 로딩과 돌진·발도·각성기 준비/타격 자세 연결, 검신합일의 적용·종료도 확인했다. 움직이지 않는 판정 검사 대상은 마을 NPC 충돌에 의해 밀리지 않도록 테스트에서만 물리 이동을 고정했다.

기본 전사·마법사의 `SkillEffects.cs`와 기본 `SkillCaster.Cast` 본문은 이전 파일과 동일함을 비교했다. 기본 직업과 기본 공격에서는 전직 회전 방향을 적용하지 않는다. 정상 저장 대신 별도 테스트 저장 폴더를 사용했고 실행 볼륨은 0이다.

바탕화면 「dotRPG 만렙 4직업 체험」의 기존 로컬 플레이어에 컴파일한 런타임 DLL과 버전 정보를 반영했다. 전체 Unity 플레이어를 새로 빌드한 결과는 아니며, 이번 변경의 실제 온라인 2클라이언트 전투는 검증하지 않았다.

