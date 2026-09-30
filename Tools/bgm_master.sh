#!/usr/bin/env bash
# Masters the Flow Music WAVs in AudioSource/Raw into loopable OGGs in Assets/Resources/Audio.
# Loudness -14 LUFS, true peak -1 dBTP, tiny fades at both ends so the Unity loop point does not click.
# Usage (from WSL): bash Tools/bgm_master.sh
set -euo pipefail
root="$(cd "$(dirname "$0")/.." && pwd)"
src="$root/AudioSource/Raw"
dst="$root/Assets/Resources/Audio"
mkdir -p "$dst"
for f in "$src"/*.wav; do
  n="$(basename "$f" .wav)"
  d="$(ffprobe -v error -show_entries format=duration -of csv=p=0 "$f")"
  st="$(awk -v d="$d" 'BEGIN { s = d - 0.06; if (s < 0) s = 0; print s }')"
  ffmpeg -loglevel error -y -i "$f" \
    -af "loudnorm=I=-14:TP=-1.0:LRA=11,aresample=48000,afade=t=in:d=0.02,afade=t=out:st=${st}:d=0.06" \
    -c:a libvorbis -q:a 6 "$dst/$n.ogg"
  printf '%-20s %6.1fs -> %s\n' "$n" "$d" "$(du -h "$dst/$n.ogg" | cut -f1)"
done
