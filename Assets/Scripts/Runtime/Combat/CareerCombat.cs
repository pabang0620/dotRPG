using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class CareerCombat : MonoBehaviour
    {
        PlayerController owner;
        static readonly Dictionary<CharacterData,CareerCombat> actors=new Dictionary<CharacterData,CareerCombat>();
        readonly Dictionary<EnemyController,float> broken=new Dictionary<EnemyController,float>();
        float shieldEnd,guardEnd,focusEnd,blessEnd,rhythmEnd,retalEnd,retalReady,hotEnd;
        int shield,guard,bless,stacks,hotVersion,focusCrit;
        CareerStatusView view;PlayerController hotSource;
        public Career ShieldCareer {get;private set;}=Career.Guardian;
        public bool GuardVisible=>Time.time<guardEnd;
        public bool FocusVisible=>Time.time<focusEnd;
        public bool BlessVisible=>Time.time<blessEnd;
        public bool HotVisible=>Time.time<hotEnd&&hotSource!=null&&!hotSource.IsDead;
        public bool CounterVisible=>Time.time<counterEnd;
        float counterEnd; int counterDamage; string lastElement="";float elementEnd;
        public bool Cursed {get;private set;} public float SlowUntil {get;private set;}
        public int Shield => Time.time<shieldEnd?shield:0;
        public int ComboStacks => Time.time<rhythmEnd?stacks:0;
        public static bool SecondaryDamage;
        public static string CurrentSkill;
        Progression Prog=>owner.Data.Progression;
        public static CareerCombat For(PlayerController p)
        {var c=p.GetComponent<CareerCombat>();if(c==null)c=p.gameObject.AddComponent<CareerCombat>();c.owner=p;c.view=CareerStatusView.For(p,c);if(p.Data!=null)actors[p.Data]=c;return c;}
        public static int SpeedFor(CharacterData d)=>d.Progression.Rank("f_rhythm")>0&&actors.TryGetValue(d,out var c)&&c!=null ? c.ComboStacks*(3+Mathf.Max(0,d.Progression.Rank("f_rhythm")-1)) : 0;
        void OnDestroy(){if(owner?.Data!=null)actors.Remove(owner.Data);}
        public void AddShield(int amount,float duration,CareerSkill visualSource=null)
        {if(visualSource!=null)ShieldCareer=visualSource.career;shield=Mathf.Max(Shield,amount);shieldEnd=Time.time+duration;CareerTrials.Record(owner,"shield",amount);}
        public void AddGuard(int percent,float duration){guard=Mathf.Max(Time.time<guardEnd?guard:0,percent);guardEnd=Time.time+duration;}
        public void AddCurse(float slow=0){Cursed=true;SlowUntil=Time.time+slow;}
        public bool Cleanse(){bool had=Cursed||Time.time<SlowUntil;Cursed=false;SlowUntil=0;return had;}
        public void ResetState(){StopAllCoroutines();shield=guard=bless=stacks=0;shieldEnd=guardEnd=blessEnd=rhythmEnd=focusEnd=retalEnd=counterEnd=0;hotVersion++;hotSource=null;Cleanse();broken.Clear();lastElement="";view?.Clear();}
        public float MoveScale=>Time.time<SlowUntil?.6f:Time.time<guardEnd&&guard>=30?.7f:1;
        public int Absorb(int damage)
        {
            if(owner.Health.IsInvulnerable)return damage;
            int reduction=(Time.time<guardEnd?guard:0)+Prog.Rank("g_steel")*(Prog.Rank("g_steel")>0?2:0)+(Prog.Rank("g_steel")>0?4:0);
            damage=Mathf.RoundToInt(damage*(1-Mathf.Min(65,reduction)/100f));
            int used=Mathf.Min(Shield,damage);shield-=used;damage-=used;
            if(used>0){CareerTrials.Record(owner,"absorbed",used);view?.ShieldHit();}
            if(Prog.Rank("g_retal")>0 && Time.time>=retalReady){retalEnd=Time.time+4;retalReady=Time.time+2;}
            if(counterEnd>Time.time){counterEnd=0;Counter();}
            return Mathf.Max(0,damage);
        }
        void Update(){
            if(counterEnd>0&&Time.time>=counterEnd){counterEnd=0;Counter();}
        }
        void Counter(){var s=CareerCatalog.Get("g_counter");CareerEffect.Play(s,owner.Center,Mathf.Min(s.radius,1.6f),owner.Facing.ToVector(),.4f,owner);CareerAreaView.Show(s,owner.Center,s.radius,.35f,owner);foreach(var e in Enemies(owner.Center,s.radius))Damage(e,counterDamage,true);CareerTrials.Record(owner,"counter",1);}
        public int ModifyDamage(EnemyController enemy,int amount)
        {
            if(SecondaryDamage)return amount;
            float mult=Time.time<blessEnd?1+bless/100f:1;
            if(broken.TryGetValue(enemy,out var until)&&Time.time<until)mult*=1.15f;
            if(Time.time<retalEnd){mult*=1+(20+5*(Prog.Rank("g_retal")-1))/100f;retalEnd=0;view?.Passive("g_retal");}
            int crit=Prog.Rank("f_edge")>0?8+4*(Prog.Rank("f_edge")-1):0;if(Time.time<focusEnd)crit+=focusCrit;
            if(Random.Range(0,100)<crit){mult*=1.5f;if(Prog.Rank("f_edge")>0)view?.Passive("f_edge");}
            return Mathf.Max(1,Mathf.RoundToInt(amount*mult));
        }
        public void OnLanded(EnemyController enemy)
        {
            if(SecondaryDamage)return;
            if(Prog.Rank("f_rhythm")>0){int previous=ComboStacks;stacks=Mathf.Min(5,ComboStacks+1);rhythmEnd=Time.time+4;if(previous<5&&stacks==5)view?.Passive("f_rhythm");}
            CareerTrials.Record(owner,"hit",1);
        }
        List<EnemyController> Enemies(Vector2 center,float radius)
        {var list=new List<EnemyController>();foreach(var e in EnemyController.Active)if(e!=null&&!e.IsDead&&Vector2.Distance(e.Center,center)<=radius)list.Add(e);list.Sort((a,b)=>(a.Center-center).sqrMagnitude.CompareTo((b.Center-center).sqrMagnitude));return list;}
        List<PlayerController> Allies(float radius)
        {var list=new List<PlayerController>{owner};if(Game.Party!=null)foreach(var p in Game.Party.Members)if(p!=owner&&!p.IsDead&&Vector2.Distance(p.Center,owner.Center)<=radius)list.Add(p);var trial=CareerTrials.Companion;if(trial!=null&&!trial.IsDead&&!list.Contains(trial)&&Vector2.Distance(trial.Center,owner.Center)<=radius)list.Add(trial);return list;}
        void Heal(PlayerController p,int value,CareerSkill s)
        {int rank=Prog.Rank("b_mercy");if(rank>0)value=Mathf.RoundToInt(value*(1+(10+5*(rank-1))/100f));if(For(p).Cursed)value=Mathf.RoundToInt(value*.35f);int actual=p.Health.Heal(value);CareerEffect.Hit(s,p.Center,Vector2.up,owner);if(actual>0)CareerTrials.Record(owner,"heal",actual);}
        void Damage(EnemyController e,int value,bool secondary=false)
        {bool old=SecondaryDamage;SecondaryDamage=secondary;try{e.TakeDamage(new DamageInfo(value,owner.Position,1.2f,Team.Player,owner.gameObject));}finally{SecondaryDamage=old;}}
        IEnumerator Burn(EnemyController e,int value){string map=Game.Session.MapId;for(int i=0;i<3;i++){yield return new WaitForSeconds(1);if(e==null||e.IsDead||!Valid(map))yield break;Damage(e,value,true);CareerEffect.Hit(CareerCatalog.Get("m_fire"),e.Center,Vector2.up,owner);}}
        Vector2 Travel(float distance)
        {var dir=owner.Facing.ToVector();Vector2 start=owner.Position,end=start;for(float d=.15f;d<=distance;d+=.15f){var next=start+dir*d;bool wall=false;foreach(var c in Physics2D.OverlapCircleAll(next+Vector2.up*.22f,.29f))if(!c.isTrigger&&c.GetComponentInParent<PlayerController>()==null&&c.GetComponentInParent<EnemyController>()==null)wall=true;if(wall)break;end=next;}return end;}
        public IEnumerator Cast(CareerSkill s,SkillNumbers n)
        {
            if(!Prog.CareerUnlocked(s))yield break;
            string castMap=Game.Session.MapId;
            Vector2 dir=owner.Facing.ToVector();
            Vector2 spellTarget=Aim(n.range,dir);
            CareerEffect.Charge(s,owner.Center,.9f,dir,s.cast,owner);
            if(s.effect=="fire")CareerPaintEffect.Meteor(s,spellTarget,n.radius,s.cast,owner);
            yield return new WaitForSeconds(s.cast);
            if(owner.IsDead||Game.Session.MapId!=castMap||!Prog.CareerUnlocked(s))yield break;
            if(s.kind==CareerSkillKind.Awakening&&owner.IsLocal){GameEvents.RaiseAwakening(s.name,CareerCatalog.Color(s.career));Game.Camera?.Shake(.1f,.15f);}
            // On a network member only VFX are predicted. The host performs all HP/status mutations.
            bool authority=!PartyNet.IsMember;
            int rank=Mathf.Max(1,Prog.Rank(s.id));float scale=n.careerPotency>0?n.careerPotency:1+.12f*(rank-1);
            if(Rebuilt(s)){yield return RebuiltCast(s,n,dir,castMap,authority,spellTarget);yield break;}
            Vector2 origin=owner.Center, center=origin;
            if(s.effect=="rush"||s.effect=="blink") {if(authority)owner.Place(Travel(s.range),owner.Facing);center=owner.Center;}
            if(s.career==Career.Arcanist&&s.effect!="blink"){
                var aimed=Enemies(owner.Center,s.range).Find(e=>Vector2.Dot((e.Center-owner.Center).normalized,dir)>.35f);
                center=aimed!=null?aimed.Center:owner.Center+dir*s.range;
            }
            else if(s.career==Career.Fighter||s.effect=="bash")center+=dir*(s.effect=="rush"?0:Mathf.Max(0,n.range-n.radius));
            CareerPresentation.Begin(s,origin,center,n,dir,owner);
            if(!authority){yield return PredictPresentation(s,n,origin,center,dir,castMap);yield break;}
            if(s.effect!="fire"&&s.effect!="ice"&&s.effect!="storm")CareerTrials.Record(owner,s.effect,1);
            switch(s.effect)
            {
                case "focus":focusCrit=Mathf.RoundToInt(25*scale);focusEnd=Time.time+s.duration;yield break;
                case "guard":AddGuard(Mathf.RoundToInt(30*scale),s.duration);yield break;
                case "blink":AddShield(Mathf.RoundToInt(owner.Health.Max*s.power*scale),s.duration,s);yield break;
                case "counter":AddGuard(25,s.duration);counterEnd=Time.time+s.duration;counterDamage=n.damage;yield break;
                case "taunt":case "citadel":
                    foreach(var e in Enemies(owner.Center,n.radius)) {ThreatTable.For(e).Force(owner,(e.IsBoss?1.2f:4)*scale);CareerPresentation.Taunted(e,(e.IsBoss?1.2f:4)*scale,owner);CareerTrials.Record(owner,"taunted",1);}
                    if(s.effect=="taunt") {foreach(var e in Enemies(center,n.radius))Damage(e,n.damage);yield break;}break;
            }
            if(s.effect=="shield"||s.effect=="ward"||s.effect=="citadel"||s.effect=="wings"||s.effect=="bless"||s.effect=="cleanse"||s.effect=="dawn")
            {
                foreach(var p in Allies(n.radius))
                {
                    var target=For(p);int grace=Prog.Rank("b_grace");float bonus=grace>0?1+(12+4*(grace-1))/100f:1;
                    if(s.effect=="bless"){target.bless=Mathf.RoundToInt(s.power*scale);target.blessEnd=Time.time+s.duration;}
                    else if(s.effect=="cleanse"||s.effect=="dawn") {if(target.Cleanse())CareerTrials.Record(owner,"cleanse",1);Heal(p,n.damage,s);if(s.effect=="dawn")target.AddShield(Mathf.RoundToInt(p.Health.Max*.25f*bonus),8,s);}
                    else {target.AddShield(Mathf.RoundToInt(p.Health.Max*s.power*scale*bonus),s.duration,s);if(s.effect=="ward"||s.effect=="citadel")target.AddGuard(s.effect=="ward"?20:35,s.duration);}
                    if(s.effect=="shield"||s.effect=="wings"||s.effect=="ward"||s.effect=="citadel")CareerEffect.Hit(s,p.Center,dir,owner);
                    if(s.effect=="dawn"||s.effect=="bless")CareerPresentation.Link(s,owner.Center,p.Center,owner);
                }
                if(s.effect=="dawn")StartCoroutine(Sanctuary(s,n.damage/6,owner.Center,n.radius,castMap,true));
                yield break;
            }
        }
        // Member-side support presentation only; the host owns HP, status and threat changes.
        IEnumerator PredictPresentation(CareerSkill s,SkillNumbers n,Vector2 origin,Vector2 center,Vector2 dir,string map)
        {
            if(s.effect=="dawn")StartCoroutine(Sanctuary(s,n.damage/6,origin,n.radius,map,false));
            if(s.effect=="taunt"||s.effect=="citadel")foreach(var enemy in Enemies(origin,n.radius))CareerPresentation.Taunted(enemy,enemy.IsBoss?1.2f:4,owner);
            if(s.effect=="shield"||s.effect=="ward"||s.effect=="citadel"||s.effect=="wings"||s.effect=="bless"||s.effect=="cleanse"||s.effect=="dawn")
                foreach(var p in Allies(n.radius)){CareerEffect.Hit(s,p.Center,dir,owner);CareerPresentation.Link(s,origin,p.Center,owner,.3f);}
            yield break;
        }
    }
}
