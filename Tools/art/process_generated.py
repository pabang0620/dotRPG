# -*- coding: utf-8 -*-
"""Generated (Flow Nano Banana, magenta background) art -> game PNGs.

Soft magenta key with un-mixing (smoke, glows keep partial alpha without a pink fringe), connected
components in reading order for sheets, buildings placed on the 1536x1024 village building canvas
(bottom step at y=980, centred at x=768) like Assets/Resources/Art/Town/*.png.
Usage: python3 process_generated.py <source dir>
"""
import os, sys
import numpy as np
from PIL import Image
from scipy import ndimage

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ART = os.path.join(ROOT, "Assets", "Resources", "Art")

def key(path, soft=1.0):
    im = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    # Magenta amount: how much the pixel is "r and b high above g".
    m = np.clip((np.minimum(r, b) - g - 40.0) / (150.0 * soft), 0.0, 1.0)
    pure = (r > 140) & (b > 140) & (g < 110)
    # Flow's sparkle watermark sits ~100 px in from the bottom-right corner (measured on 1376x768 and
    # 1024x1024 outputs): light pink / pale pixels in that box are background.
    h, w = r.shape
    box = np.zeros(r.shape, bool)
    box[h - 150:h - 45, w - 150:w - 45] = True
    sparkle = (r > 190) & (b > 190) & (g > 90)
    pale = (np.minimum(np.minimum(r, g), b) > 120) & ((np.maximum(np.maximum(r, g), b) - np.minimum(np.minimum(r, g), b)) < 110)
    im[box & (sparkle | pale)] = (255.0, 0.0, 255.0)
    r, g, b = im[..., 0], im[..., 1], im[..., 2]
    m = np.clip((np.minimum(r, b) - g - 40.0) / (150.0 * soft), 0.0, 1.0)
    pure = (r > 140) & (b > 140) & (g < 110)
    m[pure & (np.minimum(r, b) - g > 120)] = 1.0
    a = 1.0 - m
    # Un-mix the magenta that showed through: c = (obs - m*M) / (1 - m).
    out = im.copy()
    keep = a > 0.02
    for ch, M in ((0, 255.0), (1, 0.0), (2, 255.0)):
        c = out[..., ch]
        c[keep] = (c[keep] - m[keep] * M) / a[keep]
    out = np.clip(out, 0, 255)
    # What still looks magenta after un-mixing (smoke edges, ash over the background) is neutralised:
    # translucent pixels lose it, opaque ones turn to the grey they were mixed from.
    tint = (out[..., 0] > out[..., 1] + 35) & (out[..., 2] > out[..., 1] + 35)
    a[tint & (a < 0.85)] = 0.0
    grey = out[..., 1:2].repeat(3, axis=2) * 0.5 + out * 0.5
    out[tint] = np.minimum(grey[tint], out[tint])
    out[tint, 0] = out[tint, 1] + 6
    out[tint, 2] = out[tint, 1] + 4
    rgba = np.dstack([out, a * 255.0]).astype(np.uint8)
    rgba[a <= 0.02] = 0
    return rgba

def load_any(path, soft=1.0):
    """Transparent PNG (Codex) as is, magenta JPEG (Flow) keyed."""
    im = Image.open(path)
    if im.mode in ("RGBA", "LA") or "transparency" in im.info:
        rgba = np.asarray(im.convert("RGBA")).copy()
        rgba[rgba[..., 3] < 24] = 0
        return rgba
    return key(path, soft)

