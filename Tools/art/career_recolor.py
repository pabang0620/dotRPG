#!/usr/bin/env python3
"""Career base looks (2026-10-13): the class body recoloured per career, shape unchanged.

Warrior careers use FemaleWarriorPalette at runtime with the approved FemaleWarrior atlas.
This tool only exports mage career frames; it must not recreate obsolete warrior sheets.
Mage careers recolour the navy robe and hat (white hair, gold trim, outlines stay):
  Resources/Art/char_mage_<dir>_<frame>.png -> Resources/Art/char_career_<arcanist|bishop>_<dir>_<frame>.png
Usage: python3 Tools/art/career_recolor.py [repo root]
"""
import colorsys
import glob
import os
import sys

from PIL import Image

ROOT = sys.argv[1] if len(sys.argv) > 1 else os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
RES = os.path.join(ROOT, "Assets", "Resources")


def recolor(img, rule):
    img = img.convert("RGBA")
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            out = rule(h * 360, s, v)
            if out is None:
                continue
            nr, ng, nb = colorsys.hsv_to_rgb(out[0] / 360 % 1, max(0, min(1, out[1])), max(0, min(1, out[2])))
            px[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), a)
    return img


# ---------------- mage: navy robe and hat (hue 205-245, saturated)

def arcanist(h, s, v):
    if 200 <= h <= 250 and s > 0.2:
        return 278, min(1, s * 1.15), v * 1.15            # deep violet
    return None


def bishop(h, s, v):
    if 200 <= h <= 250 and s > 0.2:
        return 208, 0.30 * (1 - v) + 0.06, 0.56 + v * 1.25  # white robe with sky-blue shading (gold trim stays)
    return None


MAGE = {"career_arcanist": arcanist, "career_bishop": bishop}


def main():
    print("Warrior careers: approved FemaleWarrior body + runtime palette (no legacy export).")
    frames = sorted(glob.glob(os.path.join(RES, "Art", "char_mage_*.png")))
    for name, rule in MAGE.items():
        for f in frames:
            dst = os.path.join(os.path.dirname(f), os.path.basename(f).replace("char_mage_", "char_" + name + "_"))
            recolor(Image.open(f), rule).save(dst)
        print(name, len(frames), "frames")


if __name__ == "__main__":
    main()
