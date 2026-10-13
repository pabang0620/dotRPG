#!/usr/bin/env python3
"""Fast-cut promotion ad (15 s / 30 s) from the -dotrpgTrailer -clean 1 capture (Docs/TRAILER_2026-10-13.md).

Usage: python3 Tools/video/make_ad.py <capture folder> [30|15] [out.mp4]
Cuts land on the half beats of music_raid_enrage (80.7 BPM -> 0.372 s), every cut gets a punch-in zoom and a white
flash, impact shots shake, kinetic captions slam in (big -> normal size), then a logo slam and the call to action.
"""
import os
import subprocess
import sys

ROOT = sys.argv[1]
LENGTH = sys.argv[2] if len(sys.argv) > 2 else "30"
OUT = sys.argv[3] if len(sys.argv) > 3 else os.path.join(ROOT, f"dotRPG_ad_{LENGTH}s.mp4")
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
AUDIO = os.path.join(REPO, "Assets", "Resources", "Audio")
LOGO = os.path.join(REPO, "Assets", "Resources", "Art", "ui_logo.png")
FONT = "/mnt/c/Windows/Fonts/PretendardGOV-Black.otf"
FONT2 = "/mnt/c/Windows/Fonts/PretendardGOV-Bold.otf"
FPS = 30
BPM = 80.7
HALF = 60.0 / BPM / 2.0          # one cut unit
MUSIC_START = 0.63               # first beat of the track
WORK = os.path.join(ROOT, f"ad{LENGTH}")
os.makedirs(WORK, exist_ok=True)

# clip, source second, half beats, zoom, slow motion x, shake, sfx, caption, sub caption, caption place
# zoom: 1.0 = whole 1920x1080 frame, 1.4 = closer on the hero (the camera follows the hero, so the centre is the action)
SHOTS_30 = [
    ("king_judgment",   0.3, 4, 1.30, 1.0, True,  "boom",  "보스가 깨어났다", "", "c"),
    ("grah_seal",       5.0, 2, 1.50, 1.0, True,  "boom",  "", "", ""),
    ("king_judgment",   3.7, 2, 1.40, 1.0, True,  "slash", "", "", ""),
    ("awaken_fighter",  1.4, 3, 1.35, 1.0, False, "slash", "베고", "", "c"),
    ("awaken_fighter",  3.4, 2, 1.45, 1.0, True,  "boom",  "", "", ""),
    ("awaken_guardian", 1.5, 2, 1.30, 1.0, False, "slash", "막고", "", "c"),
    ("awaken_guardian", 3.0, 2, 1.30, 1.0, True,  "boom",  "", "", ""),
    ("awaken_arcanist", 3.0, 3, 1.20, 1.0, False, "magic", "쓸어버려", "", "c"),
    ("awaken_arcanist", 4.8, 2, 1.30, 1.0, True,  "boom",  "", "", ""),
    ("awaken_bishop",   2.4, 3, 1.15, 1.0, False, "magic", "지켜내라", "", "c"),
    ("hunt",            1.0, 6, 1.30, 1.0, False, "swing", "AI 동료 3명과 함께", "탱커 · 딜러 · 제어, 언제든 파티 완성", "b"),
    ("dungeon",         0.8, 6, 1.30, 1.0, False, "swing", "요일 던전 · 자동 사냥", "손이 바쁠 땐 맡기고, 보스는 직접", "b"),
    ("grah_seal",       0.0, 3, 1.45, 1.0, True,  "boom",  "4인 레이드", "", "c"),
    ("grah_seal",       8.0, 5, 1.20, 1.0, False, "magic", "보스의 대사를 들어라", "인원 수만큼 마법진에 올라서라", "b"),
    ("grah_seal",      10.8, 3, 1.40, 1.0, True,  "boom",  "결계 파훼!", "", "c"),
    ("grah_repel",      0.5, 3, 1.30, 1.0, True,  "boom",  "버텨라", "", "c"),
    ("king_judgment",   1.0, 4, 1.25, 1.0, False, "magic", "심판을 막아라", "스킬을 몰아 써서 시전을 끊어라", "b"),
    ("king_kill",       0.0, 4, 1.50, 1.8, True,  "boom",  "CLEAR", "", "c"),
    ("king_reward",     3.0, 4, 1.00, 1.0, False, "card",  "레이드 보상", "매주 새로운 장비", "b"),
    ("awaken_fighter",  3.5, 1, 1.55, 1.0, True,  "slash", "", "", ""),
    ("awaken_arcanist", 5.5, 1, 1.40, 1.0, True,  "slash", "", "", ""),
    ("awaken_guardian", 3.2, 1, 1.45, 1.0, True,  "slash", "", "", ""),
    ("grah_seal",       0.3, 1, 1.60, 1.0, True,  "slash", "", "", ""),
    ("dungeon",         1.0, 1, 1.50, 1.0, True,  "slash", "", "", ""),
    ("king_judgment",   4.0, 1, 1.50, 1.0, True,  "boom",  "", "", ""),
]
SHOTS_15 = [
    ("king_judgment",   0.3, 3, 1.30, 1.0, True,  "boom",  "보스가 깨어났다", "", "c"),
    ("awaken_fighter",  1.4, 2, 1.35, 1.0, True,  "slash", "베고", "", "c"),
    ("awaken_guardian", 1.5, 2, 1.30, 1.0, True,  "slash", "막고", "", "c"),
    ("awaken_arcanist", 3.0, 2, 1.20, 1.0, True,  "boom",  "쓸어버려", "", "c"),
    ("hunt",            1.0, 5, 1.30, 1.0, False, "swing", "AI 동료 3명과 함께", "", "b"),
    ("grah_seal",       8.0, 5, 1.20, 1.0, False, "magic", "보스의 대사를 들어라", "인원 수만큼 마법진에 올라서라", "b"),
    ("grah_seal",      10.8, 2, 1.40, 1.0, True,  "boom",  "결계 파훼!", "", "c"),
    ("king_kill",       0.0, 3, 1.50, 1.8, True,  "boom",  "CLEAR", "", "c"),
    ("king_reward",     3.0, 3, 1.00, 1.0, False, "card",  "레이드 보상", "", "b"),
    ("awaken_fighter",  3.5, 1, 1.55, 1.0, True,  "slash", "", "", ""),
    ("awaken_arcanist", 5.5, 1, 1.40, 1.0, True,  "slash", "", "", ""),
    ("grah_seal",       0.3, 1, 1.60, 1.0, True,  "slash", "", "", ""),
    ("king_judgment",   4.0, 1, 1.50, 1.0, True,  "boom",  "", "", ""),
]
END_HALVES = {"30": 9, "15": 7}  # logo slam + call to action
SFX = {"boom": "rock_break.ogg", "slash": "c_slash.ogg", "swing": "swing.ogg", "magic": "c_arcane.ogg", "card": "card_flip.ogg"}


