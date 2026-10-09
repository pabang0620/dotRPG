"""Register imagegen cutouts without resampling or repainting any pixels.

The source atlas rows are hand-reviewed bounds rather than assuming imagegen
obeyed an exact grid. Crop rectangles are read-only views of the original PNG.
Pillow is used only to inspect alpha and write metadata; PNG bytes are copied.
"""
import hashlib
import argparse
import json
import shutil
import uuid
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parents[2]
SOURCE = Path('C:/Users/darac/.codex/generated_images/01a0f28a-8705-72e3-96c2-e54c11e19eca')
DEST = ROOT / 'Assets/StreamingAssets/WorldProps'
SETS = {
    'forest': ('exec-1511b4d0-e742-4069-a8f9-5013bd154fbf.png',
        ['broadleaf','oak','pine','willow','cherry','fruit','golden','dead','rock','bush','log','stump'],
        [0,430,805,1086], [[0,377,740,1090,1448],[0,379,734,1103,1448],[0,376,734,1100,1448]]),
    'canyon': ('exec-a92a5255-5316-4b8e-9b32-b6c9aabad39c.png',
        ['broadleaf','oak','pine','dead','rockA','rockB','rockC','shrub','ruin','log','stump','pebbles'],
        [0,464,763,1086], [[0,372,746,1100,1448],[0,371,731,1110,1448],[0,372,734,1091,1448]]),
    'winter': ('exec-e9fe58f9-5c85-490e-a915-cc285e757e89.png',
        ['fir','pine','broadleaf','dead','rockA','rockB','shrub','stump','log','ruin','pebbles','grass'],
        [0,507,790,1086], [[0,370,732,1110,1448],[0,370,732,1110,1448],[0,388,734,1110,1448]]),
    'sanctum': ('exec-f85e16cf-fada-4362-9897-d202d1d08b81.png',
        ['pillar','rubble','longremnant','rockA','rockB','moss','bush','dead','stump','log','pebbles','urn'],
        [0,381,755,1086], [[0,373,741,1106,1448],[0,374,741,1120,1448],[0,374,741,1110,1448]])
}


def meta(path, folder=False):
    target = Path(str(path) + '.meta')
    if not target.exists():
        target.write_text('fileFormatVersion: 2\nguid: ' + uuid.uuid4().hex +
                          ('\nfolderAsset: yes' if folder else '') +
                          '\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n', encoding='utf-8')


def preserve_extra_atlases(result, previous):
    """Only this script's four base sets are regenerated; extensions keep ownership."""
    result['atlases'].extend(entry for entry in previous.get('atlases', [])
                            if entry.get('id') not in SETS)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source-dir', type=Path, default=SOURCE)
    args = parser.parse_args()
    DEST.mkdir(parents=True, exist_ok=True)
    meta(DEST, True)
    metadata = DEST / 'metadata.json'
    previous = json.loads(metadata.read_text(encoding='utf-8')) if metadata.exists() else {}
    result = dict(previous)
    result.update({'version': 1, 'rectOrigin': 'top-left', 'pivotOrigin': 'bottom-left', 'atlases': []})
    for biome, (filename, names, ys, xs) in SETS.items():
        source = args.source_dir / filename
        destination = DEST / (biome + '.png')
        # Checked-in atlas bytes can regenerate metadata even after the local
        # image-generation cache is cleared; originals are never repainted.
        if source.is_file():
            shutil.copyfile(source, destination)
        elif destination.is_file():
            source = destination
        else:
            raise FileNotFoundError(f'Supply --source-dir with {filename}')
        meta(destination)
        with Image.open(source) as source_image:
            alpha = source_image.convert('RGBA').getchannel('A')
            atlas = {'id': biome, 'file': destination.name, 'columns': 4, 'rows': 3,
                     'width': source_image.width, 'height': source_image.height,
                     'sha256': hashlib.sha256(source.read_bytes()).hexdigest(), 'cells': []}
            for index, name in enumerate(names):
                row, col = divmod(index, 4)
                window = (xs[row][col], ys[row], xs[row][col+1], ys[row+1])
                mask = alpha.crop(window).point(lambda value: 255 if value >= 16 else 0)
                bounds = mask.getbbox()
                if bounds is None:
                    raise ValueError(f'Empty sprite: {biome}/{name}')
                left, top, right, bottom = bounds
                # The feet are just above the small ground-material skirt. The
                # runtime keeps the original anchor/visual world-size contract.
                pivot_y = .055 if name not in ('pebbles','moss','grass') else .10
                atlas['cells'].append({'id': name,
                    'rect': [window[0]+left, window[1]+top, right-left, bottom-top],
                    'pivot': [.5, pivot_y]})
            result['atlases'].append(atlas)
        print(biome, len(atlas['cells']), destination.stat().st_size)
    preserve_extra_atlases(result, previous)
    metadata.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    meta(metadata)
    print(metadata)


if __name__ == '__main__':
    main()
