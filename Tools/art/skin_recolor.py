#!/usr/bin/env python3
"""Common and rare costume skins (2026-10-14): the class body recoloured, shape unchanged (same method as the careers).

Warrior common/rare skins use FemaleWarriorPalette at runtime; no old body export.
Mage skins recolour the navy robe and hat:
  Resources/Art/char_mage_<dir>_<frame>.png -> Resources/Art/char_<skin id>_<dir>_<frame>.png
Each new png gets a .meta copied from the career sheet it mirrors (same import settings, deterministic new GUID).
Ids must match SkinCatalog.cs and server starshopDefs.ts.
Usage: python3 Tools/art/skin_recolor.py [repo root]
"""
import glob
import hashlib
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from career_recolor import RES, recolor  # noqa: E402

# sheet -> (vest hue, vest sat, cloth hue, cloth sat, vest gain). Careers already use steel blue and teal.
WARRIOR = {
    "w_sand": (36, 0.30, 30, 0.42, 1.10),      # 모래빛 여행복
    "w_moss": (95, 0.26, 125, 0.45, 1.0),      # 숲지기 옷
    "w_rust": (18, 0.32, 12, 0.48, 1.0),       # 적갈 견습복
    "w_crimson": (356, 0.55, 348, 0.66, 1.0),  # 진홍 용병복
    "w_amethyst": (282, 0.40, 290, 0.55, 1.05),  # 자수정 기사복
    "w_brass": (44, 0.58, 30, 0.62, 1.12),     # 황동 경비복
}


def mage_rule(hue, sat, gain, grey=False):
    def rule(h, s, v):
        if 200 <= h <= 250 and s > 0.2:
            return hue, (0.05 if grey else sat), v * gain
        return None
    return rule


# Careers already use violet (arcanist) and white (bishop).
MAGE = {
    "skin_m_ash": mage_rule(0, 0, 1.35, grey=True),  # 잿빛 견습 로브
    "skin_m_pilgrim": mage_rule(28, 0.45, 1.05),     # 순례자의 로브
    "skin_m_sprout": mage_rule(95, 0.50, 1.15),      # 새싹 로브
    "skin_m_rose": mage_rule(335, 0.62, 1.25),       # 장밋빛 로브
    "skin_m_emerald": mage_rule(152, 0.70, 1.15),    # 에메랄드 로브
    "skin_m_sunset": mage_rule(22, 0.80, 1.30),      # 노을 로브
}


def write_meta(png, template_meta):
    rel = os.path.relpath(png, os.path.dirname(RES)).replace(os.sep, "/")
    guid = hashlib.md5(("dotRPG:" + rel).encode()).hexdigest()
    with open(template_meta, encoding="utf-8") as f:
        body = f.read()
    with open(png + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(re.sub(r"^guid: [0-9a-f]+$", "guid: " + guid, body, count=1, flags=re.M))


def write_folder_meta(folder):
    rel = os.path.relpath(folder, os.path.dirname(RES)).replace(os.sep, "/")
    guid = hashlib.md5(("dotRPG:" + rel).encode()).hexdigest()
    with open(folder + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def main():
    print("Warrior skins: approved FemaleWarrior body + runtime palette (no legacy export).")
    frames = sorted(glob.glob(os.path.join(RES, "Art", "char_mage_*.png")))
    for name, rule in MAGE.items():
        for f in frames:
            base = os.path.basename(f)
            dst = os.path.join(os.path.dirname(f), base.replace("char_mage_", "char_" + name + "_"))
            recolor(Image_open(f), rule).save(dst)
            if not os.path.exists(dst + ".meta"):
                write_meta(dst, os.path.join(os.path.dirname(f), base.replace("char_mage_", "char_career_arcanist_") + ".meta"))
        print(name, len(frames), "frames")


def Image_open(path):
    from PIL import Image
    return Image.open(path)


if __name__ == "__main__":
    main()
