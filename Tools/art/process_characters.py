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
DOT = False  # --dot: warrior format (64 px frame, feet 7 px up, hard pixels, limited palette, ppu 36)
FRAMES = ["idle0", "idle1", "walk0", "walk1", "walk2", "walk3", "attack", "hurt"]

def load(src):
    """Transparent PNG (Codex image generation) as is; magenta-background images (Flow) keyed."""
    im = Image.open(src)
    if im.mode in ("RGBA", "LA") or "transparency" in im.info:
        rgba = np.asarray(im.convert("RGBA")).copy()
        if (rgba[..., 3] < 250).mean() > 0.05:  # real transparency
            rgba[rgba[..., 3] < 24] = 0
            return rgba
        tmp = src + ".rgb.png"  # opaque PNG on magenta: key it like a Flow image
        im.convert("RGB").save(tmp)
        try: return key(tmp, soft=1.4)
        finally: os.remove(tmp)
    return key(src, soft=1.4)

def pieces(rgba, min_area=1500):
    solid = rgba[..., 3] > 100
    lab, n = ndimage.label(ndimage.binary_dilation(solid, iterations=3))
    boxes = []  # [y0, y1, x0, x1, label set]
    for i, sl in enumerate(ndimage.find_objects(lab), 1):
        if int((solid[sl] & (lab[sl] == i)).sum()) < 150: continue
        boxes.append([sl[0].start, sl[0].stop, sl[1].start, sl[1].stop, {i}, int((solid[sl] & (lab[sl] == i)).sum())])
    # A head split from its body by a translucent band: same column, small vertical gap -> one figure.
    merged = True
    while merged:
        merged = False
        for a in range(len(boxes)):
            for b in range(a + 1, len(boxes)):
                A, B = boxes[a], boxes[b]
                overlap = min(A[3], B[3]) - max(A[2], B[2])
                gap = max(A[0], B[0]) - min(A[1], B[1])
                # only a fragment (much smaller than a whole figure) joins its neighbour
                small = min(A[5], B[5]) < 0.35 * max(A[5], B[5])
                if small and overlap > 0.5 * min(A[3] - A[2], B[3] - B[2]) and gap < 30:
                    boxes[a] = [min(A[0], B[0]), max(A[1], B[1]), min(A[2], B[2]), max(A[3], B[3]), A[4] | B[4], A[5] + B[5]]
                    del boxes[b]
                    merged = True
                    break
            if merged: break
    out = []
    for y0, y1, x0, x1, labels, _ in boxes:
        piece = rgba[y0:y1, x0:x1].copy()
        piece[~np.isin(lab[y0:y1, x0:x1], list(labels))] = 0
        if int((piece[..., 3] > 100).sum()) < min_area: continue
        out.append(((y0 + y1) / 2, x0, (y0, y1), piece))
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
    if DOT:
        return place_dot(piece, scale)
    img = Image.fromarray(piece, "RGBA").resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.LANCZOS)
    ys, xs = np.nonzero(np.asarray(img)[..., 3] > 40)
    frame = Image.new("RGBA", (FRAME, FRAME), (0, 0, 0, 0))
    # feet (lowest opaque row) on the baseline, horizontally centred on the body's opaque span
    cx = (xs.min() + xs.max()) / 2
    frame.alpha_composite(img, (round(FRAME / 2 - cx), FRAME - FEET - 1 - ys.max()))
    return frame

def place_dot(piece, scale):
    """Down to the warrior's pixel grid: box filter, hard alpha, the sheet palette, feet on the baseline."""
    h, w = piece.shape[:2]
    small = Image.fromarray(piece, "RGBA").resize((max(1, round(w * scale)), max(1, round(h * scale))), Image.BOX)
    a = np.asarray(small).copy()
    a[..., 3] = np.where(a[..., 3] >= 110, 255, 0)
    rgb = Image.fromarray(a[..., :3], "RGB")
    q = np.asarray(rgb.quantize(palette=PALETTE, dither=Image.Dither.NONE).convert("RGB"))
    out = np.dstack([q, a[..., 3]]).astype(np.uint8)
    out[out[..., 3] == 0] = 0
    ys, xs = np.nonzero(out[..., 3])
    frame = Image.new("RGBA", (64, 64), (0, 0, 0, 0))
    cx = (xs.min() + xs.max()) / 2
    frame.alpha_composite(Image.fromarray(out, "RGBA"), (round(32 - cx), 64 - 7 - 1 - ys.max()))
    return frame

