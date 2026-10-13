#!/usr/bin/env python3
"""30-second fast-cut promotion ad from the -dotrpgTrailer -clean 1 capture (Docs/TRAILER_2026-10-13.md).

Usage: python3 Tools/video/make_ad.py <capture folder> [out.mp4]   (default out: games/dotRPG_videos/dotRPG_ad_30s.mp4)
Cuts land on the half beats of music_raid_enrage (80.7 BPM -> 0.372 s). Every cut gets a punch-in zoom and a white
flash, impact shots shake, the kill is slow motion. Captions are drawn once with Pillow (Black Han Sans, metal
gradient, heavy outline, glow) and slam in or slide in; the awakenings get skill name plates in their own colours.
"""
import os
import subprocess
import sys

ROOT = sys.argv[1]
REPO = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
VIDEOS = os.path.join(os.path.dirname(REPO), "dotRPG_videos")  # every finished video goes here (games/dotRPG_videos)
os.makedirs(VIDEOS, exist_ok=True)
OUT = sys.argv[2] if len(sys.argv) > 2 else os.path.join(VIDEOS, "dotRPG_ad_30s.mp4")
AUDIO = os.path.join(REPO, "Assets", "Resources", "Audio")
LOGO = os.path.join(REPO, "Assets", "Resources", "Art", "ui_logo.png")
HEAD = os.path.join(os.path.dirname(os.path.abspath(__file__)), "fonts", "BlackHanSans-Regular.ttf")  # OFL, fonts/OFL.txt
BODY = "/mnt/c/Windows/Fonts/PretendardGOV-Bold.otf"
FPS = 30
BPM = 80.7
HALF = 60.0 / BPM / 2.0          # one cut unit
MUSIC_START = 0.63               # first beat of the track
WORK = os.path.join(ROOT, "ad30")
os.makedirs(WORK, exist_ok=True)

# Colour themes: top, middle, bottom of the letter gradient, glow
THEMES = {
    "gold":   ((255, 255, 240), (255, 214, 90), (255, 128, 30), (255, 170, 40)),
    "ice":    ((255, 255, 255), (150, 225, 255), (40, 120, 255), (90, 190, 255)),
    "aegis":  ((255, 255, 240), (120, 255, 230), (20, 160, 170), (80, 240, 220)),
    "violet": ((255, 240, 255), (225, 150, 255), (130, 40, 230), (190, 90, 255)),
    "holy":   ((255, 255, 245), (255, 240, 160), (230, 170, 40), (255, 230, 120)),
    "red":    ((255, 245, 235), (255, 150, 90), (220, 30, 20), (255, 70, 40)),
}

