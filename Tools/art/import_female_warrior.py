"""Import image_gen atlases into the existing 64px warrior animation contract.

Only crops/scales/packs generated art; no procedural replacement character drawing.
Original SilverWarrior atlases and costume skins remain untouched.
"""
from pathlib import Path
import hashlib
import json
import numpy as np
from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / "Assets/ArtSources/FemaleWarrior"
DEST = ROOT / "Assets/Resources/FemaleWarrior"
REPORT = ROOT / "Docs/art/FemaleWarrior"
DIRS = ["down", "downside", "side", "upside", "up"]
CLIPS = ["idle0", "idle1", "walk0", "walk1", "walk2", "walk3", "attack", "hurt"]

def cuts(alpha, count, axis):
    projection = (alpha > 100).sum(axis=axis)
    length = len(projection)
    boundaries = [0]
    for i in range(1, count):
        target = round(i * length / count)
        radius = round(length / count * .22)
        candidates = range(target-radius, target+radius+1)
        best = min(candidates, key=lambda k: (int(projection[k]), abs(k-target)))
        boundaries.append(best)
    return boundaries + [length]

# Relative grip positions in each tightly cropped source figure, reviewed on
# the generated pose sheet. The importer snaps to the nearest opaque pixel.
REST = [(0.25,.75),(.22,.76),(.76,.75),(.22,.74),(.80,.78)]
SWING = [
    [(.14,.34),(.22,.57),(.15,.75),(.93,.58),(.94,.61),(.23,.75)],
    [(.15,.30),(.22,.59),(.22,.74),(.05,.61),(.06,.62),(.20,.75)],
    [(.50,.22),(.55,.57),(.95,.50),(.96,.54),(.94,.59),(.85,.72)],
    [(.95,.32),(.20,.75),(.15,.64),(.95,.63),(.96,.72),(.25,.75)],
    [(.90,.23),(.72,.68),(.95,.63),(.95,.60),(.95,.74),(.82,.78)],
]

def pack(name, columns):
    source = Image.open(SOURCE / f"{name}.png").convert("RGBA")
    rgba = np.asarray(source)
    assert (rgba[:,:,3] == 0).mean() > .2, "Source must have genuine alpha"
    xs, ys = cuts(rgba[:,:,3], columns, 0), cuts(rgba[:,:,3], 5, 1)
    cells = []
    for row in range(5):
        for col in range(columns):
            cell = source.crop((xs[col], ys[row], xs[col+1], ys[row+1]))
            a = np.asarray(cell)[:,:,3]
            yy, xx = np.nonzero(a > 100)
            assert len(xx) > 1000, (name,row,col,"empty cell")
            box = (int(xx.min()),int(yy.min()),int(xx.max()+1),int(yy.max()+1))
            cells.append((row,col,cell.crop(box)))
    # Mage has 45 opaque rows including its hat; warrior is 44 including hair.
    scale = 44 / float(np.median([c.height for _,_,c in cells]))
    atlas = Image.new("RGBA",(columns*64,320))
    frames, audit = [], []
    contact = Image.new("RGB",(columns*192,5*212),(35,41,48))
    draw = ImageDraw.Draw(contact)
    for row,col,cell in cells:
        w,h = max(1,round(cell.width*scale)),max(1,round(cell.height*scale))
        assert w < 60 and h < 58, (name,row,col,w,h)
        # Generated rear-three-quarter art faces left; canonical runtime view
        # faces right. This is atlas orientation, not a change to world movement.
        if row == 3:
            cell = cell.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
        small = cell.resize((w,h),Image.Resampling.NEAREST)
        arr = np.asarray(small).copy()
        arr[:,:,3] = np.where(arr[:,:,3]>=100,255,0)
        colors = Image.fromarray(arr[:,:,:3]).quantize(colors=28, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE).convert("RGB")
        arr[:,:,:3] = np.asarray(colors)
        arr[arr[:,:,3]==0] = 0
        small = Image.fromarray(arr)
        x,y = (64-w)//2,57-h
        frame = Image.new("RGBA",(64,64))
        frame.alpha_composite(small,(x,y))
        atlas.alpha_composite(frame,(col*64,row*64))
        u,v = SWING[row][col] if name=="attack" else REST[row]
        if row == 3: u = 1-u
        want = np.array([x+u*(w-1),y+v*(h-1)])
        yy,xx = np.nonzero(np.asarray(frame)[:,:,3])
        nearest = np.argmin((xx-want[0])**2+(yy-want[1])**2)
        hand = {"x":int(xx[nearest]),"y":int(yy[nearest])}
        clip = f"attack{col}" if name=="attack" else CLIPS[col]
        frames.append(dict(clip=clip,direction=DIRS[row],column=col,row=row,
                           waist=42,hand=hand))
        audit.append(dict(clip=clip,direction=DIRS[row],bounds=[x,y,x+w,y+h],hand=hand))
        contact.paste(frame.resize((192,192),Image.Resampling.NEAREST),
                      (col*192,row*212),frame.resize((192,192),Image.Resampling.NEAREST))
        hx,hy = col*192+hand["x"]*3,row*212+hand["y"]*3
        draw.ellipse((hx-3,hy-3,hx+3,hy+3),outline="cyan",width=1)
        draw.text((col*192+5,row*212+194),f"{DIRS[row]} {clip}",fill="white")
    atlas.save(DEST/f"{name}.png")
    contact.save(REPORT/f"{name}-grip-review.png")
    return frames,audit

def meta(path):
    target=Path(str(path)+".meta")
    if target.exists(): return
    rel=path.relative_to(ROOT).as_posix()
    guid=hashlib.md5(("dotRPG:"+rel).encode()).hexdigest()
    if path.suffix==".png":
        # Readable, point-filtered, uncompressed texture exactly like old atlas.
        template=(ROOT/"Assets/Resources/SilverWarrior/Skins/noir/body.png.meta").read_text()
        import re
        target.write_text(re.sub(r"guid: [a-f0-9]+",f"guid: {guid}",template,count=1))
    elif path.is_dir():
        target.write_text(f"fileFormatVersion: 2\nguid: {guid}\nfolderAsset: yes\nDefaultImporter:\n  userData:\n")
    else:
        target.write_text(f"fileFormatVersion: 2\nguid: {guid}\nTextScriptImporter:\n  externalObjects: {{}}\n  userData:\n")

if __name__=="__main__":
    DEST.mkdir(parents=True,exist_ok=True)
    REPORT.mkdir(parents=True,exist_ok=True)
    frames,audit=[],[]
    for name,cols in [("body",8),("attack",6)]:
        f,a=pack(name,cols);frames+=f;audit+=a
    assert len(frames)==70
    (DEST/"frames.json").write_text(json.dumps(dict(size=64,ppu=36,pivot=dict(x=32,y=6),frames=frames),indent=2))
    (REPORT/"import-validation.json").write_text(json.dumps(dict(frameCount=70,frames=audit),indent=2))
    for directory in [SOURCE,DEST]:
        meta(directory)
        for path in directory.iterdir():
            if path.suffix in (".png",".json"):meta(path)
    print("PASS: 70 frames, 5 facings, 64px cells, alpha and bounds; original skins untouched.")
