using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Independent geometry, painted terrain, occluding architecture and animated water.</summary>
    public static class SunkenSanctumArt
    {
        static readonly Dictionary<string,Sprite> sprites = new Dictionary<string,Sprite>();
        static Sprite terrain;
        public static string Layout()
        {
            var asset=Resources.Load<TextAsset>("Maps/SunkenSanctum");
            return asset!=null?asset.text:StreamingFiles.ReadAllText(Path.Combine(Application.streamingAssetsPath,"SunkenSanctum/layout.txt"));
        }
        static Color32 C(int r,int g,int b)=>new Color32((byte)Mathf.Clamp(r,0,255),(byte)Mathf.Clamp(g,0,255),(byte)Mathf.Clamp(b,0,255),255);
        static int Hash(int x,int y)=>unchecked((x*73856093 ^ y*19349663)&0x7fffffff);
        public static bool Land(char c)=>c=='.'||c=='L'||c=='P'||c=='<'||c=='>';
        static Sprite Raster(PixelCanvas p,string key,Vector2 pivot,float ppu=32)
        {
            var t=new Texture2D(p.Width,p.Height,TextureFormat.RGBA32,false){name=key,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            t.SetPixels32(p.ToTexturePixels());t.Apply();
            var s=Sprite.Create(t,new Rect(0,0,p.Width,p.Height),pivot,ppu,0,SpriteMeshType.FullRect);s.name=key;return s;
        }
        public static Sprite Asset(string name)
        {
            if(sprites.TryGetValue(name,out var found))return found;
            string file=Path.Combine(Application.streamingAssetsPath,"SunkenSanctum",name+".png");
            if(!StreamingFiles.Exists(file))return null;
            var t=new Texture2D(2,2,TextureFormat.RGBA32,false){name="sanctum_"+name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            ImageConversion.LoadImage(t,StreamingFiles.ReadAllBytes(file));t.filterMode=FilterMode.Point;
            var p=t.GetPixels32();int left=t.width,right=0,bottom=t.height,top=0;
            for(int y=0;y<t.height;y++)for(int x=0;x<t.width;x++)if(p[y*t.width+x].a>32){left=Math.Min(left,x);right=Math.Max(right,x);bottom=Math.Min(bottom,y);top=Math.Max(top,y);}
            if(right<=left){UnityEngine.Object.Destroy(t);return null;}
            var s=Sprite.Create(t,new Rect(left,bottom,right-left+1,top-bottom+1),new Vector2(.5f,0),32,0,SpriteMeshType.FullRect);s.name="sanctum_"+name;
            return sprites[name]=s;
        }
        static SpriteRenderer Put(Transform root,string name,Sprite sprite,Vector2 pos,Vector2 size,int order)
        {
            var go=new GameObject(name);go.transform.SetParent(root,false);go.transform.position=pos;
            var sr=go.AddComponent<SpriteRenderer>();sr.sprite=sprite;sr.sortingOrder=order;
            if(sprite!=null)go.transform.localScale=new Vector3(size.x/sprite.bounds.size.x,size.y/sprite.bounds.size.y,1);
            return sr;
        }
        static void Structure(Transform root,string name,Vector2 foot,Vector2 size,float shade=1)
        {
            var sr=Put(root,name,Asset(name),foot,size,YSort.OrderFor(foot.y));sr.color=new Color(shade,shade,shade,1);
            // Bases sit in blocked cliff cells. Upper sprite may overlap the edge of walkable ground.
            TreeFade.Attach(sr.gameObject);
        }
        public static void Build(Transform root,char[,] cells,int w,int h)
        {
            if(terrain==null)terrain=Paint(cells,w,h);
            Put(root,"Sanctum terrain",terrain,Vector2.zero,new Vector2(w,h),-30000);
            Put(root,"Terrace retaining wall west",Asset("terrace_wall"),new Vector2(16.5f,43),new Vector2(11,4.5f),-29000);
            Put(root,"Terrace retaining wall east",Asset("terrace_wall"),new Vector2(31.5f,43),new Vector2(11,4.5f),-29000);
            Structure(root,"arch",new Vector2(24,54),new Vector2(19,17));
            Structure(root,"tower_left",new Vector2(9.5f,46),new Vector2(15,24));
            Structure(root,"tower_right",new Vector2(38,46),new Vector2(15,24),.94f);
            Structure(root,"tower_left",new Vector2(5,29),new Vector2(12,23),.68f);
            Structure(root,"tower_right",new Vector2(43,29),new Vector2(12,22),.64f);
            Structure(root,"tower_right",new Vector2(8,14),new Vector2(15,23),.38f);
            Structure(root,"tower_left",new Vector2(40,14),new Vector2(15,22),.42f);
            Structure(root,"tower_left",new Vector2(2,0),new Vector2(11,18),.36f);
            Structure(root,"tower_right",new Vector2(47,0),new Vector2(11,19),.32f);
            Put(root,"Central terrace stairs",Asset("stairs"),new Vector2(24,42),new Vector2(4,6.2f),-27900);
            foreach(float x in new[]{22.15f,25.85f}){
                var curb=new GameObject("Stair side curb");curb.transform.SetParent(root,false);curb.transform.position=new Vector2(x,44.4f);
                curb.AddComponent<BoxCollider2D>().size=new Vector2(.3f,4.4f);
            }
            // The altar image is a floor layer, never an occluding building or a walkable image over water.
            var altar=Asset("altar");
            if(altar!=null)Put(root,"Ancient broken altar",altar,new Vector2(24,7.2f),new Vector2(16,13),-28000);
            var gate=Put(root,"Hunting passage marker",Game.Art.Get("arrow_right"),new Vector2(14.5f,49.5f),new Vector2(1,.7f),-27000);
            gate.color=new Color(.6f,.9f,.78f);
            Pillar(root,new Vector2(19,11),2.1f);Pillar(root,new Vector2(29,11.5f),1.8f);
            Pillar(root,new Vector2(16,50),1.15f);
            Debris(root,new Vector2(33,50),true);
            // Small peripheral rubble does not block the path.
            foreach(var pos in new[]{new Vector2(13,35),new Vector2(17,27),new Vector2(28,44),new Vector2(12,49),new Vector2(35,53)})Debris(root,pos,false);
            var water=new GameObject("Sanctum living water");water.transform.SetParent(root,false);water.AddComponent<SanctumWater>().Setup(cells,w,h);
            var marker=new PixelCanvas(40,24);marker.Ellipse(20,12,17,9,C(50,57,43));marker.Ellipse(20,10,16,8,C(156,150,111));marker.Ellipse(20,10,11,5,C(61,78,59));
            marker.Line(13,10,26,10,C(187,193,151));marker.Line(13,10,18,6,C(187,193,151));marker.Line(13,10,18,14,C(187,193,151));
            Put(root,"Return to snow hunting field",Raster(marker,"sanctum_return",Vector2.one*.5f),new Vector2(24.5f,10.5f),new Vector2(1.1f,.65f),-27000);
        }
        static void Pillar(Transform root,Vector2 pos,float height)
        {
            var s=Asset("pillar");
            var sr=Put(root,"Broken pillar",s,pos,new Vector2(height*.7f,height),YSort.OrderFor(pos.y));
            var col=new GameObject("Pillar foot collision");col.transform.SetParent(root,false);col.transform.position=pos+Vector2.up*.2f;
            col.AddComponent<BoxCollider2D>().size=new Vector2(.7f,.6f);TreeFade.Attach(sr.gameObject);
        }
        static void Debris(Transform root,Vector2 pos,bool longPiece)
        {
            string key=longPiece?"long_remnant":"rubble";
            if(!sprites.TryGetValue(key,out var s)){
                var p=new PixelCanvas(120,64);
                if(longPiece){for(int i=0;i<8;i++){int x=18+i*11,y=46-i*4;p.Rect(x,y,16,11,C(69,51,32));p.Line(x,y,x+14,y-2,C(124,96,60));p.Line(x,y+8,x+14,y+6,C(35,34,23));}}
                else for(int i=0;i<7;i++){int x=15+(i*37)%90,y=28+(i*13)%24;p.Ellipse(x,y,11,7,C(41,47,35));p.Ellipse(x-1,y-3,10,5,C(105+i*4,104+i*3,79+i*2));p.Line(x-5,y-7,x+2,y-2,C(153,143,104));}
                s=sprites[key]=Raster(p,"sanctum_"+key,new Vector2(.5f,.15f));
            }
            Put(root,key,s,pos,longPiece?new Vector2(4,2.2f):new Vector2(1.6f,.8f),YSort.OrderFor(pos.y));
            if(longPiece){var go=new GameObject("Remnant collision");go.transform.SetParent(root,false);go.transform.position=pos+Vector2.up*.5f;go.AddComponent<BoxCollider2D>().size=new Vector2(2.7f,.9f);}
        }
        static Sprite Paint(char[,] cells,int w,int h)
        {
            var p=new PixelCanvas(w*32,h*32);
            var material=Asset("floor").texture;var materialPixels=material.GetPixels32();
            char Cell(int x,int y)=>x<0||y<0||x>=w||y>=h?'W':cells[x,h-1-y];
            for(int y=0;y<p.Height;y++)for(int x=0;x<p.Width;x++){
                int tx=x/32,ty=y/32;char c=Cell(tx,ty);int bx=x%32,by=y%32;
                float nx=x/32f,ny=y/32f;
                int slabX=(x+((y/43)%2)*24)/59,slabY=y/43;int seed=Hash(slabX,slabY);
                int noise=(seed%13)-6;bool seam=(x+((y/43)%2)*24)%59<2||y%43<2;
                Color32 color;
                Color32 stone=materialPixels[((y*2)%material.height)*material.width+(x*2)%material.width];
                if(Land(c)){
                    int light=ny>51?15:ny<27?-7:3;
                    color=C(stone.r+light,stone.g+light,stone.b+light);
                    if(!Land(Cell(tx,ty+1))&&by>26)color=by<29?C(177,161,118):C(69,74,54);
                    if(!Land(Cell(tx-1,ty))&&bx<3)color=C(85,96,55);
                    if(c=='L'){int step=y%16;color=step<3?C(185,173,134):step<11?C(139,136,110):C(63,70,55);if(bx<2&&(tx==22||tx==25))color=C(170,162,123);}
                    bool moss=(Hash(x/13,y/11)%19<3)&&(!Land(Cell(tx+1,ty))||!Land(Cell(tx,ty-1)));
                    if(moss)color=C((int)(stone.r*.72f),(int)(stone.g*.88f),(int)(stone.b*.57f));
                }else if(c=='~'){
                    float distance=3;
                    for(int yy=ty-2;yy<=ty+2;yy++)for(int xx=tx-2;xx<=tx+2;xx++)if(Land(Cell(xx,yy)))
                        distance=Mathf.Min(distance,Vector2.Distance(new Vector2(nx,ny),new Vector2(Mathf.Clamp(nx,xx,xx+1),Mathf.Clamp(ny,yy,yy+1))));
                    float shallow=Mathf.Floor(Mathf.Clamp01(1-distance/3)*8)/8;
                    color=C((int)(22+shallow*19+stone.r*.025f),(int)(54+shallow*25+stone.g*.03f),(int)(43+shallow*14+stone.b*.02f));
                    if(ny>51&&Mathf.Abs(nx-24)<13)color=C(color.r+8,color.g+10,color.b+2);
                }else{
                    float central=Mathf.Clamp01(1-Mathf.Abs(nx-24)/24);int v=(int)(central*13);
                    color=C((int)(stone.r*.18f)+v/3,(int)(stone.g*.23f)+v/3,(int)(stone.b*.2f)+v/3);
                    // Thick exposed front of the raised upper terrace; no walking on this face.
                    if(ty>=25&&ty<29&&tx>=11&&tx<=36)color=C(stone.r/3,stone.g/3,stone.b/3);
                }
                p.Set(x,y,color);
            }
            // Broken outer ring: separate ruins on the bank around the altar pool.
            for(int i=0;i<52;i++){
                float a=i*Mathf.PI*2/52;float ringX=24+19*Mathf.Cos(a),ringY=58+12*Mathf.Sin(a);
                if(ringY<50||ringY>70)continue;int x=(int)(ringX*32),y=(int)(ringY*32);int size=19+Hash(i,5)%15;
                for(int yy=-size/2;yy<size/2;yy++)for(int xx=-size;xx<size;xx++){
                    int cut=Math.Abs(yy)*2/5;if(Math.Abs(xx)>size-cut||Hash(i,5)%7==0)continue;
                    var stone=materialPixels[((y+yy+material.height)%material.height)*material.width+(x+xx+material.width)%material.width];
                    float shade=yy>size/7?.43f:.83f;p.Set(x+xx,y+yy,C((int)(stone.r*shade),(int)(stone.g*shade),(int)(stone.b*shade)));
                }
            }
            return Raster(p,"sanctum_terrain",Vector2.zero);
        }
    }
}


