# -*- coding: utf-8 -*-
"""Generated character sheets (magenta, rows = front / back / right side) -> game frames.

Writes Assets/Resources/Art/char_<lookId>_<dir>_<frame>.png (128x128, feet 12 px above the bottom, imported
at ppu 72 = the world size of the 64 px / ppu 36 warrior). SpriteLibrary.GetCharacter picks them up by name.
Usage: python3 process_characters.py <source dir>
"""
import os, sys
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from process_generated import key, ART

FRAME, FEET = 128, 12
FRAMES = ["idle0", "idle1", "walk0", "walk1", "walk2", "walk3", "attack", "hurt"]

def pieces(rgba, min_area=1500):
    solid = rgba[..., 3] > 100
    lab, n = ndimage.label(ndimage.binary_dilation(solid, iterations=3))
    out = []
    for i, sl in enumerate(ndimage.find_objects(lab), 1):
        area = int((solid[sl] & (lab[sl] == i)).sum())
        if area < min_area: continue
        piece = rgba[sl].copy()
        piece[lab[sl] != i] = 0
        out.append(((sl[0].start + sl[0].stop) / 2, sl[1].start, sl, piece))
    # rows: split by the largest vertical gaps between centres
    out.sort(key=lambda t: t[0])
    ys = [t[0] for t in out]
    gaps = sorted(range(1, len(ys)), key=lambda i: ys[i] - ys[i - 1], reverse=True)[:2]
    rows, start = [], 0
    for cut in sorted(gaps):
        rows.append(out[start:cut]); start = cut
    rows.append(out[start:])
    return [sorted(r, key=lambda t: t[1]) for r in rows]

def place(piece, scale):
    h, w = piece.shape[:2]
    img = Image.fromarray(piece, "RGBA").resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)
    ys, xs = np.nonzero(np.asarray(img)[..., 3] > 40)
    frame = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    # feet (lowest opaque row) on the baseline, horizontally centred on the body's opaque span
    cx = (xs.min() + xs.max()) / 2
    frame.alpha_composite(img, (round(FRAME / 2 - cx), FRAME - FEET - 1 - ys.max()))
    return frame

def sheet(src, look_id, mapping, height=104):
    """mapping: {dir: (row index, [component index per FRAMES entry])}. height = idle0 front height in px."""
    rows = pieces(key(src, soft=1.4))
    print(look_id, "rows:", [len(r) for r in rows])
    idle = rows[mapping["down"][0]][mapping["down"][1][0]][3]
    ys = np.nonzero(idle[..., 3] > 100)[0]
    scale = height / (ys.max() - ys.min() + 1)
    for d, (row, idx) in mapping.items():
        for name, i in zip(FRAMES, idx):
            frame = place(rows[row][i][3], scale)
            frame.save(os.path.join(ART, f"char_{look_id}_{d}_{name}.png"))
    print("wrote", look_id, len(mapping) * len(FRAMES), "frames, scale", round(scale, 3))

# Nine figures per row in these sheets: idle, idle, idle, walk x4 (3/4 turned), cast, hurt.
# Diagonals (downside / upside) use the 3/4-turned walking figures; the game mirrors every left facing.
NINE = {
    "down": (0, [0, 1, 3, 4, 5, 6, 7, 8]),
    "downside": (0, [3, 5, 3, 4, 5, 6, 7, 8]),
    "up": (1, [0, 1, 3, 4, 5, 6, 0, 2]),
    "upside": (1, [3, 5, 3, 4, 5, 6, 0, 2]),
    "side": (2, [0, 1, 3, 4, 5, 6, 7, 0]),
}

if __name__ == "__main__":
    src = sys.argv[1]
    sheet(os.path.join(src, "gen_mage_sheet2.jpg"), "mage", NINE, height=108)
