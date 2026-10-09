"""Register the reviewed 16-cell forest-fields atlas without altering PNG bytes.

Rows and columns follow observed transparent gutters, not an assumed equal grid.
Pillow inspects alpha only; all crop rectangles are runtime views of the source.
The four existing regional atlas entries and their files are never regenerated.
"""
import argparse
import hashlib
import json
import shutil
from pathlib import Path

from PIL import Image
from register_world_props import DEST, meta


SOURCE = Path('C:/Users/darac/.codex/generated_images/01a0f28a-8705-72e3-96c2-e54c11e19eca/exec-898afaa0-4368-4193-a60b-e5f169ebbb67.png')
NAMES = ['broadleaf', 'oak', 'pine', 'willow',
         'broadleaf_open', 'oak_lean', 'pine_open', 'dead',
         'rock', 'bush', 'log', 'stump',
         'ruin', 'dead_bent', 'rock_slab', 'fern']
ROWS = [0, 334, 686, 927, 1254]
COLUMNS = [[0, 309, 662, 934, 1254], [0, 312, 667, 960, 1254],
           [0, 319, 616, 951, 1254], [0, 337, 605, 943, 1254]]
# Reviewed foot locations, especially the off-centre root of oak_lean. These
# pivots align the painted trunk, rather than its crown centre, to the old root.
PIVOT_X = [.542, .518, .486, .506, .526, .263, .473, .545,
           .5, .5, .5, .54, .445, .471, .5, .5]


def inspect(source):
    with Image.open(source) as image:
        if image.mode != 'RGBA' or image.size != (1254, 1254):
            raise ValueError('This reviewed registration requires the 1254x1254 RGBA original')
        alpha = image.getchannel('A')
        significant = alpha.point(lambda value: 255 if value >= 16 else 0)
        atlas = {'id': 'forest_fields', 'file': 'forest_fields.png', 'columns': 4, 'rows': 4,
                 'width': image.width, 'height': image.height,
                 'sha256': hashlib.sha256(source.read_bytes()).hexdigest(), 'cells': []}
        covered = 0
        audit = []
        for index, name in enumerate(NAMES):
            row, col = divmod(index, 4)
            window = (COLUMNS[row][col], ROWS[row], COLUMNS[row][col + 1], ROWS[row + 1])
            mask = significant.crop(window)
            bounds = mask.getbbox()
            if bounds is None:
                raise ValueError('Empty forest sprite: ' + name)
            left, top, right, bottom = bounds
            rect = [window[0] + left, window[1] + top, right - left, bottom - top]
            covered += mask.histogram()[255]
            atlas['cells'].append({'id': name, 'rect': rect, 'pivot': [PIVOT_X[index], .055]})
            source_edge = rect[0] == 0 or rect[1] == 0 or rect[0] + rect[2] == image.width or rect[1] + rect[3] == image.height
            if source_edge:
                raise ValueError('Significant silhouette touches the source boundary: ' + name)
            audit.append({'id': name, 'rect': rect, 'sourceEdgeTouch': source_edge})
        expected = significant.histogram()[255]
        if covered != expected:
            raise ValueError('Reviewed windows leave significant source pixels uncovered')
        # The first open-pine tip begins at the next row boundary, but the previous
        # solid pine ends seven pixels above it. The top solid tip has seven clear
        # rows at alpha>=16, so it is not chopped by either the source or our crop.
        return atlas, {'alphaThreshold': 16, 'significantPixels': expected,
                       'coveredPixels': covered, 'uncoveredPixels': expected - covered,
                       'pineTopMarginPixels': atlas['cells'][2]['rect'][1], 'cells': audit}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=SOURCE)
    parser.add_argument('--destination-dir', type=Path, default=DEST)
    parser.add_argument('--audit', type=Path)
    args = parser.parse_args()
    destination = args.destination_dir / 'forest_fields.png'
    source = args.source if args.source.is_file() else destination
    if not source.is_file():
        raise FileNotFoundError('Supply --source with the original forest-fields image')
    metadata = args.destination_dir / 'metadata.json'
    result = json.loads(metadata.read_text(encoding='utf-8'))
    existing = result.get('atlases', [])
    if len({entry['id'] for entry in existing}) != len(existing):
        raise ValueError('Duplicate existing atlas IDs')
    atlas, audit = inspect(source)
    preserved = [entry for entry in existing if entry['id'] != 'forest_fields']
    # Append only the owned extension; leave every base/other extension dict intact.
    result['atlases'] = preserved + [atlas]
    if source.resolve() != destination.resolve():
        shutil.copyfile(source, destination)
    if hashlib.sha256(destination.read_bytes()).hexdigest() != atlas['sha256']:
        raise ValueError('Destination PNG differs from the original bytes')
    meta(destination)
    metadata.write_text(json.dumps(result, indent=2) + '\n', encoding='utf-8')
    if args.audit:
        args.audit.parent.mkdir(parents=True, exist_ok=True)
        args.audit.write_text(json.dumps(audit, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'file': str(destination), 'sha256': atlas['sha256'], 'cells': len(atlas['cells']),
                      'preservedAtlasIds': [entry['id'] for entry in preserved],
                      'uncoveredPixels': audit['uncoveredPixels'], 'pineTopMarginPixels': audit['pineTopMarginPixels']}))


if __name__ == '__main__':
    main()
