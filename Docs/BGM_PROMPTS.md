# BGM 목록과 Flow Music 프롬프트 (Gen2: 기억에 남는 멜로디 + 이음매 없는 루프)

생성: Google Flow Music, instrumental. 프롬프트 원본은 `Tools/audio/bgm_jobs.json`(곡마다 2개, 같은 프롬프트로 후보를 여러 개 뽑는다).

## 만드는 순서

1. 같은 프롬프트로 곡마다 후보 2개 이상 생성 → `AudioSource/Gen2/c<N>/<키>_c<N>.wav` (git 제외, 로컬 보관)
2. `Tools/audio/bgm_loop.py`: 템포·박 흔들림·대표 멜로디 반복·루프 지점 검색·첫 박 크로스페이드·-14 LUFS·이음매 검사
3. `Tools/audio/bgm_pick.py --apply`: 모든 후보를 검사하고 곡마다 가장 나은 것을 `Assets/Resources/Audio/<키>.ogg`로 넣는다. 비교용 후보는 `AudioSource/Gen2/candidates/`, 이음매 미리듣기는 `AudioSource/Gen2/seam_preview/`, 결과는 `AudioSource/Gen2/report.txt`
4. 사람이 듣고 확인: `Docs/AUDIO_TEST_CHECKLIST.md`

게임은 `Game.Audio.PlayMusic(<키>)`로 곡 전체를 반복 재생한다. 파일 자체가 끝에서 처음으로 이어지게 잘려 있으므로 끝 페이드를 넣지 않는다(예전 `Tools/bgm_master.sh`는 끝 페이드가 있어 쓰지 않는다).

## 곡 목록

| 키 | 상황 | BPM | 조 | 대표 악기 |
|---|---|---|---|---|
| music_title | 타이틀 | 92 | D장조 | 플루트 |
| music_village | 해골 숲 옆 작은 마을 | 100 | G장조 | 오카리나, 기타, 글로켄슈필 |
| music_canyon | 바위 협곡 마을, 협곡 사냥터, 침수된 고대 성소 | 104 | D 도리안 | 마림바, 핸드드럼 |
| music_winter | 눈꽃 숲 마을 | 88 | E플랫장조 3/4 | 오르골, 첼레스타 |
| music_forest | 사냥터 · 해골 숲 | 120 | A단조 | 피치카토, 바순 |
| music_dgn_canyon | 요일던전 협곡 광산 | 128 | C단조 | 금관 리프, 타이코 |
| music_dgn_forest | 요일던전 숲 묘지 | 112 | B단조 | 하프시코드, 오르간 |
| music_dgn_winter | 요일던전 겨울 동굴 | 132 | F#단조 | 크리스털 벨 |
| music_boss | 던전 보스전 | 150 | E단조 | - |
| music_raid | 레이드 | 140 | C단조 | 호른 |
| music_raid_enrage | 레이드 광폭화 | 165 | - | - |
| music_clear | 던전 클리어 · 결과 | 112 | C장조 | - |
| music_fail | 던전 실패 | 76 | A단조 | 피아노 |

## 프롬프트 공통 규칙

- 흥얼거릴 수 있는 4마디 대표 멜로디 하나를 처음 8초 안에 들려주고, 곡 내내 조금씩 바꿔 여러 번 반복한다. 두 악기가 주고받게 한다.
- 마을마다 대표 악기를 정해 다른 마을과 귀로 구분되게 한다.
- 클릭에 맞춘 일정한 템포. 템포 변화·루바토 금지.
- SEAMLESS LOOP: 인트로·엔딩·페이드아웃·마지막 화음 없이 마지막 마디가 첫 마디로 바로 이어지게.
- 보컬·합창·보컬 찹 금지. 실제 곡이나 실제 게임 이름을 프롬프트에 쓰지 않는다.

## 프롬프트

### music_title
```
Warm heroic fantasy adventure main theme for a cute pixel-art RPG, 92 BPM in D major, signature instrument: bright wooden flute playing the hook over lush strings, harp arpeggios, light snare and timpani, nostalgic and hopeful. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:30] A: flute states the hook, strings answer
[0:30-1:00] A': horns take the hook, full orchestra
[1:00-1:30] B: harp and strings, gentle contrast
[1:30-2:00] A'': hook again, building, ends leading back to the start
```

### music_village
```
Cheerful cozy village theme for a cute pixel-art RPG town by a river, 100 BPM in G major, bouncy and carefree, signature instrument: sweet ocarina playing the hook, acoustic guitar strumming, glockenspiel answers, pizzicato bass, light shaker and woodblock. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:30] A: ocarina hook, glockenspiel answers
[0:30-1:00] A': clarinet joins in harmony
[1:00-1:30] B: playful bridge with accordion
[1:30-2:00] A: hook returns, last bar turns back to the start
```

### music_canyon
```
Sunny merchant town in a red rock canyon, 104 BPM in D dorian, warm and lively market feel, signature instrument: marimba playing the hook, nylon guitar and oud-like plucked strings, hand drums and frame drum groove, light pan flute answers. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:30] A: marimba hook over hand drums
[0:30-1:00] A': pan flute answers the hook
[1:00-1:30] B: guitar solo over the groove
[1:30-2:00] A: hook again, groove continues into the start
```

