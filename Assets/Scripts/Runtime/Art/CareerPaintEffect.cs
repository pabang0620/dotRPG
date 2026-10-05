using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Animated native RGBA cels plus deterministic motion. This renderer never runs combat logic.</summary>
    public sealed class CareerPaintEffect : MonoBehaviour
    {
        sealed class Part
        {
            public SpriteRenderer image;public Career career;public int row,first=1,last=3;
            public Vector2 offset,travel,size;public float angle,turn,start,end=1,alpha=1,grow=.12f,arc;
            public bool ground,staticSprite,loop;public Color tint=Color.white;
        }
        static readonly List<CareerPaintEffect> live=new List<CareerPaintEffect>(80);
        static readonly Dictionary<string,int> starts=new Dictionary<string,int>();
        readonly List<Part> parts=new List<Part>(12);
        PlayerController owner;string map,id;float age,life,pulseUntil;bool bound,follow;int mode;
        public static int ActiveCount=>live.Count;
        public static int PeakCount {get;private set;}
        public static int FrameChanges {get;private set;}
        public static int Starts(string skill)=>starts.TryGetValue(skill,out int n)?n:0;
        public static bool Available(CareerSkill s)=>s!=null&&CareerPulseArt.Frame(s.career,0,0)!=null;
        public static void Clear(PlayerController source)
        {foreach(var fx in live.ToArray())if(fx!=null&&fx.owner==source)Destroy(fx.gameObject);}
        public static void Pulse(PlayerController source,string skill)
        {foreach(var fx in live)if(fx!=null&&fx.owner==source&&fx.id==skill&&fx.mode==2)fx.pulseUntil=Time.time+.18f;}
        static CareerPaintEffect New(CareerSkill s,Vector2 at,float duration,PlayerController source)
        {
            if(live.Count+CareerRenewalFx.Count>=80||!Available(s))return null;
            var go=new GameObject("Pulse "+s.id);if(Fx.Root!=null)go.transform.SetParent(Fx.Root,false);go.transform.position=at;
            var fx=go.AddComponent<CareerPaintEffect>();fx.owner=source;fx.bound=source!=null;fx.map=Game.Session.MapId;fx.id=s.id;fx.life=Mathf.Max(.06f,duration);
            live.Add(fx);PeakCount=Mathf.Max(PeakCount,live.Count);starts.TryGetValue(s.id,out int previous);starts[s.id]=previous+1;return fx;
        }
        Part Add(Career c,int row,Vector2 size,Vector2 offset,float angle=0,bool ground=false,float alpha=1,float start=0,float end=1,int first=1,int last=3)
        {
            var go=new GameObject("Animated cel "+row);go.transform.SetParent(transform,false);var image=go.AddComponent<SpriteRenderer>();
            image.sprite=CareerPulseArt.Frame(c,row,first);
            var part=new Part{image=image,career=c,row=row,size=size,offset=offset,angle=angle,ground=ground,alpha=alpha,start=start,end=end,first=first,last=last};parts.Add(part);return part;
        }
        Part Accent(Sprite sprite,Vector2 size,Vector2 at,Color color,float angle,float start=0,float end=1)
        {var p=Add(Career.Fighter,0,size,at,angle,false,1,start,end);p.staticSprite=true;p.image.sprite=sprite;p.tint=color;return p;}
        void Sparks(Career c,Vector2 dir,int count,float distance,float start=0,float end=1)
        {
            var color=CareerCatalog.Color(c);float baseAngle=Angle(dir);
            for(int i=0;i<count;i++){
                float angle=baseAngle+(i-(count-1)*.5f)*(c==Career.Fighter?19:43);
                Vector2 path=new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad));
                var p=Accent(CareerPulseArt.Shard,new Vector2(.23f+(i%3)*.05f,.09f),path*.07f,color,angle,start+i*.012f,end);
                p.travel=path*distance*(.65f+(i%3)*.17f);p.grow=-.6f;p.arc=c==Career.Bishop?.18f:-.12f;
            }
        }
        static float Angle(Vector2 d)=>Mathf.Atan2(d.y,d.x)*Mathf.Rad2Deg;
        public static bool Spawn(CareerSkill s,Vector2 at,float radius,Vector2 direction,float duration,int phase,PlayerController source)
        {
            if(!Available(s))return false;
            var fx=New(s,at,duration,source);if(fx==null)return true;
            var c=s.career;float a=Angle(direction),r=Mathf.Max(.45f,radius);int row=CareerPulseArt.Row(s);
            if(phase==1){
                fx.mode=4;fx.follow=true;
                var charge=fx.Add(c,c==Career.Fighter?2:c==Career.Guardian?0:c==Career.Bishop?2:3,new Vector2(1.45f,1.1f),Vector2.down*.18f,0,true,.52f,0,1,0,1);
                charge.grow=-.24f;charge.turn=c==Career.Arcanist?45:0;
                int count=s.kind==CareerSkillKind.Awakening?6:3;
                for(int i=0;i<count;i++){
                    float angle=i*360f/count;var p=fx.Accent(CareerPulseArt.Shard,new Vector2(.25f,.08f),new Vector2(Mathf.Cos(angle*Mathf.Deg2Rad),Mathf.Sin(angle*Mathf.Deg2Rad))*.8f,CareerCatalog.Color(c),angle+180,i*.025f);
                    p.travel=-p.offset*.85f;p.arc=.1f;
                }
                if(s.kind==CareerSkillKind.Awakening){var p=fx.Add(c,row,new Vector2(2.1f,2.1f),Vector2.up*.45f,0,false,.3f,0,1,0,1);p.grow=.45f;}
            }
            else if(phase==2){
                int hit=c==Career.Fighter?7:c==Career.Guardian?(s.effect=="bash"||s.effect=="counter"||s.effect=="taunt"?1:0):c==Career.Bishop&&(s.effect=="heal"||s.effect=="hot"||s.effect=="dawn")?0:row;
                float tall=c==Career.Arcanist&&row==2?1.65f:1.05f;
                var p=fx.Add(c,hit,new Vector2(1.05f,tall),Vector2.up*(tall-1)*.3f,c==Career.Fighter?a:0,false,.85f,0,1,2,3);p.grow=.06f;
                fx.Accent(CareerPulseArt.Glint,new Vector2(.46f,.46f),Vector2.zero,Color.white,a,0,.2f);
                fx.Sparks(c,direction,c==Career.Fighter?5:3,.7f,0,.95f);
                if(c==Career.Bishop){p.travel=Vector2.up*.3f;p.alpha=.7f;}
            }
            else if(c==Career.Fighter){
                if(s.effect=="focus"){
                    var p=fx.Add(c,2,new Vector2(1.2f,1.9f),Vector2.up*.2f,0,false,.65f);p.travel=Vector2.up*.3f;
                    fx.Sparks(c,Vector2.up,4,.55f);
                }else{
                    var cut=fx.Add(c,s.effect=="rush"?1:0,new Vector2(r*2,r*1.75f),Vector2.zero,s.effect=="rush"?a:a+180);cut.turn=phase==3?55:-45;
                    cut.travel=direction*.18f;cut.arc=phase==3?.12f:-.12f;
                    var echo=fx.Add(c,0,new Vector2(r*1.7f,r*1.45f),-direction*.15f,a+157,false,.26f,.14f,.8f);echo.turn=cut.turn;
                    fx.Sparks(c,direction,4,r*.65f,.03f);
                }
            }
            else if(c==Career.Guardian){
                if(s.effect=="bash"){
                    var p=fx.Add(c,1,new Vector2(r*2,r*1.75f),Vector2.zero,a);p.travel=direction*.3f;
                    fx.Add(c,0,new Vector2(.9f,1.25f),-direction*.2f,0,false,.7f,0,.65f);
                    fx.Sparks(c,direction,5,r*.8f);
                }else if(s.effect=="counter"||s.effect=="taunt"){
                    for(int i=0;i<4;i++){float q=i*90;var d=new Vector2(Mathf.Cos(q*Mathf.Deg2Rad),Mathf.Sin(q*Mathf.Deg2Rad));var p=fx.Add(c,1,new Vector2(r*.9f,r*.8f),d*r*.15f,q,false,.6f);p.travel=d*r*.65f;}
                    fx.Add(c,0,new Vector2(.9f,1.3f),Vector2.up*.35f,0,false,.85f,0,.75f);
                }else if(s.effect=="guard"){
                    var p=fx.Add(c,0,new Vector2(1.4f,1.85f),direction*.35f,0,false,.78f);p.grow=.35f;
                }else if(s.effect=="citadel"){
                    fx.Add(c,3,new Vector2(r*2.2f,r*1.7f),Vector2.up*r*.3f,0,false,.6f).grow=.42f;
                    for(int i=-1;i<=1;i++){var p=fx.Add(c,0,new Vector2(.65f,1.1f),new Vector2(i*r*.72f,r*.5f),0,false,.7f,.1f+Mathf.Abs(i)*.08f,.9f);p.travel=Vector2.up*.28f;}
                    fx.Sparks(c,Vector2.up,5,r*.5f,.05f);
                }else{
                    fx.Add(c,2,new Vector2(r*2,r*1.7f),Vector2.up*r*.25f,0,false,.54f).grow=.35f;
                    if(s.effect=="ward")for(int i=-1;i<=1;i+=2)fx.Add(c,0,new Vector2(.55f,1),new Vector2(i*r*.65f,0),0,false,.65f,.1f);
                }
            }
            else if(c==Career.Arcanist){
                if(s.effect=="blink"){
                    var p=fx.Add(c,3,new Vector2(1.3f,1.8f),Vector2.zero,0,false,.75f);p.turn=-55;p.grow=-.6f;
                    fx.Sparks(c,direction,5,.75f);
                }else if(row==0){
                    fx.Add(c,0,new Vector2(r*2,r*2),Vector2.up*r*.25f,0,false,.92f,0,1,2,3).grow=.06f;
                    fx.Sparks(c,Vector2.up,7,r*.8f,0,.85f);
                }else if(row==1){
                    var p=fx.Add(c,1,new Vector2(r*1.9f,r*1.8f),Vector2.up*r*.5f,0,false,.84f);p.grow=.25f;
                    for(int i=-1;i<=1;i+=2){var ice=fx.Add(c,1,new Vector2(r*.45f,r*.75f),new Vector2(i*r*.6f,0),i*-15,false,.75f,.08f,.75f);ice.travel=Vector2.up*.25f;}
                }else if(row==2){
                    fx.Add(c,2,new Vector2(r*1.35f,r*2.6f),Vector2.up*r*.7f,0,false,.92f,0,1,2,3);
                    fx.Sparks(c,direction,4,r*.7f);
                }else{
                    var p=fx.Add(c,3,new Vector2(r*1.9f,r*1.6f),Vector2.zero,0,true,.6f);p.turn=-60;p.grow=-.22f;
                    fx.Add(c,3,new Vector2(r*.85f,r*.85f),Vector2.up*.1f,60,false,.8f,.04f,.75f,2,3);
                    fx.Sparks(c,Vector2.up,5,r*.7f,.05f);
                }
            }
            else{
                if(s.effect=="heal"){
                    fx.Add(c,0,new Vector2(r*.85f,r*1.45f),Vector2.up*r*.4f,0,false,.72f).travel=Vector2.up*.2f;
                    fx.Sparks(c,Vector2.up,4,r*.35f,.1f);
                }else if(s.effect=="wings"){
                    fx.Add(c,1,new Vector2(r*2.1f,r*1.9f),Vector2.up*r*.48f,0,false,.73f).grow=.3f;
                    fx.Sparks(c,Vector2.up,5,r*.4f,.15f);
                }else if(s.effect=="dawn"){
                    fx.Add(c,3,new Vector2(r*1.75f,r*2.25f),Vector2.up*r*.5f,0,false,.57f).grow=.4f;
                    fx.Add(c,1,new Vector2(r*2.1f,r*1.35f),Vector2.up*r*.7f,0,false,.55f,.15f,.8f);
                    fx.Sparks(c,Vector2.down,7,r*.6f,.12f);
                }else if(s.effect=="cleanse"){
                    var p=fx.Add(c,2,new Vector2(r*1.25f,r*1.2f),Vector2.zero,0,true,.65f);p.travel=Vector2.up*.2f;p.grow=.45f;
                    fx.Sparks(c,Vector2.up,5,r*.5f);
                }else if(s.effect=="bless"){
                    fx.Add(c,1,new Vector2(r*1.2f,r*1.1f),Vector2.up*.6f,0,false,.72f).travel=Vector2.up*.3f;
                    fx.Add(c,2,new Vector2(r*1.6f,r*.8f),Vector2.down*.15f,0,true,.4f);
                }
            }
            if(fx.parts.Count>0)CareerLivingFx.Attach(fx.parts[0].image,s,phase==1?2:0);
            fx.Draw(0);return true;
        }
        public static CareerPaintEffect Wave(CareerSkill s,Vector2 at,float radius,Vector2 dir,float duration,PlayerController source)
        {
            var fx=New(s,at,duration,source);if(fx==null)return null;fx.mode=1;
            var p=fx.Add(s.career,1,new Vector2(radius*2.2f,radius*2),Vector2.zero,Angle(dir));p.grow=0;p.loop=true;
            var echo=fx.Add(s.career,1,new Vector2(radius*1.5f,radius*1.45f),-dir*.32f,Angle(dir),false,.25f);echo.loop=true;
            fx.Draw(0);return fx;
        }
        public static CareerPaintEffect Star(CareerSkill s,Vector2 at,PlayerController source)
        {
            var fx=New(s,at,.65f,source);if(fx==null)return null;fx.mode=1;
            var p=fx.Add(s.career,3,new Vector2(.6f,.6f),Vector2.zero);p.turn=160;p.loop=true;p.grow=0;
            fx.Accent(CareerPulseArt.Glint,new Vector2(.25f,.25f),Vector2.zero,Color.white,0);fx.Draw(0);return fx;
        }
        public static void Beam(CareerSkill s,Vector2 from,Vector2 dir,float length,float halfWidth,float duration,PlayerController source)
        {
            var fx=New(s,from+dir*length*.5f,duration,source);if(fx==null)return;
            float angle=Angle(dir);
            if(s.career==Career.Bishop){
                var p=fx.Add(s.career,0,new Vector2(halfWidth*1.8f,length),Vector2.zero,angle-90,false,.8f,0,1,1,3);p.grow=0;
                fx.Add(s.career,1,new Vector2(1.1f,1.3f),-dir*length*.43f,angle-90,false,.65f,0,.6f);
            }else{
                // The white fissure reaches the real corridor immediately. Residue travels afterwards, never the hit.
                var p=fx.Add(s.career,3,new Vector2(length/.94f,halfWidth*2.4f),Vector2.zero,angle,false,.95f,0,1,2,3);p.grow=0;
                int count=s.effect=="five"?7:3;
                for(int i=0;i<count;i++){
                    var spark=fx.Add(s.career,2,new Vector2(halfWidth*.65f,halfWidth*.85f),dir*(i/(float)(count-1)-.5f)*length*.82f,angle,false,.68f,i*.015f,.72f+i*.025f,2,3);spark.grow=.1f;
                }
                fx.Accent(CareerPulseArt.Shard,new Vector2(length,halfWidth*.12f),Vector2.zero,Color.white,angle,0,.1f);
            }
            fx.Draw(0);
        }
        public static void Ground(CareerSkill s,Vector2 at,float radius,float duration,PlayerController source)
        {
            foreach(var old in live.ToArray())if(old!=null&&old.mode==2&&old.id==s.id&&old.owner==source)Destroy(old.gameObject);
            var fx=New(s,at,duration,source);if(fx==null)return;fx.mode=2;
            var p=fx.Add(s.career,s.career==Career.Bishop?2:3,new Vector2(radius*1.85f,radius*1.5f),Vector2.zero,0,true,.3f,0,1,2,2);p.grow=0;
            fx.Draw(0);
        }
        public static void Meteor(CareerSkill s,Vector2 target,float radius,float duration,PlayerController source)
        {
            var fx=New(s,target,duration,source);if(fx==null)return;fx.mode=3;
            var p=fx.Add(s.career,0,new Vector2(radius*1.2f,radius*1.2f),new Vector2(-1.6f,3.3f),0,false,.88f,0,1,0,1);p.travel=new Vector2(1.6f,-3.3f);p.grow=.18f;
            fx.Draw(0);
        }
        public static void Trail(CareerSkill s,Vector2 at,Vector2 dir,PlayerController source)
        {
            if(Time.frameCount%3!=0)return;var fx=New(s,at,.19f,source);if(fx==null)return;
            fx.Add(s.career,s.career==Career.Guardian?0:1,new Vector2(.65f,1),Vector2.zero,s.career==Career.Guardian?0:Angle(dir),false,.28f,0,1,3,3).travel=-dir*.2f;
            fx.Draw(0);
        }
        public static void FighterGhost(CareerSkill s,Vector2 at,PlayerController source,float life)
        {
            var body=source.GetComponent<CharacterAnimator>()?.Renderer;if(body==null||body.sprite==null)return;
            var fx=New(s,at+(Vector2)body.transform.position-source.Center,life,source);if(fx==null)return;
            var p=fx.Accent(body.sprite,new Vector2(body.transform.lossyScale.x,body.transform.lossyScale.y),Vector2.zero,new Color(1,.8f,.4f,.5f),0);
            p.image.flipX=body.flipX;p.image.flipY=body.flipY;p.grow=0;p.alpha=.4f;fx.Draw(0);
        }
        public static void AwakeningStage(CareerSkill s,Vector2 at,float radius,Vector2 dir,int stage,PlayerController source)
        {
            var fx=New(s,at,.52f,source);if(fx==null)return;
            fx.Add(Career.Arcanist,stage,new Vector2(radius*1.9f,radius*(stage==2?2.7f:1.9f)),Vector2.up*radius*(stage==2?.65f:.25f),0,false,.8f,0,1,stage==1?1:2,3);
            var ring=fx.Add(Career.Arcanist,3,new Vector2(radius*1.65f,radius*1.25f),Vector2.zero,stage*45,true,.38f,0,1,2,3);ring.turn=stage==1?-30:30;
            fx.Sparks(Career.Arcanist,dir,6,radius*.65f);fx.Draw(0);
        }
        void Update()
        {
            if(Game.Session.MapId!=map||(bound&&(owner==null||owner.IsDead||!owner.gameObject.activeInHierarchy))){Destroy(gameObject);return;}
            age+=Time.deltaTime;if(age>=life){Destroy(gameObject);return;}
            if(follow&&owner!=null)transform.position=owner.Center;
            // Effects that cannot be seen still age and clean up, but skip renderer work.
            var camera=Game.Camera?.Camera;if(camera!=null){var v=camera.WorldToViewportPoint(transform.position);if((v.x<-.7f||v.x>1.7f||v.y<-.7f||v.y>1.7f)&&mode!=2)return;}
            Draw(age/life);
        }
        void Draw(float t)
        {
            foreach(var p in parts){
                float u=(t-p.start)/Mathf.Max(.01f,p.end-p.start);p.image.enabled=u>=0&&u<=1;if(!p.image.enabled)continue;
                float ease=1-Mathf.Pow(1-u,3),fade=mode==2?Mathf.Min(1,(1-u)*life*5):mode==3?1:Mathf.Clamp01((1-u)*3.5f);
                // Release->peak is fast, peak holds briefly, then the independently drawn residue dissolves.
                if(!p.staticSprite){int frame=p.first==p.last?p.first:p.loop?1+(Mathf.FloorToInt(age*14)%2):p.first==0?(u<.65f?0:1):p.first==2?(u<.4f?2:3):u<.13f?1:u<.5f?2:3;
                    var cel=CareerPulseArt.Frame(p.career,p.row,Mathf.Clamp(frame,p.first,p.last));if(p.image.sprite!=cel){p.image.sprite=cel;FrameChanges++;}}
                float expansion=1-p.grow+p.grow*ease;
                var pos=p.offset+p.travel*(mode==3?u*u:ease)+Vector2.up*(Mathf.Sin(u*Mathf.PI)*p.arc);
                pos.x=Mathf.Round(pos.x*96)/96;pos.y=Mathf.Round(pos.y*96)/96;
                p.image.transform.localPosition=pos;p.image.transform.localScale=p.staticSprite?new Vector3(p.size.x*expansion,p.size.y*expansion,1):CareerPulseArt.Scale(p.career,p.row,p.size*expansion);
                p.image.transform.localRotation=Quaternion.Euler(0,0,p.angle+p.turn*ease);
                float alpha=p.alpha*fade;if(mode==2)alpha*=Time.time<pulseUntil?1.6f:1;
                if(mode==4)alpha*=Mathf.Lerp(.45f,1,u);
                var color=p.tint;color.a=alpha;p.image.color=color;
                p.image.sortingOrder=p.ground?SkillFx.GroundOrder+23:SkillFx.At(transform.position.y,85);
            }
        }
        void OnDestroy(){live.Remove(this);}
    }
}
