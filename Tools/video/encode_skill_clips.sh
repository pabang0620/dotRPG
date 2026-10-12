#!/usr/bin/env bash
# Joins the frames written by the -dotrpgSkillClip capture mode into videos (Docs/SKILL_VIDEO_CAPTURE.md).
# Usage: Tools/video/encode_skill_clips.sh <capture folder> [preview width, default 640]
#   <capture folder>/clips/<skill id>/f0000.png ... -> <capture folder>/videos/<skill id>.mp4 (full size)
#                                                   -> <capture folder>/videos/preview/<skill id>.mp4 (small, for the in-game preview)
set -euo pipefail
root="${1:?capture folder}"
width="${2:-640}"
mkdir -p "$root/videos/preview"
for dir in "$root"/clips/*/; do
  id="$(basename "$dir")"
  ffmpeg -y -loglevel error -framerate 30 -i "$dir/f%04d.png" -c:v libx264 -pix_fmt yuv420p -crf 18 "$root/videos/$id.mp4"
  ffmpeg -y -loglevel error -i "$root/videos/$id.mp4" -vf "scale=$width:-2" -an -c:v libx264 -pix_fmt yuv420p -crf 26 -movflags +faststart "$root/videos/preview/$id.mp4"
  echo "$id"
done
