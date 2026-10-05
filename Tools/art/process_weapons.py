# -*- coding: utf-8 -*-
"""Generated weapon sheet (Flow, magenta, 2 rows x 4 upright weapons) -> held sprites and bag icons.

Row 1 = katanas (tiers 0-3), row 2 = staffs (tiers 0-3).
  - Katanas: Assets/Resources/SilverWarrior/katanas_v2.png (4 cells of 64x64, 48 px long, pommel on row 57 so the
    grip sits at KatanaArt.GripY).
  - Staffs: Assets/Resources/Art/Weapons/wpn_staff_<tier>.png (64x72, ppu 72, pivot near the foot like the HD canvas).
  - Icons: Assets/Resources/Art/Weapons/eqicon_sword_<tier>.png / eqicon_staff_<tier>.png (64x64, diagonal).
The shipped originals stay where they are; SpriteLibrary / KatanaArt prefer these files.
Usage: python3 process_weapons.py <generated.jpg>
"""
import os, sys
import numpy as np
from PIL import Image
from scipy import ndimage
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from process_generated import key, ROOT, ART


def cells(rgba):
    """The 8 weapons: cut the 4x2 grid, drop the dark grid lines, keep the weapon and its own sparkles."""
    h, w = rgba.shape[:2]
    out = []
    for r in range(2):
        for c in range(4):
            x0, x1 = round(c * w / 4) + 10, round((c + 1) * w / 4) - 10
            y0, y1 = round(r * h / 2) + 10, round((r + 1) * h / 2) - 10
            cell = rgba[y0:y1, x0:x1].copy()
            # grid lines: near-black, fully opaque, long straight runs at the cell border
            dark = (cell[..., :3].max(axis=2) < 40) & (cell[..., 3] > 0)
            for col in range(cell.shape[1]):
                if dark[:, col].mean() > 0.6: cell[:, col] = 0
            for row in range(cell.shape[0]):
                if dark[row, :].mean() > 0.6: cell[row, :] = 0
            solid = cell[..., 3] > 60
            lab, n = ndimage.label(ndimage.binary_dilation(solid, iterations=2))
            if n == 0:
                out.append(cell); continue
            sizes = ndimage.sum(solid, lab, range(1, n + 1))
            big = max(sizes)
            keep = np.zeros(solid.shape, bool)
            for i, s in enumerate(sizes, 1):
                if s >= max(12, big * 0.002): keep |= lab == i
            cell[~keep] = 0
            ys, xs = np.nonzero(cell[..., 3] > 60)
            out.append(cell[ys.min():ys.max() + 1, xs.min():xs.max() + 1])
    return out


def palette_of(pieces, colors=48):
    px = np.concatenate([p[p[..., 3] > 200][:, :3] for p in pieces])
    sample = Image.fromarray(px[:: max(1, len(px) // 200000)].reshape(-1, 1, 3), "RGB")
    return sample.quantize(colors=colors, method=Image.Quantize.MEDIANCUT)


def shrink(piece, height, palette):
    """Down to the game grid: centre sampling for crisp pixels, hard alpha, the sheet palette, 1-px dark rim."""
    h, w = piece.shape[:2]
    s = height / h
    size = (max(1, round(w * s)), height)
    img = Image.fromarray(piece, "RGBA")
    sharp = np.asarray(img.resize(size, Image.NEAREST)).copy()
    soft = np.asarray(img.resize(size, Image.BOX))
    alpha = np.where(soft[..., 3] >= 110, 255, 0).astype(np.uint8)
    q = np.asarray(Image.fromarray(sharp[..., :3], "RGB").quantize(palette=palette, dither=Image.Dither.NONE).convert("RGB"))
    out = np.dstack([q, alpha]).astype(np.uint8)
    out[alpha == 0] = 0
    solid = alpha > 0
    pad = np.pad(solid, 1)
    edge = solid & ~(pad[:-2, 1:-1] & pad[2:, 1:-1] & pad[1:-1, :-2] & pad[1:-1, 2:])
    rgb = out[..., :3].astype(np.float32)
    rim = edge & (rgb.mean(axis=2) > 70)
    out[..., :3][rim] = np.clip(rgb * 0.35 + 8, 0, 255)[rim].astype(np.uint8)
    return out


def paste(canvas, img, x, y):
    h, w = img.shape[:2]
    for yy in range(h):
        for xx in range(w):
            if img[yy, xx, 3] and 0 <= y + yy < canvas.shape[0] and 0 <= x + xx < canvas.shape[1]:
                canvas[y + yy, x + xx] = img[yy, xx]


def icon(piece, palette):
    """Bag icon: the weapon turned 45 degrees (tip to the top right), fitted into 56 px of a 64 px square."""
    img = Image.fromarray(piece, "RGBA").rotate(-45, resample=Image.NEAREST, expand=True)
    a = np.asarray(img)
    ys, xs = np.nonzero(a[..., 3] > 60)
    a = a[ys.min():ys.max() + 1, xs.min():xs.max() + 1]
    side = max(a.shape[:2])
    small = shrink(a, max(1, round(56 * a.shape[0] / side)), palette)
    canvas = np.zeros((64, 64, 4), np.uint8)
    paste(canvas, small, (64 - small.shape[1]) // 2, (64 - small.shape[0]) // 2)
    return canvas


def main(src):
    out_dir = os.path.join(ART, "Weapons")
    os.makedirs(out_dir, exist_ok=True)
    rgba = key(src, soft=1.4)
    pieces = cells(rgba)
    pals = [palette_of([p], 28) for p in pieces]  # one palette per weapon: small accents (leaves, gems) survive
    sheet = np.zeros((64, 256, 4), np.uint8)
    for t in range(4):
        k = shrink(pieces[t], 48, pals[t])
        paste(sheet, k, t * 64 + 32 - k.shape[1] // 2, 57 - k.shape[0] + 1)
        Image.fromarray(icon(pieces[t], pals[t]), "RGBA").save(os.path.join(out_dir, f"eqicon_sword_{t}.png"))
    Image.fromarray(sheet, "RGBA").save(os.path.join(ROOT, "Assets", "Resources", "SilverWarrior", "katanas_v2.png"))
    for t in range(4):
        s = shrink(pieces[4 + t], 68, pals[4 + t])
        canvas = np.zeros((72, 64, 4), np.uint8)
        paste(canvas, s, 32 - s.shape[1] // 2, 72 - 2 - s.shape[0])
        Image.fromarray(canvas, "RGBA").save(os.path.join(out_dir, f"wpn_staff_{t}.png"))
        Image.fromarray(icon(pieces[4 + t], pals[4 + t]), "RGBA").save(os.path.join(out_dir, f"eqicon_staff_{t}.png"))
    print("wrote katanas_v2.png and Art/Weapons (wpn_staff_0-3, eqicon_sword_0-3, eqicon_staff_0-3)")


if __name__ == "__main__":
    main(sys.argv[1])
