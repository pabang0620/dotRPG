"""Read-only background/layout guard for the prop integration pass."""
import argparse
import hashlib
import json
from pathlib import Path


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    repo = Path(__file__).resolve().parents[2]
    parser.add_argument('--repo', type=Path, default=repo)
    parser.add_argument('--baseline', type=Path, default=repo / 'Docs/World/PropIntegration/baseline.json')
    parser.add_argument('--surface-index', type=Path)
    parser.add_argument('--underground-index', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    baseline = json.loads(args.baseline.read_text(encoding='utf-8'))
    failures = []
    for entry in baseline['files']:
        path = args.repo / entry['path']
        if not path.is_file() or digest(path) != entry['sha256']:
            failures.append({'path': entry['path'], 'reason': 'protected file missing or changed'})
    runtime_count = 0
    if args.surface_index:
        index = json.loads(args.surface_index.read_text(encoding='utf-8-sig'))
        art = {Path(e['path']).stem: e['sha256'] for e in baseline['files'] if e['category'] == 'surface_background'}
        for row in index:
            composition = row.get('surfaceComposition') or {}
            expected = art.get(row['id'])
            if not expected or composition.get('sourceSha256') != expected:
                failures.append({'path': row['id'], 'reason': 'runtime source SHA differs or absent'})
            runtime_count += 1
    if args.underground_index:
        index = json.loads(args.underground_index.read_text(encoding='utf-8-sig'))
        art = {Path(e['path']).name: e['sha256'] for e in baseline['files']
               if '/Underworld/composition-' in e['path']}
        for row in index:
            name = row.get('sourcePath', '').replace('\\', '/').rsplit('/', 1)[-1]
            if not art.get(name) or row.get('sourceSha256') != art[name]:
                failures.append({'path': row['id'], 'reason': 'runtime underground source SHA differs or absent'})
            if (row.get('propIntegration') or {}).get('applied') != 0:
                failures.append({'path': row['id'], 'reason': 'embedded underground artwork unexpectedly replaced'})
            runtime_count += 1
    result = {'protectedFiles': len(baseline['files']), 'runtimeMaps': runtime_count,
              'failed': len(failures), 'failures': failures}
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if args.report:
        args.report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
    raise SystemExit(1 if failures else 0)


if __name__ == '__main__':
    main()