def run(args):
    subprocess.run(args, check=True)


def caption_png(i, cap, sub, place):
    """Draws the caption once as a transparent PNG (drawtext with an animated size crashes this ffmpeg build)."""
    from PIL import Image, ImageDraw, ImageFont
    if place == "c":
        font = ImageFont.truetype(FONT, 170)
        box = font.getbbox(cap, stroke_width=12)
        w, h = box[2] - box[0] + 60, box[3] - box[1] + 60
        img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        # drop shadow, then white -> gold gradient text with a thick black outline
        shadow = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        ImageDraw.Draw(shadow).text((30 - box[0] + 10, 30 - box[1] + 12), cap, font=font, fill=(0, 0, 0, 150), stroke_width=12, stroke_fill=(0, 0, 0, 150))
        img.alpha_composite(shadow)
        ImageDraw.Draw(img).text((30 - box[0], 30 - box[1]), cap, font=font, fill=(255, 255, 255, 255), stroke_width=12, stroke_fill=(0, 0, 0, 255))
        mask = Image.new("L", (w, h), 0)
        ImageDraw.Draw(mask).text((30 - box[0], 30 - box[1]), cap, font=font, fill=255)
        grad = Image.new("RGBA", (w, h))
        for y in range(h):
            k = y / max(1, h - 1)
            grad.paste((255, int(255 - 40 * k), int(255 - 190 * k), 255), (0, y, w, y + 1))
        img.paste(grad, (0, 0), mask)
    else:
        font, font2 = ImageFont.truetype(FONT, 104), ImageFont.truetype(FONT2, 46)
        b1 = font.getbbox(cap, stroke_width=8)
        b2 = font2.getbbox(sub, stroke_width=4) if sub else (0, 0, 0, 0)
        w = max(b1[2] - b1[0], b2[2] - b2[0]) + 160
        h = (b1[3] - b1[1]) + ((b2[3] - b2[1]) + 30 if sub else 0) + 80
        img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        d = ImageDraw.Draw(img)
        # a slanted gold bar behind the headline
        d.polygon([(0, 26), (w - 60, 26), (w - 20, h - 18), (40, h - 18)], fill=(20, 16, 30, 170))
        d.polygon([(0, 26), (22, 26), (62, h - 18), (40, h - 18)], fill=(255, 205, 60, 255))
        d.text((80 - b1[0], 40 - b1[1]), cap, font=font, fill=(255, 255, 255, 255), stroke_width=8, stroke_fill=(0, 0, 0, 255))
        if sub:
            d.text((86 - b2[0], 40 + (b1[3] - b1[1]) + 22 - b2[1]), sub, font=font2, fill=(255, 226, 122, 255), stroke_width=4, stroke_fill=(0, 0, 0, 255))
    path = os.path.join(WORK, f"cap{i:02d}.png")
    img.save(path)
    return path


