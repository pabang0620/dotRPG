# -*- coding: utf-8 -*-
"""Generated UI kit (magenta background) -> Assets/Resources/Art/ui_*.png 9-slice sprites.

Elements in reading order; each is downscaled with nearest sampling so the hand-placed pixels stay
crisp. Border sizes for the importer are in Editor/GeneratedArtImport.cs (UiKit table).
Usage: python3 process_ui.py <source dir>
"""
import os, sys
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from process_generated import load_any, components, ART

# (key, target size) in reading order
UI = [("ui_panel", (40, 40)), ("ui_dark", (40, 40)), ("ui_btn", (40, 40)), ("ui_btngray", (40, 40)),
      ("ui_slot", (40, 40)), ("ui_slotblue", (40, 40)), ("ui_frame", (40, 40)), ("ui_tooltip", (40, 40)),
      ("ui_select", (40, 40)), ("ui_bar", (96, 24)), ("ui_badge", (40, 40)), ("ui_header", (96, 24))]

if __name__ == "__main__":
    src = sys.argv[1]
    rgba = load_any(os.path.join(src, "codex_uikit.png"))
    pieces = components(rgba, min_area=900, merge=3)
    print(len(pieces), "pieces")
    for (key, size), piece in zip(UI, pieces):
        p = piece[4:-4, 4:-4]  # drop the trim padding
        img = Image.fromarray(p, "RGBA").resize(size, Image.NEAREST)
        a = np.asarray(img).copy()
        a[..., 3] = np.where(a[..., 3] >= 128, 255, 0)
        a[a[..., 3] == 0] = 0
        Image.fromarray(a, "RGBA").save(os.path.join(ART, key + ".png"))
        print("wrote", key, size)
