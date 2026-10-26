"""Reviewable pixel edits for the warrior skin cleanup. No resampling or alpha changes to clothing."""
from pathlib import Path
import json
import shutil
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
SKINS = ROOT / 'Assets/Resources/SilverWarrior/Skins'
OUT = ROOT / 'Logs/warrior-skin-review'
OUT.mkdir(parents=True, exist_ok=True)

def read(path):
    return np.array(Image.open(path).convert('RGBA'))

def backup(path):
    dest = OUT / path.parent.name / path.name
    dest.parent.mkdir(parents=True, exist_ok=True)
    if not dest.exists():
        shutil.copy2(path, dest)
    return read(dest)

def twin():
    body = read(SKINS / 'w_whitetwin/body.png')
    path = SKINS / 'w_whitetwin/attack.png'
    attack = backup(path)
    yy, xx = np.indices((64, 64))
    for row in range(5):
        src = body[row*64:(row+1)*64, :64]
        # The complete head and the two low tails come from the matching idle direction.
        hair = (src[:, :, :3].max(2).astype(int)-src[:, :, :3].min(2).astype(int)<42) & (src[:, :, :3].mean(2)>80)
        mask = (src[:, :, 3]>0) & ((yy<35) | (hair & (yy<44) & ((xx<26)|(xx>37))))
        for col in range(6):
            dst = attack[row*64:(row+1)*64, col*64:(col+1)*64]
            oldhair = (dst[:, :, :3].max(2).astype(int)-dst[:, :, :3].min(2).astype(int)<42) & (dst[:, :, :3].mean(2)>80)
            erase = (yy<35) | (oldhair & (yy<44) & ((xx<26)|(xx>37)))
            # Preserve the raised hand outside the head, identified by its warm skin colour.
            skin = (dst[:,:,0].astype(int)>dst[:,:,2].astype(int)+35) & (dst[:,:,0]>140)
            erase &= ~skin | ((xx>=25)&(xx<=39)&(yy>=24))
            dst[erase] = 0
            dst[mask] = src[mask]
    Image.fromarray(attack).save(path)

PATTERNS = {
    'w_stripe': ((226,237,243),(38,70,111),['11111','11111','00000','00000','11111']),
    'w_pajama': ((168,213,230),(255,248,184),['00100','01110','11111','01110','01010']),
    'w_starnight': ((31,43,76),(247,200,82),['00100','00100','11111','00100','00100']),
    'w_blossom': ((248,226,232),(217,105,150),['01010','11111','01110','11111','01010']),
    'w_festival': ((169,38,55),(251,204,87),['00100','01110','11011','01110','00100']),
}

def patterns():
    for name, (base, ink, glyph) in PATTERNS.items():
        for sheet, cols in [('body',8),('attack',6)]:
            path = SKINS / name / (sheet+'.png')
            arr = backup(path)
            for row in range(5):
                for col in range(cols):
                    cell = arr[row*64:(row+1)*64,col*64:(col+1)*64]
                    # Embroidery is centred on the cloth panel, below the face and above the belt.
                    cx = [31,32,33,32,31][row]
                    top = 39 + (1 if sheet=='body' and col in (1,5) else 0)
                    for gy in range(5):
                        for gx in range(5):
                            x,y = cx-2+gx,top+gy
                            r,g,b,a = map(int,cell[y,x])
                            if not a or max(r,g,b)<55: continue
                            # Do not repaint bare hands or the face.
                            if r>g+18 and g>b+15 and r>150 and name!='w_festival': continue
                            colour = ink if glyph[gy][gx]=='1' else base
                            cell[y,x,:3]=colour
            Image.fromarray(arr).save(path)

def anchors():
    frames=json.loads((SKINS.parent/'frames.json').read_text())['frames']
    changes=[]
    for folder in sorted(SKINS.iterdir()):
        if not folder.is_dir() or not (folder/'attack.png').exists(): continue
        sheets={s:read(folder/(s+'.png')) for s in ('body','attack')}
        overrides=[]
        for f in frames:
            a=sheets['attack' if f['clip'].startswith('attack') and len(f['clip'])>6 else 'body']
            cell=a[f['row']*64:(f['row']+1)*64,f['column']*64:(f['column']+1)*64]
            x,y=int(f['hand']['x']),int(f['hand']['y'])
            if cell[y-1:y+2,x-1:x+2,3].any(): continue
            candidates=[(dx*dx+dy*dy,dy,dx) for dy in range(-3,4) for dx in range(-3,4) if max(abs(dx),abs(dy))<=3 and cell[y+dy,x+dx,3]>0]
            if not candidates:
                changes.append([folder.name,f['direction'],f['clip'],'manual review']); continue
            _,dy,dx=min(candidates)
            overrides.append(dict(direction=f['direction'],clip=f['clip'],hand=dict(x=x+dx,y=y+dy)))
            changes.append([folder.name,f['direction'],f['clip'],[x,y],[x+dx,y+dy]])
        # Nearest opaque pixels are audit candidates, not necessarily a hand.
        # Never install unreviewed candidates into the runtime atlas.
    (OUT/'anchors.json').write_text(json.dumps(changes,indent=2)+'\n',encoding='utf-8')
    print('anchor overrides',len(changes),'manual',sum(x[-1]=='manual review' for x in changes))

def reviewed_anchors():
    reviewed = {
        'w_greybun': [('up','walk0',40,44),('up','walk1',40,44),('side','attack3',47,38)],
        'w_bluecoat': [('down','idle1',26,46)],
        'w_valkyrie': [('down','walk2',24,41)],
        'w_violet': [('down','idle1',28,41)],
        'noir': [('down','idle1',29,47)],
        'w_blackbob': [('side','attack3',47,38)],
        'w_bluebraids': [('side','attack3',47,38)],
    }
    for skin, rows in reviewed.items():
        frames=[dict(direction=d,clip=c,hand=dict(x=x,y=y)) for d,c,x,y in rows]
        (SKINS/skin/'hands.json').write_text(json.dumps(dict(frames=frames),indent=2)+'\n',encoding='utf-8')

if __name__=='__main__':
    twin()
    patterns()
    anchors()
    reviewed_anchors()
