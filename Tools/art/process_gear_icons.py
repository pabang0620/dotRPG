# -*- coding: utf-8 -*-
"""Cuts a generated gear icon sheet (Flow, magenta, gear_sheets.py layout) into Art/Gear/eqicon_<id>.png (64x64).
Each cell keeps its biggest shape (and the small sparkles near it), is fitted into 56 px with crisp pixels, the icon's
own palette and a 1-px dark rim, and gets a .meta copied from an existing icon (new guid). Existing files are not
overwritten unless --force.
Usage: python3 process_gear_icons.py <generated.jpg> <sheet name> [--force]
"""
import os, re, sys, uuid
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from process_generated import key, ART
from process_weapons import shrink, palette_of, paste
from gear_sheets import sheets, ids_of

META_SRC = os.path.join(ART, "maticon_essence.png.meta")


def cut(rgba, rows, cols):
    h, w = rgba.shape[:2]
    out = []
    for r in range(rows):
        row = []
        for c in range(cols):
            x0, x1 = round(c * w / cols) + 6, round((c + 1) * w / cols) - 6
            y0, y1 = round(r * h / rows) + 6, round((r + 1) * h / rows) - 6
            cell = rgba[y0:y1, x0:x1].copy()
            solid = cell[..., 3] > 60
            lab, n = ndimage.label(ndimage.binary_dilation(solid, iterations=3))
            if n == 0:
                row.append(None); continue
            sizes = ndimage.sum(solid, lab, range(1, n + 1))
            big = max(sizes)
            keep = np.zeros(solid.shape, bool)
            for i, s in enumerate(sizes, 1):
                if s >= max(10, big * 0.01): keep |= lab == i
            cell[~keep] = 0
            ys, xs = np.nonzero(cell[..., 3] > 60)
            row.append(cell[ys.min():ys.max() + 1, xs.min():xs.max() + 1] if len(ys) else None)
        out.append(row)
    return out


def icon(piece):
    pal = palette_of([piece], 32)
    side = max(piece.shape[:2])
    small = shrink(piece, max(1, round(56 * piece.shape[0] / side)), pal)
    canvas = np.zeros((64, 64, 4), np.uint8)
    paste(canvas, small, (64 - small.shape[1]) // 2, (64 - small.shape[0]) // 2)
    return canvas


def main(src, name, force=False):
    sheet = next(s for s in sheets() if s["name"] == name)
    ids = ids_of(sheet)
    cols = 5 if "legend" not in sheet else len(sheet["legend"])
    rgba = key(src, soft=1.4)
    h, w = rgba.shape[:2]
    rgba[int(h * 0.9):, int(w * 0.9):] = 0  # Flow watermark corner
    pieces = cut(rgba, len(ids), cols)
    out_dir = os.path.join(ART, "Gear")
    os.makedirs(out_dir, exist_ok=True)
    meta = open(META_SRC).read()
    done = []
    for r, row_ids in enumerate(ids):
        for c, item in enumerate(row_ids):
            piece = pieces[r][c]
            if piece is None:
                print("EMPTY", item); continue
            path = os.path.join(out_dir, f"eqicon_{item}.png")
            if os.path.exists(path) and not force:
                print("SKIP (exists)", item); continue
            Image.fromarray(icon(piece), "RGBA").save(path)
            if not os.path.exists(path + ".meta"):
                open(path + ".meta", "w").write(re.sub(r"guid: [0-9a-f]{32}", "guid: " + uuid.uuid4().hex, meta, count=1))
            done.append(item)
    print("wrote", len(done), "icons for", name)


if __name__ == "__main__":
    main(sys.argv[1], sys.argv[2], "--force" in sys.argv)
