"""Read-only release inventory. Never reads .env or prints credentials. Exit 1 = blockers remain."""
import argparse
import hashlib
import json
import re
import struct
from pathlib import Path

parser = argparse.ArgumentParser()
parser.add_argument("--output", type=Path)
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
dot = (root / "Assets/Scripts/Runtime/Net/ApiClient.cs").exists()
build = root / ("Builds/WindowsRelease" if dot else "builds/Windows")
exe = build / ("dotRPG.exe" if dot else "Starspire.exe")
checks = []
def check(name, ok, detail):
    checks.append(dict(name=name, status="PASS" if ok else "BLOCKED", detail=detail))

check("release executable", exe.is_file(), str(exe.relative_to(root)))
check("no test App ID file", not list(build.rglob("steam_appid.txt")), "steam_appid.txt must not ship")
check("Steam native library", any(build.rglob("steam_api64.dll")), "steam_api64.dll required for advertised Steam features")
if dot:
    source = (root / "Assets/Scripts/Runtime/Net/ApiClient.cs").read_text(encoding="utf-8-sig")
    server = re.search(r'const string ReleaseServer = "([^"]*)"', source)
    check("release server", bool(server and server[1].startswith("https://")), "approved HTTPS endpoint must be configured")
    manifest = build / "release-manifest.json"
    valid = False
    if manifest.is_file() and exe.is_file():
        data = json.loads(manifest.read_text(encoding="utf-8-sig"))
        valid = data.get("release") is True and data.get("executableSha256") == hashlib.sha256(exe.read_bytes()).hexdigest()
    check("release provenance", valid, "successful BuildWindowsRelease manifest and executable hash")
else:
    source = (root / "client/Assets/Starspire/Scripts/Platform/SteamConfig.cs").read_text(encoding="utf-8-sig")
    app = re.search(r'const uint AppId = (\d+)', source)
    check("Steam App ID", bool(app and int(app[1]) not in (0, 480)), "real App ID required; 0/480 are not release IDs")

art = root / ("Docs/steam/art" if dot else "docs/steam/art")
specs = {"header.png": (920, 430), "small.png": (462, 174), "main.png": (1232, 706),
         "vertical.png": (748, 896), "library.png": (600, 900), "hero.png": (3840, 1240)}
for name, wanted in specs.items():
    path = art / name
    size = None
    if path.is_file():
        data = path.read_bytes()[:24]
        if data[:8] == b"\x89PNG\r\n\x1a\n": size = struct.unpack(">II", data[16:24])
    check("art " + name, size == wanted, f"required {wanted[0]}x{wanted[1]}; actual {size or 'missing'}")

report = dict(project=root.name, scope="Payments excluded; external Steam review and production validation are not certified by this check", checks=checks)
text = json.dumps(report, ensure_ascii=False, indent=2)
if args.output:
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(text + "\n", encoding="utf-8")
print(text)
raise SystemExit(0 if all(c["status"] == "PASS" for c in checks) else 1)
