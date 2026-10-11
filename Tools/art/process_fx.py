# -*- coding: utf-8 -*-
"""Generated skill effect art (Flow, Docs/PLAN_SKILL_VFX.md) -> Assets/Resources/Art/VfxImg/<id>.png + <id>.json.

Input: Tools/art/vfx_raw/manifest.json = [{id, file, background: "black"|"magenta", cols, rows, frames, note}]
       (file is relative to vfx_raw). Raw files are only read, never written.
Steps per entry:
  1. Alpha. magenta: the soft key with un-mixing from process_generated.key (no pink fringe).
     black: glow art -> alpha = brightest channel, colour = rgb / alpha (drawn additive in game, json additive=true).
  2. Frames. Flow's watermark corner is cleared (and an optional "crop": [x0,y0,x1,y1] applied), then rows and
     frames are found from the empty bands between drawings (the grid Flow returns often differs from the prompt),
     each cut to what is drawn. The first `frames` (reading order) are used.
  3. One scale for the whole clip (the largest frame fits the target frame with a small margin), every frame
     centred on the same canvas, so the animation does not jump. Downscaling uses area sampling.
  4. A horizontal strip PNG (frames side by side) + json {frames, frameW, frameH, fps, additive, pivotX, pivotY}.
Usage: python3 Tools/art/process_fx.py [--only <id>]
"""
import json, os, sys
import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
RAW = os.path.join(HERE, "vfx_raw")
OUT = os.path.join(ROOT, "Assets", "Resources", "Art", "VfxImg")
sys.path.insert(0, HERE)
from process_generated import load_any  # noqa: E402  (magenta key with un-mixing; transparent PNGs pass through)

# Target frame size (px) and playback fps from the PLAN table (3.2 mage, 3.3 bishop, 3.4 fighter, 3.5 guardian).
TARGET = {
    "m2_fireball": (128, 128, 16), "m2_fire_explosion": (192, 192, 18), "m2_ice_spear": (256, 64, 1),
    "m2_ice_shatter": (128, 128, 18), "m2_lightning_bolt": (256, 64, 20), "m2_lightning_hit": (128, 128, 20),
    "m2_charge_orb": (128, 128, 14), "m2_nebula_burst": (256, 256, 18), "m2_blink": (128, 128, 18),
    "m2_rift": (256, 160, 10), "m2_meteor_fire": (128, 192, 1), "m2_meteor_ice": (128, 192, 1),
    "m2_meteor_storm": (128, 192, 1), "m2_cataclysm_collapse": (384, 384, 16),
}
DEFAULT = (128, 128, 14)


def alpha_black(path):
    im = np.asarray(Image.open(path).convert("RGB")).astype(np.float32)
    a = im.max(axis=2)
    # Flow's near-black is never exactly 0: lift the floor so the background goes fully transparent.
    a = np.clip((a - 10.0) / (255.0 - 10.0), 0.0, 1.0)
    rgb = np.where(a[..., None] > 0.01, im / np.maximum(a[..., None] * 255.0, 1.0) * 255.0, 0.0)
    rgb = np.clip(rgb, 0, 255)
    out = np.dstack([rgb, a * 255.0]).astype(np.uint8)
    out[a <= 0.03] = 0
    return out


def bands(profile, min_gap, min_mass):
    """Runs of non-empty lines separated by at least min_gap empty lines (Flow's gutters are uneven)."""
    on = profile > 0
    runs, start, gap = [], None, 0
    for i, v in enumerate(on):
        if v:
            if start is None:
                start = i
            gap = 0
            end = i
        elif start is not None:
            gap += 1
            if gap >= min_gap:
                runs.append((start, end + 1))
                start, gap = None, 0
    if start is not None:
        runs.append((start, end + 1))
    return [r for r in runs if profile[r[0]:r[1]].sum() >= min_mass]


def cells(rgba, frames, threshold):
    """Frames in reading order: rows found from empty horizontal bands, then frames from empty vertical bands in
    each row, each cut to what is drawn. Robust to grids that differ from the prompt and to uneven spacing."""
    solid = rgba[..., 3] > threshold
    rows = bands(solid.sum(axis=1), 6, 40)
    out = []
    for y0, y1 in rows:
        band = solid[y0:y1]
        for x0, x1 in bands(band.sum(axis=0), 6, 40):
            sub = band[:, x0:x1]
            ys, xs = np.where(sub)
            out.append(rgba[y0 + ys.min():y0 + ys.max() + 1, x0 + xs.min():x0 + xs.max() + 1])
    return out[:frames] if frames else out


