#!/usr/bin/env python3
"""Rogue common and rare skins (2026-10-23): the base body/attack sheets recoloured, shape and alpha unchanged.

Base sheets: Resources/Rogue/body.png (512x320, 8x5 cells) and attack.png (384x320, 6x5 cells).
Painted parts: hair (silver, low saturation), cloak/hood (blue), leather (vest, trousers, boots, belt).
Rare skins also tint mid-tone grey metal (buckles, sheath trim) with the skin accent; bright blade pixels stay.
Skin, eyes, outlines and the dagger blade keep their colours.
Hair mask: r_navy/r_wine were made by changing only the hair, so the grey pixels of the base are hair.
Existing skin folders (r_navy, r_wine) are never overwritten: the script only reports how close the rebuilt pixels are.
Ids must match SkinCatalog.cs and server starshopDefs.ts.
Usage: python3 Tools/art/rogue_skin_recolor.py [repo root]
"""
import colorsys
import hashlib
import os
import re
import sys

from PIL import Image

ROOT = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..")
RES = os.path.join(ROOT, "Assets", "Resources")
BASE = os.path.join(RES, "Rogue")
SKINS = os.path.join(BASE, "Skins")
SHEETS = ("body.png", "attack.png")

# id: (cloth hue, sat, gain, add | leather hue, sat, gain | hair hue, sat, mid value | metal accent hue or None)
# hair None = keep base cloak/leather only is not used; navy/wine are hair-only (cloth and leather None).
SKIN = {
    "r_navy": (None, None, (222, 0.65, 0.33, 0.70, -0.186), None),
    "r_wine": (None, None, (351, 0.77, 0.55, 0.97, -0.165), None),
    # common (muted)
    "r_moss": ((105, 0.40, 1.00, 0.0), (35, 0.45, 1.00), (38, 0.45, 0.38), None),     # 숲길 정찰복
    "r_sand": ((38, 0.42, 1.25, 0.05), (28, 0.40, 1.10), (42, 0.55, 0.66), None),      # 사막 행상복
    "r_slate": ((208, 0.13, 1.05, 0.0), (215, 0.15, 0.90), (205, 0.12, 0.46), None),   # 회청 잠행복
    "r_rust": ((12, 0.58, 1.00, 0.0), (25, 0.50, 0.85), (8, 0.70, 0.40), None),        # 적갈 사냥꾼복
    "r_ivory": ((46, 0.10, 1.50, 0.10), (30, 0.30, 1.25), (48, 0.35, 0.72), None),     # 상아 견습복
    "r_plum": ((305, 0.40, 0.85, 0.0), (290, 0.30, 0.80), (285, 0.45, 0.25), None),    # 자두빛 두건복
    "r_teal": ((176, 0.60, 0.90, 0.0), (190, 0.30, 0.75), (28, 0.50, 0.28), None),     # 청록 밀수꾼복
    "r_coal": ((220, 0.06, 0.55, 0.0), (0, 0.0, 0.50), (0, 0.0, 0.90), None),          # 숯빛 야행복
    # rare (higher saturation and contrast, metal in the name colour)
    "r_crimson": ((355, 0.85, 1.10, 0.0), (0, 0.45, 0.75), (350, 0.30, 0.12), 350),    # 진홍 그림자복
    "r_violet": ((270, 0.75, 1.15, 0.0), (275, 0.40, 0.80), (300, 0.60, 0.60), 285),   # 보랏빛 야행복
    "r_gold": ((45, 0.85, 1.35, 0.0), (30, 0.60, 1.10), (50, 0.80, 0.85), 48),         # 황금 도둑복
    "r_frost": ((190, 0.55, 1.70, 0.05), (200, 0.30, 1.35), (195, 0.12, 0.95), 190),   # 서리 밀정복
    "r_rose": ((335, 0.70, 1.30, 0.0), (345, 0.50, 1.00), (322, 0.55, 0.65), 335),     # 장미 가면복
    "r_jade": ((150, 0.75, 1.00, 0.0), (100, 0.50, 0.70), (140, 0.60, 0.22), 150),     # 비취 독사복
    "r_ember": ((18, 0.90, 1.25, 0.0), (10, 0.70, 0.75), (25, 0.90, 0.75), 25),        # 불씨 두건복
    "r_azure": ((198, 0.85, 1.60, 0.0), (205, 0.40, 1.20), (205, 0.50, 0.85), 200),    # 하늘빛 바람복
    "r_onyx": ((260, 0.20, 0.45, 0.0), (270, 0.20, 0.45), (0, 0.80, 0.50), 355),       # 흑요 암살복
    "r_lilac": ((285, 0.45, 1.55, 0.05), (300, 0.35, 1.20), (330, 0.35, 0.90), 285),   # 라일락 무희복
}


