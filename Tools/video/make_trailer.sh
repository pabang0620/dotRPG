#!/usr/bin/env bash
# Cuts the 1-minute promotion video from the -dotrpgTrailer capture (Docs/TRAILER_2026-10-13.md).
# Usage: Tools/video/make_trailer.sh <capture folder> [out.mp4]
#   <capture folder>/clips/<scene>/f0000.jpg ... (30 fps) -> 1920x1080 H.264 + AAC, captions in Korean, game music.
set -euo pipefail
root="${1:?capture folder}"
out="${2:-$root/dotRPG_trailer.mp4}"
repo="$(cd "$(dirname "$0")/../.." && pwd)"
font="/usr/share/fonts/truetype/nanum/NanumSquareRoundB.ttf"
logo="$repo/Assets/Resources/Art/ui_logo.png"
music="$repo/Assets/Resources/Audio/music_raid_enrage.ogg"
work="$root/cut"
mkdir -p "$work"
rm -f "$work"/*.mp4 "$work/list.txt"

# clip | start second in the clip | length | caption (top line) | sub caption (small)
segments=(
  "hunt|1.0|7.0|AI 동료와 함께 떠나는 자동 사냥|최대 3명의 AI 파티원이 탱커, 딜러, 제어를 맡는다"
  "awaken_fighter|0.0|4.6|전직 각성기 · 천검귀일|화면을 가르는 두 줄기 검기"
  "awaken_arcanist|0.0|6.5|전직 각성기 · 유성우|하늘을 뒤덮는 혜성의 비"
  "dungeon|2.0|6.0|요일 던전|매일 바뀌는 던전, 4단계 난이도"
  "grah_seal|7.0|7.0|레이드 · 수호자 그라흐|대사가 들리면 결계 의식, 인원 수만큼 마법진에 올라서라"
  "grah_repel|0.0|4.0|파동|벽까지 날아가지 않게 회피하라"
  "king_judgment|0.0|7.0|레이드 · 해골왕|망자의 심판, 스킬을 몰아 써서 저지하라"
  "king_kill|0.0|3.5|보스 처치|파티원과 함께 쓰러뜨려라"
  "king_reward|0.5|6.0|레이드 보상|매주 새로운 장비와 레이드 재료"
)

fade=0.35
# 1) title card (3.5 s): logo on black, tagline under it
ffmpeg -y -loglevel error -f lavfi -i "color=c=0x0b0d14:s=1920x1080:r=30:d=3.5" -i "$logo" -filter_complex \
 "[1:v]scale=-1:300[l];[0:v][l]overlay=(W-w)/2:(H-h)/2-80,drawtext=fontfile=$font:text='AI 동료와 함께하는 도트 액션 RPG':fontsize=52:fontcolor=white:x=(w-tw)/2:y=h/2+150,fade=t=in:st=0:d=0.6,fade=t=out:st=3.1:d=0.4" \
 -c:v libx264 -pix_fmt yuv420p -crf 18 "$work/00_title.mp4"
echo "file '00_title.mp4'" >> "$work/list.txt"

i=1
for seg in "${segments[@]}"; do
  IFS='|' read -r clip start len cap sub <<< "$seg"
  src="$root/clips/$clip"
  [ -d "$src" ] || { echo "missing clip $clip" >&2; continue; }
  name=$(printf "%02d_%s.mp4" "$i" "$clip")
  # trim first (then the fades count from the cut, not from the clip start)
  ffmpeg -y -loglevel error -framerate 30 -i "$src/f%04d.jpg" -vf \
"trim=start=$start:duration=$len,setpts=PTS-STARTPTS,scale=1920:1080:flags=lanczos,\
drawbox=x=0:y=ih-170:w=iw:h=170:color=black@0.55:t=fill,\
drawtext=fontfile=$font:text='$cap':fontsize=58:fontcolor=0xffe066:borderw=3:bordercolor=black:x=70:y=h-150,\
drawtext=fontfile=$font:text='$sub':fontsize=34:fontcolor=white:borderw=2:bordercolor=black:x=72:y=h-74,\
fade=t=in:st=0:d=$fade,fade=t=out:st=$(echo "$len - $fade" | bc):d=$fade" \
    -r 30 -c:v libx264 -pix_fmt yuv420p -crf 18 "$work/$name"
  echo "file '$name'" >> "$work/list.txt"
  i=$((i+1))
done

# 2) end card (4 s)
ffmpeg -y -loglevel error -f lavfi -i "color=c=0x0b0d14:s=1920x1080:r=30:d=4" -i "$logo" -filter_complex \
 "[1:v]scale=-1:260[l];[0:v][l]overlay=(W-w)/2:(H-h)/2-110,drawtext=fontfile=$font:text='지금 모험을 시작하세요':fontsize=60:fontcolor=0xffe066:x=(w-tw)/2:y=h/2+110,drawtext=fontfile=$font:text='솔로, AI 파티, 최대 4인 온라인 파티':fontsize=36:fontcolor=white:x=(w-tw)/2:y=h/2+200,fade=t=in:st=0:d=0.5" \
 -c:v libx264 -pix_fmt yuv420p -crf 18 "$work/99_end.mp4"
echo "file '99_end.mp4'" >> "$work/list.txt"

# 3) join, then lay the raid music under it (fade in / out to the video length)
ffmpeg -y -loglevel error -f concat -safe 0 -i "$work/list.txt" -c copy "$work/joined.mp4"
dur=$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$work/joined.mp4")
ffmpeg -y -loglevel error -i "$work/joined.mp4" -stream_loop -1 -i "$music" -filter_complex \
 "[1:a]atrim=0:$dur,afade=t=in:st=0:d=1.5,afade=t=out:st=$(echo "$dur - 2.5" | bc):d=2.5,volume=0.9[a]" \
 -map 0:v -map "[a]" -c:v copy -c:a aac -b:a 192k -shortest -movflags +faststart "$out"
echo "$out ($(printf '%.1f' "$dur") s)"