def grid_cells(rgba, entry, threshold):
    """"mode": "grid": even cols x rows over the crop box (for sheets whose drawings touch across the gutters)."""
    h, w = rgba.shape[:2]
    x0, y0, x1, y1 = entry.get("crop", [0, 0, w, h])
    cols, rows = int(entry["cols"]), int(entry["rows"])
    frames = int(entry.get("frames", cols * rows))
    out = []
    for r in range(rows):
        for c in range(cols):
            if len(out) >= frames:
                return out
            cx0, cx1 = x0 + round(c * (x1 - x0) / cols), x0 + round((c + 1) * (x1 - x0) / cols)
            cy0, cy1 = y0 + round(r * (y1 - y0) / rows), y0 + round((r + 1) * (y1 - y0) / rows)
            cell = rgba[cy0:cy1, cx0:cx1]
            solid = cell[..., 3] > threshold
            if not solid.any():
                continue
            ys, xs = np.where(solid)
            out.append(cell[ys.min():ys.max() + 1, xs.min():xs.max() + 1])
    return out


def clean(rgba, entry):
    """Drop Flow's watermark sparkle (bottom-right corner) and anything outside an optional crop box."""
    h, w = rgba.shape[:2]
    rgba = rgba.copy()
    rgba[int(h * 0.9):, int(w * 0.9):, 3] = 0
    if "crop" in entry:
        x0, y0, x1, y1 = entry["crop"]
        mask = np.zeros((h, w), bool)
        mask[y0:y1, x0:x1] = True
        rgba[~mask, 3] = 0
    rgba[rgba[..., 3] == 0] = 0
    return rgba


def process(entry):
    cid = entry["id"]
    src = os.path.join(RAW, entry["file"])
    if not os.path.exists(src):
        return f"{cid}: raw missing ({entry['file']})"
    black = entry.get("background", "magenta") == "black"
    rgba = alpha_black(src) if black else load_any(src)
    rgba = clean(rgba, entry)
    frames = int(entry.get("frames", 0))
    threshold = 30 if black else 100
    parts = grid_cells(rgba, entry, threshold) if entry.get("mode") == "grid" else cells(rgba, frames, threshold)
    used = [p for p in parts if p is not None]
    if not used:
        return f"{cid}: nothing drawn"
    fw, fh, fps = TARGET.get(cid, DEFAULT)
    big_w = max(p.shape[1] for p in used)
    big_h = max(p.shape[0] for p in used)
    k = min(fw * 0.94 / big_w, fh * 0.94 / big_h, 1.0)  # never upscale
    strip = Image.new("RGBA", (fw * len(parts), fh), (0, 0, 0, 0))
    for i, p in enumerate(parts):
        if p is None:
            continue
        im = Image.fromarray(p, "RGBA")
        if k < 1.0:
            im = im.resize((max(1, round(im.width * k)), max(1, round(im.height * k))), Image.BOX)
        strip.alpha_composite(im, (i * fw + (fw - im.width) // 2, (fh - im.height) // 2))
    os.makedirs(OUT, exist_ok=True)
    strip.save(os.path.join(OUT, cid + ".png"))
    meta = {"frames": len(parts), "frameW": fw, "frameH": fh, "fps": float(entry.get("fps", fps)),
            "additive": black, "pivotX": 0.5, "pivotY": 0.5}
    with open(os.path.join(OUT, cid + ".json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(meta, f)
    return f"{cid}: {len(parts)} frames {fw}x{fh} {'additive' if black else 'keyed'} (scale {k:.2f})"


def main():
    only = sys.argv[sys.argv.index("--only") + 1] if "--only" in sys.argv else None
    manifest = os.path.join(RAW, "manifest.json")
    if not os.path.exists(manifest):
        print("no manifest:", manifest)
        return
    with open(manifest, encoding="utf-8") as f:
        entries = json.load(f)
    for e in entries:
        if only and e.get("id") != only:
            continue
        print(process(e))


if __name__ == "__main__":
    main()
