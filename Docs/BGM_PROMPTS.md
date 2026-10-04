# BGM 목록과 Flow Music 프롬프트 (R14: 마을마다, 상황마다)

생성: Google Flow Music (Lyria 3.5), instrumental. 원본 WAV → `AudioSource/Raw/` (git 제외, 로컬 보관) → `Tools/bgm_master.sh`(WSL ffmpeg: -14 LUFS, -1 dBTP, 끝 페이드) → `Assets/Resources/Audio/<키>.ogg`.
게임은 `Game.Audio.PlayMusic(<키>)`로 재생한다(`Resources/Audio/<키>`).

| 키 | 상황 | 분위기 |
|---|---|---|
| music_title | 타이틀 | 모험의 시작, 서정적 메인 테마 |
| music_village | 해골 숲 옆 작은 마을 | 따뜻하고 한가로운 시골 마을 |
| music_canyon | 바위 협곡 마을 | 햇볕 드는 협곡, 상인 마을, 기타·핸드드럼 |
| music_winter | 눈꽃 숲 마을 | 고요한 눈, 벨·첼레스타 |
| music_forest | 사냥터 · 해골 숲 | 긴장감 있는 필드 전투 |
| music_dgn_canyon | 요일던전 협곡 광산 (월·화) | 어두운 광산, 금속 타악, 추진력 |
| music_dgn_forest | 요일던전 숲 묘지 (수·목) | 음산한 묘지, 합창 없는 오르간·현 |
| music_dgn_winter | 요일던전 겨울 동굴 (금) | 차가운 얼음 동굴, 긴박 |
| music_boss | 요일던전 보스전 | 강렬한 보스 배틀 |
| music_raid | 레이드 해골왕 | 웅장한 오케스트라 레이드 |
| music_raid_enrage | 해골왕 페이즈 3 광폭화 | 더 빠르고 절박하게 |
| music_clear | 던전 클리어 · 결과 화면 | 승리 팡파르 후 잔잔한 루프 |
| music_fail | 던전 실패 | 짧고 무거운 패배 |

## 프롬프트 (공통: instrumental 토글 ON, 끝에 구조 블록으로 길이 지정)

### music_title
```
Epic yet gentle fantasy RPG main theme, instrumental at 84 BPM in D minor moving to F major, heroic and nostalgic, solo flute and warm strings open, then full orchestra with french horns, harp arpeggios and soft timpani, spacious hall reverb, wide stereo image, sounds like the title screen of a classic 2D action RPG.
no vocals, no choir, no vocal chops, no electronic drums.

[0:00 - 0:12] Intro: solo flute over soft strings, sparse
[0:12 - 0:50] Main theme: horns carry the melody, full orchestra, heroic
[0:50 - 1:20] Main (변주): harp and strings, gentle, hopeful
[1:20 - 1:30] Outro: sparse, resolve softly
```

### music_village
```
Cozy peaceful village theme for a pixel-art RPG, instrumental at 96 BPM in G major, warm, bright, relaxed, acoustic guitar picking, pizzicato strings, light woodwinds (flute and clarinet), soft glockenspiel accents, gentle hand percussion, subtle room reverb, wide stereo image, loopable town background music.
no vocals, no vocal chops, no heavy drums, no electric guitar.

[0:00 - 0:08] Intro: guitar and pizzicato, sparse
[0:08 - 0:55] Main: flute melody, full light arrangement, warm
[0:55 - 1:35] Main (변주): clarinet takes the melody, glockenspiel accents
[1:35 - 1:40] Outro: sparse, return to the opening
```

### music_canyon
```
Sunny canyon trading town theme for a fantasy RPG, instrumental at 104 BPM in A mixolydian, warm and adventurous, nylon-string guitar strumming, hand drums and shakers, fiddle melody, upright bass, dry close-mic warmth, wide stereo image, bustling but relaxed market mood, loopable.
no vocals, no vocal chops, no synths, no electronic drums.

[0:00 - 0:08] Intro: guitar strumming and shaker
[0:08 - 0:55] Main: fiddle melody, hand drums, full arrangement
[0:55 - 1:35] Main (변주): guitar lead, lighter percussion
[1:35 - 1:40] Outro: sparse, loop back
```

### music_winter
```
Quiet snowy forest village theme for a fantasy RPG, instrumental at 80 BPM in E major, calm, gentle, sparkling and cold, celesta and music box melody, soft piano, warm string pads, sleigh bells used sparingly, spacious hall reverb, wide stereo image, cozy fireplace warmth against the snow, loopable.
no vocals, no vocal chops, no drum kit, no electric guitar.

[0:00 - 0:10] Intro: music box alone, sparse
[0:10 - 0:55] Main: celesta and piano, string pads, calm
[0:55 - 1:35] Main (변주): strings carry the melody, soft bells
[1:35 - 1:40] Outro: music box, fade to the loop point
```

### music_forest
```
Adventurous hunting ground field battle theme for a 2D action RPG, instrumental at 132 BPM in E minor, tense and driving but not dark, staccato strings, taiko and snare, brass stabs, fast woodwind runs, a solid bass foundation, subtle room reverb, wide stereo image, keep the energy constant for looping.
no vocals, no vocal chops, no long quiet breakdowns, keep the level constant.

[0:00 - 0:06] Intro: staccato strings and taiko
[0:06 - 0:50] Main: brass melody, full arrangement, driving
[0:50 - 1:25] Main (변주): woodwind runs, strings, driving
[1:25 - 1:30] Outro: hit and loop back
```