def shot(i, s):
    clip, start, halves, zoom, slow, shake, _sfx, cap, sub, place = s
    dur = halves * HALF
    frames = round(dur * FPS)
    src_frames = int(frames / slow) + 2
    first = int(start * FPS)
    path = os.path.join(ROOT, "clips", clip)
    shake_x = "+if(lt(on\\,6)\\,(random(1)-0.5)*60\\,0)" if shake else ""
    shake_y = "+if(lt(on\\,6)\\,(random(2)-0.5)*40\\,0)" if shake else ""
    zexpr = f"{zoom}*(1+0.14*max(0\\,1-on/5))"   # punch-in: 14% closer on the cut, settling in 5 frames
    vf = [f"setpts={slow}*PTS", f"fps={FPS}",
          f"zoompan=z='{zexpr}':d=1:s=1920x1080:fps={FPS}:x='iw/2-(iw/zoom/2){shake_x}':y='ih/2-(ih/zoom/2){shake_y}'",
          "eq=contrast=1.10:saturation=1.25:brightness=0.01", "vignette=PI/5",
          "fade=t=in:st=0:d=0.12:color=white"]
    out = os.path.join(WORK, f"s{i:02d}.mp4")
    args = ["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS), "-start_number", str(first), "-i", os.path.join(path, "f%04d.jpg")]
    chain = "[0:v]" + ",".join(vf) + "[v]"
    if cap:
        args += ["-loop", "1", "-framerate", str(FPS), "-i", caption_png(i, cap, sub, place)]
        if place == "c":
            # slam: lands from 1.8x to 1x in 0.12 s, centred
            chain += f";[1:v]format=rgba,scale=w='iw*(1.8-0.8*min(t\\,0.12)/0.12)':h=-1:eval=frame[c];[v][c]overlay=x=(W-w)/2:y=(H-h)/2-30:eval=frame[o]"
        else:
            # slides in from the left in 0.15 s, lower third
            chain += f";[1:v]format=rgba[c];[v][c]overlay=x='-w+(w+60)*min(1\\,t/0.15)':y=H-h-80:eval=frame[o]"
        last = "[o]"
    else:
        last = "[v]"
    args += ["-filter_complex", chain, "-map", last, "-t", f"{dur:.4f}", "-r", str(FPS), "-an", "-c:v", "libx264", "-preset", "medium",
             "-crf", "17", "-pix_fmt", "yuv420p", out]
    run(args)
    return out, dur


def cta_png():
    from PIL import Image, ImageDraw, ImageFont
    f1, f2 = ImageFont.truetype(FONT, 120), ImageFont.truetype(FONT2, 46)
    t1, t2 = "지금 플레이", "솔로 · AI 파티 · 최대 4인 온라인 레이드"
    b1, b2 = f1.getbbox(t1, stroke_width=9), f2.getbbox(t2, stroke_width=4)
    w = max(b1[2] - b1[0], b2[2] - b2[0]) + 40
    h = (b1[3] - b1[1]) + (b2[3] - b2[1]) + 70
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.text(((w - (b1[2] - b1[0])) / 2 - b1[0], 10 - b1[1]), t1, font=f1, fill=(255, 216, 74, 255), stroke_width=9, stroke_fill=(0, 0, 0, 255))
    d.text(((w - (b2[2] - b2[0])) / 2 - b2[0], 10 + (b1[3] - b1[1]) + 40 - b2[1]), t2, font=f2, fill=(255, 255, 255, 255), stroke_width=4, stroke_fill=(0, 0, 0, 255))
    path = os.path.join(WORK, "cta.png")
    img.save(path)
    return path


def end_card(i, halves, background_clip):
    dur = halves * HALF
    out = os.path.join(WORK, f"s{i:02d}.mp4")
    bg = os.path.join(ROOT, "clips", background_clip, "f%04d.jpg")
    # blurred, dark gameplay behind; the logo drops in big and lands (0.18 s), then the call to action fades up
    fc = (f"[0:v]fps={FPS},scale=1920:1080,gblur=sigma=18,eq=brightness=-0.25:saturation=1.2[bg];"
          f"[1:v]format=rgba,scale=w='900*(1.7-0.7*min(t\\,0.18)/0.18)':h=-1:eval=frame[lg];"
          f"[2:v]format=rgba,fade=t=in:st=0.35:d=0.25:alpha=1[ct];"
          f"[bg][lg]overlay=x=(W-w)/2:y=(H-h)/2-140:eval=frame[a];[a][ct]overlay=x=(W-w)/2:y=H/2+150,"
          f"fade=t=in:st=0:d=0.12:color=white,fade=t=out:st={dur - 0.5:.3f}:d=0.5[o]")
    run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS), "-start_number", "30", "-i", bg,
         "-loop", "1", "-framerate", str(FPS), "-i", LOGO, "-loop", "1", "-framerate", str(FPS), "-i", cta_png(),
         "-filter_complex", fc, "-map", "[o]", "-t", f"{dur:.4f}", "-r", str(FPS), "-an", "-c:v", "libx264", "-crf", "17", "-pix_fmt", "yuv420p", out])
    return out, dur


