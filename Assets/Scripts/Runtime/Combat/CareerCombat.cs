using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed class CareerCombat : MonoBehaviour
    {
        PlayerController owner;
        static readonly Dictionary<CharacterData,CareerCombat> actors=new Dictionary<CharacterData,CareerCombat>();
        readonly Dictionary<EnemyController,float> broken=new Dictionary<EnemyController,float>();
        float shieldEnd,guardEnd,focusEnd,blessEnd,rhythmEnd,retalEnd,retalReady,hotEnd;
        int shield,guard,bless,stacks,hotVersion,focusCrit; SpriteRenderer aura; float awakeUntil;
        float counterEnd; int counterDamage; string lastElement="";float elementEnd;
        public bool Cursed {get;private set;} public float SlowUntil {get;private set;}
        public int Shield => Time.time<shieldEnd?shield:0;
        public int ComboStacks => Time.time<rhythmEnd?stacks:0;
        public static bool SecondaryDamage;
        public static string CurrentSkill;
        Progression Prog=>owner.Data.Progression;
        public static CareerCombat For(PlayerController p)
        {var c=p.GetComponent<CareerCombat>();if(c==null)c=p.gameObject.AddComponent<CareerCombat>();c.owner=p;if(p.Data!=null)actors[p.Data]=c;return c;}
        public static int SpeedFor(CharacterData d)=>d.Progression.Rank("f_rhythm")>0&&actors.TryGetValue(d,out var c)&&c!=null ? c.ComboStacks*(3+Mathf.Max(0,d.Progression.Rank("f_rhythm")-1)) : 0;
        void OnDestroy(){if(owner?.Data!=null)actors.Remove(owner.Data);}
        public void AddShield(int amount,float duration)
        {shield=Mathf.Max(Shield,amount);shieldEnd=Time.time+duration;CareerTrials.Record(owner,"shield",amount);}
        public void AddGuard(int percent,float duration){guard=Mathf.Max(Time.time<guardEnd?guard:0,percent);guardEnd=Time.time+duration;}
        public void AddCurse(float slow=0){Cursed=true;SlowUntil=Time.time+slow;}
        public bool Cleanse(){bool had=Cursed||Time.time<SlowUntil;Cursed=false;SlowUntil=0;return had;}
        public void ResetState(){StopAllCoroutines();shield=guard=bless=stacks=0;shieldEnd=guardEnd=blessEnd=rhythmEnd=focusEnd=retalEnd=counterEnd=awakeUntil=0;hotVersion++;Cleanse();broken.Clear();lastElement="";}
        public float MoveScale=>Time.time<SlowUntil?.6f:Time.time<guardEnd&&guard>=30?.7f:1;
        public int Absorb(int damage)
        {
            if(owner.Health.IsInvulnerable)return damage;
            int reduction=(Time.time<guardEnd?guard:0)+Prog.Rank("g_steel")*(Prog.Rank("g_steel")>0?2:0)+(Prog.Rank("g_steel")>0?4:0);
            damage=Mathf.RoundToInt(damage*(1-Mathf.Min(65,reduction)/100f));
            int used=Mathf.Min(Shield,damage);shield-=used;damage-=used;
            if(used>0)CareerTrials.Record(owner,"absorbed",used);
            if(Prog.Rank("g_retal")>0 && Time.time>=retalReady){retalEnd=Time.time+4;retalReady=Time.time+2;}
            if(counterEnd>Time.time){counterEnd=0;Counter();}
            return Mathf.Max(0,damage);
        }
        void Update(){
            if(counterEnd>0&&Time.time>=counterEnd){counterEnd=0;Counter();}
            bool show=Shield>0 || Time.time<awakeUntil;
            if(show && aura==null){aura=new GameObject("CareerAura").AddComponent<SpriteRenderer>();aura.transform.SetParent(transform,false);aura.transform.localPosition=Vector3.up*.2f;aura.sortingOrder=2;}
            if(aura!=null){aura.enabled=show;if(show){var c=Prog.IsPromoted?Prog.Career:Career.Guardian;var s=CareerCatalog.For(c)[8];aura.sprite=CareerArt.Frame(s,6);aura.transform.localScale=Vector3.one*.7f;aura.color=new Color(1,1,1,.35f);}}
        }
        void Counter(){var s=CareerCatalog.Get("g_counter");CareerEffect.Play(s,owner.Center,s.radius,owner.Facing.ToVector());foreach(var e in Enemies(owner.Center,s.radius))Damage(e,counterDamage,true);CareerTrials.Record(owner,"counter",1);}
        public int ModifyDamage(EnemyController enemy,int amount)
        {
            if(SecondaryDamage)return amount;
            float mult=Time.time<blessEnd?1+bless/100f:1;
            if(broken.TryGetValue(enemy,out var until)&&Time.time<until)mult*=1.15f;
            if(Time.time<retalEnd){mult*=1+(20+5*(Prog.Rank("g_retal")-1))/100f;retalEnd=0;}
            int crit=Prog.Rank("f_edge")>0?8+4*(Prog.Rank("f_edge")-1):0;if(Time.time<focusEnd)crit+=focusCrit;
            if(Random.Range(0,100)<crit)mult*=1.5f;
            return Mathf.Max(1,Mathf.RoundToInt(amount*mult));
        }
        public void OnLanded(EnemyController enemy)
        {
            if(SecondaryDamage)return;
            if(Prog.Rank("f_rhythm")>0){stacks=Mathf.Min(5,ComboStacks+1);rhythmEnd=Time.time+4;}
            CareerTrials.Record(owner,"hit",1);
        }
        List<EnemyController> Enemies(Vector2 center,float radius)
        {var list=new List<EnemyController>();foreach(var e in EnemyController.Active)if(e!=null&&!e.IsDead&&Vector2.Distance(e.Center,center)<=radius)list.Add(e);list.Sort((a,b)=>(a.Center-center).sqrMagnitude.CompareTo((b.Center-center).sqrMagnitude));return list;}
        List<PlayerController> Allies(float radius)
        {var list=new List<PlayerController>{owner};if(Game.Party!=null)foreach(var p in Game.Party.Members)if(p!=owner&&!p.IsDead&&Vector2.Distance(p.Center,owner.Center)<=radius)list.Add(p);var trial=CareerTrials.Companion;if(trial!=null&&!trial.IsDead&&!list.Contains(trial)&&Vector2.Distance(trial.Center,owner.Center)<=radius)list.Add(trial);return list;}
        PlayerController Wounded(float range)
        {var a=Allies(range);a.Sort((x,y)=>((float)x.Health.Current/x.Health.Max).CompareTo((float)y.Health.Current/y.Health.Max));return a[0];}
        void Heal(PlayerController p,int value,CareerSkill s)
        {int rank=Prog.Rank("b_mercy");if(rank>0)value=Mathf.RoundToInt(value*(1+(10+5*(rank-1))/100f));if(For(p).Cursed)value=Mathf.RoundToInt(value*.35f);int actual=p.Health.Heal(value);CareerEffect.Hit(s,p.Center,Vector2.up);if(actual>0)CareerTrials.Record(owner,"heal",actual);}
        void Damage(EnemyController e,int value,bool secondary=false)
        {bool old=SecondaryDamage;SecondaryDamage=secondary;try{e.TakeDamage(new DamageInfo(value,owner.Position,1.2f,Team.Player,owner.gameObject));}finally{SecondaryDamage=old;}}
        IEnumerator Burn(EnemyController e,int value){for(int i=0;i<3;i++){yield return new WaitForSeconds(1);if(e==null||e.IsDead||owner.IsDead)yield break;Damage(e,value,true);CareerEffect.Hit(CareerCatalog.Get("m_fire"),e.Center,Vector2.up);}}
        IEnumerator Hot(PlayerController target,int amount,CareerSkill s)
        {var status=For(target);int version=++status.hotVersion;status.hotEnd=Time.time+5;for(int i=0;i<5;i++){yield return new WaitForSeconds(1);if(target==null||target.IsDead||status.hotVersion!=version||owner.IsDead)yield break;Heal(target,amount,s);}}
        Vector2 Travel(float distance)
        {var dir=owner.Facing.ToVector();Vector2 start=owner.Position,end=start;for(float d=.15f;d<=distance;d+=.15f){var next=start+dir*d;bool wall=false;foreach(var c in Physics2D.OverlapCircleAll(next+Vector2.up*.22f,.29f))if(!c.isTrigger&&c.GetComponentInParent<PlayerController>()==null&&c.GetComponentInParent<EnemyController>()==null)wall=true;if(wall)break;end=next;}return end;}
        public IEnumerator Cast(CareerSkill s,SkillNumbers n)
        {
            if(!Prog.CareerUnlocked(s))yield break;
            string castMap=Game.Session.MapId;
            Vector2 dir=owner.Facing.ToVector();
            CareerEffect.Charge(s,owner.Center,.9f,dir,s.cast);
            yield return new WaitForSeconds(s.cast);
            if(owner.IsDead||Game.Session.MapId!=castMap||!Prog.CareerUnlocked(s))yield break;
            if(s.kind==CareerSkillKind.Awakening)awakeUntil=Time.time+2;
            if(s.kind==CareerSkillKind.Awakening&&owner.IsLocal){GameEvents.RaiseAwakening(s.name,CareerCatalog.Color(s.career));Game.Camera?.Shake(.1f,.15f);}
            // On a network member only VFX are predicted. The host performs all HP/status mutations.
            bool authority=!PartyNet.IsMember;
            int rank=Mathf.Max(1,Prog.Rank(s.id));float scale=n.careerPotency>0?n.careerPotency:1+.12f*(rank-1);
            Vector2 origin=owner.Center, center=origin;
            if(s.effect=="rush"||s.effect=="blink") {if(authority)owner.Place(Travel(s.range),owner.Facing);center=owner.Center;}
            if(s.career==Career.Arcanist&&s.effect!="blink"){
                var aimed=Enemies(owner.Center,s.range).Find(e=>Vector2.Dot((e.Center-owner.Center).normalized,dir)>.35f);
                center=aimed!=null?aimed.Center:owner.Center+dir*s.range;
            }
            else if(s.career==Career.Fighter||s.effect=="bash")center+=dir*(s.effect=="rush"?0:Mathf.Max(0,n.range-n.radius));
            CareerEffect.Play(s,center,Mathf.Max(.8f,n.radius),dir,s.kind==CareerSkillKind.Awakening?1.15f:.55f);
            if(!authority)yield break;
            if(s.effect!="fire"&&s.effect!="ice"&&s.effect!="storm")CareerTrials.Record(owner,s.effect,1);
            switch(s.effect)
            {
                case "focus":focusCrit=Mathf.RoundToInt(25*scale);focusEnd=Time.time+s.duration;yield break;
                case "guard":AddGuard(Mathf.RoundToInt(30*scale),s.duration);yield break;
                case "blink":AddShield(Mathf.RoundToInt(owner.Health.Max*s.power*scale),s.duration);yield break;
                case "counter":AddGuard(25,s.duration);counterEnd=Time.time+s.duration;counterDamage=n.damage;yield break;
                case "taunt":case "citadel":
                    foreach(var e in Enemies(owner.Center,n.radius)) {ThreatTable.For(e).Force(owner,(e.IsBoss?1.2f:4)*scale);CareerTrials.Record(owner,"taunted",1);}
                    if(s.effect=="taunt") {foreach(var e in Enemies(center,n.radius))Damage(e,n.damage);yield break;}break;
                case "heal":Heal(Wounded(s.range),n.damage,s);yield break;
                case "hot":StartCoroutine(Hot(Wounded(s.range),n.damage,s));yield break;
            }
            if(s.effect=="shield"||s.effect=="ward"||s.effect=="citadel"||s.effect=="wings"||s.effect=="bless"||s.effect=="cleanse"||s.effect=="dawn")
            {
                foreach(var p in Allies(n.radius))
                {
                    var target=For(p);int grace=Prog.Rank("b_grace");float bonus=grace>0?1+(12+4*(grace-1))/100f:1;
                    if(s.effect=="bless"){target.bless=Mathf.RoundToInt(15*scale);target.blessEnd=Time.time+s.duration;}
                    else if(s.effect=="cleanse"||s.effect=="dawn") {if(target.Cleanse())CareerTrials.Record(owner,"cleanse",1);Heal(p,n.damage,s);if(s.effect=="dawn")target.AddShield(Mathf.RoundToInt(p.Health.Max*.25f*bonus),8);}
                    else {target.AddShield(Mathf.RoundToInt(p.Health.Max*s.power*scale*bonus),s.duration);if(s.effect=="ward"||s.effect=="citadel")target.AddGuard(s.effect=="ward"?20:35,s.duration);}
                    CareerEffect.Hit(s,p.Center,dir);
                }yield break;
            }
            var targetList=Enemies(center,Mathf.Max(n.radius,.8f));
            if(s.effect=="rush")
            {
                targetList.Clear();Vector2 path=center-origin;float length=path.sqrMagnitude;
                foreach(var e in EnemyController.Active)if(e!=null&&!e.IsDead){float along=length>.001f?Mathf.Clamp01(Vector2.Dot(e.Center-origin,path)/length):0;if(Vector2.Distance(e.Center,origin+path*along)<=n.radius)targetList.Add(e);}
                for(int i=0;i<4;i++)CareerEffect.Play(s,Vector2.Lerp(origin,center,i/3f),n.radius,dir);
            }
            if(s.effect=="light"||s.effect=="execute")targetList=Enemies(owner.Center,n.range).FindAll(e=>Vector2.Dot((e.Center-owner.Center).normalized,dir)>.35f);
            if(s.effect=="storm")targetList=Enemies(owner.Center,n.range);
            float elemental=1;
            bool element=s.effect=="fire"||s.effect=="ice"||s.effect=="storm";
            if(element && lastElement!=""&&lastElement!=s.effect&&Time.time<elementEnd&&Prog.Rank("m_elements")>0)elemental+=.12f+.04f*(Prog.Rank("m_elements")-1);
            int count=s.effect=="storm"?1:s.hits;
            for(int hit=0;hit<count;hit++)
            {
                if(owner.IsDead||Game.Session.MapId!=castMap)yield break;
                if(s.effect=="rift")targetList=Enemies(center,n.radius);
                int targets=0;
                foreach(var e in targetList)
                {
                    if(e==null||e.IsDead)continue;
                    if((s.effect=="light"||s.effect=="execute")&&targets>=1)break;
                    if(s.effect=="storm"&&targets>=4)break;
                    float mult=elemental;
                    if(s.effect=="execute"&&e.Health.Current<e.Health.Max*.35f)mult*=1.5f;
                    if(s.effect=="flurry")mult*=1+ComboStacks*.03f;
                    if(s.effect=="five"&&hit==4)mult*=3;
                    if(s.effect=="break")broken[e]=Time.time+6;
                    int before=e.Health.Current;Damage(e,Mathf.RoundToInt(n.damage*mult));targets++;
                    if(element && e.Health.Current<before){CareerTrials.Record(owner,s.effect,1);lastElement=s.effect;elementEnd=Time.time+5;}
                    if(s.effect=="eclipse"&&hit==1)e.Freeze(1);
                    if(s.effect=="ice")e.Freeze(1.5f);if(s.effect=="bash")e.Stun(1);
                    if(s.effect=="fire")StartCoroutine(Burn(e,Mathf.RoundToInt(owner.Data.Stats.AttackDamage(owner.Class)*.2f)));
                    CareerEffect.Hit(s,e.Center,dir);
                }
                if(hit<count-1)yield return new WaitForSeconds(s.effect=="rift"?1:s.kind==CareerSkillKind.Awakening?.24f:.16f);
            }
        }
    }
}
