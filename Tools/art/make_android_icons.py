#!/usr/bin/env python3
"""Android launcher icons from the game icon (Assets/Art/AppIcon/app_icon_1024.png).

Android 8+ launchers draw an adaptive icon: a background layer and a foreground layer, cut to a circle, squircle
or rounded square by the launcher. The game icon is one full-bleed square (navy field + sword and shield), so:
  android_bg.png     solid navy field
  android_fg.png     the artwork with the navy keyed out, shrunk so the diagonal sword fits the visible circle
  android_round.png  bg + fg in a circle (legacy round icon)
  android_legacy.png the original square icon (legacy icon)
ProjectSetup.ApplyAndroidIcons puts them in the Android player settings.

  python3 Tools/art/make_android_icons.py [preview.png]
"""
import os
import sys

import numpy as np
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ICON_DIR = os.path.join(ROOT, "Assets", "Art", "AppIcon")
SRC = os.path.join(ICON_DIR, "app_icon_1024.png")
SIZE = 1024
NAVY = (21, 27, 59)
SCALE = 0.54          # artwork size inside the 108dp layer: the diagonal sword fits the visible circle (middle 72/108)
ROUND_SCALE = 0.80    # artwork size in the legacy round icon (no launcher mask)
KEY_FULL, KEY_ZERO = 70, 38   # colour distance from the field: >= full is opaque, <= zero is transparent


def keyed_artwork():
    rgb = np.array(Image.open(SRC).convert("RGB")).astype(float)
    # The field is a soft navy gradient: key against the local field colour estimate (median of the border ring).
    border = np.concatenate([rgb[:48].reshape(-1, 3), rgb[-48:].reshape(-1, 3), rgb[:, :48].reshape(-1, 3), rgb[:, -48:].reshape(-1, 3)])
    field = np.median(border, axis=0)
    dist = np.sqrt(((rgb - field) ** 2).sum(-1))
    alpha = np.clip((dist - KEY_ZERO) / (KEY_FULL - KEY_ZERO), 0, 1)
    # Drop the dark vignette at the very edge of the source square.
    edge = 36
    alpha[:edge] = alpha[-edge:] = 0
    alpha[:, :edge] = alpha[:, -edge:] = 0
    out = np.dstack([rgb, alpha * 255]).astype(np.uint8)
    return Image.fromarray(out, "RGBA")


def main():
    art = keyed_artwork()
    small = art.resize((int(SIZE * SCALE),) * 2, Image.LANCZOS)
    fg = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    off = (SIZE - small.width) // 2
    fg.alpha_composite(small, (off, off))
    bg = Image.new("RGBA", (SIZE, SIZE), NAVY + (255,))

    full = bg.copy()
    full.alpha_composite(fg)
    legacy = Image.open(SRC).convert("RGBA")
    big = art.resize((int(SIZE * ROUND_SCALE),) * 2, Image.LANCZOS)
    round_full = bg.copy()
    round_full.alpha_composite(big, ((SIZE - big.width) // 2,) * 2)
    circle = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(circle).ellipse((0, 0, SIZE - 1, SIZE - 1), fill=255)
    round_icon = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    round_icon.paste(round_full, (0, 0), circle)

    bg.save(os.path.join(ICON_DIR, "android_bg.png"))
    fg.save(os.path.join(ICON_DIR, "android_fg.png"))
    legacy.save(os.path.join(ICON_DIR, "android_legacy.png"))
    round_icon.save(os.path.join(ICON_DIR, "android_round.png"))

    if len(sys.argv) > 1:
        # Preview: what launchers show (circle and rounded square cut of the middle 72/108).
        vis = int(SIZE * 72 / 108)
        v0 = (SIZE - vis) // 2
        mid = full.crop((v0, v0, v0 + vis, v0 + vis)).resize((256, 256), Image.LANCZOS)
        sheet = Image.new("RGBA", (256 * 4 + 50, 276), (235, 235, 235, 255))
        m = Image.new("L", (256, 256), 0)
        ImageDraw.Draw(m).ellipse((0, 0, 255, 255), fill=255)
        sheet.paste(mid, (10, 10), m)
        m2 = Image.new("L", (256, 256), 0)
        ImageDraw.Draw(m2).rounded_rectangle((0, 0, 255, 255), radius=60, fill=255)
        sheet.paste(mid, (276, 10), m2)
        sheet.paste(round_icon.resize((256, 256), Image.LANCZOS), (542, 10), round_icon.resize((256, 256)).split()[3])
        sheet.paste(legacy.resize((256, 256), Image.LANCZOS), (808, 10))
        sheet.save(sys.argv[1])
    print("android icons written to", ICON_DIR)


if __name__ == "__main__":
    main()