# clip, source second, half beats, zoom, slow x, shake, sfx, style, text, sub, theme, focus (x, y in 0..1 or None = centre)
#   style: "slam" big centre title / "low" slam at the bottom / "plate" awakening name plate (sub = the small label) /
#          "band" lower third / "" none
SHOTS = [
    ("king_judgment",   0.3, 4, 1.30, 1.0, True,  "boom",  "slam",  "이 손맛, 도트 맞아?", "", "red", None),
    ("grah_seal",       5.0, 2, 1.50, 1.0, True,  "boom",  "", "", "", "", None),
    ("king_judgment",   3.7, 2, 1.40, 1.0, True,  "slash", "", "", "", "", None),
    ("awaken_fighter",  0.9, 3, 1.30, 1.0, False, "slash", "plate", "천검귀일", "파이터 각성", "ice", None),
    ("awaken_fighter",  4.9, 2, 1.35, 1.0, True,  "boom",  "", "", "", "", None),
    ("awaken_guardian", 1.9, 2, 1.30, 1.0, False, "slash", "plate", "천쇄방패", "수호자 각성", "aegis", None),
    ("awaken_guardian", 3.0, 2, 1.35, 1.0, True,  "boom",  "", "", "", "", None),
    ("awaken_arcanist", 3.0, 3, 1.25, 1.0, False, "magic", "plate", "유성우", "아케니스트 각성", "violet", None),
    ("awaken_arcanist", 4.6, 2, 1.30, 1.0, True,  "boom",  "", "", "", "", None),
    ("awaken_bishop",   2.9, 3, 1.20, 1.0, False, "magic", "plate", "천상의 행진", "비숍 각성", "holy", None),
    ("hunt",            1.0, 6, 1.30, 1.0, False, "swing", "band",  "혼자여도 4인 파티", "AI 동료가 탱커 · 딜러 · 제어를 맡는다", "gold", None),
    ("dungeon",         0.8, 5, 1.30, 1.0, False, "swing", "band",  "손은 가볍게, 전투는 화끈하게", "자동 사냥 · 요일 던전", "gold", None),
    ("grah_seal",       0.0, 3, 1.45, 1.0, True,  "boom",  "slam",  "보스가 판을 뒤집는다", "", "red", None),
    ("grah_seal",       8.0, 5, 1.20, 1.0, False, "magic", "band",  "대사가 들리면, 달려라", "인원 수만큼 열리는 결계 마법진", "violet", None),
    ("grah_seal",      10.8, 3, 1.40, 1.0, True,  "boom",  "slam",  "결계 붕괴", "", "violet", None),
    ("grah_repel",      0.5, 2, 1.30, 1.0, True,  "boom",  "slam",  "밀려나지 마라", "", "gold", None),
    ("king_judgment",   1.0, 4, 1.25, 1.0, False, "magic", "band",  "심판을 끊어내라", "아껴 둔 스킬을 한 번에 쏟아부어라", "red", None),
    ("king_kill",       0.0, 4, 1.60, 1.5, True,  "boom",  "slam",  "격파", "", "gold", (0.46, 0.18)),
    ("king_reward",     3.6, 2, 1.45, 1.0, False, "card",  "", "", "", "", (0.70, 0.36)),
    ("king_reward",     6.3, 6, 1.45, 1.0, True,  "boom",  "low",   "전설이 떨어진다", "", "gold", (0.70, 0.36)),
    ("awaken_fighter",  5.0, 1, 1.50, 1.0, True,  "slash", "", "", "", "", None),
    ("awaken_arcanist", 5.2, 1, 1.40, 1.0, True,  "slash", "", "", "", "", None),
    ("awaken_guardian", 3.2, 1, 1.45, 1.0, True,  "slash", "", "", "", "", None),
    ("grah_seal",       0.3, 1, 1.60, 1.0, True,  "slash", "", "", "", "", None),
    ("dungeon",         1.0, 1, 1.50, 1.0, True,  "slash", "", "", "", "", None),
]
END_HALVES = 9  # logo slam + call to action
SFX = {"boom": "rock_break.ogg", "slash": "c_slash.ogg", "swing": "swing.ogg", "magic": "c_arcane.ogg", "card": "card_flip.ogg"}


def run(args):
    subprocess.run(args, check=True)


# ---------------------------------------------------------------- captions (Pillow; drawtext with an animated size crashes this ffmpeg)

