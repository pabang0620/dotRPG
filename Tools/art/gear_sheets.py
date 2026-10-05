# -*- coding: utf-8 -*-
"""Gear icon sheets for Flow (Docs/PLAN_GEAR_RENEWAL.md 7). One sheet = one kind, two level tiers, 5 grades per row
(2 rows x 5 columns, magenta background). Lv.1 rows have 3 grades; legendaries get their own sheets.
Prints the sheet list and prompts; process_gear_icons.py cuts them into Art/Gear/eqicon_<id>.png.
Usage: python3 gear_sheets.py [--json]
"""
import json, sys

TIERS = [1, 10, 15, 20, 25, 30, 35, 40]
THEME = {
    1: "simple village training gear: plain wood, rough iron, linen and leather",
    10: "skeleton forest: bleached bone, old iron, grave moss and faint ghostly green",
    15: "red stone canyon pass: rust-red rock, copper, sandstone and ember orange",
    20: "knight order steel: polished steel, royal blue cloth and gold trim",
    25: "frost pine forest: dark pine wood, white fur, frosted silver and pale green",
    30: "frozen lake: clear blue ice crystal, glacier blue and silver",
    35: "blizzard peak: storm silver, white snow, steel blue and lightning white",
    40: "constellation: midnight navy, starlight gold, glowing star crystals",
}
GRADES = ["c", "u", "r", "e", "un"]
GRADE_LOOK = [
    "COMMON: plain, worn and simple, dull colours, no decoration",
    "UNCOMMON: clean and sturdy, small trim",
    "RARE: finer craft, a small gem or engraved pattern",
    "EPIC: ornate, glowing accents, a bright gem, purple-tinted magic shine",
    "UNIQUE: very ornate masterwork, intricate gold filigree, radiant aura glow, the most beautiful of the row",
]
KINDS = {
    "sword": "a warrior's one-handed sword shown upright, blade up",
    "staff": "a mage's long staff shown upright, head up",
    "plate": "a warrior's chest armour (breastplate) seen from the front",
    "greaves": "a warrior's leg armour (a pair of greaves/armoured trousers) seen from the front",
    "robe": "a mage's long robe top (coat with sleeves) seen from the front",
    "skirt": "a mage's long skirt/robe bottom seen from the front",
    "neck": "a necklace with a pendant",
    "ring": "a ring seen at a slight angle",
}
LEGEND = {
    20: "LEGENDARY dragon-bone relic: ivory dragon bone and molten gold, burning orange glow",
    40: "LEGENDARY genesis star relic: white gold and violet starlight, a swirling galaxy glow",
}


def sheets():
    out = []
    for kind in KINDS:
        pairs = [(1, 10), (15, 20), (25, 30), (35, 40)]
        for a, b in pairs:
            out.append({"name": f"gear_{kind}_{a}_{b}", "kind": kind, "rows": [a, b]})
    for kind in KINDS:
        pass
    out.append({"name": "gear_legend_weapons_armour", "legend": ["sword", "staff", "plate", "greaves", "robe", "skirt"]})
    out.append({"name": "gear_legend_accessories", "legend": ["neck", "ring"]})
    return out


def ids_of(sheet):
    if "legend" in sheet:
        return [[f"eq_{k}_20_l" for k in sheet["legend"]], [f"eq_{k}_40_l" for k in sheet["legend"]]]
    rows = []
    for lv in sheet["rows"]:
        n = 3 if lv == 1 else 5
        rows.append([f"eq_{sheet['kind']}_{lv}_{GRADES[g]}" for g in range(n)])
    return rows


def prompt(sheet):
    common = ("Generate a single static IMAGE (not a video): a sprite sheet of fantasy RPG inventory ICONS in chunky "
              "hand-crafted 16-bit pixel art (every pixel a crisp square block, dark 1-pixel outline, limited palette, soft "
              "top-left highlight, readable at 48x48), matching the attached icon style exactly. ")
    if "legend" in sheet:
        kinds = ", ".join(KINDS[k] for k in sheet["legend"])
        body = (f"Two rows, {len(sheet['legend'])} icons per row, each icon centred in its own equal cell with wide empty space "
                f"between cells, none touching. Left to right in each row: {kinds}. Row 1 is {LEGEND[20]}. Row 2 is {LEGEND[40]}. "
                "Every icon looks precious and legendary. ")
    else:
        rows = []
        for i, lv in enumerate(sheet["rows"]):
            n = 3 if lv == 1 else 5
            looks = "; ".join(f"{j + 1}) {GRADE_LOOK[j]}" for j in range(n))
            rows.append(f"Row {i + 1}: {n} icons, theme {THEME[lv]}. Left to right by grade: {looks}.")
        body = (f"Every icon is {KINDS[sheet['kind']]}. Two rows of icons, each icon centred in its own equal cell (5 cells per "
                "row, a 3-icon row leaves the last two cells empty magenta), wide empty space between cells, none touching. "
                + " ".join(rows) + " Each icon in a row clearly looks stronger and fancier than the one on its left. ")
    tail = ("No text, no letters, no numbers, no frames, no grid lines, no cards, no shadows on the background. Do not use "
            "magenta or pink on the icons. The background is one flat uniform pure magenta #FF00FF filled edge to edge, fully "
            "opaque, no gradient, no checkerboard.")
    return common + body + tail


if __name__ == "__main__":
    all_sheets = sheets()
    if "--json" in sys.argv:
        print(json.dumps([{"name": s["name"], "ids": ids_of(s), "prompt": prompt(s)} for s in all_sheets], ensure_ascii=False, indent=1))
    else:
        for s in all_sheets:
            print(s["name"], ids_of(s))
        print(len(all_sheets), "sheets")
