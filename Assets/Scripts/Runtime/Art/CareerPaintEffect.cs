using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Original RGBA art, loaded once. Cells stay at native resolution; no bilinear enlargement.</summary>
    public static class CareerPaintArt
    {
        static readonly Dictionary<Career,Sprite[]> sprites=new Dictionary<Career,Sprite[]>();
        static readonly Dictionary<Career,Color32[]> pixels=new Dictionary<Career,Color32[]>();
        public static Sprite Cell(Career career,int index)
        {
            if(!sprites.TryGetValue(career,out var sheet)){
                sheet=new Sprite[4];sprites[career]=sheet;
                string file=Path.Combine(Application.streamingAssetsPath,"CareerReborn",career+".png");
                if(!File.Exists(file)){Debug.LogWarning("Career artwork missing: "+file);return null;}
                var texture=new Texture2D(2,2,TextureFormat.RGBA32,false){name="CareerReborn_"+career,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
                if(!ImageConversion.LoadImage(texture,File.ReadAllBytes(file),false)){UnityEngine.Object.Destroy(texture);return null;}
                int w=texture.width/2,h=texture.height/2;
                for(int i=0;i<4;i++)sheet[i]=Sprite.Create(texture,new Rect(i%2*w,(1-i/2)*h,w,h),Vector2.one*.5f,w,0,SpriteMeshType.FullRect);
            }
            return sheet[Mathf.Clamp(index,0,3)];
        }
        public static int Motif(CareerSkill s)
        {
            if(s.career==Career.Fighter)return s.effect=="focus"||s.kind==CareerSkillKind.Passive?2:s.effect=="execute"||s.effect=="five"||s.effect=="rush"?1:s.effect=="flurry"?3:0;
            if(s.career==Career.Guardian)return s.effect=="ward"||s.effect=="citadel"?2:s.effect=="shield"?1:s.effect=="bash"||s.effect=="counter"?3:0;
            if(s.career==Career.Arcanist)return s.effect=="fire"?0:s.effect=="ice"?1:s.effect=="storm"?2:3;
            return s.effect=="wings"||s.effect=="dawn"?0:s.effect=="heal"?1:s.effect=="hot"||s.effect=="cleanse"?2:3;
        }
        public static PixelCanvas Icon(CareerSkill s)
        {
            var sprite=Cell(s.career,0);if(sprite==null)return CareerArt.Icon(s);
            if(!pixels.TryGetValue(s.career,out var rgba)){rgba=sprite.texture.GetPixels32();pixels[s.career]=rgba;}
            var p=new PixelCanvas(64,64);Color32 color=CareerCatalog.Color(s.career);
            p.Rect(1,1,62,62,new Color32(13,17,29,255));p.Rect(3,3,58,58,PixelCanvas.Shade(color,.75f));p.Rect(5,5,54,54,new Color32(20,25,43,255));
            int primary=Motif(s);float angle=s.career==Career.Fighter?(s.index==1?-30:s.index==7?28:0):0;
            PaintIcon(p,s.career,primary,32,30,48,angle,rgba);
            // A second contextual motif differentiates the skill from other uses of the same material.
            if(s.index==1&&s.career==Career.Fighter)PaintIcon(p,s.career,0,33,30,38,135,rgba);
            else if(s.index==3&&s.career==Career.Fighter){PaintIcon(p,s.career,0,18,27,28,-30,rgba);PaintIcon(p,s.career,0,43,33,28,30,rgba);}
            else if(s.index==8){PaintIcon(p,s.career,s.career==Career.Arcanist?0:s.career==Career.Bishop?3:2,46,45,27,0,rgba);}
            else if(s.index==0||s.index==4||s.index==2||s.index==6)PaintIcon(p,s.career,(primary+(s.index==2?2:1))%4,45,44,23,0,rgba);
            // Tier pips and the awakening crown remain legible even at the skill tree's smaller scale.
            for(int i=0;i<(s.index==8?5:s.index%4+1);i++){int x=24+i*5-(s.index==8?3:0);p.Rect(x,56,3,3,color);p.Set(x,56,new Color32(255,248,221,255));}
            if(s.kind==CareerSkillKind.Passive){p.Rect(8,8,5,5,color);p.Set(10,7,color);}
            return p;
        }
        static void PaintIcon(PixelCanvas p,Career career,int cell,int cx,int cy,int size,float angle,Color32[] rgba)
        {
            var s=Cell(career,cell);var rect=s.rect;int width=s.texture.width;
            float a=angle*Mathf.Deg2Rad,co=Mathf.Cos(a),si=Mathf.Sin(a);
            for(int y=5;y<55;y++)for(int x=5;x<59;x++){
                float dx=(x-cx)/(float)size,dy=(y-cy)/(float)size;
                float u=dx*co-dy*si+.5f,v=dx*si+dy*co+.5f;if(u<0||u>=1||v<0||v>=1)continue;
                var fg=rgba[((int)rect.y+(int)((1-v)*(rect.height-1)))*width+(int)rect.x+(int)(u*(rect.width-1))];
                if(fg.a==0)continue;var bg=p.Pixels[y*p.Width+x];float alpha=fg.a/255f;
                p.Set(x,y,new Color32((byte)Mathf.Lerp(bg.r,fg.r,alpha),(byte)Mathf.Lerp(bg.g,fg.g,alpha),(byte)Mathf.Lerp(bg.b,fg.b,alpha),255));
            }
        }
    }
    /// <summary>Bounded layered choreography, independent of combat RNG and game stats.</summary>
    public sealed class CareerPaintEffect : MonoBehaviour
    {
        sealed class Part
        {
            public SpriteRenderer renderer;public Vector2 offset,travel,size;public float angle,spin,start,end=1,alpha=1,grow=.3f;public bool ground;
        }
        readonly List<Part> parts=new List<Part>(8);
        PlayerController owner;string map;float age,life;bool bound,follow;int mode;
        public static int ActiveCount {get;private set;}
        public static int PeakCount {get;private set;}
        public static bool Available(CareerSkill s)=>CareerPaintArt.Cell(s.career,0)!=null;
        static CareerPaintEffect New(CareerSkill s,Vector2 at,float life,PlayerController source)
        {
            if(ActiveCount>=80||!Available(s))return null;
            var go=new GameObject("Reborn "+s.id);if(Fx.Root!=null)go.transform.SetParent(Fx.Root,false);go.transform.position=at;
            var fx=go.AddComponent<CareerPaintEffect>();fx.owner=source;fx.bound=source!=null;fx.map=Game.Session.MapId;fx.life=Mathf.Max(.06f,life);
            ActiveCount++;PeakCount=Mathf.Max(PeakCount,ActiveCount);return fx;
        }
        Part Add(Career c,int cell,Vector2 size,Vector2 offset,float angle=0,bool ground=false,float alpha=1,float start=0,float end=1)
        {
            var go=new GameObject("Painted part");go.transform.SetParent(transform,false);var r=go.AddComponent<SpriteRenderer>();r.sprite=CareerPaintArt.Cell(c,cell);
            r.sortingOrder=ground?SkillFx.GroundOrder+23:SkillFx.At(transform.position.y,85+parts.Count);
            var p=new Part{renderer=r,size=size,offset=offset,angle=angle,ground=ground,alpha=alpha,start=start,end=end};parts.Add(p);return p;
        }
        static float Angle(Vector2 direction)=>Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg;
        public static bool Spawn(CareerSkill s,Vector2 at,float radius,Vector2 direction,float life,int phase,PlayerController source)
        {
            if(!Available(s))return false;
            var fx=New(s,at,life,source);if(fx==null)return true;
            var c=s.career;float a=Angle(direction),r=Mathf.Max(.45f,radius);
            if(phase==1){
                fx.follow=true;int seal=c==Career.Arcanist?3:2;
                var p=fx.Add(c,seal,new Vector2(1.8f,1.15f),new Vector2(0,-.25f),0,true,.7f);p.spin=c==Career.Guardian?0:35;p.grow=.75f;
                if(s.kind==CareerSkillKind.Awakening)fx.Add(c,c==Career.Bishop?0:c==Career.Guardian?0:seal,new Vector2(2.2f,2.2f),Vector2.up*.55f,0,false,.5f).grow=.7f;
            }
            else if(phase==2){
                int motif=c==Career.Fighter||c==Career.Guardian?3:c==Career.Arcanist?CareerPaintArt.Motif(s):3;
                var p=fx.Add(c,motif,new Vector2(1.05f,1.05f),Vector2.zero,c==Career.Fighter?a:0,false,.9f);p.grow=.55f;p.spin=c==Career.Fighter?18:0;
                if(c==Career.Bishop)p.travel=Vector2.up*.32f;
            }
            else if(c==Career.Fighter){
                if(s.effect=="focus"){
                    fx.Add(c,2,new Vector2(2.4f,1.8f),new Vector2(0,-.15f),0,true,.7f).spin=45;
                }else{
                    // Crescent's convex cutting edge faces left in the source atlas.
                    var p=fx.Add(c,0,new Vector2(r*2,r*2),Vector2.zero,a+180,false,1);p.spin=phase==3?80:25;p.grow=.2f;
                    var echo=fx.Add(c,0,new Vector2(r*1.8f,r*1.8f),-direction*.12f,a+155,false,.28f,.12f);echo.spin=90;
                }
            }
            else if(c==Career.Guardian){
                if(s.effect=="bash"||s.effect=="counter"){
                    fx.Add(c,3,new Vector2(r*2,r*1.6f),Vector2.zero,a,false,.9f).grow=.7f;
                    fx.Add(c,0,new Vector2(1.1f,1.5f),-direction*.25f,0,false,.9f).travel=direction*.25f;
                }else if(s.effect=="taunt"){
                    fx.Add(c,2,new Vector2(r*2.4f,r*2),Vector2.zero,0,true,.65f).grow=.9f;
                    fx.Add(c,0,new Vector2(1.2f,1.6f),Vector2.up*.55f,0,false,.8f);
                }else{
                    fx.Add(c,2,new Vector2(r*2,r*1.65f),Vector2.down*.15f,0,true,.85f).grow=.5f;
                    fx.Add(c,1,new Vector2(r*2,r*1.85f),Vector2.up*r*.25f,0,false,.45f).grow=.65f;
                    if(s.effect=="guard")fx.Add(c,0,new Vector2(1.2f,1.65f),direction*.4f,0,false,.8f);
                    if(s.effect=="citadel")for(int i=-1;i<=1;i++)fx.Add(c,0,new Vector2(.85f,1.3f),new Vector2(i*r*.62f,r*.42f),0,false,.7f,.08f*i+.15f).travel=Vector2.up*.25f;
                }
            }
            else if(c==Career.Arcanist){
                fx.Add(c,3,new Vector2(r*2,r*1.6f),Vector2.down*.25f,0,true,.7f).spin=12;
                if(s.effect=="fire"){
                    for(int i=0;i<3;i++){float q=i*2.094f;var p=fx.Add(c,0,new Vector2(r*.9f,r*.9f),new Vector2(Mathf.Cos(q),Mathf.Sin(q))*.15f,135+i*120,false,.85f,i*.035f);p.travel=new Vector2(Mathf.Cos(q),Mathf.Sin(q))*r*.6f;p.grow=.65f;}
                }else if(s.effect=="ice"){
                    var p=fx.Add(c,1,new Vector2(r*1.8f,r*1.8f),Vector2.up*r*.4f,0,false,.95f);p.grow=.85f;
                }else if(s.effect=="storm"){
                    var p=fx.Add(c,2,new Vector2(r*1.5f,r*2.8f),Vector2.up*r*.65f,0,false,.95f);p.grow=.15f;
                }else{
                    var p=fx.Add(c,3,new Vector2(r*1.45f,r*1.45f),Vector2.up*.1f,0,false,.7f);p.spin=-55;
                    if(s.effect=="eclipse")fx.Add(c,2,new Vector2(r*1.5f,r*2.5f),Vector2.up*r*.65f,0,false,.7f,.2f);
                }
            }
            else{
                fx.Add(c,2,new Vector2(r*2,r*1.6f),Vector2.down*.2f,0,true,.65f).grow=.4f;
                if(s.effect=="wings"||s.effect=="dawn"){
                    var wings=fx.Add(c,0,new Vector2(r*2.4f,r*1.9f),Vector2.up*r*.6f,0,false,.85f);wings.grow=.75f;wings.travel=Vector2.up*.2f;
                    fx.Add(c,3,new Vector2(r*.8f,r*.8f),Vector2.up*r*.9f,0,false,.8f,.15f).grow=.6f;
                }else if(s.effect=="heal"){
                    var p=fx.Add(c,1,new Vector2(r*1.15f,r*1.8f),Vector2.up*r*.4f,0,false,.7f);p.travel=Vector2.up*.35f;
                }else if(s.effect=="bless"||s.effect=="cleanse"){
                    var p=fx.Add(c,3,new Vector2(r*1.5f,r*1.5f),Vector2.up*.5f,0,false,.85f);p.travel=Vector2.up*.3f;
                }
            }
            fx.Draw(0);return true;
        }
        public static CareerPaintEffect Wave(CareerSkill s,Vector2 at,float radius,Vector2 dir,float life,PlayerController p)
        {var fx=New(s,at,life,p);if(fx==null)return null;fx.mode=1;var part=fx.Add(s.career,0,new Vector2(radius*1.4f,radius*2),Vector2.zero,Angle(dir)+180);part.grow=0;fx.Draw(0);return fx;}
        public static CareerPaintEffect Star(CareerSkill s,Vector2 at,PlayerController p)
        {var fx=New(s,at,.65f,p);if(fx==null)return null;fx.mode=1;var part=fx.Add(s.career,3,new Vector2(.7f,.7f),Vector2.zero);part.spin=160;part.grow=0;fx.Draw(0);return fx;}
        public static void Beam(CareerSkill s,Vector2 from,Vector2 dir,float length,float width,float life,PlayerController p)
        {
            var fx=New(s,from+dir*length*.5f,life,p);if(fx==null)return;
            if(s.career==Career.Bishop){fx.Add(Career.Bishop,1,new Vector2(width*2,length),Vector2.zero,Angle(dir)-90,false,.9f).grow=.1f;}
            else{
                // A continuous blade fissure, exactly aligned to the actual rectangular hit corridor.
                var line=fx.Add(Career.Fighter,1,new Vector2(length/ .94f,width*3.5f),Vector2.zero,Angle(dir),false,.95f);line.grow=.03f;
                if(s.effect=="five")for(int i=0;i<4;i++){
                    var flash=fx.Add(Career.Fighter,3,new Vector2(width*1.15f,width*1.15f),dir*(i-1.5f)*length*.22f,Angle(dir),false,.75f,i*.025f,.75f+i*.05f);flash.grow=.7f;
                }
            }
            fx.Draw(0);
        }
        public static void Ground(CareerSkill s,Vector2 at,float radius,float life,PlayerController p)
        {var fx=New(s,at,life,p);if(fx==null)return;fx.mode=2;var a=fx.Add(s.career,s.career==Career.Arcanist?3:2,new Vector2(radius*2,radius*1.8f),Vector2.zero,0,true,.45f);a.grow=.25f;a.spin=s.career==Career.Arcanist?18:0;fx.Draw(0);}
        public static void Meteor(CareerSkill s,Vector2 target,float radius,float life,PlayerController p)
        {var fx=New(s,target,life,p);if(fx==null)return;fx.mode=3;var a=fx.Add(Career.Arcanist,0,new Vector2(radius*1.5f,radius*1.5f),new Vector2(-1.6f,3.3f));a.travel=new Vector2(1.6f,-3.3f);a.grow=.15f;fx.Draw(0);}
        public static void Trail(CareerSkill s,Vector2 at,Vector2 dir,PlayerController p)
        {if((Time.frameCount%3)!=0)return;var fx=New(s,at,.18f,p);if(fx==null)return;fx.Add(s.career,s.career==Career.Guardian?0:1,new Vector2(.65f,1.15f),Vector2.zero,s.career==Career.Guardian?0:Angle(dir),false,.35f);fx.Draw(0);}
        void Update()
        {
            if(Game.Session.MapId!=map||(bound&&(owner==null||owner.IsDead||!owner.gameObject.activeInHierarchy))){Destroy(gameObject);return;}
            age+=Time.deltaTime;if(age>=life){Destroy(gameObject);return;}
            if(follow&&owner!=null)transform.position=owner.Center;Draw(age/life);
        }
        void Draw(float t)
        {
            foreach(var p in parts){
                float u=(t-p.start)/(p.end-p.start);p.renderer.enabled=u>=0&&u<=1;if(!p.renderer.enabled)continue;
                float appear=Mathf.Clamp01(u*9+.15f),fade=mode==3?1:mode==2?Mathf.Min(1,(1-u)*life*4):mode==1?Mathf.Min(1,(1-u)*6):Mathf.Clamp01((1-u)*3);
                float ease=1-Mathf.Pow(1-u,3);float expansion=1-p.grow+p.grow*ease;
                var pos=p.offset+p.travel*(mode==3?u*u:ease);pos.x=Mathf.Round(pos.x*96)/96;pos.y=Mathf.Round(pos.y*96)/96;
                p.renderer.transform.localPosition=pos;p.renderer.transform.localScale=new Vector3(p.size.x*expansion,p.size.y*expansion,1);
                p.renderer.transform.localRotation=Quaternion.Euler(0,0,p.angle+p.spin*u);
                p.renderer.color=new Color(1,1,1,p.alpha*appear*fade);
                if(!p.ground)p.renderer.sortingOrder=SkillFx.At(transform.position.y,85);
            }
        }
        void OnDestroy(){ActiveCount=Mathf.Max(0,ActiveCount-1);}
    }
}