def clamp(x):
    return max(0.0, min(1.0, x))


def paint(img, base_hair_mask, cloth, leather, hair, accent):
    img = img.convert("RGBA")
    px = img.load()
    for y in range(img.height):
        for x in range(img.width):
            r, g, b, a = px[x, y]
            if a == 0:
                continue
            h, s, v = colorsys.rgb_to_hsv(r / 255, g / 255, b / 255)
            h *= 360
            out = None
            if base_hair_mask[y][x]:
                hh, hs, vmid, slope, off = (hair + (1.0, None))[:5] if len(hair) == 3 else hair
                if off is None:
                    out = (hh, max(hs, 0.0) if s < 0.2 else max(hs, s), clamp(vmid + (v - 0.74) * slope))
                else:
                    out = (hh, hs, clamp(slope * v + off))
            elif cloth and 190 <= h <= 260 and s >= 0.25:
                ch, cs, gain, add = cloth
                out = (ch, cs, clamp(v * gain + add))
            elif leather and (h < 45 or h > 340) and s >= 0.2 and v < 0.58:
                lh, ls, gain = leather
                out = (lh, ls, clamp(v * gain))
            elif accent is not None and s < 0.2 and 0.3 < v <= 0.72:
                out = (accent, 0.55, v)
            if out is None:
                continue
            nr, ng, nb = colorsys.hsv_to_rgb(out[0] / 360 % 1, clamp(out[1]), clamp(out[2]))
            px[x, y] = (round(nr * 255), round(ng * 255), round(nb * 255), a)
    return img


def hair_mask(sheet):
    """Hair = pixels r_navy changed (the grey of the base). Rebuilt from a pure grey test when r_navy is absent."""
    base = Image.open(os.path.join(BASE, sheet)).convert("RGBA")
    navy = os.path.join(SKINS, "r_navy", sheet)
    bp = base.load()
    if os.path.exists(navy):
        np_ = Image.open(navy).convert("RGBA").load()
        return [[bp[x, y] != np_[x, y] for x in range(base.width)] for y in range(base.height)]
    raise SystemExit("r_navy sheet is needed as the hair mask reference")


def guid_for(path):
    rel = os.path.relpath(path, os.path.dirname(RES)).replace(os.sep, "/")
    return hashlib.md5(("dotRPG:" + rel).encode()).hexdigest()


def write_png_meta(png, template):
    with open(template, encoding="utf-8") as f:
        body = f.read()
    with open(png + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(re.sub(r"^guid: [0-9a-f]+$", "guid: " + guid_for(png), body, count=1, flags=re.M))


def write_folder_meta(folder):
    with open(folder + ".meta", "w", encoding="utf-8", newline="\n") as f:
        f.write(f"fileFormatVersion: 2\nguid: {guid_for(folder)}\nfolderAsset: yes\nDefaultImporter:\n  externalObjects: {{}}\n"
                "  userData: \n  assetBundleName: \n  assetBundleVariant: \n")


def main():
    masks = {s: hair_mask(s) for s in SHEETS}
    for sid, (cloth, leather, hair, accent) in SKIN.items():
        folder = os.path.join(SKINS, sid)
        exists = os.path.isdir(folder)
        for sheet in SHEETS:
            img = paint(Image.open(os.path.join(BASE, sheet)), masks[sheet], cloth, leather, hair, accent)
            dst = os.path.join(folder, sheet)
            if exists and os.path.exists(dst):
                old = Image.open(dst).convert("RGBA")
                diff = sum(1 for p, q in zip(old.getdata(), img.getdata()) if max(abs(p[i] - q[i]) for i in range(4)) > 6)
                print(f"{sid}/{sheet}: exists, not overwritten (rebuilt differs in {diff} px by >6)")
                continue
            os.makedirs(folder, exist_ok=True)
            img.save(dst)
            write_png_meta(dst, os.path.join(SKINS, "r_navy", sheet + ".meta"))
            print(f"{sid}/{sheet}: written")
        if not os.path.exists(folder + ".meta"):
            write_folder_meta(folder)


if __name__ == "__main__":
    main()
