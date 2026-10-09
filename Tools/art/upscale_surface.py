"""Register generated surface plates at 48 pixels/tile using approved local Real-ESRGAN.

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
    a=ap.parse_args(); repo=Path(__file__).resolve().parents[2]
    art=repo/'Assets/StreamingAssets/SurfaceWorld'; a.work.mkdir(parents=True, exist_ok=True)
    originals=a.work/'originals'; originals.mkdir(exist_ok=True)
    rows=json.loads(a.index.read_text(encoding='utf-8-sig'))
    if a.only: rows=[r for r in rows if r['id'] in a.only.split(',')]
    elif len(rows)!=21: raise ValueError('Expected 21 surface maps')
    report={'method':'Generated original pixel-art plates; local Real-ESRGAN x4plus then Lanczos registration to exactly 48 pixels/tile',
       'tool_release':'https://github.com/xinntao/Real-ESRGAN/releases/tag/v0.2.5.0',
       'tool_sha256':sha(a.tool), 'model_sha256':sha(a.tool.parent/'models/realesrgan-x4plus.bin'),
       'note':'Upscaling preserves registration; it does not establish collision alignment of generated artwork. In-game sampling remains Point. No camera or world-scale changes.', 'assets':[]}
    for row in rows:
        key=row['id']; dest=art/(key+'.png'); source=originals/(key+'.png')
        if not source.exists(): shutil.copyfile(dest,source)
        raw=a.work/(key+'-4x.png'); stamp=a.work/(key+'-4x.sha256')
        if not raw.exists() or not stamp.exists() or stamp.read_text()!=sha(source):
            with (a.work/(key+'.log')).open('w') as log:
                subprocess.run([str(a.tool.resolve()),'-i',str(source.resolve()),'-o',str(raw.resolve()),'-n','realesrgan-x4plus','-s','4','-t','256','-j','1:1:1'],cwd=a.tool.parent,stdout=log,stderr=log,check=True)
            stamp.write_text(sha(source))
        with Image.open(source) as orig, Image.open(raw) as inferred:
            size=(row['width']*48,row['height']*48)
            if abs((orig.width/orig.height)/(size[0]/size[1])-1)>.02: raise ValueError(key+' aspect ratio mismatch')
            if inferred.size!=(orig.width*4,orig.height*4): raise ValueError('inference size mismatch')
            inferred.convert('RGB').resize(size,Image.Resampling.LANCZOS).save(dest,optimize=True)
            report['assets'].append({'id':key,'source':str(source),'source_size':list(orig.size),'source_sha256':sha(source),'output':dest.relative_to(repo).as_posix(),'output_size':list(size),'output_sha256':sha(dest)})
        meta=Path(str(dest)+'.meta')
        if not meta.exists(): meta.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\nDefaultImporter:\n  externalObjects: {}\n  userData:\n  assetBundleName:\n  assetBundleVariant:\n')
        print(key+': '+str(size),flush=True)
    name='surface-upscale-manifest'+('-'+a.only.replace(',','-') if a.only else '')+'.json'
    (repo/'Docs/World'/name).write_text(json.dumps(report,indent=2,ensure_ascii=False)+'\n',encoding='utf-8')

if __name__=='__main__': main()