def styled_text(text, size, theme, shear=-0.14, stroke=None):
    """Metal-gradient letters with a heavy dark outline, a coloured glow and a drop shadow, slightly italic."""
    from PIL import Image, ImageDraw, ImageFilter, ImageFont
    top, mid, bot, glow = THEMES[theme]
    font = ImageFont.truetype(HEAD, size)
    stroke = stroke or max(6, size // 11)
    box = font.getbbox(text, stroke_width=stroke)
    pad = size // 2
    w, h = box[2] - box[0] + pad * 2, box[3] - box[1] + pad * 2
    origin = (pad - box[0], pad - box[1])
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    g = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(g).text(origin, text, font=font, fill=glow + (200,), stroke_width=stroke + 6, stroke_fill=glow + (200,))
    img.alpha_composite(g.filter(ImageFilter.GaussianBlur(size / 7)))
    sh = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    ImageDraw.Draw(sh).text((origin[0] + size // 16, origin[1] + size // 12), text, font=font, fill=(0, 0, 0, 170), stroke_width=stroke, stroke_fill=(0, 0, 0, 170))
    img.alpha_composite(sh)
    ImageDraw.Draw(img).text(origin, text, font=font, fill=(20, 8, 4, 255), stroke_width=stroke, stroke_fill=(20, 8, 4, 255))
    mask = Image.new("L", (w, h), 0)
    ImageDraw.Draw(mask).text(origin, text, font=font, fill=255)
    grad = Image.new("RGBA", (w, h))
    y0, y1 = origin[1] + box[1], origin[1] + box[3]
    for y in range(h):
        k = min(1.0, max(0.0, (y - y0) / max(1, y1 - y0)))
        a, b, t = (top, mid, k / 0.5) if k < 0.5 else (mid, bot, (k - 0.5) / 0.5)
        col = tuple(int(a[c] + (b[c] - a[c]) * t) for c in range(3))
        if 0.46 < k < 0.52:  # the metal edge line
            col = tuple(min(255, int(v * 0.8 + 255 * 0.2)) for v in col)
        grad.paste(col + (255,), (0, y, w, y + 1))
    img.paste(grad, (0, 0), mask)
    if shear:
        img = img.transform((w + int(abs(shear) * h), h), Image.AFFINE, (1, shear, shear * h if shear < 0 else 0, 0, 1, 0), resample=Image.BICUBIC)
    return img


def caption_png(i, style, text, sub, theme):
    from PIL import Image, ImageDraw, ImageFont
    if style in ("slam", "low"):
        img = styled_text(text, 190 if style == "slam" else 160, theme)
    elif style == "plate":
        # awakening name plate: a small ribbon label over the big skill name
        name = styled_text(text, 200, theme)
        f = ImageFont.truetype(BODY, 46)
        lb = f.getbbox(sub)
        lw, lh = lb[2] - lb[0] + 70, lb[3] - lb[1] + 30
        w, h = max(name.width, lw), name.height + lh - 20
        img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        glow = THEMES[theme][3]
        x0 = (w - lw) // 2
        d = ImageDraw.Draw(img)
        d.polygon([(x0 + 18, 0), (x0 + lw, 0), (x0 + lw - 18, lh), (x0, lh)], fill=(12, 10, 20, 225))
        d.line([(x0 + 18, 0), (x0 + lw, 0)], fill=glow + (255,), width=4)
        d.line([(x0, lh), (x0 + lw - 18, lh)], fill=glow + (255,), width=4)
        d.text((x0 + 35 - lb[0], 15 - lb[1]), sub, font=f, fill=(255, 255, 255, 255))
        img.alpha_composite(name, ((w - name.width) // 2, lh - 40))
    else:  # band
        head = styled_text(text, 112, theme, shear=-0.1)
        f = ImageFont.truetype(BODY, 44)
        sb = f.getbbox(sub, stroke_width=3) if sub else (0, 0, 0, 0)
        w = max(head.width, sb[2] - sb[0] + 140) + 120
        h = head.height + (sb[3] - sb[1] + 20 if sub else 0) - 30
        img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
        glow = THEMES[theme][3]
        d = ImageDraw.Draw(img)
        bar_top = 60
        d.polygon([(0, bar_top), (w - 80, bar_top), (w - 30, h - 6), (0, h - 6)], fill=(10, 8, 18, 200))
        d.polygon([(0, bar_top), (14, bar_top), (14, h - 6), (0, h - 6)], fill=glow + (255,))
        d.line([(0, h - 6), (w - 30, h - 6)], fill=glow + (255,), width=5)
        img.alpha_composite(head, (40, 0))
        if sub:
            d.text((80 - sb[0], head.height - 52 - sb[1]), sub, font=f, fill=(255, 236, 170, 255), stroke_width=3, stroke_fill=(0, 0, 0, 255))
    path = os.path.join(WORK, f"cap{i:02d}.png")
    img.save(path)
    return path


# ---------------------------------------------------------------- shots

def shot(i, s):
    clip, start, halves, zoom, slow, shake, _sfx, style, text, sub, theme, focus = s
    dur = halves * HALF
    first = int(start * FPS)
    path = os.path.join(ROOT, "clips", clip)
    fx, fy = focus if focus else (0.5, 0.5)
    sx = "+if(lt(on\\,6)\\,(random(1)-0.5)*70\\,0)" if shake else ""
    sy = "+if(lt(on\\,6)\\,(random(2)-0.5)*45\\,0)" if shake else ""
    z = f"{zoom}*(1+0.14*max(0\\,1-on/5))"  # punch-in: 14% closer on the cut, settling in 5 frames
    x = f"max(0\\,min(iw-iw/zoom\\,iw*{fx}-iw/zoom/2)){sx}"
    y = f"max(0\\,min(ih-ih/zoom\\,ih*{fy}-ih/zoom/2)){sy}"
    vf = [f"setpts={slow}*PTS", f"fps={FPS}",
          f"zoompan=z='{z}':d=1:s=1920x1080:fps={FPS}:x='{x}':y='{y}'",
          "eq=contrast=1.12:saturation=1.3:brightness=0.01", "vignette=PI/5",
          "fade=t=in:st=0:d=0.12:color=white"]
    out = os.path.join(WORK, f"s{i:02d}.mp4")
    args = ["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS), "-start_number", str(first), "-i", os.path.join(path, "f%04d.jpg")]
    chain = "[0:v]" + ",".join(vf) + "[v]"
    last = "[v]"
    if style:
        args += ["-loop", "1", "-framerate", str(FPS), "-i", caption_png(i, style, text, sub, theme)]
        if style in ("slam", "plate", "low"):
            # lands from 1.9x to 1x in 0.12 s, centred (plates a little lower, "low" under the reward cards)
            yy = {"slam": "(H-h)/2-20", "plate": "(H-h)/2+120", "low": "H-h-40"}[style]
            chain += f";[1:v]format=rgba,scale=w='iw*(1.9-0.9*min(t\\,0.12)/0.12)':h=-1:eval=frame[c];[v][c]overlay=x=(W-w)/2:y={yy}:eval=frame[o]"
        else:
            # slides in from the left in 0.15 s, lower third
            chain += ";[1:v]format=rgba[c];[v][c]overlay=x='-w+w*min(1\\,t/0.15)':y=H-h-60:eval=frame[o]"
        last = "[o]"
    args += ["-filter_complex", chain, "-map", last, "-t", f"{dur:.4f}", "-r", str(FPS), "-an", "-c:v", "libx264", "-preset", "medium",
             "-crf", "17", "-pix_fmt", "yuv420p", out]
    run(args)
    return out, dur


def end_card(i, halves, background_clip):
    from PIL import Image, ImageDraw, ImageFont
    dur = halves * HALF
    cta = styled_text("지금 플레이", 150, "gold")
    f = ImageFont.truetype(BODY, 46)
    t2 = "솔로 · AI 파티 · 최대 4인 온라인 레이드"
    b = f.getbbox(t2, stroke_width=4)
    w = max(cta.width, b[2] - b[0] + 40)
    img = Image.new("RGBA", (w, cta.height + 40), (0, 0, 0, 0))
    img.alpha_composite(cta, ((w - cta.width) // 2, 0))
    ImageDraw.Draw(img).text(((w - (b[2] - b[0])) // 2 - b[0], cta.height - 50 - b[1]), t2, font=f, fill=(255, 255, 255, 255), stroke_width=4, stroke_fill=(0, 0, 0, 255))
    cta_path = os.path.join(WORK, "cta.png")
    img.save(cta_path)
    out = os.path.join(WORK, f"s{i:02d}.mp4")
    bg = os.path.join(ROOT, "clips", background_clip, "f%04d.jpg")
    fc = (f"[0:v]fps={FPS},scale=1920:1080,gblur=sigma=18,eq=brightness=-0.28:saturation=1.3[bg];"
          f"[1:v]format=rgba,scale=w='980*(1.8-0.8*min(t\\,0.18)/0.18)':h=-1:eval=frame[lg];"
          f"[2:v]format=rgba,fade=t=in:st=0.35:d=0.25:alpha=1[ct];"
          f"[bg][lg]overlay=x=(W-w)/2:y=(H-h)/2-170:eval=frame[a];[a][ct]overlay=x=(W-w)/2:y=H/2+60,"
          f"fade=t=in:st=0:d=0.12:color=white,fade=t=out:st={dur - 0.5:.3f}:d=0.5[o]")
    run(["ffmpeg", "-y", "-loglevel", "error", "-framerate", str(FPS), "-start_number", "120", "-i", bg,
         "-loop", "1", "-framerate", str(FPS), "-i", LOGO, "-loop", "1", "-framerate", str(FPS), "-i", cta_path,
         "-filter_complex", fc, "-map", "[o]", "-t", f"{dur:.4f}", "-r", str(FPS), "-an", "-c:v", "libx264", "-crf", "17", "-pix_fmt", "yuv420p", out])
    return out, dur


def main():
    parts, cuts = [], []
    t = 0.0
    for i, s in enumerate(SHOTS):
        path, dur = shot(i, s)
        parts.append(path)
        cuts.append((t, s[6]))
        t += dur
    path, dur = end_card(len(SHOTS), END_HALVES, "awaken_arcanist")
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
    mix.append("".join(labels) + "amix=inputs=" + str(len(labels)) + ":normalize=0,alimiter=limit=0.95,loudnorm=I=-14:TP=-1:LRA=9[a]")
    run(["ffmpeg", "-y", "-loglevel", "error", *inputs, "-filter_complex", ";".join(mix), "-map", "0:v", "-map", "[a]",
         "-c:v", "copy", "-c:a", "aac", "-b:a", "192k", "-ar", "48000", "-shortest", "-movflags", "+faststart", OUT])
    print(f"{OUT} ({t:.1f} s)")


if __name__ == "__main__":
    main()