### music_winter
```
Gentle snowy village theme, 88 BPM in 3/4 waltz time in E flat major, cozy warm fireplace feeling in the snow, signature instrument: music box and celesta playing the hook, soft strings and warm horn pads, sleigh bells, light harp. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:30] A: music box hook, strings answer
[0:30-1:00] A': celesta and flute in harmony
[1:00-1:30] B: warm horn melody contrast
[1:30-2:00] A: hook returns, last bar leads back to the start
```

### music_forest
```
Adventurous field hunting theme in a spooky-cute skeleton forest, 120 BPM in A minor, playful tension, signature instrument: pizzicato strings and bassoon playing the hook, xylophone answers, light marching snare and bass drum, tambourine. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:30] A: pizzicato and bassoon hook
[0:30-1:00] A': xylophone answers, snare drives
[1:00-1:30] B: brass stabs, more energetic
[1:30-2:00] A: hook again, flows back to the start
```

### music_dgn_canyon
```
Driving dungeon theme in an abandoned canyon mine, 128 BPM in C minor, adventurous and pushing forward, signature instrument: low brass and cellos playing a strong riff hook, taiko and snare groove, steady eighth-note string ostinato, bright trumpet answers. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:30] A: brass riff hook over taiko
[0:30-1:00] A': trumpet answers, ostinato strings
[1:00-1:30] B: darker breakdown, still driving
[1:30-2:00] A: riff returns, straight back to the start
```

### music_dgn_forest
```
Eerie but catchy dungeon theme in a forest graveyard, 112 BPM in B minor, mysterious and a little spooky-cute, signature instrument: harpsichord playing the hook, pipe organ pads, pizzicato strings, soft timpani and clock-like percussion. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:30] A: harpsichord hook, organ answers
[0:30-1:00] A': strings join, tension grows
[1:00-1:30] B: quiet eerie bridge
[1:30-2:00] A: hook again, leads back to the start
```

### music_dgn_winter
```
Tense icy cave dungeon theme, 132 BPM in F sharp minor, cold and urgent, signature instrument: crystal bells and glass marimba playing the hook, fast staccato strings, deep taiko, swelling brass. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:30] A: crystal bell hook over staccato strings
[0:30-1:00] A': brass answers the hook
[1:00-1:30] B: driving drums break
[1:30-2:00] A: hook again, straight back to the start
```

### music_boss
```
Intense boss battle theme for a cute pixel-art action RPG, 150 BPM in E minor, heroic and dangerous, signature instrument: electric-guitar-like lead and brass playing a strong riff hook, full orchestra, driving drums and timpani. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:30] A: big riff hook, drums drive
[0:30-1:00] A': strings and brass trade the hook
[1:00-1:30] B: breakdown and build
[1:30-2:00] A: riff again, loops straight back to the start
```

### music_raid
```
Epic raid boss theme against an undead king, 140 BPM in C minor, grand and menacing, signature instrument: french horns and trombones playing a heroic hook, pounding taiko and timpani, string ostinato, organ swells. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:30] A: horn hook over pounding drums
[0:30-1:00] A': full orchestra with the hook
[1:00-1:30] B: menacing low brass bridge
[1:30-2:00] A: hook again, back to the start
```

### music_raid_enrage
```
Final enraged phase of a raid boss, 165 BPM in C minor, frantic and desperate, signature instrument: trumpets and strings playing the same heroic hook faster, double-time drums, alarm-like brass stabs. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:20] A: fast hook, double-time drums
[0:20-0:40] A': brass stabs answer
[0:40-1:00] B: frantic build
[1:00-1:20] A: hook again, loops back to the start
```

### music_clear
```
Victory and results screen theme for a cute RPG, 112 BPM in C major, proud and happy, signature instrument: bright trumpet and glockenspiel playing a short catchy victory hook, light strings and snare, relaxed afterwards. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:15] A: victory hook on trumpet
[0:15-0:45] B: relaxed glockenspiel and strings, hook echoes
[0:45-1:00] A': hook again softly, loops back to the start
```

### music_fail
```
Short gentle defeat theme for a cute RPG, 76 BPM in A minor, sad but not heavy, encouraging to try again, signature instrument: soft piano playing a simple 4-bar melody, warm strings, light music box. Built around ONE simple, catchy, hummable 4-bar melody hook (stepwise with one memorable leap, easy to sing back), stated clearly in the first 8 seconds and repeated many times through the piece with small variations, call-and-response between two instruments. Steady constant tempo played to a click, no rubato, no tempo changes. SEAMLESS LOOP: no intro, no ending, no fade out, no final chord; the last bar must flow straight back into the first bar. Instrumental only: no vocals, no choir, no vocal chops.

[0:00-0:20] A: piano melody
[0:20-0:40] A': strings join, a little hope
[0:40-0:50] A: melody again softly, loops back to the start
```
