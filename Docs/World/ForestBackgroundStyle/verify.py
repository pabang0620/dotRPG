"""Verify a background-only restyle of the four existing forest fields."""
import argparse
import hashlib
import json
import struct
import subprocess
from pathlib import Path

REPO = next((p for p in Path(__file__).resolve().parents if (p / 'Assets/Scripts').is_dir()),
            Path('C:/Users/darac/OneDrive/Desktop/dot-rpg'))
WORK = Path(__file__).parent
TARGETS = {'forest', 'forest_ruins', 'forest_depths', 'forest_crossing'}
ALLOWED = {f'Assets/StreamingAssets/SurfaceWorld/{name}.png' for name in TARGETS}
CODE_EXCEPTION_FILES = {
    'Assets/Scripts/Runtime/Art/HuntingScenery.cs',
    'Assets/Scripts/Runtime/Core/DevCapture.SanctumFields.cs',
}


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def dimensions(path):
    with path.open('rb') as stream:
        header = stream.read(24)
    if len(header) != 24 or header[:8] != b'\x89PNG\r\n\x1a\n':
        raise ValueError(f'Invalid PNG: {path}')
    return list(struct.unpack('>II', header[16:24]))


def props(row):
    scene = row.get('surfaceComposition') or {}
    keys = ('biome', 'atlasId', 'atlasSourceSha256', 'eligible', 'applied', 'skipped', 'resources',
            'staticProps', 'species', 'atlasWidth', 'atlasHeight', 'floorRubbleCount', 'slots')
    return {'details': [{k: value.get(k) for k in keys}
                        for value in (row.get('propIntegration') or {}).get('details', [])],
            'grounding': scene.get('propGrounding'), 'canopy': scene.get('forestCanopy'),
            'hiddenNature': scene.get('hiddenUnsupportedNature'),
            'hiddenUnsupportedLandmarks': scene.get('hiddenUnsupportedLandmarks')}


def geometry(row):
    scene = row['surfaceComposition']
    ppu = scene['pixelsPerUnit']
    return {'bounds': scene['worldBounds'],
            'layerCells': [n / (ppu * ppu) for n in scene['layerRasterPixelCounts']],
            'vertices': scene['layerVertexCounts'], 'triangles': scene['layerTriangleCounts'],
            'hiddenOriginalGroundRenderers': scene['hiddenOriginalGroundRenderers']}


def snapshot(args):
    if args.baseline.exists():
        raise SystemExit('Refusing to replace the pre-change baseline.')
    prior = read(args.prior_baseline)
    paths = {v['path'] for v in prior['files']}
    paths.update(p.relative_to(args.repo).as_posix() for p in (args.repo / 'Assets/Scripts/Runtime').rglob('*.cs'))
    paths.update(p.relative_to(args.repo).as_posix() for p in (args.repo / 'Assets/StreamingAssets/WorldProps').glob('*') if p.is_file())
    paths.update(ALLOWED)
    rows = read(args.previous_index)
    if len(rows) != 21 or not TARGETS.issubset({r['id'] for r in rows}):
        raise SystemExit('A complete approved 21-map baseline is required.')
    result = {'version': 1, 'allowedChanges': sorted(ALLOWED),
              'gitHead': subprocess.check_output(['git', '-C', str(args.repo), 'rev-parse', 'HEAD'], text=True).strip(),
              'preexistingChanges': subprocess.check_output(['git', '-C', str(args.repo), 'status', '--short'], text=True).splitlines(),
              'previousIndex': str(args.previous_index), 'previousIndexSha256': sha(args.previous_index),
              'files': [{'path': name, 'allowChange': name in ALLOWED, 'sha256': sha(args.repo / name),
                         'bytes': (args.repo / name).stat().st_size,
                         'pngDimensions': dimensions(args.repo / name) if name.endswith('.png') else None}
                        for name in sorted(paths)],
              'maps': {r['id']: {'width': r['width'], 'height': r['height'],
                                'baselineHash': r['baselineHash'], 'collisionHash': r['collisionHash'],
                                'terrainHash': r['terrainHash'], 'props': props(r), 'geometry': geometry(r),
                                'sourceSha256': r['surfaceComposition']['sourceSha256'],
                                'pixelsPerUnit': r['surfaceComposition']['pixelsPerUnit']}
                       for r in rows}}
    args.baseline.parent.mkdir(parents=True, exist_ok=True)
    args.baseline.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'recordedFiles': len(result['files']), 'allowedBackgroundChanges': 4,
                      'baselineMaps': len(rows), 'baseline': str(args.baseline)}, ensure_ascii=False, indent=2))


