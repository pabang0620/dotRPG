"""Register generated surface plates at 48 or 96 pixels/tile using local Real-ESRGAN.

Keeps unmodified generation outputs in the supplied work directory. Uses the existing
map index for dimensions, never changes layout/collision, and records exact hashes.
"""
import argparse, hashlib, json, shutil, subprocess, uuid
from pathlib import Path
from PIL import Image

def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--tool', type=Path, required=True)
    ap.add_argument('--index', type=Path, required=True)
    ap.add_argument('--work', type=Path, required=True)
    ap.add_argument('--only', default='')
    ap.add_argument('--pixels-per-tile', type=int, choices=(48,96), default=48)
    ap.add_argument('--source-manifest', type=Path, help='Reuse exact original sources and 4x caches from a previous registration')
    ap.add_argument('--source-root', type=Path, default=Path.cwd(), help='Base directory for relative source paths in the input manifest')
    ap.add_argument('--backup-dir', type=Path, help='Keep each previous registered PNG before replacing it')
    ap.add_argument('--manifest-output', type=Path)
    a=ap.parse_args(); repo=Path(__file__).resolve().parents[2]
    art=repo/'Assets/StreamingAssets/SurfaceWorld'; a.work.mkdir(parents=True, exist_ok=True)
    originals=a.work/'originals'; originals.mkdir(exist_ok=True)
    rows=json.loads(a.index.read_text(encoding='utf-8-sig'))
    if a.only:
        selected=set(a.only.split(','))
        if not selected.issubset({r['id'] for r in rows}): raise ValueError('Unknown map id')
        rows=[r for r in rows if r['id'] in selected]
    elif len(rows)!=21: raise ValueError('Expected 21 surface maps')
    previous={}
    def source_path(record):
        path=Path(record['source'])
        return (path if path.is_absolute() else a.source_root/path).resolve()
    if a.source_manifest:
        previous={r['id']:r for r in json.loads(a.source_manifest.read_text(encoding='utf-8-sig'))['assets']}
        # Check every source before touching the first destination. Do not silently
        # enlarge a previously downsampled registration when originals are missing.
        for row in rows:
            old=previous[row['id']]; source=source_path(old)
            if old.get('postprocess'):
                raise ValueError(row['id']+' has a localized art edit; replay its recorded postprocess before replacing the registered PNG')
            if not source.is_file() or sha(source)!=old['source_sha256']:
                raise ValueError(row['id']+' original source missing or changed')
            dest=art/(row['id']+'.png')
            if not dest.is_file() or sha(dest)!=old['output_sha256']:
                raise ValueError(row['id']+' registered input differs from source manifest')
    if a.backup_dir: a.backup_dir.mkdir(parents=True,exist_ok=True)
    report={'method':f'Generated original pixel-art plates; local Real-ESRGAN x4plus then Lanczos registration to exactly {a.pixels_per_tile} pixels/tile',
       'pixels_per_tile':a.pixels_per_tile,
       'tool_release':'https://github.com/xinntao/Real-ESRGAN/releases/tag/v0.2.5.0',
       'tool_sha256':sha(a.tool), 'model_sha256':sha(a.tool.parent/'models/realesrgan-x4plus.bin'),
       'note':'Upscaling preserves registration; it does not establish collision alignment of generated artwork. In-game sampling remains Point. No camera or world-scale changes.', 'assets':[]}
    for row in rows:
        key=row['id']; dest=art/(key+'.png')
        source=source_path(previous[key]) if previous else originals/(key+'.png')
        if not source.exists(): shutil.copyfile(dest,source)
        cache=source.parent.parent if previous else a.work
        raw=cache/(key+'-4x.png'); stamp=cache/(key+'-4x.sha256')
        reused=raw.exists() and stamp.exists() and stamp.read_text()==sha(source)
        if not reused:
            with (a.work/(key+'.log')).open('w') as log:
                subprocess.run([str(a.tool.resolve()),'-i',str(source.resolve()),'-o',str(raw.resolve()),'-n','realesrgan-x4plus','-s','4','-t','256','-j','1:1:1'],cwd=a.tool.parent,stdout=log,stderr=log,check=True)
            stamp.write_text(sha(source))
        with Image.open(source) as orig, Image.open(raw) as inferred:
            size=(row['width']*a.pixels_per_tile,row['height']*a.pixels_per_tile)
            if max(size)>8192: raise ValueError(key+' exceeds supported 8192px source size')
            if abs((orig.width/orig.height)/(size[0]/size[1])-1)>.02: raise ValueError(key+' aspect ratio mismatch')
            if inferred.size!=(orig.width*4,orig.height*4): raise ValueError('inference size mismatch')
            old_hash=sha(dest)
            with Image.open(dest) as old_image: old_size=old_image.size
            if a.backup_dir:
                backup=a.backup_dir/(key+'.png')
                if backup.exists() and sha(backup)!=old_hash: raise ValueError(key+' backup already contains a different revision')
                if not backup.exists(): shutil.copyfile(dest,backup)
            temporary=a.work/(key+'-registered.png')
            inferred.convert('RGB').resize(size,Image.Resampling.LANCZOS).save(temporary,optimize=True)
            if temporary.stat().st_size>64*1024*1024: raise ValueError(key+' exceeds runtime 64MiB PNG input cap')
            shutil.copyfile(temporary,dest)
            report['assets'].append({'id':key,'source':str(source),'source_size':list(orig.size),'source_sha256':sha(source),
                'inference_path':str(raw),'inference_sha256':sha(raw),'inference_cache_reused':reused,
                'previous_output_size':list(old_size),'previous_output_sha256':old_hash,
                'output':dest.relative_to(repo).as_posix(),'output_size':list(size),'output_sha256':sha(dest)})
        meta=Path(str(dest)+'.meta')
        if not meta.exists(): meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
        print(key+': '+str(size),flush=True)
    name='surface-upscale-manifest'+('-'+a.only.replace(',','-') if a.only else '')+'.json'
    target=a.manifest_output or repo/'Docs/World'/name
    target.parent.mkdir(parents=True,exist_ok=True)
    target.write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')

if __name__=='__main__': main()
