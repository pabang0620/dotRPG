"""Build geometry-preserving 2x cave artwork with a local Real-ESRGAN tool.

Requires Pillow and the official portable Real-ESRGAN ncnn Vulkan release.
The executable/model are development tools, not dependencies of the game.
Original compositions are never overwritten; runtime selects the _hd siblings.
"""
import argparse
import hashlib
import json
import subprocess
import uuid
from pathlib import Path

from PIL import Image


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--tool", required=True, type=Path)
    parser.add_argument("--work", required=True, type=Path)
    parser.add_argument("--reuse-inference", action="store_true")
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    tool = args.tool.resolve()
    work = args.work.resolve()
    work.mkdir(parents=True, exist_ok=True)
    art = repo / "Assets/StreamingAssets/Underworld"
    report = {
        "method": "Real-ESRGAN realesrgan-x4plus, then Lanczos downsample to exactly 2x source dimensions",
        "tool_release": "https://github.com/xinntao/Real-ESRGAN/releases/tag/v0.2.5.0",
        "tool_sha256": digest(tool),
        "model_sha256": digest(tool.parent / "models/realesrgan-x4plus.bin"),
        "model_param_sha256": digest(tool.parent / "models/realesrgan-x4plus.param"),
        "invariants": "No crop, warp, color grading, landmark relocation, or collision changes. Model may reconstruct fine texture.",
        "runtime": {"world_tiles": [56, 48], "pixels_per_unit": 48, "raster": [2688, 2304]},
        "assets": [],
    }
    for kind in ("descent", "roots", "fungal", "depths"):
        source = art / f"composition-{kind}.png"
        raw = work / f"{kind}-general4.png"
        target = art / f"composition-{kind}_hd.png"
        if not args.reuse_inference:
            command = [str(tool), "-i", str(source), "-o", str(raw), "-n",
                       "realesrgan-x4plus", "-s", "4", "-t", "256", "-j", "1:1:1"]
            with (work / f"{kind}-general4.log").open("w", encoding="utf-8") as log:
                subprocess.run(command, cwd=tool.parent, stdout=log, stderr=log, check=True)
        with Image.open(source) as original, Image.open(raw) as inferred:
            assert inferred.size == (original.width * 4, original.height * 4), kind
            assert original.convert("RGBA").getchannel("A").getextrema() == (255, 255), kind
            size = (original.width * 2, original.height * 2)
            final = inferred.convert("RGB").resize(size, Image.Resampling.LANCZOS)
            final.save(target, optimize=True)
            report["assets"].append({"kind": kind, "source": source.relative_to(repo).as_posix(),
                "source_size": list(original.size), "source_sha256": digest(source),
                "output": target.relative_to(repo).as_posix(), "output_size": list(size),
                "output_sha256": digest(target), "output_bytes": target.stat().st_size})
        meta = Path(str(target) + ".meta")
        if not meta.exists():
            meta.write_text("fileFormatVersion: 2\nguid: " + uuid.uuid4().hex
                            + "\nDefaultImporter:\n  externalObjects: {}\n  userData:\n"
                            + "  assetBundleName:\n  assetBundleVariant:\n", encoding="utf-8")
        print(f"{kind}: {size[0]}x{size[1]}")
    manifest = repo / "Docs/World/underworld-upscale-manifest.json"
    manifest.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