PALETTE = None

def sheet(src, look_id, mapping, height=104):
    """mapping: {dir: (row index, [component index per FRAMES entry])}. height = idle0 front height in px."""
    rgba = load(src)
    rows = pieces(rgba)
    print(look_id, "rows:", [len(r) for r in rows])
    # Rows that did not split into 9 figures (touching or broken figures): cut the evenly spaced sheet
    # into 9 equal columns inside that row's band.
    W = rgba.shape[1]
    for k, row in enumerate(rows):
        if len(row) > 9:
            # extra fragments (a dropped hat feather, a spark): keep the 9 largest figures
            row = sorted(row, key=lambda t: -(t[3][..., 3] > 100).sum())[:9]
            rows[k] = sorted(row, key=lambda t: t[1])
            print(f"  row {k}: kept the 9 largest pieces")
            continue
        if len(row) in (8, 9): continue
        y0 = min(t[2][0] for t in row); y1 = max(t[2][1] for t in row)
        fixed = []
        for c in range(9):
            x0, x1 = round(c * W / 9), round((c + 1) * W / 9)
            cell = rgba[y0:y1, x0:x1].copy()
            # drop slivers of the neighbours: keep components centred in the middle of the cell
            clab, cn = ndimage.label(ndimage.binary_dilation(cell[..., 3] > 100, iterations=2))
            keep = np.zeros(cell.shape[:2], bool)
            for j, csl in enumerate(ndimage.find_objects(clab), 1):
                cx = (csl[1].start + csl[1].stop) / 2
                if 0.2 * cell.shape[1] < cx < 0.8 * cell.shape[1]: keep |= clab == j
            cell[~keep] = 0
            if (cell[..., 3] > 100).sum() < 500: continue
            fixed.append(((y0 + y1) / 2, x0, (y0, y1), cell))
        print(f"  row {k}: {len(row)} pieces -> {len(fixed)} grid cells")
        rows[k] = fixed
    if all(len(r) == 8 for r in rows):
        # 8 figures per row (the warrior sheet's layout): idle, idle, walk x4, action, hurt.
        mapping = EIGHT
        print("  8-column sheet")
    idle = rows[mapping["down"][0]][mapping["down"][1][0]][3]
    ys = np.nonzero(idle[..., 3] > 100)[0]
    if DOT:
        height = round(height * 44 / 104)  # same relative heights, warrior = 44 px
        global PALETTE
        opaque = rgba[rgba[..., 3] > 200][:, :3]
        sample = Image.fromarray(opaque[::max(1, len(opaque) // 200000)].reshape(-1, 1, 3), "RGB")
        PALETTE = sample.quantize(colors=28, method=Image.Quantize.MEDIANCUT)
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

EIGHT = {
    "down": (0, [0, 1, 2, 3, 4, 5, 6, 7]),
    "downside": (0, [2, 4, 2, 3, 4, 5, 6, 7]),
    "up": (1, [0, 1, 2, 3, 4, 5, 6, 7]),
    "upside": (1, [2, 4, 2, 3, 4, 5, 6, 7]),
    "side": (2, [0, 1, 2, 3, 4, 5, 6, 7]),
}

# Codex sheets keep the facing per row (row 2 attack / hurt are back views too).
CODEX = dict(NINE)
CODEX["up"] = (1, [0, 1, 3, 4, 5, 6, 7, 8])
CODEX["upside"] = (1, [3, 5, 3, 4, 5, 6, 7, 8])
CODEX["side"] = (2, [0, 1, 3, 4, 5, 6, 7, 8])

if __name__ == "__main__":
    src = sys.argv[1]
    only = [a for a in sys.argv[2:] if not a.startswith("--")]
    DOT = "--dot" in sys.argv
    # (source file, look id(s), height of the front idle figure in px of the 128 frame)
    jobs = [
        ("gen_mage_sheet2.jpg", ["mage"], 108),
        ("codex_merc_bron.png", ["merc_bron"], 104),
        ("codex_merc_kai.png", ["merc_kai"], 100),
        ("codex_merc_elin.png", ["merc_elin"], 98),
        ("codex_merc_sera.png", ["merc_sera"], 104),
        ("codex_kael.png", ["kael"], 108),
        ("codex_ria.png", ["ria"], 96),
        ("codex_bram.png", ["bram"], 100),
        ("codex_hanna.png", ["hanna"], 98),
        ("codex_leona.png", ["leona"], 106),
        ("codex_orban.png", ["orban"], 110),
        ("codex_grah.png", ["grah"], 116),
        ("codex_bargas.png", ["bargas"], 116),
        ("codex_knight.png", ["knight", "knight_dorn", "knight_ivy", "knight_mo"], 104),
        ("codex_herbalist.png", ["herbalist"], 100),
        ("codex_rock_golem.png", ["boss_rock_golem"], 86),
        # dungeon monsters (dot only)
        ("codex_skeleton.png", ["skeleton"], 92), ("codex_skel_warrior.png", ["skel_warrior"], 96), ("codex_skel_gold.png", ["skel_gold"], 90),
        ("codex_skel_miner.png", ["skel_miner"], 94), ("codex_skel_necro.png", ["skel_necro"], 98), ("codex_skel_archer.png", ["skel_archer"], 94),
        ("codex_skel_shield.png", ["skel_shield"], 96), ("codex_skel_knight.png", ["skel_knight"], 104),
        ("codex_boss_gold_foreman.png", ["boss_gold_foreman"], 108), ("codex_boss_mine_captain.png", ["boss_mine_captain"], 108),
        ("codex_boss_lich.png", ["boss_lich"], 110), ("codex_boss_archer_chief.png", ["boss_archer_chief"], 108),
        ("codex_boss_armory_warden.png", ["boss_armory_warden"], 110), ("codex_boss_skeleton_king.png", ["boss_skeleton_king"], 112),
        # villagers
        ("codex_chief.png", ["chief"], 100), ("codex_farmer.png", ["farmer"], 98), ("codex_fisher.png", ["fisher"], 100),
        ("codex_builder.png", ["builder"], 102), ("codex_lumberjack.png", ["lumberjack"], 106), ("codex_miner.png", ["miner"], 98),
        ("codex_carrier.png", ["carrier"], 98), ("codex_kid.png", ["kid"], 82), ("codex_merchant.png", ["merchant"], 98),
        ("codex_smith.png", ["smith"], 104), ("codex_keeper.png", ["keeper"], 100), ("codex_trader.png", ["trader"], 100),
        ("codex_dungeon_guide.png", ["dungeon_guide"], 100),
    ]
    for f, ids, height in jobs:
        path = os.path.join(src, f)
        if DOT:
            dot = os.path.join(src, "dot_" + f.replace("codex_", "").replace("gen_mage_sheet2.jpg", "mage.png"))
            if not os.path.exists(dot): continue
            path, f = dot, "codex_" + os.path.basename(dot)
        if not os.path.exists(path) or (only and not set(ids) & set(only)): continue
        for look_id in ids:
            try: sheet(path, look_id, CODEX if f.startswith("codex_") else NINE, height=height)
            except Exception as e: print("FAILED", look_id, e)
    # Frames that came out cut in their sheet reuse a neighbour (checked on the contact sheet).
    import shutil
    for look_id, d, bad, good in [("hanna", "up", "walk3", "walk1"), ("hanna", "upside", "walk3", "walk1")]:
        shutil.copyfile(os.path.join(ART, f"char_{look_id}_{d}_{good}.png"), os.path.join(ART, f"char_{look_id}_{d}_{bad}.png"))