def verify(args):
    baseline = read(args.baseline)
    failures, changed = [], []
    approved_code = {}
    changed_code = []
    if args.exceptions.is_file():
        scope = read(args.exceptions)
        if scope.get('baselineSha256') != sha(args.baseline) or set(scope.get('targetMaps', [])) != TARGETS:
            failures.append({'reason': 'scope exception is not tied to the immutable original baseline and four forests'})
        for exception in scope.get('codeFiles', []):
            name = exception.get('path')
            old = next((entry for entry in baseline['files'] if entry['path'] == name), None)
            if name not in CODE_EXCEPTION_FILES or old is None or exception.get('beforeSha256') != old['sha256']:
                failures.append({'file': name, 'reason': 'unapproved runtime exception'})
            else:
                approved_code[name] = exception['afterSha256']
        # These exact decorative names were never in GroundedPropKind or an atlas
        # census. Their removal must change neither shadow counts nor prop records.
        if scope.get('expectedGroundingAndPropCensusDelta') != 0:
            failures.append({'reason': 'decorative gate removal cannot relax grounding or prop invariants'})
    for entry in baseline['files']:
        path = args.repo / entry['path']
        if not path.is_file():
            failures.append({'file': entry['path'], 'reason': 'required file missing'})
            continue
        if sha(path) != entry['sha256']:
            if entry['allowChange']:
                changed.append(entry['path'])
            elif entry['path'] in approved_code and sha(path) == approved_code[entry['path']]:
                changed_code.append(entry['path'])
            else:
                failures.append({'file': entry['path'], 'reason': 'protected background/props/runtime/layout changed'})
    for name in sorted(TARGETS):
        path = args.repo / f'Assets/StreamingAssets/SurfaceWorld/{name}.png'
        if not path.is_file():
            continue
        size = dimensions(path)
        old = baseline['maps'][name]
        if abs((size[0] / size[1]) / (old['width'] / old['height']) - 1) > .02:
            failures.append({'map': name, 'reason': 'source aspect ratio differs from actual world'})
        if not args.allow_lower_ppu and size != [old['width'] * 96, old['height'] * 96]:
            failures.append({'map': name, 'reason': 'registered 96ppu dimensions changed', 'actual': size})
    if args.require_updated and set(changed) != ALLOWED:
        failures.append({'reason': 'all four requested backgrounds must be replaced', 'changed': changed})
    count = 0
    if args.index:
        seen = set()
        for row in read(args.index):
            name = row['id']
            if name not in baseline['maps'] or name in seen:
                failures.append({'map': name, 'reason': 'unexpected/repeated map'})
                continue
            seen.add(name)
            old = baseline['maps'][name]
            scene = row.get('surfaceComposition') or {}
            for key in ('baselineHash', 'collisionHash', 'terrainHash'):
                if row.get(key) != old[key]:
                    failures.append({'map': name, 'reason': key + ' changed'})
            if props(row) != old['props']:
                failures.append({'map': name, 'reason': 'existing independent prop appearance/census changed'})
            if not scene or geometry(row) != old['geometry']:
                failures.append({'map': name, 'reason': 'mesh partition, collision-independent terrain geometry, or original hidden layers changed'})
                continue
            source = args.repo / f'Assets/StreamingAssets/SurfaceWorld/{name}.png'
            if not source.is_file() or scene.get('sourceSha256') != sha(source):
                failures.append({'map': name, 'reason': 'runtime did not load the current registered PNG'})
            if name not in TARGETS and scene['sourceSha256'] != old['sourceSha256']:
                failures.append({'map': name, 'reason': 'non-target background changed'})
            if (not args.allow_lower_ppu or name not in TARGETS) and scene['pixelsPerUnit'] != old['pixelsPerUnit']:
                failures.append({'map': name, 'reason': 'runtime pixel density changed'})
            if scene['rasterOpaquePixels'] != scene['rasterWidth'] * scene['rasterHeight']:
                failures.append({'map': name, 'reason': 'background contains uncovered transparent areas'})
            if any(scene.get(k, 0) != 0 for k in ('uncoveredPixels', 'overlappingPixels', 'mismatchedPixels', 'wrongOwnerPixels', 'pixelBoundsErrors', 'supportOnWalkablePixels')):
                failures.append({'map': name, 'reason': 'runtime layer coverage/UV audit failed'})
            count += 1
        expected = TARGETS if args.forest_only else set(baseline['maps'])
        if not args.partial and seen != expected:
            failures.append({'reason': 'verification set incomplete', 'expected': sorted(expected), 'actual': sorted(seen)})
    result = {'recordedFiles': len(baseline['files']),
              'protectedFiles': sum(not e['allowChange'] and e['path'] not in approved_code for e in baseline['files']),
              'baselineSha256': sha(args.baseline), 'changedApprovedCode': changed_code,
              'changedAllowedBackgrounds': changed, 'runtimeMaps': count, 'failed': len(failures), 'failures': failures,
              'visualReviewRequired': 'Collision and layer hashes cannot prove painted shoreline semantics; compare native views with walk/water guides.'}
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if args.report:
        args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    raise SystemExit(1 if failures else 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=REPO)
    parser.add_argument('--baseline', type=Path, default=REPO / 'Docs/World/ForestBackgroundStyle/baseline.json')
    parser.add_argument('--previous-index', type=Path, default=WORK / 'forest-refinement-release/surface-index.json')
    parser.add_argument('--prior-baseline', type=Path, default=REPO / 'Docs/World/ForestRefinement/baseline.json')
    parser.add_argument('--snapshot', action='store_true')
    parser.add_argument('--index', type=Path)
    parser.add_argument('--partial', action='store_true')
    parser.add_argument('--forest-only', action='store_true')
    parser.add_argument('--allow-lower-ppu', action='store_true')
    parser.add_argument('--require-updated', action='store_true')
    parser.add_argument('--exceptions', type=Path, default=REPO / 'Docs/World/ForestBackgroundStyle/scope-exceptions.json')
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    snapshot(args) if args.snapshot else verify(args)


if __name__ == '__main__':
    main()
