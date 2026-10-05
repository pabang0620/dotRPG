using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Visible moving weapons, waves and fields, advanced by the same coroutine as their collision.</summary>
    public sealed class CareerRenewalFx : MonoBehaviour
    {
        static readonly List<CareerRenewalFx> active=new List<CareerRenewalFx>();
        public static int Count=>active.Count;
        public static int Peak {get;private set;}
        public static int Started {get;private set;}
        static readonly Dictionary<string,int> starts=new Dictionary<string,int>();
        public static int Starts(string skill)=>starts.TryGetValue(skill,out int n)?n:0;
        PlayerController owner;string map;Career career;string id;SpriteRenderer art,echo;
        float age,life,angle,totalLife;int row,mode;Vector2 dimensions;bool follow,forwardArc;
        public string SkillId=>id;
        public static CareerRenewalFx Make(CareerSkill skill,Vector2 at,int motif,Vector2 size,float seconds,PlayerController source,int type=0,float rotation=0,bool tracks=false)
        {
            if(active.Count+CareerPaintEffect.ActiveCount>=80||CareerPulseArt.Frame(skill.career,motif,0)==null)return null;
            var go=new GameObject("Renewal "+skill.id);if(Fx.Root!=null)go.transform.SetParent(Fx.Root,false);go.transform.position=at;
            var v=go.AddComponent<CareerRenewalFx>();v.owner=source;v.map=Game.Session.MapId;v.career=skill.career;v.id=skill.id;v.row=motif;v.dimensions=size;v.life=Mathf.Max(.06f,seconds);v.totalLife=v.life;v.mode=type;v.angle=rotation;v.follow=tracks;
            var artwork=new GameObject("Artwork");artwork.transform.SetParent(go.transform,false);
            v.art=artwork.AddComponent<SpriteRenderer>();v.art.sprite=CareerPulseArt.Frame(skill.career,motif,type==2?0:type==1?1:2);
            var child=new GameObject("Directional echo");child.transform.SetParent(go.transform,false);v.echo=child.AddComponent<SpriteRenderer>();
            CareerLivingFx.Attach(v.art,skill,type);
            active.Add(v);Peak=Mathf.Max(Peak,Count);Started++;starts.TryGetValue(skill.id,out int previous);starts[skill.id]=previous+1;v.Draw(0);return v;
        }
        public static void Clear(PlayerController p){foreach(var v in active.ToArray())if(v!=null&&v.owner==p)Destroy(v.gameObject);}
        public static void Pulse(PlayerController p,string skill){foreach(var v in active)if(v.owner==p&&v.id==skill&&v.mode==3)v.age=0;}
        public void Place(Vector2 at,float rotation){transform.position=at;angle=rotation;}
        public void Resize(Vector2 size){dimensions=size;}
        // Concept cels point along local +X. Keep both arc and echo inside the facing front.
        public void ForwardArc(){forwardArc=true;Draw(age);}
        public void Release(float seconds=.16f){mode=0;age=.12f;life=Mathf.Max(.06f,seconds);follow=false;}
        public static float Angle(Vector2 dir)=>Mathf.Atan2(dir.y,dir.x)*Mathf.Rad2Deg;
        public static CareerRenewalFx Projectile(CareerSkill s,Vector2 at,int motif,Vector2 size,Vector2 dir,float life,PlayerController p)
        =>Make(s,at,motif,size,life,p,1,Angle(dir));
        public static void Burst(CareerSkill s,Vector2 at,int motif,float radius,PlayerController p)
        {Make(s,at,motif,new Vector2(radius*1.9f,radius*1.8f),.3f,p);}
        public static CareerRenewalFx Zone(CareerSkill s,Vector2 at,float radius,float life,PlayerController p,bool follow=false)
        =>Make(s,at,s.career==Career.Guardian?(s.effect=="citadel"?3:2):s.career==Career.Bishop?2:3,new Vector2(radius*1.9f,radius*1.45f),life,p,3,0,follow);
        public static void Prepare(CareerSkill s,PlayerController p,Vector2 dir)
        {
            if(s.id=="g_awake"){GuardianAwakeningFx.Charge(p,s.cast);return;}
            if(s.career==Career.Fighter){
                int row=FighterReforgeArt.Row(s);bool upright=row==3||row==4||row==6;
                Make(s,p.Center+dir*.25f,row,upright?new Vector2(.8f,1.2f):new Vector2(.9f,.85f),s.cast,p,2,upright?0:Angle(dir));return;
            }
            Make(s,p.Center,CareerPulseArt.Row(s),new Vector2(1.1f,.85f),s.cast,p,2,s.career==Career.Fighter?Angle(dir):0,true);
        }
        void Update()
        {
            if(owner==null||owner.IsDead||!owner.gameObject.activeInHierarchy||Game.Session.MapId!=map){Destroy(gameObject);return;}
            // Pulse only changes cel brightness; the expiry clock is never reset by a field pulse.
            life-=Time.deltaTime;age+=Time.deltaTime;if(life<=0){Destroy(gameObject);return;}
            if(follow)transform.position=owner.Center;Draw(age);
        }
        void Draw(float time)
        {
            int frame=mode==4?(time<.07f?1:2):mode==1?(career==Career.Fighter?(time<.07f?1:2):1+Mathf.FloorToInt(time*14)%2):mode==2?0:mode==3?2:time<.12f?2:3;
            // Every burst reads all four cels: gather, acceleration, contact, broken residual.
            float progress=Mathf.Clamp01(time/Mathf.Max(.01f,totalLife));
            if(mode==0)frame=progress<.10f?0:progress<.26f?1:progress<.61f?2:3;
            if(mode==2)frame=progress<.72f?0:1;
            if(mode==3)frame=(Mathf.FloorToInt(time*5)+row)%3==0?1:2;
            if(mode==1)frame=1+Mathf.FloorToInt(time*(12+row*2))%2;
            art.sprite=CareerPulseArt.Frame(career,row,frame);
            float expansion=mode==2?Mathf.Lerp(.65f,1,progress):mode==0?Mathf.Lerp(.82f,1,Mathf.Min(1,progress*4)):1;
            art.transform.localScale=Vector3.one*expansion;
            // Interior circulation never changes the separate, exact gameplay boundary.
            float circulation=mode==3?Mathf.Sin(time*2.5f+row)*3:mode==2?Mathf.Sin(progress*Mathf.PI)*5:0;
            art.transform.localRotation=Quaternion.Euler(0,0,id=="g_wall"&&mode==1?time*620:circulation);

            transform.localScale=CareerPulseArt.Scale(career,row,dimensions);
            transform.localRotation=Quaternion.Euler(0,0,id=="f_flurry"?0:angle);
            float alpha=mode==3?.21f+(age<.16f?.14f:0):mode==2?.45f:mode==1?.92f:Mathf.Clamp01(life*7);
            art.color=new Color(1,1,1,alpha);art.sortingOrder=mode==3?SkillFx.GroundOrder+23:SkillFx.At(transform.position.y,86);
            if(career==Career.Fighter&&mode==2)art.color=new Color(1,1,1,Mathf.Lerp(.22f,.7f,Mathf.Clamp01(time/Mathf.Max(.01f,time+life))));
            echo.enabled=mode==1||mode==4;echo.sprite=CareerPulseArt.Frame(career,row,3);echo.color=career==Career.Fighter?new Color(.45f,.62f,1,.22f):new Color(1,1,1,.22f);
            echo.transform.localPosition=new Vector3(forwardArc?0:-.12f-.08f*Mathf.Sin(time*18),0,0);
            echo.transform.localScale=Vector3.one*(.88f+.06f*Mathf.Sin(time*14));echo.sortingOrder=art.sortingOrder-1;
        }
        void OnDestroy(){active.Remove(this);}
    }
    public static class CareerMoves
    {
        public static string Key(Career c,int i)
        {
            string[] f={"passive","moon-projectile","flash-step","violet-three-beats","passive","falling-sword","rising-eruptions","pursuing-echoes","sword-convergence"};
            string[] g={"passive","shield-stance","returning-shield","moving-bastion","passive","taunt-wave","shield-uppercut","stored-counter","shieldquake-aoe"};
            string[] m={"passive","fire-lance","crystal-spears","seeking-lightning","passive","astral-salvo","phase-echo","travelling-vortex","orbital-collapse"};
            string[] b={"passive","healing-feathers","healing-waves","cleanse-wave","passive","piercing-light","feather-guard","blessing-chain","pilgrim-sanctuary"};
            return (c==Career.Fighter?f:c==Career.Guardian?g:c==Career.Arcanist?m:b)[i];
        }
        public static float Recovery(CareerSkill s)
        {
            if(s.career==Career.Fighter)return s.effect=="f_convergence"?1.1f:s.effect=="f_vortex"?.55f:s.effect=="f_eruption"||s.effect=="f_echo"?.45f:s.effect=="f_wave"?.55f:.3f;
            if(s.effect=="slash")return .38f;if(s.effect=="flurry")return .5f;
            if(s.effect=="rush"||s.effect=="bash")return .4f;
            if(s.effect=="break")return .35f;if(s.effect=="five"||s.effect=="execute")return .24f;
            if(s.effect=="eclipse")return .95f;return .1f;
        }
    }
}