def main():
    shots = SHOTS_30 if LENGTH == "30" else SHOTS_15
    parts, cuts = [], []
    t = 0.0
    for i, s in enumerate(shots):
        path, dur = shot(i, s)
        parts.append(path)
        cuts.append((t, s[6]))
        t += dur
    path, dur = end_card(len(shots), END_HALVES[LENGTH], "awaken_arcanist")
    parts.append(path)
    cuts.append((t, "boom"))
    t += dur
    listing = os.path.join(WORK, "list.txt")
    with open(listing, "w") as f:
        for p in parts:
            f.write(f"file '{os.path.basename(p)}'\n")
    video = os.path.join(WORK, "video.mp4")
    run(["ffmpeg", "-y", "-loglevel", "error", "-f", "concat", "-safe", "0", "-i", listing, "-c", "copy", video])
    # music from its first beat + one sound effect on every cut
    inputs = ["-i", video, "-ss", f"{MUSIC_START}", "-i", os.path.join(AUDIO, "music_raid_enrage.ogg")]
    mix = [f"[1:a]atrim=0:{t:.3f},afade=t=out:st={t - 1.2:.3f}:d=1.2,volume=0.85[m]"]
    labels = ["[m]"]
    for k, (at, kind) in enumerate(cuts):
        inputs += ["-i", os.path.join(AUDIO, SFX[kind])]
        ms = int(at * 1000)
        vol = 0.9 if kind in ("boom", "slash") else 0.6
        mix.append(f"[{k + 2}:a]adelay={ms}|{ms},volume={vol}[s{k}]")
        labels.append(f"[s{k}]")
    mix.append("".join(labels) + f"amix=inputs={len(labels)}:normalize=0,alimiter=limit=0.95,loudnorm=I=-14:TP=-1:LRA=9[a]")
    run(["ffmpeg", "-y", "-loglevel", "error", *inputs, "-filter_complex", ";".join(mix), "-map", "0:v", "-map", "[a]",
         "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-shortest", "-movflags", "+faststart", OUT])
    print(f"{OUT} ({t:.1f} s)")


if __name__ == "__main__":
    main()