def components(rgba, min_area=900, merge=9):
    solid = rgba[..., 3] > 100
    grown = ndimage.binary_dilation(solid, iterations=merge)
    lab, n = ndimage.label(grown)
    boxes = []
    for i, sl in enumerate(ndimage.find_objects(lab), 1):
        area = int((solid[sl] & (lab[sl] == i)).sum())
        if area < min_area: continue
        boxes.append((sl, i))
    # Reading order: rows by vertical centre.
    boxes.sort(key=lambda t: ((t[0][0].start + t[0][0].stop) // 2) // 220 * 10000 + t[0][1].start)
    crops = []
    for sl, i in boxes:
        y0, y1, x0, x1 = sl[0].start, sl[0].stop, sl[1].start, sl[1].stop
        piece = rgba[y0:y1, x0:x1].copy()
        piece[lab[y0:y1, x0:x1] != i] = 0
        crops.append(trim(piece))
    return crops

def trim(rgba, pad=4):
    ys, xs = np.nonzero(rgba[..., 3] > 8)
    piece = rgba[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    return np.pad(piece, ((pad, pad), (pad, pad), (0, 0)))

def save(rgba, name):
    path = os.path.join(ART, name + ".png")
    Image.fromarray(rgba, "RGBA").save(path)
    print("wrote", os.path.relpath(path, ROOT), rgba.shape[1], "x", rgba.shape[0])

def building(src, name, scale, left=108):
    """Scales by <scale> and puts the image's left edge at canvas x=<left>, bottom step at y=980
    (the same scale for a building and its variants keeps them the same size)."""
    rgba = trim(key(src, soft=1.3), pad=0)
    h, w = rgba.shape[:2]
    img = Image.fromarray(rgba, "RGBA").resize((round(w * scale), round(h * scale)), Image.LANCZOS)
    canvas = Image.new("RGBA", (1536, 1024), (0, 0, 0, 0))
    canvas.alpha_composite(img, (left, 980 - img.height))
    path = os.path.join(ART, "Town", name + ".png")
    canvas.save(path)
    print("wrote", os.path.relpath(path, ROOT), "building", img.size, "scale", round(scale, 3))
    return scale

SHEETS = {
    # sheet file: game keys in reading order (None = skip)
    "gen_story_props.jpg": ["story_trough", "story_horse", "story_grave", "story_grave_1", "story_lantern", "story_ribbon"],
    "gen_props_landmarks.jpg": ["town_fountain_0", "town_well", "town_board", "town_lamp"],
    "gen_props_small.jpg": ["town_barrel", "town_crate", "town_sacks", "town_hay", "town_woodpile", "town_ccrate"],
    "gen_props_misc.jpg": ["town_pot", "town_planter", "town_mailbox", "town_bench", "town_sign_0", None, "town_anvil"],
    "codex_props2.png": ["town_site_0", "town_site_1", "town_pile_0", "town_pile_1", "town_stump", "town_grave_0", "town_grave_1", "town_ruin", "town_bones"],
}

if __name__ == "__main__":
    src = sys.argv[1]
    for f, names in SHEETS.items():
        if not os.path.exists(os.path.join(src, f)): continue
        crops = components(load_any(os.path.join(src, f), soft=1.6 if "story" in f or "landmarks" in f else 1.0), merge=4 if "story" in f else 9)
        print(f, "->", len(crops), "pieces")
        for crop, name in zip(crops, names):
            if name: save(crop, name)
    # The anvil's stump sits where the watermark was: drop its pale grey sparkle pixels.
    anvil = np.asarray(Image.open(os.path.join(ART, "town_anvil.png"))).copy()
    h, w = anvil.shape[:2]
    reg = anvil[int(h * 0.72):, int(w * 0.75):]
    rgb = reg[..., :3].astype(int)
    pale = (rgb.min(axis=2) > 80) & (rgb.max(axis=2) - rgb.min(axis=2) < 40) & (reg[..., 3] > 0)
    reg[pale] = (92, 60, 34, 255)  # stump bark brown
    save(anvil, "town_anvil")
    # The fountain is one image for all three animation keys.
    fountain = np.asarray(Image.open(os.path.join(ART, "town_fountain_0.png")))
    for i in (1, 2): save(fountain, f"town_fountain_{i}")
    os.makedirs(os.path.join(ART, "Town"), exist_ok=True)
    # Stable: 1320 px wide on the canvas (~9.7 tiles at ppu 136), door centre at x~1101 (VillageBuildingArt).
    stable_scale = 1320 / 1193
    building(os.path.join(src, "gen_stable.jpg"), "stable", stable_scale)
    building(os.path.join(src, "gen_stable_burned.jpg"), "stable_burned", stable_scale)
