#!/usr/bin/env python3
"""Career base looks (2026-10-13): the class body recoloured per career, shape unchanged.

Warrior careers recolour the novice's grey vest and olive cloth (hair, skin, leather and outlines stay):
  Resources/SilverWarrior/{body,attack}.png -> Resources/SilverWarrior/Skins/career_<fighter|guardian>/
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


# ---------------- warrior: grey vest (low saturation, mid value) and olive cloth (hue 60-110)

def warrior_rule(vest_hue, vest_sat, cloth_hue, cloth_sat, vest_gain=1.0):
    def rule(h, s, v):
        if s < 0.16 and 0.18 < v < 0.72:          # grey vest / plates
            return vest_hue, vest_sat, v * vest_gain
        if 55 <= h <= 115 and s > 0.15 and v > 0.12:  # olive cloth
            return cloth_hue, cloth_sat, v * 1.1
        return None
    return rule


WARRIOR = {
    "career_fighter": warrior_rule(215, 0.38, 222, 0.55, 1.05),   # steel blue vest, navy cloth
    "career_guardian": warrior_rule(172, 0.42, 168, 0.55, 1.0),   # teal plates, deep teal cloth
}

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
    for name, rule in WARRIOR.items():
        out = os.path.join(RES, "SilverWarrior", "Skins", name)
        os.makedirs(out, exist_ok=True)
        for sheet in ("body", "attack"):
            recolor(Image.open(os.path.join(RES, "SilverWarrior", sheet + ".png")), rule).save(os.path.join(out, sheet + ".png"))
        print(name, "->", out)
    frames = sorted(glob.glob(os.path.join(RES, "Art", "char_mage_*.png")))
    for name, rule in MAGE.items():
        for f in frames:
            dst = os.path.join(os.path.dirname(f), os.path.basename(f).replace("char_mage_", "char_" + name + "_"))
            recolor(Image.open(f), rule).save(dst)
        print(name, len(frames), "frames")


if __name__ == "__main__":
    main()
