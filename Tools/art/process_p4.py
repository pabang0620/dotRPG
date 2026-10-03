# -*- coding: utf-8 -*-
"""P4 art: level badge, menu icons, resource icons, skill and support-gem icons -> Assets/Resources/Art.

Sheets (Codex, magenta background, 4 x 4): codex_ui2a.png (badge + menu + resources), codex_ui2b.png (skills + gems).
Usage: python3 Tools/art/process_p4.py <folder with the sheets>
"""
import os, sys
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import numpy as np
from scipy import ndimage
from process_generated import load_any, trim, ART
from process_p3 import icon

UI2A = ["ui_level_badge", "menuicon_menu", "menuicon_bag", "menuicon_skill",
        "menuicon_map", "menuicon_quest", "menuicon_dungeon", "menuicon_raid",
        "menuicon_party", "menuicon_finder", "menuicon_friend", "menuicon_auction",
        "menuicon_capacity", "icon_wood", "icon_stone", "icon_carrot"]
# [SKILL v2] new skills and the boss-hunter support gem (3 x 2; the 6th cell, a padlock, is a spare).
UI3 = ["gem_crush", "gem_lance", "gem_guard", "gem_barrier", "gem_sup_boss", "ui_lock"]
UI2B = ["gem_whirl", "gem_slam", "gem_wave", "gem_cry",
        "gem_blades", "gem_arc", "gem_nova", "gem_frostorb",
        "gem_thunder", "gem_meteor", "gem_sup_dmg", "gem_sup_aoe",
        "gem_sup_multi", "gem_sup_eff", "gem_sup_leech", "gem_sup_chain"]


def run(src):
    # merge radius: sheet A has loose parts (the friends' heart); sheet B plates are solid but their glows nearly touch.
    for sheet, names, merge, cols, rows in (("codex_ui2a.png", UI2A, 12, 4, 4), ("codex_ui2b.png", UI2B, 2, 4, 4), ("codex_ui3.png", UI3, 2, 3, 2)):
        path = os.path.join(src, sheet)
        if not os.path.exists(path):
            print(sheet, "missing"); continue
        rgba = load_any(path)
        H, W = rgba.shape[:2]
        # Whole-sheet pieces, each assigned to the 4 x 4 cell its centre falls in (the largest piece wins):
        # the art does not sit exactly inside equal cells, so cutting by cells clips plates.
        solid = rgba[..., 3] > 100
        lab, _ = ndimage.label(ndimage.binary_dilation(solid, iterations=merge))
        best = {}
        for i, sl in enumerate(ndimage.find_objects(lab), 1):
            area = int((solid[sl] & (lab[sl] == i)).sum())
            if area < 900: continue
            cy, cx = (sl[0].start + sl[0].stop) / 2, (sl[1].start + sl[1].stop) / 2
            cell = int(cy * rows // H) * cols + int(cx * cols // W)
            if cell not in best or area > best[cell][0]:
                piece = rgba[sl].copy()
                piece[lab[sl] != i] = 0
                best[cell] = (area, trim(piece))
        for i, n in enumerate(names):
            if i not in best:
                print("  ", n, "EMPTY CELL"); continue
            p = best[i][1]
            size = 96 if n == "ui_level_badge" else 64
            icon(p, size=size, inner=size - 6).save(os.path.join(ART, n + ".png"))
            print("  ", n, p.shape[1], "x", p.shape[0])


if __name__ == "__main__":
    run(sys.argv[1])
