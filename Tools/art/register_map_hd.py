"""Register existing, hash-verified super-resolution masters for HD map rendering.
No new inference is performed. Preserve local art corrections and world registration.
"""
import argparse, hashlib, json, shutil
from pathlib import Path
from PIL import Image

def sha(p): return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--work',required=True,type=Path)
    ap.add_argument('--cave-cache',required=True,type=Path)
    a=ap.parse_args();r=Path(__file__).resolve().parents[2];a.work.mkdir(parents=True,exist_ok=True)
    report={'method':'Hash-verified existing Real-ESRGAN masters, registered without crop or geometry changes. 128 pixels/tile surface; 96 pixels/tile underground. Local stair correction preserved.','assets':[]}
    m=json.loads((r/'Docs/World/surface-upscale-manifest.json').read_text())
    # Validate every input before replacing any assets.
    for row in m['assets']:
        assert sha(r/row['output'])==row['output_sha256'],row['id']+' changed'
        assert sha(Path(row['inference_path']))==row['inference_sha256'],row['id']+' master changed'
    for row in m['assets']:
        p=r/row['output'];backup=a.work/p.name
        if not backup.exists():shutil.copyfile(p,backup)
        assert sha(backup)==row['output_sha256']
        size=tuple(v//96*128 for v in row['output_size'])
        with Image.open(row['inference_path']) as raw:
            out=raw.convert('RGB').resize(size,Image.Resampling.LANCZOS)
        if row.get('postprocess'):
            # Preserve the complete already-feathered correction, with a small
            # border outside the original patch. The border uses unchanged stone.
            pp=row['postprocess'];box=pp['edited_source_bounds'];cw,ch=pp['source_canvas']
            bounds=(int((box[0]-2)*size[0]/cw),int((box[1]-2)*size[1]/ch),int((box[2]+2)*size[0]/cw),int((box[3]+2)*size[1]/ch))
            with Image.open(backup) as old:
                corrected=old.convert('RGB').resize(size,Image.Resampling.LANCZOS)
                out.paste(corrected.crop(bounds),bounds)
        out.save(p,optimize=True)
        assert p.stat().st_size<96*1024*1024
        report['assets'].append({'id':row['id'],'output':row['output'],'size':list(size),'ppu':128,'previous_sha256':row['output_sha256'],'master_sha256':row['inference_sha256'],'output_sha256':sha(p),'preserved_postprocess':row.get('postprocess')})
        print(row['id'],size,flush=True)
    for kind in ('descent','roots','fungal','depths'):
        p=r/f'Assets/StreamingAssets/Underworld/composition-{kind}_hd.png';raw=a.cave_cache/f'{kind}-general4.png'
        backup=a.work/p.name
        if not backup.exists():shutil.copyfile(p,backup)
        size=(56*96,48*96)
        with Image.open(raw) as im:im.convert('RGB').resize(size,Image.Resampling.LANCZOS).save(p,optimize=True)
        report['assets'].append({'id':'hollow_'+kind,'output':p.relative_to(r).as_posix(),'size':list(size),'ppu':96,'previous_sha256':sha(backup),'master_sha256':sha(raw),'output_sha256':sha(p)})
        print(kind,size,flush=True)
    (r/'Docs/World/map-hd-manifest.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
if __name__=='__main__':main()
