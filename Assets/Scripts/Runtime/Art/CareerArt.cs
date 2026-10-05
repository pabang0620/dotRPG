using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Authored pixel assets. The same integer raster source exports PNGs and renders in old preview players.</summary>
    public static class CareerArt
    {
        static readonly Dictionary<string,Sprite> cache=new Dictionary<string,Sprite>();
        static Color32 Ink=new Color32(18,22,39,255), White=new Color32(255,245,216,255), Steel=new Color32(182,209,225,255);
        public static Sprite Get(string key)
        {
            if(key==null || !key.StartsWith("career_")) return null;
            if(cache.TryGetValue(key,out var found)) return found;
            var s=CareerCatalog.Get(key.Substring(7)); if(s==null) return null;
            return cache[key]=CareerPaintArt.Cell(s.career,0)!=null?SpriteOf(CareerPaintArt.Icon(s),key,64):Resources.Load<Sprite>("Art/Careers/Icons/"+key) ?? SpriteOf(Icon(s),key,32);
        }
        public static Sprite SpriteOf(PixelCanvas p,string name,float ppu)
        {
            var t=new Texture2D(p.Width,p.Height,TextureFormat.RGBA32,false){name=name,filterMode=FilterMode.Point,wrapMode=TextureWrapMode.Clamp};
            t.SetPixels32(p.ToTexturePixels());t.Apply();
            return Sprite.Create(t,new Rect(0,0,p.Width,p.Height),Vector2.one*.5f,ppu);
        }
        static void Stroke(PixelCanvas p,int x,int y,int xx,int yy,Color32 c,int width=1)
        { for(int n=-width/2;n<=width/2;n++) p.Line(x+n,y,xx+n,yy,c); }
        static void Ring(PixelCanvas p,int x,int y,int r,Color32 c,int count=16)
        { for(int i=0;i<count;i++) {float a=i*Mathf.PI*2/count,b=(i+1)*Mathf.PI*2/count; p.Line(x+Mathf.RoundToInt(Mathf.Cos(a)*r),y+Mathf.RoundToInt(Mathf.Sin(a)*r),x+Mathf.RoundToInt(Mathf.Cos(b)*r),y+Mathf.RoundToInt(Mathf.Sin(b)*r),c);} }
        static void Spark(PixelCanvas p,int x,int y,Color32 c,int size=2) {p.HLine(x-size,x+size,y,c);p.VLine(x,y-size,y+size,c);p.Set(x+1,y+1,White);}
        static void Sword(PixelCanvas p,int x,int y,int len,Color32 c)
        { Stroke(p,x,y,x+len,y-len,Ink,5);Stroke(p,x,y,x+len,y-len,Steel,3);p.Line(x,y,x+len,y-len,White);p.Line(x-3,y-2,x+2,y+3,c);Stroke(p,x-4,y+4,x-1,y+1,c,3);p.Set(x-4,y+5,White); }
        static void Shield(PixelCanvas p,int x,int y,int size,Color32 c)
        {for(int i=0;i<size;i++){int w=i<size/2?size/2:Mathf.Max(0,size-i);p.HLine(x-w,x+w,y+i,Ink);if(w>1)p.HLine(x-w+1,x+w-1,y+i,PixelCanvas.Shade(c,.6f));}p.VLine(x,y+1,y+size-2,Steel);p.HLine(x-size/2+2,x+size/2-2,y+2,c);p.Set(x+1,y+3,White);}
        static void Wing(PixelCanvas p,int x,int y,int side,Color32 c)
        {for(int i=0;i<5;i++){int xx=x+side*(i+1)*2;Stroke(p,xx,y-i,xx+side*2,y+6-i,Ink,3);p.Line(xx,y-i,xx+side,y+4-i,c);} }
        static void Crystal(PixelCanvas p,int x,int y,int r,Color32 c)
        {for(int i=-r;i<=r;i++){int w=(r-Mathf.Abs(i))/2;p.HLine(x-w,x+w,y+i,Ink);if(w>0)p.HLine(x-w+1,x,y+i,c);if(w>1)p.HLine(x+1,x+w-1,y+i,PixelCanvas.Shade(c,.55f));}p.VLine(x,y-r+2,y+2,White);}
        public static PixelCanvas Icon(CareerSkill s)
        {
            var p=new PixelCanvas(32,32);Color32 c=CareerCatalog.Color(s.career), dark=PixelCanvas.Shade(c,.28f);
            // Icon plate is part of the artwork; lock/selection/cooldown remain external UI layers.
            p.Rect(2,2,28,28,Ink);p.Rect(3,3,26,26,dark);p.Rect(4,4,24,24,new Color32(28,31,48,255));
            for(int y=5;y<28;y+=3)for(int x=5;x<28;x+=3)if((x+y)%2==0)p.Set(x,y,PixelCanvas.Shade(c,.21f));
            int i=s.index;
            if(s.career==Career.Fighter)
            {
                if(i==0){Ring(p,16,16,10,c);Sword(p,11,21,11,c);Spark(p,8,10,c);}
                if(i==1){Sword(p,8,22,15,c);Stroke(p,9,8,24,23,c,3);Spark(p,16,15,White,3);}
                if(i==2){Stroke(p,5,23,18,10,c,3);p.Line(5,17,12,10,c);Sword(p,15,18,11,c);}
                if(i==3){for(int k=0;k<3;k++) {p.Line(5,18+k*4,22,5+k*4,c);p.Line(9,20+k*3,25,7+k*3,Steel);}}
                if(i==4){Crystal(p,16,15,10,c);p.HLine(7,25,16,White);Spark(p,16,16,White,4);}
                if(i==5){Shield(p,17,8,16,Steel);Stroke(p,7,25,24,7,c,3);p.Set(16,16,White);}
                if(i==6){Ring(p,16,16,9,c);Ring(p,16,16,5,Steel);Spark(p,16,16,White,4);for(int k=0;k<4;k++)p.Line(4+k*7,26,10+k*5,20,c);}
                if(i==7){Sword(p,8,24,18,c);for(int k=0;k<5;k++)p.Line(19,16,25+k%2,9+k*3,c);}
                if(i==8){Ring(p,16,16,11,c);for(int k=0;k<5;k++){float a=k*6.283f/5;p.Line(16,16,16+(int)(Mathf.Cos(a)*12),16+(int)(Mathf.Sin(a)*12),White);}Sword(p,12,20,11,c);}
            }
            else if(s.career==Career.Guardian)
            {
                if(i==0){Shield(p,16,6,20,c);Crystal(p,16,15,5,White);}
                if(i==1){p.Rect(5,23,22,3,Steel);Shield(p,16,7,17,c);p.VLine(6,11,22,c);p.VLine(26,11,22,c);}
                if(i==2){Shield(p,10,11,12,c);Shield(p,23,11,12,c);Ring(p,16,16,12,Steel,6);}
                if(i==3){Ring(p,16,17,12,c,6);p.Rect(9,13,14,11,Steel);p.Rect(11,17,10,7,dark);for(int x=8;x<26;x+=5)p.Rect(x,9,3,5,c);}
                if(i==4){Shield(p,13,8,16,c);p.Line(18,24,26,16,White);p.Line(26,16,20,16,c);p.VLine(26,16,22,c);}
                if(i==5){Shield(p,13,13,12,c);for(int r=4;r<13;r+=4){p.Line(17+r/2,17-r,20+r/2,17,c);p.Line(20+r/2,17,17+r/2,17+r,c);}}
                if(i==6){Shield(p,21,8,17,c);for(int y=10;y<25;y+=4)p.HLine(4,12,y,Steel);}
                if(i==7){Shield(p,11,11,14,c);Sword(p,17,24,11,c);Spark(p,24,9,White);}
                if(i==8){Ring(p,16,16,13,c,6);Shield(p,16,8,17,c);Wing(p,9,12,-1,Steel);Wing(p,23,12,1,Steel);}
            }
            else if(s.career==Career.Arcanist)
            {
                Ring(p,16,17,10,dark,8);
                if(i==0){Crystal(p,10,12,6,new Color32(255,153,57,255));Crystal(p,22,12,6,Steel);Spark(p,16,23,c,4);}
                if(i==1){Sword(p,10,24,13,c);for(int k=0;k<5;k++)Stroke(p,15+k*2,18,12+k*3,7+k%3,new Color32(255,136,58,255),3);Spark(p,18,12,White);}
                if(i==2){Crystal(p,16,14,11,new Color32(123,219,255,255));Crystal(p,7,21,5,c);Crystal(p,25,20,6,c);}
                if(i==3){Stroke(p,21,5,12,16,White,3);Stroke(p,12,16,20,16,c,3);Stroke(p,20,16,10,27,White,3);Spark(p,7,9,new Color32(255,223,102,255));}
                if(i==4){Ring(p,16,16,11,c);Ring(p,16,16,6,Steel);Crystal(p,16,16,5,White);p.Rect(25,11,3,5,White);}
                if(i==5){Ring(p,16,16,10,c);for(int k=0;k<3;k++){float a=k*2.09f;p.Circle(16+Mathf.Cos(a)*9,16+Mathf.Sin(a)*9,3,Steel);}Crystal(p,16,16,6,c);}
                if(i==6){Crystal(p,23,14,10,c);for(int k=0;k<3;k++)p.Line(4+k*4,23,9+k*4,8,PixelCanvas.Shade(c,.5f+k*.15f));}
                if(i==7){p.Line(18,4,12,12,White);p.Line(12,12,21,18,c);p.Line(21,18,13,28,White);for(int k=0;k<5;k++)Spark(p,7+k*4,9+k%3*7,c,1);}
                if(i==8){Ring(p,16,16,12,Steel);p.Circle(16,16,8,c);p.Circle(18,14,7,Ink);Crystal(p,7,12,5,new Color32(255,148,67,255));Crystal(p,25,12,5,Steel);Spark(p,16,26,White);}
            }
            else
            {
                if(i==0){Wing(p,15,10,-1,c);Wing(p,17,10,1,c);p.Circle(16,18,4,new Color32(152,215,138,255));Spark(p,16,17,White);}
                if(i==1){Wing(p,14,18,-1,c);Wing(p,18,18,1,c);p.Rect(14,8,4,14,White);p.Rect(9,12,14,4,White);}
                if(i==2){p.VLine(16,11,26,Steel);for(int k=0;k<3;k++){p.Ellipse(12,13+k*5,4,2,new Color32(153,211,142,255));p.Ellipse(20,10+k*5,4,2,c);}Spark(p,16,6,White);}
                if(i==3){p.Rect(11,12,10,9,c);p.Circle(16,12,5,c);p.HLine(8,24,23,White);p.Rect(14,24,4,2,Steel);Ring(p,16,15,12,dark);Spark(p,24,7,White);}
                if(i==4){p.Rect(12,14,8,7,c);p.HLine(8,24,12,White);p.VLine(16,21,25,White);p.HLine(11,21,26,c);p.Ellipse(16,8,7,2,c);}
                if(i==5){Stroke(p,7,25,25,7,c,3);p.Line(13,8,25,7,White);p.Line(25,7,24,19,White);Spark(p,15,16,White,3);}
                if(i==6){Wing(p,14,10,-1,White);Wing(p,18,10,1,White);Shield(p,16,15,11,c);}
                if(i==7){p.Ellipse(16,8,10,3,c);for(int x=8;x<28;x+=5){p.VLine(x,13,22,Steel);Spark(p,x,24,c,1);} }
                if(i==8){Wing(p,14,10,-1,White);Wing(p,18,10,1,White);Ring(p,16,15,7,c);p.Ellipse(16,26,12,2,c);Spark(p,16,16,White,4);}
            }
            return p;
        }
        public static PixelCanvas Effect(CareerSkill s,int frame) => CareerVfxArt.Effect(s,frame);
        public static Sprite DetailFrame(Career career,bool charge,int frame)
        {
            string path="Art/Careers/Reforged/Details/"+career+(charge?"_charge":"_hit");
            string key=path+"/"+frame;
            if(cache.TryGetValue(key,out var cached))return cached;
            var sheet=Resources.Load<Texture2D>(path);
            if(sheet!=null&&sheet.width==CareerRaster.Size*12&&sheet.height==CareerRaster.Size){sheet.filterMode=FilterMode.Point;return cache[key]=Sprite.Create(sheet,new Rect(frame*CareerRaster.Size,0,CareerRaster.Size,CareerRaster.Size),Vector2.one*.5f,CareerRaster.Ppu);}
            return cache[key]=SpriteOf(CareerVfxArt.Detail(career,charge,frame),key,CareerRaster.Ppu);
        }
        public static Sprite VisualFrame(string id,int frame)
        {
            string key="visual/"+id+"/"+frame;if(cache.TryGetValue(key,out var found))return found;
            var sheet=Resources.Load<Texture2D>("Art/Careers/Reforged/Presentation/"+id);
            if(sheet!=null&&sheet.width==CareerRaster.Size*12&&sheet.height==CareerRaster.Size){sheet.filterMode=FilterMode.Point;return cache[key]=Sprite.Create(sheet,new Rect(frame*CareerRaster.Size,0,CareerRaster.Size,CareerRaster.Size),Vector2.one*.5f,CareerRaster.Ppu);}
            return cache[key]=SpriteOf(CareerVfxArt.Visual(id,frame),key,CareerRaster.Ppu);
        }
        public static Sprite Frame(CareerSkill s,int i)
        {
            string key=s.id+"/"+i;if(cache.TryGetValue(key,out var sprite))return sprite;
            var sheet=Resources.Load<Texture2D>("Art/Careers/Reforged/Effects/"+s.id);
            if(sheet!=null&&sheet.width==CareerRaster.Size*12&&sheet.height==CareerRaster.Size){sheet.filterMode=FilterMode.Point;sprite=Sprite.Create(sheet,new Rect(i*CareerRaster.Size,0,CareerRaster.Size,CareerRaster.Size),Vector2.one*.5f,CareerRaster.Ppu);}
            else sprite=SpriteOf(Effect(s,i),key,CareerRaster.Ppu);
            return cache[key]=sprite;
        }
        public static Sprite PlaneFrame(CareerSkill s,int frame,bool back)
        {
            string id=s.id+(back?"_back":"_front"),key="plane/"+id+"/"+frame;
            if(cache.TryGetValue(key,out var sprite))return sprite;
            var sheet=Resources.Load<Texture2D>("Art/Careers/Reforged/Layered/"+id);
            if(sheet!=null&&sheet.width==CareerRaster.Size*12&&sheet.height==CareerRaster.Size){sheet.filterMode=FilterMode.Point;sprite=Sprite.Create(sheet,new Rect(frame*CareerRaster.Size,0,CareerRaster.Size,CareerRaster.Size),Vector2.one*.5f,CareerRaster.Ppu);}
            else sprite=SpriteOf(CareerVfxArt.Plane(Effect(s,frame),back),key,CareerRaster.Ppu);
            return cache[key]=sprite;
        }
    }
    /// <summary>Layered pixel sprite + additive edge + one bounded particle mesh; VFX never consume combat RNG.</summary>
    public sealed class CareerEffect : MonoBehaviour
    {
        CareerSkill skill; SpriteRenderer core,edge,rear; float age,life,radius; int phase,particles;
        Mesh mesh; Vector3[] vertices; Color[] colors; static int live;
        static Material particleMaterial;
        PlayerController source; string map; bool bound;
        public static int ActiveCount=>live+CareerPaintEffect.ActiveCount;
        public static void Play(CareerSkill s,Vector2 at,float radius,Vector2 direction,float life=.55f,PlayerController source=null)=>Spawn(s,at,radius,direction,life,0,source);
        public static void Charge(CareerSkill s,Vector2 at,float radius,Vector2 direction,float life,PlayerController source=null)=>Spawn(s,at,radius,direction,Mathf.Max(.15f,life),1,source);
        public static void Hit(CareerSkill s,Vector2 at,Vector2 direction,PlayerController source=null)=>Spawn(s,at,.46f,direction,.34f,2,source);
        public static void Cut(CareerSkill s,Vector2 at,float radius,Vector2 direction,int index,bool final,PlayerController source)
        {
            float angle=(index%2==0?-25:25)*Mathf.Deg2Rad;
            var d=new Vector2(direction.x*Mathf.Cos(angle)-direction.y*Mathf.Sin(angle),direction.x*Mathf.Sin(angle)+direction.y*Mathf.Cos(angle));
            Spawn(s,at,Mathf.Min(radius,1.85f),d,final?.32f:.15f,final?4:3,source);
        }
        static void Spawn(CareerSkill s,Vector2 at,float radius,Vector2 direction,float life,int phase,PlayerController source)
        {
            if(s!=null&&CareerPaintEffect.Spawn(s,at,radius,direction,life,phase,source))return;
            if(s==null||live>=96||(phase==2&&live>=64))return;
            var go=new GameObject("FX_"+s.id+(phase==1?"_charge":phase==2?"_hit":""));
            if(Fx.Root!=null)go.transform.SetParent(Fx.Root,false);
            var fx=go.AddComponent<CareerEffect>();live++;fx.skill=s;fx.life=life;fx.radius=Mathf.Max(.3f,radius);fx.phase=phase;fx.source=source;fx.bound=source!=null;fx.map=Game.Session.MapId;
            go.transform.position=at;go.transform.localScale=Vector3.one*fx.radius*(48f/43f);
            if(phase!=1&&(s.career==Career.Fighter||s.effect=="bash"||s.effect=="light"))go.transform.rotation=Quaternion.Euler(0,0,Mathf.Atan2(direction.y,direction.x)*Mathf.Rad2Deg);
            fx.core=go.AddComponent<SpriteRenderer>();fx.core.sortingOrder=SkillFx.At(at.y,90);
            if(phase==0&&s.career!=Career.Fighter&&s.effect!="bash"&&s.effect!="light"){
                fx.rear=new GameObject("Far side artwork").AddComponent<SpriteRenderer>();fx.rear.transform.SetParent(go.transform,false);fx.rear.sortingOrder=SkillFx.At(at.y,-10);
            }
            if(phase==1)fx.core.sortingOrder=SkillFx.At(at.y,-8);
            fx.edge=new GameObject("Pixel radiance").AddComponent<SpriteRenderer>();fx.edge.transform.SetParent(go.transform,false);fx.edge.transform.localScale=Vector3.one;fx.edge.sortingOrder=fx.core.sortingOrder-1;fx.edge.sharedMaterial=FxMaterials.Additive;
            fx.particles=phase==1?4:phase==2?4:s.kind==CareerSkillKind.Awakening?10:6;
            fx.BuildParticles();fx.Animate(0);
        }
        void BuildParticles()
        {
            var go=new GameObject("Embers and fragments");go.transform.SetParent(transform,false);
            var filter=go.AddComponent<MeshFilter>();var renderer=go.AddComponent<MeshRenderer>();
            if(particleMaterial==null){particleMaterial=new Material(FxMaterials.Additive);particleMaterial.mainTexture=Texture2D.whiteTexture;}
            renderer.sharedMaterial=particleMaterial;renderer.sortingOrder=core.sortingOrder+1;
            mesh=new Mesh{name="Career particles"};mesh.MarkDynamic();filter.sharedMesh=mesh;
            vertices=new Vector3[particles*4];colors=new Color[particles*4];var indices=new int[particles*6];var uv=new Vector2[particles*4];
            for(int i=0;i<particles;i++){int v=i*4,j=i*6;indices[j]=v;indices[j+1]=v+1;indices[j+2]=v+2;indices[j+3]=v;indices[j+4]=v+2;indices[j+5]=v+3;for(int k=0;k<4;k++)uv[v+k]=Vector2.one*.5f;}
            mesh.vertices=vertices;mesh.triangles=indices;mesh.uv=uv;mesh.bounds=new Bounds(Vector3.zero,Vector3.one*5);
        }
        void Update(){
            if(Game.Session.MapId!=map||(bound&&(source==null||source.IsDead||!source.gameObject.activeInHierarchy))){Destroy(gameObject);return;}
            age+=Time.deltaTime;if(age>=life){Destroy(gameObject);return;}
            if(phase==1&&source!=null)transform.position=source.Center;
            Animate(age/life);
        }
        void Animate(float t)
        {
            int f=Mathf.Clamp((int)(t*12),0,11);var sprite=phase==0?(rear!=null?CareerArt.PlaneFrame(skill,f,false):CareerArt.Frame(skill,f)):phase==1?CareerArt.DetailFrame(skill.career,true,f):CareerArt.VisualFrame(phase==2?skill.id+"_impact":phase==3?skill.id+"_cut":"finisher",f);
            core.sprite=edge.sprite=sprite;
            float fade=Mathf.Clamp01((1-t)*3.5f),appear=Mathf.Clamp01(t*12+.4f);
            core.color=new Color(1,1,1,fade*(phase==1?appear*.62f:phase==0?.9f:1f));edge.color=new Color(1,1,1,fade*.025f);
            if(rear!=null){rear.sprite=CareerArt.PlaneFrame(skill,f,true);rear.color=core.color;}
            Color tint=skill.effect=="fire"?new Color(1,.55f,.2f):skill.effect=="ice"?new Color(.6f,.9f,1):skill.effect=="storm"?new Color(1,.85f,.4f):CareerCatalog.Color(skill.career);
            for(int i=0;i<particles;i++){
                float seed=Mathf.Repeat(i*.6180339f+skill.index*.137f,1),a=i*2.39996f+skill.index*.47f;
                float q=phase==1?1-t:t;float dist=phase==1?.2f+q*.6f:.12f+q*(.42f+seed*.53f);
                var direction=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var center=direction*dist;
                if(skill.career==Career.Bishop)center.y+=t*t*.28f;
                else if(skill.career==Career.Arcanist){center+=new Vector2(-direction.y,direction.x)*Mathf.Sin(t*3)*.15f;}
                else center.y-=t*t*.28f;
                center.x=Mathf.Round(center.x*48)/48;center.y=Mathf.Round(center.y*48)/48;
                float size=(i%4==0?.045f:.025f)*(1-t*.7f),longSide=size*(skill.career==Career.Bishop?2:1.5f);
                var axis=phase==1?direction:skill.career==Career.Bishop?new Vector2(.5f,.86f):direction;
                var side=new Vector2(-axis.y,axis.x);
                vertices[i*4]=(Vector3)(center+axis*longSide);vertices[i*4+1]=(Vector3)(center+side*size);
                vertices[i*4+2]=(Vector3)(center-axis*longSide);vertices[i*4+3]=(Vector3)(center-side*size);
                Color color=i%4==0?Color.Lerp(tint,Color.white,.8f):tint;color.a=fade*.7f;
                for(int k=0;k<4;k++)colors[i*4+k]=color;
            }
            mesh.vertices=vertices;mesh.colors=colors;
        }
        void OnDestroy(){live=Mathf.Max(0,live-1);if(mesh!=null)Destroy(mesh);}
    }
}
