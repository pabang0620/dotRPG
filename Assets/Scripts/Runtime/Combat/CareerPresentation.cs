using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Presentation observers only. No damage, movement, status duration or time-scale writes.</summary>
    public static class CareerPresentation
    {
        public static void Begin(CareerSkill s,Vector2 from,Vector2 center,SkillNumbers n,Vector2 dir,PlayerController owner)
        {
            switch(s.effect)
            {
                case "slash":case "flurry":case "five":case "execute":case "light":case "storm":case "heal":case "hot":return;
                case "counter":CareerEffect.Charge(s,owner.Center,.65f,dir,.3f,owner);return;
                case "rush":case "blink":Link(s,from,center,owner,.24f);return;
                case "rift":CareerAreaView.Show(s,center,n.radius,s.duration,owner);return;
            }
            bool area=s.effect=="shield"||s.effect=="ward"||s.effect=="citadel"||s.effect=="wings"||s.effect=="bless"||s.effect=="cleanse"||s.effect=="dawn"||s.effect=="taunt"||s.effect=="ice"||s.effect=="fire"||s.effect=="eclipse";
            if(area&&n.radius>0)CareerAreaView.Show(s,center,n.radius,.5f,owner);
            if(s.effect=="eclipse")return; // each of its three releases is dispatched with its actual hit
            CareerEffect.Play(s,center,Mathf.Min(1.75f,Mathf.Max(.5f,n.radius)),dir,s.kind==CareerSkillKind.Awakening?.85f:.45f,owner);
        }
        public static void Strike(CareerSkill s,Vector2 center,float radius,Vector2 dir,int hit,PlayerController owner)
        {
            if(s.effect=="slash"||s.effect=="flurry"||s.effect=="five")CareerEffect.Cut(s,center,radius,dir,hit,s.effect=="five"&&hit==4,owner);
            if(s.effect=="rift"){CareerAreaView.Pulse(owner,s.id);CareerEffect.Play(s,center,Mathf.Min(radius,1.15f),dir,.5f,owner);}
            if(s.effect=="eclipse"){
                var element=CareerCatalog.Get(hit==0?"m_fire":hit==1?"m_ice":"m_storm");
                CareerEffect.Play(element,center,1.5f,dir,.3f,owner);
                if(hit==2)CareerEffect.Play(s,center,1.8f,dir,.6f,owner);
            }
        }
        public static void Landed(CareerSkill s,Vector2 origin,EnemyController enemy,Vector2 dir,PlayerController owner)
        {
            if(s.effect=="light"||s.effect=="storm"||s.effect=="fire"||s.effect=="orbit")Link(s,origin,enemy.Center,owner,.16f);
            if(s.effect=="execute")CareerEffect.Play(s,enemy.Center,.8f,dir,.28f,owner);
            if(s.effect=="light")CareerEffect.Play(s,enemy.Center,.6f,(enemy.Center-origin).normalized,.22f,owner);
            CareerEffect.Hit(s,enemy.Center,dir,owner);
        }
        public static void Link(CareerSkill s,Vector2 a,Vector2 b,PlayerController owner,float life=.2f)
        {if((a-b).sqrMagnitude>.01f)CareerLinkView.Show(s,a,b,life,owner);}
        public static void Taunted(EnemyController enemy,float duration,PlayerController owner)
        {CareerMarkView.Show(enemy,duration,owner);}
    }

    /// <summary>Exact circular boundary, separate from the smaller central artwork.</summary>
    public sealed class CareerAreaView : MonoBehaviour
    {
        static readonly List<CareerAreaView> active=new List<CareerAreaView>();
        public static int Count=>active.Count;
        PlayerController owner;string map,id;float end,pulse;SpriteRenderer image;
        public float Radius {get;private set;}
        public static void Show(CareerSkill s,Vector2 at,float radius,float life,PlayerController source)
        {
            if(active.Count>=24||radius<=0)return;
            var go=new GameObject("Career boundary "+s.id);if(Fx.Root!=null)go.transform.SetParent(Fx.Root,false);go.transform.position=at;
            var v=go.AddComponent<CareerAreaView>();v.owner=source;v.map=Game.Session.MapId;v.id=s.id;v.end=Time.time+life;v.Radius=radius;v.pulse=Time.time+.12f;
            v.image=go.AddComponent<SpriteRenderer>();v.image.sprite=CareerArt.VisualFrame("boundary",0);v.image.sortingOrder=SkillFx.GroundOrder+24;
            go.transform.localScale=Vector3.one*radius*48/43;v.image.color=CareerCatalog.Color(s.career);active.Add(v);
        }
        public static void Pulse(PlayerController p,string id){foreach(var v in active)if(v.owner==p&&v.id==id)v.pulse=Time.time+.14f;}
        void Update(){if(Time.time>=end||owner==null||owner.IsDead||!owner.gameObject.activeInHierarchy||Game.Session.MapId!=map){Destroy(gameObject);return;}var c=image.color;c.a=Time.time<pulse?.5f:.22f;image.color=c;}
        void OnDestroy(){active.Remove(this);}
    }
    /// <summary>An instantaneous tracer at the actual hit, not a delayed gameplay projectile.</summary>
    public sealed class CareerLinkView : MonoBehaviour
    {
        static int count; PlayerController owner;string map;float start,life;LineRenderer line;Color tint;
        public static int Count=>count;
        public static void Show(CareerSkill s,Vector2 from,Vector2 to,float life,PlayerController source)
        {
            if(count>=40)return;
            var go=new GameObject("Career tracer");if(Fx.Root!=null)go.transform.SetParent(Fx.Root,false);
            var v=go.AddComponent<CareerLinkView>();v.owner=source;v.map=Game.Session.MapId;v.start=Time.time;v.life=life;count++;
            v.line=go.AddComponent<LineRenderer>();v.line.sharedMaterial=FxMaterials.Alpha;v.line.useWorldSpace=true;v.line.numCapVertices=v.line.numCornerVertices=0;v.line.sortingOrder=SkillFx.At(to.y,80);
            v.tint=s.effect=="fire"?new Color(1,.55f,.2f):s.effect=="storm"?new Color(1,.88f,.45f):CareerCatalog.Color(s.career);
            int n=s.effect=="storm"?9:2;v.line.positionCount=n;Vector2 side=new Vector2(-(to-from).y,(to-from).x).normalized;
            for(int i=0;i<n;i++){var at=Vector2.Lerp(from,to,i/(float)(n-1));if(i>0&&i<n-1)at+=side*((i%2==0?1:-1)*.09f);v.line.SetPosition(i,new Vector3(Mathf.Round(at.x*48)/48,Mathf.Round(at.y*48)/48,0));}
            v.line.startWidth=.065f;v.line.endWidth=.025f;v.line.startColor=v.line.endColor=v.tint;
        }
        void Update(){float t=(Time.time-start)/life;if(t>=1||owner==null||owner.IsDead||!owner.gameObject.activeInHierarchy||Game.Session.MapId!=map){Destroy(gameObject);return;}var c=tint;c.a=1-t;line.startColor=line.endColor=c;}
        void OnDestroy(){count=Mathf.Max(0,count-1);}
    }
    public sealed class CareerMarkView : MonoBehaviour
    {
        EnemyController enemy;PlayerController owner;string map;float end;SpriteRenderer image;
        public static void Show(EnemyController e,float duration,PlayerController source)
        {
            var v=e.GetComponent<CareerMarkView>();if(v==null){v=e.gameObject.AddComponent<CareerMarkView>();v.image=new GameObject("Taunt pennant").AddComponent<SpriteRenderer>();v.image.transform.SetParent(e.transform,false);}
            v.enemy=e;v.owner=source;v.map=Game.Session.MapId;v.end=Time.time+duration;
            v.image.sprite=CareerArt.VisualFrame("Guardian_taunt",0);v.image.transform.localScale=Vector3.one*.55f;
        }
        void LateUpdate(){if(enemy==null||enemy.IsDead||owner==null||owner.IsDead||Game.Session.MapId!=map||Time.time>=end){Destroy(this);return;}image.transform.position=enemy.Center+Vector2.up*.15f;image.sortingOrder=SkillFx.At(enemy.Center.y,100);}
        void OnDestroy(){if(image!=null)Destroy(image.gameObject);}
    }

    /// <summary>Reads existing timers and shield amount. It never starts or extends a combat buff.</summary>
    public sealed class CareerStatusView : MonoBehaviour
    {
        PlayerController player;CareerCombat state;SpriteRenderer[] icons=new SpriteRenderer[6];
        readonly float[] fadeUntil=new float[6];readonly bool[] wasOn=new bool[6];
        SpriteRenderer passive;float passiveUntil,impactUntil;
        public int VisibleCount {get {int n=0;foreach(var icon in icons)if(icon!=null&&icon.enabled)n++;return n;}}
        public static CareerStatusView For(PlayerController p,CareerCombat c)
        {var v=p.GetComponent<CareerStatusView>();if(v==null)v=p.gameObject.AddComponent<CareerStatusView>();v.player=p;v.state=c;return v;}
        public void ShieldHit(){impactUntil=Time.time+.12f;}
        public void Passive(string id)
        {
            if(Time.time<passiveUntil-.35f)return;
            if(passive==null){passive=new GameObject("Career passive feedback").AddComponent<SpriteRenderer>();passive.transform.SetParent(transform,false);passive.transform.localScale=Vector3.one*.32f;}
            passive.sprite=CareerArt.Get("career_"+id);passiveUntil=Time.time+.65f;
        }
        public void Clear(){foreach(var icon in icons)if(icon!=null)icon.enabled=false;for(int i=0;i<6;i++){wasOn[i]=false;fadeUntil[i]=0;}if(passive!=null)passive.enabled=false;passiveUntil=0;}
        void LateUpdate()
        {
            if(player==null||state==null||player.IsDead||!player.gameObject.activeInHierarchy){Clear();return;}
            Draw(0,state.Shield>0,state.ShieldCareer+"_shield",1f);
            Draw(1,state.GuardVisible,"Guardian_guard",.85f);
            Draw(2,state.FocusVisible,"Fighter_focus",.7f);
            Draw(3,state.BlessVisible,"Bishop_bless",.75f);
            Draw(4,state.HotVisible,"Bishop_hot",.75f);
            Draw(5,state.CounterVisible,"Guardian_counter",.8f);
            if(passive!=null){passive.enabled=Time.time<passiveUntil;passive.transform.position=player.Center+Vector2.up*.95f;passive.sortingOrder=SkillFx.At(player.Center.y,110);}
        }
        void Draw(int index,bool on,string key,float size)
        {
            if(wasOn[index]&&!on)fadeUntil[index]=Time.time+.22f;wasOn[index]=on;
            bool visible=on||Time.time<fadeUntil[index];if(!visible){if(icons[index]!=null)icons[index].enabled=false;return;}
            if(icons[index]==null){icons[index]=new GameObject("Career state "+index).AddComponent<SpriteRenderer>();icons[index].transform.SetParent(transform,false);}
            var icon=icons[index];icon.enabled=true;icon.sprite=CareerArt.VisualFrame(key,Mathf.FloorToInt(Time.time*4)%12);icon.transform.position=player.Center;
            icon.transform.localScale=Vector3.one*size;icon.sortingOrder=SkillFx.At(player.Center.y,index==0?35:30);
            float alpha=on?(index==0&&Time.time<impactUntil?.95f:.5f):Mathf.Clamp01((fadeUntil[index]-Time.time)/.22f)*.5f;icon.color=new Color(1,1,1,alpha);
        }
        void OnDisable(){Clear();}
    }
}
