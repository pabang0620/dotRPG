using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public static partial class UnderworldArt
    {
        static readonly Dictionary<int,Sprite> panoramas=new Dictionary<int,Sprite>();
        static readonly Dictionary<int,Sprite> depthLandmarks=new Dictionary<int,Sprite>();
        static Texture2D depthAtlas;
        static Texture2D DepthTexture(string filename)
        {
            var path=Path.Combine(Application.streamingAssetsPath,"Underworld",filename);
            if(!File.Exists(path)){Debug.LogError("Missing cave scenery: "+path);return null;}
            var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name=filename};
            ImageConversion.LoadImage(texture,File.ReadAllBytes(path));
            texture.filterMode=FilterMode.Point;texture.wrapMode=TextureWrapMode.Clamp;
            return texture;
        }
        static Sprite Panorama(int variant)
        {
            if(panoramas.TryGetValue(variant,out var sprite))return sprite;
            string[] files={"descent","roots","fungal","depths"};
            var texture=DepthTexture("backdrop-"+files[variant]+".png");if(texture==null)return null;
            sprite=Sprite.Create(texture,new Rect(0,0,texture.width,texture.height),Vector2.zero,32,0,SpriteMeshType.FullRect);
            sprite.name="Cavern panorama "+files[variant];return panoramas[variant]=sprite;
        }
        static Sprite DepthLandmark(int variant)
        {
            if(depthLandmarks.TryGetValue(variant,out var sprite))return sprite;
            if(depthAtlas==null)depthAtlas=DepthTexture("depth-landmarks.png");
            if(depthAtlas==null)return null;
            int cw=depthAtlas.width/2,ch=depthAtlas.height/2;
            int ox=(variant%2)*cw,oy=(1-variant/2)*ch;
            var pixels=depthAtlas.GetPixels32();int l=ox+cw,b=oy+ch,r=ox,t=oy;
            for(int y=oy;y<oy+ch;y++)for(int x=ox;x<ox+cw;x++)
            {
                if(pixels[y*depthAtlas.width+x].a<128)continue;
                l=Math.Min(l,x);r=Math.Max(r,x);b=Math.Min(b,y);t=Math.Max(t,y);
            }
            if(r<=l)return null;
            int sw=r-l+1,sh=t-b+1,pw=variant==3?240:224,ph=Mathf.RoundToInt(sh*pw/(float)sw);
            var raster=new Color32[pw*ph];
            for(int y=0;y<ph;y++)for(int x=0;x<pw;x++)
            {
                int sx=l+Mathf.Min(sw-1,(int)((x+.5f)*sw/pw)),sy=b+Mathf.Min(sh-1,(int)((y+.5f)*sh/ph));
                var c=pixels[sy*depthAtlas.width+sx];c.a=c.a<128?(byte)0:(byte)255;raster[y*pw+x]=c;
            }
            sprite=Make(raster,pw,ph,"Ground-scale monument "+variant,new Vector2(.5f,0));
            sprite.name="Authored cave monument "+variant;return depthLandmarks[variant]=sprite;
        }
        static Vector2 DepthSite(char[,] cells,int w,int h,UnderworldContour contour,Vector2 desired,float width,float height)
        {
            Vector2 best=desired;float score=float.MaxValue;
            // Wide, already blocked shelves only. The artwork never creates an invisible obstacle.
            for(int y=4;y<h-Mathf.CeilToInt(height)-1;y++)for(int x=5;x<w-5;x++)
            {
                if(cells[x,y]!='W'||!contour.Land(x+.5f,y-1))continue;
                bool safe=true;
                for(float dy=.15f;dy<2;dy+=.5f)for(float dx=-width*.46f;dx<=width*.46f;dx+=.4f)
                    if(contour.Land(x+.5f+dx,y+dy)||contour.WaterDistance(x+.5f+dx,y+dy)>=0)safe=false;
                if(!safe)continue;
                var at=new Vector2(x+.5f,y+.16f);float distance=(at-desired).sqrMagnitude;
                if(distance<score){best=at;score=distance;}
            }
            return best;
        }
        static Vector2 BuildDepthScenery(Transform root,int variant,char[,] cells,int w,int h,UnderworldContour contour)
        {
            var scenery=new GameObject("Underworld scenery");scenery.transform.SetParent(root,false);
            var backdrop=Put(scenery.transform,"Continuous distant cavern panorama",Panorama(variant),Vector2.zero,w,-31000);
            if(backdrop!=null)
            {
                // One coherent image per map; never tiled. Near terrain above this retains all collisions.
                var size=backdrop.sprite.bounds.size;
                backdrop.transform.localScale=new Vector3(w/size.x,h/size.y,1);
                backdrop.color=new Color(.66f,.69f,.72f,1);
            }
            Vector2[] targets={new Vector2(19,33),new Vector2(23,29),new Vector2(27,31),new Vector2(34,36)};
            var art=DepthLandmark(variant);float width=variant==3?7.5f:7;
            float height=art!=null?width*art.bounds.size.y/art.bounds.size.x:7;
            var site=DepthSite(cells,w,h,contour,targets[variant],width,height);
            var main=Put(scenery.transform,"Primary landmark",art,site,width,YSort.OrderFor(site.y));
            if(main!=null)
            {
                main.color=variant==0?new Color(.83f,.80f,.72f):variant==1?new Color(.72f,.81f,.72f):
                    variant==2?new Color(.70f,.88f,.89f):new Color(.73f,.79f,.92f);
                TreeFade.Attach(main.gameObject);
            }
            BuildMonumentFooting(scenery.transform,variant,site,contour,width);
            BuildStructuralReturns(scenery.transform,variant,cells,contour,w,h,site);
            scenery.AddComponent<UnderworldDepthAmbience>().Setup(variant,site,contour,w,h);
            return site;
        }
    }

    /// <summary>Fixed small pool of dust/spores; owned by the map and destroyed with it.</summary>
    public sealed class UnderworldDepthAmbience : MonoBehaviour
    {
        struct Mote { public SpriteRenderer sprite; public Vector2 origin; public float phase; }
        readonly List<Mote> motes=new List<Mote>();
        static Sprite mote;
        public void Setup(int variant,Vector2 landmark,UnderworldContour contour,int w,int h)
        {
            if(mote==null)
            {
                var texture=new Texture2D(3,3,TextureFormat.RGBA32,false){name="Cave dust pixel",filterMode=FilterMode.Point};
                var pixels=new Color32[9];pixels[4]=new Color32(235,245,227,255);
                texture.SetPixels32(pixels);texture.Apply();
                mote=Sprite.Create(texture,new Rect(0,0,3,3),new Vector2(.5f,.5f),32);
            }
            for(int i=0;i<24;i++)
            {
                var at=new Vector2(3+(i*17.31f)%(w-6),3+(i*11.71f)%(h-6));
                if(i<8)at=landmark+new Vector2((i%4-1.5f)*1.1f,1+i/4*1.6f);
                var go=new GameObject("Slow cave mote");go.transform.SetParent(transform,false);go.transform.position=at;
                var sr=go.AddComponent<SpriteRenderer>();sr.sprite=mote;sr.sortingOrder=-27010;
                sr.color=variant<2?new Color(.86f,.70f,.38f,.3f):new Color(.36f,.80f,.93f,.3f);
                motes.Add(new Mote{sprite=sr,origin=at,phase=i*1.87f});
            }
        }
        void Update()
        {
            float clock=Time.time*.24f;
            foreach(var m in motes)
            {
                var p=m.origin+new Vector2(Mathf.Sin(clock+m.phase)*.16f,Mathf.Sin(clock*.7f+m.phase)*.22f);
                m.sprite.transform.position=new Vector3(Mathf.Round(p.x*32)/32,Mathf.Round(p.y*32)/32,0);
                var color=m.sprite.color;color.a=.16f+.12f*(.5f+.5f*Mathf.Sin(clock+m.phase));m.sprite.color=color;
            }
        }
    }
}
