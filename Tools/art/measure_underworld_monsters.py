"""Measure original atlas masks and optionally repair JSON pivots; never edits a PNG.

Dependency: python -m pip install Pillow
Usage: python Tools/art/measure_underworld_monsters.py [--apply] [--output report.json]
The runtime sampling below mirrors UnderworldMonsterArt.LoadPoses. This distinguishes
an authored limb change from a whole-body pivot jump without launching the player.
"""
from pathlib import Path
from PIL import Image
import argparse
import json
import statistics

ROOT = Path(__file__).resolve().parents[2] / "Assets" / "StreamingAssets" / "Underworld"
IDS = ("hollow_scarab", "hollow_guard", "hollow_hexer")
KEYS = (0, 0, 1, 0, 2, 0, 3, 0)
WIDTH = HEIGHT = 128
FEET_Y = 116


def band_median(mask, lo, hi):
    h, w = len(mask), len(mask[0])
    return float(statistics.median(x for y in range(round(h*lo), round(h*hi)) for x in range(w) if mask[y][x]))


def hood_center(mask):
    """Median centre of the longest opaque run per hood/shoulder row.

    The thin disconnected staff and an extended hand are not the actor's root.
    Restricting to the dominant run excludes them without changing their artwork.
    """
    h, w = len(mask), len(mask[0])
    centers = []
    for y in range(round(h*.20), round(h*.50)):
        runs = []
        start = None
        for x in range(w+1):
            on = x < w and mask[y][x]
            if on and start is None:
                start = x
            if not on and start is not None:
                runs.append((x-start, (x-1+start)/2))
                start = None
        if runs:
            length, center = max(runs)
            if length >= h*.10:
                centers.append(center)
    return float(statistics.median(centers))


def native_points(mask, cell, scale, frame):
    h, w = len(mask), len(mask[0])
    result = []
    for y in range(2, HEIGHT-2):
        rise = min(1, max(0, (FEET_Y-y)/55))
        bob = rise if frame in (1, 3, 5) else 0
        recoil = 2*rise if frame == 7 else 0
        py = round((y-FEET_Y+bob)/scale+cell["anchorY"])
        if not 0 <= py < h:
            continue
        for x in range(2, WIDTH-2):
            px = round((x-WIDTH*.5+recoil)/scale+cell["anchorX"])
            if 0 <= px < w and mask[py][px]:
                result.append((x, y))
    return result


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--apply", action="store_true")
    parser.add_argument("--output", type=Path, default=Path("underworld-monster-alignment.json"))
    args = parser.parse_args()
    report = {}
    for species in IDS:
        path = ROOT/(species+".json")
        data = json.loads(path.read_text(encoding="utf-8-sig"))
        image = Image.open(ROOT/(species+".png")).convert("RGBA")
        masks = []
        changes = []
        for index, cell in enumerate(data["cells"]):
            x, y, w, h = (cell[k] for k in ("x", "y", "width", "height"))
            mask = [[image.getpixel((x+xx, y+yy))[3] >= 160 for xx in range(w)] for yy in range(h)]
            masks.append(mask)
            old = cell["anchorX"]
            if species == "hollow_scarab":
                anchor = band_median(mask, .10, .65)
            elif species == "hollow_guard":
                anchor = band_median(mask, .05, .30)
                if index % 4 == 3:
                    # Grounded midpoint of both boots measured in each local attack cell.
                    # The tower-shield tip is deliberately excluded from the right foot.
                    boots = ((67,188), (81,207), (62,190), (42,172), (37,178))
                    anchor = sum(boots[index//4])/2
            else:
                anchor = hood_center(mask)
            if args.apply:
                cell["anchorX"] = anchor
            changes.append([old, anchor])
        rows = []
        for direction in range(5):
            poses = []
            for frame in range(8):
                i = direction*4+KEYS[frame]
                pts = native_points(masks[i], data["cells"][i], data["scale"], frame)
                body = [x for x,y in pts if 60 <= y <= 100]
                poses.append({"frame":frame,"centroidX":round(statistics.mean(x for x,y in pts),3),
                    "bodyCentroidX":round(statistics.mean(body),3),"bottom":max(y for x,y in pts),
                    "bounds":[min(x for x,y in pts),min(y for x,y in pts),max(x for x,y in pts),max(y for x,y in pts)]})
            moving = [p for p in poses if p["frame"] != 6]
            spread = max(p["centroidX"] for p in moving)-min(p["centroidX"] for p in moving)
            bodyspread = max(p["bodyCentroidX"] for p in moving)-min(p["bodyCentroidX"] for p in moving)
            rows.append({"direction":direction,"idleWalkHurtSpread":round(spread,3),"bodySpread":round(bodyspread,3),"frames":poses})
        print(species, "unchanged scale", data["scale"], "idle/walk/hurt centroid spreads", [row["idleWalkHurtSpread"] for row in rows],
              "body spreads", [row["bodySpread"] for row in rows])
        print("  anchors", [c["anchorX"] for c in data["cells"]])
        report[species] = {"unchangedSharedScale":data["scale"],"anchorChanges":changes,"rows":rows}
        if args.apply:
            path.write_text(json.dumps(data, indent=2)+"\n", encoding="utf-8")
    output=args.output
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(report, indent=2)+"\n", encoding="utf-8")
    print("Report:", output)


if __name__ == "__main__":
    main()
