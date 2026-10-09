"""Guard the four-field forest refinement against changes outside its visual scope."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

TARGETS = {'forest', 'forest_ruins', 'forest_depths', 'forest_crossing'}
WORK = Path(__file__).resolve().parents[2] / "Docs/World/ForestRefinement"
REPO = Path(__file__).resolve().parents[2]


def sha(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def prop_signature(row):
    scene = row.get('surfaceComposition') or {}
    fields = ('biome', 'eligible', 'applied', 'skipped', 'resources', 'staticProps', 'species',
              'atlasWidth', 'atlasHeight', 'floorRubbleCount', 'slots')
    return {
        'props': [{key: detail.get(key) for key in fields}
                  for detail in (row.get('propIntegration') or {}).get('details', [])],
        'grounding': scene.get('propGrounding'),
        'hiddenNature': scene.get('hiddenUnsupportedNature'),
        'canopy': scene.get('forestCanopy'),
    }


def snapshot(args):
    if args.baseline.exists():
        raise SystemExit('Baseline already exists; refusing to overwrite the pre-change record.')
    old = read(REPO / 'Docs/World/PropIntegration/baseline.json')
    paths = {entry['path']: entry['category'] for entry in old['files']}
    for folder in ('Assets/StreamingAssets/WorldProps', 'Assets/StreamingAssets/SurfaceWorld/Props'):
        for path in (args.repo / folder).rglob('*.png'):
            paths[path.relative_to(args.repo).as_posix()] = 'existing_prop_atlas'
    for folder in ('Assets/Scripts/Runtime/Combat', 'Assets/Scripts/Runtime/Enemies'):
        for path in (args.repo / folder).rglob('*.cs'):
            paths[path.relative_to(args.repo).as_posix()] = 'combat_source'
    paths['Assets/Scripts/Runtime/Interaction/ResourceNode.cs'] = 'resource_behavior'
    for name in ('GameConfig.cs', 'EnemyStats.cs', 'PlayerStats.cs'):
        paths['Assets/Scripts/Runtime/Data/' + name] = 'combat_stats'
    rows = read(args.previous_index)
    if len(rows) != 21 or len({r['id'] for r in rows} - TARGETS) != 17:
        raise SystemExit('Expected the complete approved 21-map runtime baseline.')
    result = {
        'version': 1,
        'targets': sorted(TARGETS),
        'gitHead': subprocess.check_output(['git', '-C', str(args.repo), 'rev-parse', 'HEAD'], text=True).strip(),
        'preexistingChanges': subprocess.check_output(['git', '-C', str(args.repo), 'status', '--short'], text=True).splitlines(),
        'previousIndex': str(args.previous_index),
        'previousIndexSha256': sha(args.previous_index),
        'files': [{'path': name, 'category': category, 'bytes': (args.repo / name).stat().st_size,
                   'sha256': sha(args.repo / name)} for name, category in sorted(paths.items())],
        'originalAtlases': read(args.repo / 'Assets/StreamingAssets/WorldProps/metadata.json')['atlases'],
        'maps': {r['id']: {'baselineHash': r['baselineHash'], 'collisionHash': r['collisionHash'],
                           'terrainHash': r['terrainHash'], 'sourceSha256': r['surfaceComposition']['sourceSha256'],
                           'props': prop_signature(r)} for r in rows},
    }
    args.baseline.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    print(json.dumps({'protectedFiles': len(result['files']), 'baselineMaps': len(rows),
                      'unchangedMaps': 17, 'baseline': str(args.baseline)}, ensure_ascii=False, indent=2))


def verify(args):
    baseline = read(args.baseline)
    failures = []
    for entry in baseline['files']:
        path = args.repo / entry['path']
        if not path.is_file() or sha(path) != entry['sha256']:
            failures.append({'file': entry['path'], 'reason': 'protected file missing or changed'})
    atlases = {r['id']: r for r in read(args.repo / 'Assets/StreamingAssets/WorldProps/metadata.json')['atlases']}
    for old in baseline['originalAtlases']:
        if atlases.get(old['id']) != old:
            failures.append({'atlas': old['id'], 'reason': 'existing atlas crop/pivot/provenance changed'})
    count = 0
    if args.index:
        rows = read(args.index)
        seen = set()
        lifecycle = set()
        for row in rows:
            map_id = row['id']
            if map_id not in baseline['maps'] or map_id in seen:
                failures.append({'map': map_id, 'reason': 'unexpected or repeated map'})
                continue
            seen.add(map_id)
            old = baseline['maps'][map_id]
            for key in ('baselineHash', 'collisionHash', 'terrainHash'):
                if row.get(key) != old[key]:
                    failures.append({'map': map_id, 'reason': key + ' changed'})
            scene = row.get('surfaceComposition') or {}
            if scene.get('sourceSha256') != old['sourceSha256']:
                failures.append({'map': map_id, 'reason': 'runtime background changed'})
            if map_id not in TARGETS and prop_signature(row) != old['props']:
                failures.append({'map': map_id, 'reason': 'non-target prop appearance/census changed'})
            details = (row.get('propIntegration') or {}).get('details', [])
            if len(details) != 1:
                failures.append({'map': map_id, 'reason': 'expected exactly one atlas scene'})
            for detail in details:
                expected_atlas = 'forest_fields' if map_id in TARGETS else detail.get('biome')
                source = args.repo / ('Assets/StreamingAssets/WorldProps/' + str(expected_atlas) + '.png')
                if detail.get('atlasId') != expected_atlas or not source.is_file() or detail.get('atlasSourceSha256') != sha(source):
                    failures.append({'map': map_id, 'reason': 'wrong or unverified atlas source'})
            probe = row.get('resourceVisualLifecycle') or {}
            for sample in probe.get('samples', []):
                if sample.get('depletedArt') and sample.get('respawnArtAndCollider') and sample.get('configurationPreserved'):
                    lifecycle.add((probe.get('atlasId'), sample.get('kind')))
            count += 1
        if not args.partial and seen != set(baseline['maps']):
            failures.append({'reason': 'complete run must include exactly 21 maps'})
        if not args.partial:
            for atlas in ('forest', 'forest_fields'):
                for kind in ('Tree', 'Rock'):
                    if (atlas, kind) not in lifecycle:
                        failures.append({'atlas': atlas, 'resource': kind, 'reason': 'separate full/stump respawn lifecycle was not verified'})
    result = {'protectedFiles': len(baseline['files']), 'originalAtlasDeclarations': len(baseline['originalAtlases']),
              'runtimeMaps': count, 'failed': len(failures), 'failures': failures}
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if args.report:
        args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    raise SystemExit(1 if failures else 0)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--repo', type=Path, default=REPO)
    parser.add_argument('--baseline', type=Path, default=WORK / 'baseline.json')
    parser.add_argument('--previous-index', type=Path, default=REPO / 'Docs/World/PropIntegration/surface-index.json')
    parser.add_argument('--snapshot', action='store_true')
    parser.add_argument('--index', type=Path)
    parser.add_argument('--partial', action='store_true')
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    snapshot(args) if args.snapshot else verify(args)


if __name__ == '__main__':
    main()