### music_dgn_canyon
```
Dark abandoned mine dungeon theme for a classic fantasy action RPG, instrumental at 128 BPM in D minor, gritty and propulsive, metallic anvil and pickaxe-like percussion, low brass, driving cellos and basses, distorted-free, dry close-mic percussion with a cavernous hint, wide stereo image, constant energy for dungeon combat loops.
no vocals, no vocal chops, no long quiet breakdowns, keep the level constant.

[0:00 - 0:06] Intro: metallic percussion alone
[0:06 - 0:50] Main: cellos and low brass riff, full arrangement
[0:50 - 1:25] Main (변주): higher strings, added snare rolls
[1:25 - 1:30] Outro: percussion hit, loop back
```

### music_dgn_forest
```
Eerie haunted graveyard dungeon theme for a classic fantasy action RPG, instrumental at 120 BPM in C minor, spooky and tense, pipe organ and harpsichord, tremolo strings, low toms and frame drum, bells tolling, spacious hall reverb, wide stereo image, constant energy for dungeon combat loops.
no vocals, no choir, no vocal chops, no long quiet breakdowns, keep the level constant.

[0:00 - 0:06] Intro: bell toll and organ
[0:06 - 0:50] Main: harpsichord melody, tremolo strings, toms
[0:50 - 1:25] Main (변주): organ lead, full arrangement
[1:25 - 1:30] Outro: bell, loop back
```

### music_dgn_winter
```
Frozen ice cave dungeon theme for a classic fantasy action RPG, instrumental at 136 BPM in F# minor, cold, urgent and crystalline, icy synth-free orchestral textures: string ostinato, glockenspiel and crotales, snare and taiko, french horn melody, a solid bass foundation, spacious hall reverb, wide stereo image, constant energy for dungeon combat loops.
no vocals, no vocal chops, no long quiet breakdowns, keep the level constant.

[0:00 - 0:06] Intro: string ostinato and crotales
[0:06 - 0:50] Main: horn melody, taiko, full arrangement
[0:50 - 1:25] Main (변주): glockenspiel counter-melody, strings
[1:25 - 1:30] Outro: hit, loop back
```

### music_boss
```
Intense boss battle theme for a classic fantasy action RPG, instrumental at 150 BPM in C minor, aggressive, heroic and urgent, rock drums with orchestral brass and fast strings, electric guitar riffs, a solid bass foundation, subtle room reverb, wide stereo image, relentless energy for a boss fight loop.
no vocals, no vocal chops, no long quiet breakdowns, keep the level constant.

[0:00 - 0:05] Intro: drum fill and brass hit
[0:05 - 0:50] Main: guitar riff and brass melody, full arrangement
[0:50 - 1:25] Main (변주): string runs, double-time drums
[1:25 - 1:30] Outro: hit, loop back
```

### music_raid
```
Grand raid boss theme against an undead Skeleton King for a classic fantasy action RPG, instrumental at 140 BPM in D minor, epic, dark and majestic, full symphonic orchestra, heavy taiko and timpani, low brass choir-like chords played by horns and trombones, pipe organ swells, fast string ostinato, spacious hall reverb, wide stereo image, relentless energy for a long raid fight loop.
no vocals, no choir, no vocal chops, no long quiet breakdowns, keep the level constant.

[0:00 - 0:08] Intro: organ swell and timpani
[0:08 - 0:55] Main: horn and trombone theme, string ostinato, full
[0:55 - 1:45] Main (변주): organ and strings, taiko drive
[1:45 - 1:50] Outro: hit, loop back
```

### music_raid_enrage
```
Final phase enraged Skeleton King raid theme, instrumental at 165 BPM in D minor, desperate, frantic and overwhelming, full orchestra with rock drums, blasting brass, shrieking string runs, pounding taiko, pipe organ stabs, a solid bass foundation, subtle room reverb, wide stereo image, maximum intensity, constant level for looping.
no vocals, no choir, no vocal chops, no quiet breakdowns, keep the level constant.

[0:00 - 0:04] Intro: organ stab and drum fill
[0:04 - 0:45] Main: brass theme at full force, string runs
[0:45 - 1:10] Main (변주): double-time taiko, organ stabs
[1:10 - 1:15] Outro: hit, loop back
```

### music_clear
```
Victory and dungeon result screen music for an action RPG, instrumental at 108 BPM in C major, triumphant then relaxed, short brass fanfare opening, then light orchestral loop with pizzicato strings, glockenspiel and warm horns, subtle room reverb, wide stereo image, satisfying reward mood.
no vocals, no vocal chops, no heavy drums.

[0:00 - 0:08] Intro: triumphant brass fanfare
[0:08 - 0:45] Main: light pizzicato and glockenspiel loop, warm
[0:45 - 0:55] Main (변주): horns join softly
[0:55 - 1:00] Outro: sparse, loop back
```

### music_fail
```
Short sad defeat theme for an action RPG dungeon failure screen, instrumental at 70 BPM in A minor, somber and heavy but not despairing, low strings, solo cello melody, soft piano, distant timpani, spacious hall reverb, wide stereo image.
no vocals, no choir, no vocal chops, no drums kit.

[0:00 - 0:08] Intro: low strings and timpani
[0:08 - 0:35] Main: solo cello melody over piano
[0:35 - 0:40] Outro: sparse, fade out
```

