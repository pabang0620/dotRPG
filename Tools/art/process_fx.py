# -*- coding: utf-8 -*-
"""Generated skill effect art (Flow, Docs/PLAN_SKILL_VFX.md) -> Assets/Resources/Art/VfxImg/<id>.png + <id>.json.

Input: Tools/art/vfx_raw/manifest.json = [{id, file, background: "black"|"magenta", cols, rows, frames, note}]
       (file is relative to vfx_raw). Raw files are only read, never written.
Steps per entry:
  1. Alpha. magenta: the soft key with un-mixing from process_generated.key (no pink fringe).
     black: glow art -> alpha = brightest channel, colour = rgb / alpha (drawn additive in game, json additive=true).
  2. Grid. The sheet is split into cols x rows cells; Flow's gutters are uneven, so each cell is re-cut to the
     bounding box of what is actually drawn in it. Only the first `frames` cells (reading order) are used.
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


def cells(rgba, cols, rows, frames, threshold):
    h, w = rgba.shape[:2]
    out = []
    for r in range(rows):
        for c in range(cols):
            if len(out) >= frames:
                return out
            x0, x1 = round(c * w / cols), round((c + 1) * w / cols)
            y0, y1 = round(r * h / rows), round((r + 1) * h / rows)
            cell = rgba[y0:y1, x0:x1]
            solid = cell[..., 3] > threshold
            if not solid.any():
                out.append(None)  # an empty cell keeps its place (a blank frame)
                continue
            ys, xs = np.where(solid)
            out.append(cell[ys.min():ys.max() + 1, xs.min():xs.max() + 1])
    return out


def process(entry):
    cid = entry["id"]
    src = os.path.join(RAW, entry["file"])
    if not os.path.exists(src):
        return f"{cid}: raw missing ({entry['file']})"
    black = entry.get("background", "magenta") == "black"
    rgba = alpha_black(src) if black else load_any(src)
    cols, rows = int(entry.get("cols", 1)), int(entry.get("rows", 1))
    frames = int(entry.get("frames", cols * rows))
    parts = cells(rgba, cols, rows, frames, 30 if black else 100)
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
