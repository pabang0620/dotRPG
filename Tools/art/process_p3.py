# -*- coding: utf-8 -*-
"""P3 art: item icons, title logo, dungeon banners -> Assets/Resources/Art (keys used by the game)."""
import os, sys
import numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from process_generated import load_any, components, ART

ICONS1 = ["eqicon_sword_0", "eqicon_sword_1", "eqicon_sword_2", "eqicon_sword_3", "eqicon_staff_0", "eqicon_staff_1", "eqicon_staff_2", "eqicon_staff_3",
          "eqicon_neck_0", "eqicon_neck_1", "eqicon_neck_2", "eqicon_ring_0", "eqicon_ring_1", "eqicon_ring_2", "eqicon_top_0", "eqicon_top_1"]
ICONS2 = ["eqicon_top_2", "eqicon_bot_0", "eqicon_bot_1", "icon_gold", "icon_potion_hp", "icon_potion_mp", "icon_scroll", "icon_ticket",
          "maticon_bone", "maticon_ore", "maticon_essence", "icon_key"]
BANNERS = ["banner_gold_vein", "banner_smelter", "banner_mana_graveyard", "banner_training_forest", "banner_armory",
           "banner_raid_skeleton_king", "banner_raid_bargas", "banner_raid_golem", "banner_raid_grah"]

def icon(piece, size=64, inner=56):
    p = piece[4:-4, 4:-4]
    h, w = p.shape[:2]
    k = inner / max(h, w)
    img = Image.fromarray(p, "RGBA").resize((max(1, round(w * k)), max(1, round(h * k))), Image.BOX)
    a = np.asarray(img).copy()
    a[..., 3] = np.where(a[..., 3] >= 110, 255, 0)
    a[a[..., 3] == 0] = 0
    out = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    out.alpha_composite(Image.fromarray(a, "RGBA"), ((size - a.shape[1]) // 2, (size - a.shape[0]) // 2))
    return out

def run(src):
    for sheet, names in (("codex_icons1.png", ICONS1), ("codex_icons2.png", ICONS2)):
        pieces = components(load_any(os.path.join(src, sheet)), merge=12)
        print(sheet, len(pieces), "pieces")
        for p, n in zip(pieces, names):
            icon(p).save(os.path.join(ART, n + ".png"))
    logo = components(load_any(os.path.join(src, "codex_logo.png")), merge=40)[0][4:-4, 4:-4]
    h, w = logo.shape[:2]
    k = 640 / w
    img = Image.fromarray(logo, "RGBA").resize((640, round(h * k)), Image.BOX)
    a = np.asarray(img).copy(); a[..., 3] = np.where(a[..., 3] >= 110, 255, 0); a[a[..., 3] == 0] = 0
    Image.fromarray(a, "RGBA").save(os.path.join(ART, "ui_logo.png"))
    print("logo", img.size)
    # Banners: a 3 x 3 grid of tiles; equal cells, inset past the dark gutters.
    sheet = Image.open(os.path.join(src, "codex_banners.png")).convert("RGB")
    W, H = sheet.size
    i = 0
    for r in range(3):
        for c in range(3):
            x0, x1 = c * W // 3, (c + 1) * W // 3
            y0, y1 = r * H // 3, (r + 1) * H // 3
            sheet.crop((x0 + 14, y0 + 14, x1 - 14, y1 - 14)).resize((480, 160), Image.BOX).save(os.path.join(ART, BANNERS[i] + ".png")); i += 1
    print("banners", i)

if __name__ == "__main__":
    run(sys.argv[1])
