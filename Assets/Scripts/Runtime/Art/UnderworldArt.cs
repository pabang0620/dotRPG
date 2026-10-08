using System;
using System.IO;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Original cave landmarks on independent terrain, collision and occlusion layers.</summary>
    public static partial class UnderworldArt
    {
        static readonly Dictionary<string,Sprite> floors=new Dictionary<string,Sprite>();
        static readonly Dictionary<int,Sprite> props=new Dictionary<int,Sprite>();
        static Texture2D atlas;
        static Sprite passage;
        static int Hash(int x,int y)=>unchecked((x*73856093^y*19349663)&int.MaxValue);
        public static Sprite Prop(int index)
        {
            if(props.TryGetValue(index,out var cached))return cached;
            if(atlas==null)
            {
                string file=Path.Combine(Application.streamingAssetsPath,"Underworld/landmarks.png");
                if(!File.Exists(file)){Debug.LogError("Missing underworld landmark atlas: "+file);return null;}
                atlas=new Texture2D(2,2,TextureFormat.RGBA32,false){name="Underworld original landmarks"};
                ImageConversion.LoadImage(atlas,File.ReadAllBytes(file));atlas.filterMode=FilterMode.Point;atlas.wrapMode=TextureWrapMode.Clamp;
            }
            // Measured individual bounds retain the full generated silhouettes; source has real alpha.
            RectInt[] regions={new RectInt(0,0,553,545),new RectInt(554,0,448,525),new RectInt(1002,0,534,539),
                new RectInt(0,545,533,479),new RectInt(534,525,468,499),new RectInt(1002,539,534,485)};
            var box=regions[index];int l=box.xMax,b=atlas.height,r=box.x,t=0;
            var pixels=atlas.GetPixels32();
            for(int y=box.y;y<box.yMax;y++)for(int x=box.x;x<box.xMax;x++)
            {
                int yy=atlas.height-1-y;
                if(pixels[yy*atlas.width+x].a<128)continue;
                l=Math.Min(l,x);r=Math.Max(r,x);b=Math.Min(b,yy);t=Math.Max(t,yy);
            }
            if(r<=l)return null;
            var sprite=Sprite.Create(atlas,new Rect(l,b,r-l+1,t-b+1),new Vector2(.5f,0),32,0,SpriteMeshType.FullRect);
            sprite.name="underworld_landmark_"+index;return props[index]=sprite;
        }
        static Sprite Make(Color32[] pixels,int w,int h,string name,Vector2 pivot)
        {
            var texture=new Texture2D(w,h,TextureFormat.RGBA32,false){name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            texture.SetPixels32(pixels);texture.Apply();return Sprite.Create(texture,new Rect(0,0,w,h),pivot,32,0,SpriteMeshType.FullRect);
        }
        static SpriteRenderer Put(Transform parent,string name,Sprite sprite,Vector2 foot,float width,int order)
        {
            if(sprite==null)return null;
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.position=foot;
            var sr=go.AddComponent<SpriteRenderer>();sr.sprite=sprite;sr.sortingOrder=order;
            go.transform.localScale=Vector3.one*(width/sprite.bounds.size.x);return sr;
        }
        static Sprite Terrain(string id,char[,] cells,int w,int h)
        {
            if(floors.TryGetValue(id,out var sprite))return sprite;
            bool town=id=="undergate";int variant=id=="hollow_roots"?1:id=="hollow_fungal"?2:id=="hollow_depths"?3:0;
            var pixels=CanyonTerrain.Paint(cells,w,h,out int pw,out int ph,null,town?-1:variant);
            var distance=new int[w,h];
            for(int cy=0;cy<h;cy++)for(int cx=0;cx<w;cx++)
            {
                distance[cx,cy]=5;
                for(int dy=-4;dy<=4;dy++)for(int dx=-4;dx<=4;dx++)
                    if(cx+dx>=0&&cy+dy>=0&&cx+dx<w&&cy+dy<h&&cells[cx+dx,cy+dy]!='W')
                        distance[cx,cy]=Math.Min(distance[cx,cy],Math.Max(Math.Abs(dx),Math.Abs(dy)));
            }
            for(int y=0;y<ph;y++)for(int x=0;x<pw;x++)
            {
                int cx=x/32,cy=y/32;char c=cells[cx,cy];var p=pixels[y*pw+x];
                float brightness;
                if(town)brightness=c=='W'?.62f:.87f;
                else
                {
                    // Open floors stay readable; rock masses recede behind their layered lit lip.
                    int near=distance[cx,cy];
                    brightness=c=='W'?Mathf.Lerp(.56f,.16f,near/5f):.70f;
                    if(c=='~')brightness=.62f;
                }
                float r=town?.86f:variant==2?.68f:variant==3?.59f:1;
                float g=town?1:variant==2?1:variant==3?.87f:variant==1?1:.92f;
                float b=town?.82f:variant==2?1.29f:variant==3?1.39f:variant==1?.8f:.73f;
                pixels[y*pw+x]=new Color32((byte)Mathf.Clamp(p.r*brightness*r,0,255),(byte)Mathf.Clamp(p.g*brightness*g,0,255),(byte)Mathf.Clamp(p.b*brightness*b,0,255),255);
            }
            return floors[id]=Make(pixels,pw,ph,"Underworld terrain "+id,Vector2.zero);
        }
        public static void Build(Transform root,string id,char[,] cells,int w,int h,UnderworldContour contour=null)
        {
            if(id!="undergate"&&TryBuildComposition(root,id,cells,w,h,contour))return;
            bool town=id=="undergate";var floor=town?Terrain(id,cells,w,h):CaveTerrain(id,cells,w,h,contour);
            Put(root,"Cave floor and stratified rock",floor,Vector2.zero,w,-30000);
            if(town)
            {
                var tree=Put(root,"Rootwell descent tree",Prop(0),new Vector2(26.5f,24.7f),10,YSort.OrderFor(24.7f));
                if(tree!=null)TreeFade.Attach(tree.gameObject);
                // Split buttress collisions leave the staircase centre physically open.
                foreach(float xx in new[]{22.9f,30.1f})Block(root,new Vector2(xx,27),new Vector2(2,3.5f));
                Block(root,new Vector2(26.5f,30.3f),new Vector2(5.2f,4));
                for(int y=3;y<h-2;y+=5)for(int x=3;x<w-2;x+=5)
                {
                    if(cells[x,y]!='.'||!(x<6||x>w-7||y<5||y>h-6))continue;
                    var sr=Put(root,"Gateway woodland",Game.Art.Get(VillageNatureArt.Prefix+(Hash(x,y)%3==0?"willow":"oak_round")),new Vector2(x,y),3.2f,YSort.OrderFor(y));
                    if(sr!=null)TreeFade.Attach(sr.gameObject);
                    Block(root,new Vector2(x,y+.3f),new Vector2(.6f,.6f));
                }
            }
            else
            {
                int variant=id=="hollow_roots"?1:id=="hollow_fungal"?2:id=="hollow_depths"?3:0;
                var focal=BuildDepthScenery(root,variant,cells,w,h,contour);
                var used=new List<Vector2>();var candidates=new List<Vector2>();
                for(int y=h-7;y>=3;y--)for(int x=3;x<w-3;x++)
                {
                    if(cells[x,y]!='W'||cells[x,y-1]=='W')continue;
                    bool blocked=true;for(int xx=-1;xx<=1;xx++)for(int yy=0;yy<=1;yy++)if(cells[x+xx,y+yy]!='W')blocked=false;
                    var pos=new Vector2(x+.5f,y+.12f);
                    if(blocked)candidates.Add(pos);
                }
                // Place landmarks across the whole cave, in asymmetrical clusters with breathing room.
                candidates.Sort((a,b)=>Hash((int)a.x+variant*13,(int)a.y).CompareTo(Hash((int)b.x+variant*13,(int)b.y)));
                foreach(var pos in candidates)
                {
                    if(used.Count>=4||Vector2.Distance(focal,pos)<8||NearInteriorMasonry(variant,pos)||used.Exists(p=>Vector2.Distance(p,pos)<9))continue;
                    int n=used.Count,kind=variant==2?(n%3==0?3:1):variant==1?(n%3==0?1:4):variant==3?(n%3==0?4:3):4;
                    float size=n==0?3.4f:n%3==0?1.8f:2.5f;
                    var sr=Put(root,"Cave grove landmark "+kind,Prop(kind),pos,size,YSort.OrderFor(pos.y));
                    if(sr!=null){TreeFade.Attach(sr.gameObject);sr.color=new Color(.72f,.84f,.80f);}
                    used.Add(pos);
                    // Smaller neighbours make a grove instead of a row of identical isolated stamps.
                    for(int nudge=-1;nudge<=1;nudge+=2)
                    {
                        int xx=(int)pos.x+nudge*2,yy=(int)pos.y;
                        if(xx<1||xx>=w-1||cells[xx,yy]!='W')continue;
                        var small=Put(root,"Grove companion",Prop(kind==1?1:3),new Vector2(xx+.5f,yy+.2f),.9f+(n%2)*.45f,YSort.OrderFor(yy+.2f));
                        if(small!=null)small.color=new Color(.65f,.79f,.76f);
                    }
                }
                // Small shoulder accents are clustered by biome and do not form a continuous decorative fence.
                int accents=0;
                for(int y=2;y<h-2;y++)for(int x=2;x<w-2;x++)
                    if(cells[x,y]=='W'&&cells[x,y-1]!='W'&&Vector2.Distance(focal,new Vector2(x,y))>5&&!NearInteriorMasonry(variant,new Vector2(x,y))&&Hash(x/4,y/3)%4==0&&Hash(x,y)%3==0&&accents++<12)
                        Put(root,"Mineral shoulder",Prop(variant==2?1:3),new Vector2(x+.5f,y+.1f),.55f,YSort.OrderFor(y+.1f));
                CaveGroundDetails(root,contour,variant,w,h);
                root.gameObject.AddComponent<UnderworldAmbience>().Setup(used,variant);
            }
            var water=new GameObject("Cave spring ripples");water.transform.SetParent(root,false);water.AddComponent<SanctumWater>().Setup(cells,w,h,false);
        }
        static void Block(Transform root,Vector2 at,Vector2 size)
        {var go=new GameObject("Root or tree footprint");go.transform.SetParent(root,false);go.transform.position=at;go.AddComponent<BoxCollider2D>().size=size;}
        public static void Passage(Transform root,Vector2 at,string destination,bool down,bool plaza=false)
        {
            if(passage==null)
            {
                var p=new PixelCanvas(80,56);var ink=PixelCanvas.Hex("243b37");var stone=PixelCanvas.Hex("7b8970");var edge=PixelCanvas.Hex("cbd1a6");
                p.Ellipse(40,41,35,12,ink);p.Ellipse(40,38,32,10,stone);p.Ellipse(40,38,25,7,PixelCanvas.Hex("31534a"));
                for(int x=18;x<65;x+=12){p.Line(x,32,x+3,45,edge);}
                foreach(int x in new[]{7,66}){p.Rect(x,13,7,27,ink);p.Rect(x+1,12,4,26,stone);p.Rect(x-2,8,10,6,edge);p.Rect(x,9,6,3,PixelCanvas.Hex("8ce0cd"));}
                passage=Make(p.ToTexturePixels(),80,56,"World passage waystone",new Vector2(.5f,.28f));
            }
            if(plaza)Put(root,"Signed world passage",passage,at,2.5f,-27000);
            else Put(root,down?"Descent stair marker":"Passage arrow",Game.Art.Get(down?"arrow_up":at.x<2?"arrow_left":at.x>48?"arrow_right":at.y<2?"arrow_down":"arrow_up"),at,.7f,-26000);
            var label=new GameObject("Destination "+destination);label.transform.SetParent(root,false);label.transform.position=at+Vector2.up*(plaza?1.1f:.9f);
            var tm=label.AddComponent<TextMesh>();tm.text=(down?"지하 1층 · ":"")+destination;tm.fontSize=32;tm.characterSize=.08f;tm.anchor=TextAnchor.LowerCenter;tm.color=new Color(.82f,.92f,.79f);
            // Use the same Korean font as the UI; labels remain below combat feedback.
            tm.font=UIFont.Get();
            var renderer=label.GetComponent<MeshRenderer>();renderer.sharedMaterial=tm.font.material;renderer.sortingOrder=19000;
        }
    }
    public sealed class UnderworldAmbience : MonoBehaviour
    {
        readonly List<(SpriteRenderer light,Vector2 at,float phase)> motes=new List<(SpriteRenderer,Vector2,float)>();
        public void Setup(List<Vector2> sites,int variant)
        {
            for(int i=0;i<sites.Count;i++)
            {
                var go=new GameObject("Local cave glow");go.transform.SetParent(transform,false);go.transform.position=sites[i]+Vector2.down*.25f;
                go.transform.localScale=new Vector3(3.3f,1.7f,1);var sr=go.AddComponent<SpriteRenderer>();sr.sprite=Game.Art.Get("fx_glow");sr.sortingOrder=-27800;
                sr.color=variant==0?new Color(.9f,.57f,.22f,.10f):new Color(.24f,.75f,.64f,.12f);
                motes.Add((sr,sites[i],i*2.17f));
            }
        }
        void Update()
        {
            foreach(var m in motes){var c=m.light.color;c.a=.08f+.025f*Mathf.Sin(Time.time*.7f+m.phase);m.light.color=c;}
        }
    }
}
