"""Package imagegen exit-road corrections over HD plates, preserving every pixel outside measured patches."""
from pathlib import Path
import argparse,json,hashlib,shutil
import numpy as np
from PIL import Image

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 ap=argparse.ArgumentParser();ap.add_argument('--work',type=Path,required=True);a=ap.parse_args();r=Path(__file__).resolve().parents[2]
 patches={'village':(0,.535,.255,.665),'canyon':(.155,0,.345,.175),'winter':(0,.385,.185,.515),'sunken_sanctum':(0,.195,.365,.325)}
 report=[]
 for key,rect in patches.items():
  dest=r/'Assets/Resources/WorldArt/Surface'/f'{key}.png';backup=a.work/f'{key}-before-road.png';raw=a.work/f'{key}-4x.png'
  if not backup.exists():shutil.copyfile(dest,backup)
  with Image.open(backup) as b,Image.open(raw) as edit:
   image=b.convert('RGB');w,h=image.size;box=tuple(round(v*(w if i%2==0 else h)) for i,v in enumerate(rect));x0,y0,x1,y1=box
   replacement=edit.convert('RGB').resize((w,h),Image.Resampling.LANCZOS).crop(box)
   yy,xx=np.ogrid[y0:y1,x0:x1];alpha=np.ones((y1-y0,x1-x0),dtype=np.float32)
   feather=min(w,h)*.012
   if x0>0:alpha=np.minimum(alpha,(xx-x0)/feather)
   if y0>0:alpha=np.minimum(alpha,(yy-y0)/feather)
   if x1<w:alpha=np.minimum(alpha,(x1-1-xx)/feather)
   if y1<h:alpha=np.minimum(alpha,(y1-1-yy)/feather)
   alpha=np.clip(alpha,0,1);alpha=alpha*alpha*(3-2*alpha);alpha=alpha[:,:,None]
   merged=np.rint(np.asarray(image.crop(box))*(1-alpha)+np.asarray(replacement)*alpha).astype(np.uint8)
   image.paste(Image.fromarray(merged),box);image.save(dest,optimize=True)
  report.append({'id':key,'pixelBounds':box,'before':digest(backup),'after':digest(dest),'source':f'Docs/World/HubRoadSources/{key}.png','source_sha256':digest(r/f'Docs/World/HubRoadSources/{key}.png'),'outsidePatch':'unchanged','master_sha256':digest(raw)})
  print(key,box,flush=True)
 (r/'Docs/World/HubRoadSources/registration.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
if __name__=='__main__':main()
