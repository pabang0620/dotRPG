"""Regenerate the offline credits resource from the bundled license sources."""
from pathlib import Path
import textwrap

root = Path(__file__).resolve().parents[2]
parts = ["dotRPG — Credits / Licenses", "dotRPG Team", ""]
parts += ["Galmuri11 — Lee Minseo — SIL OFL 1.1", "Steamworks.NET — Riley Labrecque — MIT",
          "Unity — Unity Technologies", "Steamworks SDK — Valve", "",
          "Images: Google Flow / Nano Banana 2, edited by the development team.",
          "Music: Google Flow Music, edited by the development team.",
          "Sound effects: synthesized by the development team.", ""]
licenses = [root / "Assets/Resources/Fonts/Galmuri-LICENSE.txt"]
packages = list((root / "Library/PackageCache").glob("com.rlabrecque.steamworks.net*"))
if not packages:
    raise SystemExit("Steamworks.NET package missing: open Unity to resolve packages first")
license_files = list(packages[0].glob("LICENSE*"))
if not license_files:
    raise SystemExit("Steamworks.NET license missing")
licenses += [p for p in license_files if p.suffix != ".meta"]
for path in licenses:
    parts += ["", path.name, ""]
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        parts.extend(textwrap.wrap(line, width=90) or [""])
(root / "Assets/Resources/ThirdPartyNotices.txt").write_text("\n".join(parts), encoding="utf-8")
print("Updated Assets/Resources/ThirdPartyNotices.txt")
