using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Shared by authoritative hits, member prediction and geometry tests.</summary>
    public static class CareerGeometry
    {
        public static bool Corridor(Vector2 point,Vector2 start,Vector2 direction,float length,float halfWidth)
        {var d=point-start;var forward=direction.normalized;float along=Vector2.Dot(d,forward);return along>=0&&along<=length&&Mathf.Abs(d.x*forward.y-d.y*forward.x)<=halfWidth;}
        public static bool Fan(Vector2 point,Vector2 start,Vector2 direction,float range,float degrees)
        {var d=point-start;return d.sqrMagnitude<=range*range&&(d.sqrMagnitude<.001f||Vector2.Dot(d.normalized,direction.normalized)>=Mathf.Cos(degrees*.5f*Mathf.Deg2Rad));}
    }
    public sealed partial class CareerCombat
    {
        readonly Dictionary<string,int> sanctuaryVersions=new Dictionary<string,int>();
        Vector2 Aim(float range,Vector2 dir)
        {var e=Enemies(owner.Center,range).Find(x=>Vector2.Dot((x.Center-owner.Center).normalized,dir)>.35f);return e!=null?e.Center:owner.Center+dir*range;}
        bool Valid(string map)=>owner!=null&&!owner.IsDead&&owner.gameObject.activeInHierarchy&&Game.Session.MapId==map;
        static bool Rebuilt(CareerSkill s)=>s.effect=="slash"||s.effect=="flurry"||s.effect=="rush"||s.effect=="break"||s.effect=="execute"||s.effect=="five"||s.effect=="bash"||s.effect=="fire"||s.effect=="ice"||s.effect=="storm"||s.effect=="orbit"||s.effect=="rift"||s.effect=="eclipse"||s.effect=="heal"||s.effect=="hot"||s.effect=="light";
        List<EnemyController> Corridor(Vector2 from,Vector2 dir,float range,float width)
        {var result=new List<EnemyController>();foreach(var e in EnemyController.Active)if(e!=null&&!e.IsDead&&CareerGeometry.Corridor(e.Center,from,dir,range,width))result.Add(e);result.Sort((a,b)=>(a.Center-from).sqrMagnitude.CompareTo((b.Center-from).sqrMagnitude));return result;}
        List<PlayerController> AlliesAt(Vector2 at,float radius)
        {var all=Allies(float.MaxValue);all.RemoveAll(p=>p==null||p.IsDead||Vector2.Distance(p.Center,at)>radius);return all;}
        float ElementPower(CareerSkill s)
        {return (s.effect=="fire"||s.effect=="ice"||s.effect=="storm")&&lastElement!=""&&lastElement!=s.effect&&Time.time<elementEnd&&Prog.Rank("m_elements")>0?1.12f+.04f*(Prog.Rank("m_elements")-1):1;}
        void StrikeEnemy(CareerSkill s,EnemyController e,int damage,Vector2 from,Vector2 dir,bool authority,float multiplier=1,int stage=0)
        {
            if(e==null||e.IsDead)return;
            if(authority){
                if(s.effect=="break")broken[e]=Time.time+s.duration;
                if(s.effect=="execute"&&e.Health.Current<e.Health.Max*.35f)multiplier*=1.5f;
                if(s.effect=="flurry")multiplier*=1+ComboStacks*.03f;
                int before=e.Health.Current;Damage(e,Mathf.RoundToInt(damage*multiplier));
                if(e.Health.Current<before){
                    if(s.effect=="ice")e.Freeze(s.duration);
                    if(s.effect=="bash")e.Stun(s.duration);
                    if(s.effect=="eclipse"&&stage==1)e.Freeze(1);
                    if(s.effect=="fire")StartCoroutine(Burn(e,Mathf.RoundToInt(owner.Data.Stats.AttackDamage(owner.Class)*.2f)));
                    if(s.effect=="fire"||s.effect=="ice"||s.effect=="storm"){
                        CareerTrials.Record(owner,s.effect,1);lastElement=s.effect;elementEnd=Time.time+5;if(multiplier>1)view?.Passive("m_elements");
                    }
                }
            }
            CareerEffect.Hit(s,e.Center,dir,owner);
        }
        IEnumerator RebuiltCast(CareerSkill s,SkillNumbers n,Vector2 dir,string map,bool authority,Vector2 spellTarget)
        {
            Vector2 origin=owner.Center,center=origin;float elemental=ElementPower(s);
            if(s.career==Career.Arcanist)center=spellTarget;
            if(s.effect=="slash"||s.effect=="flurry"){
                for(int i=0;i<s.hits;i++){
                    if(!Valid(map))yield break;
                    // Re-evaluate live targets for each actual swing; a dodged swing cannot hit an old snapshot.
                    CareerEffect.Cut(s,origin+dir*n.range*.42f,n.range*.58f,dir,i,false,owner);
                    foreach(var e in Enemies(origin,n.range))if(CareerGeometry.Fan(e.Center,origin,dir,n.range,s.effect=="slash"?120:140))StrikeEnemy(s,e,n.damage,origin,dir,authority);
                    if(i<s.hits-1)yield return new WaitForSeconds(s.effect=="flurry"?(i==0?.12f:.24f):.18f);
                }yield break;
            }
            if(s.effect=="rush"||s.effect=="bash"){
                Vector2 start=owner.Position,end=Travel(n.range),offset=owner.Center-owner.Position,previous=owner.Center;var hit=new HashSet<EnemyController>();float duration=.22f;
                for(float t=0;t<duration;t+=Time.deltaTime){
                    if(!Valid(map))yield break;
                    Vector2 next=Vector2.Lerp(start,end,Mathf.Clamp01((t+Time.deltaTime)/duration));
                    Vector2 from=previous,to=next+offset;previous=to;
                    if(authority)owner.Place(next,owner.Facing);
                    if(hit.Count==0||t>.1f)CareerPaintEffect.Trail(s,from,dir,owner);
                    Vector2 segment=to-from;float length=segment.magnitude;
                    foreach(var e in EnemyController.Active){
                        if(e==null||e.IsDead)continue;
                        float along=length>.001f?Mathf.Clamp01(Vector2.Dot(e.Center-from,segment)/(length*length)):0;
                        if(Vector2.Distance(e.Center,from+segment*along)<=n.radius&&hit.Add(e))StrikeEnemy(s,e,n.damage,from,dir,authority);
                    }
                    yield return null;
                }
                CareerEffect.Play(s,owner.Center,n.radius,dir,.4f,owner);yield break;
            }
            if(s.effect=="break"){
                var hit=new HashSet<EnemyController>();float travelled=0;const float speed=14;
                var wave=CareerPaintEffect.Wave(s,origin,n.radius,dir,n.range/speed+.1f,owner);
                while(travelled<n.range){
                    if(!Valid(map)){if(wave!=null)Destroy(wave.gameObject);yield break;}
                    float next=Mathf.Min(n.range,travelled+speed*Time.deltaTime);
                    foreach(var e in Corridor(origin+dir*travelled,dir,next-travelled,n.radius))if(hit.Add(e))StrikeEnemy(s,e,n.damage,origin,dir,authority);
                    travelled=next;if(wave!=null)wave.transform.position=origin+dir*travelled;
                    yield return null;
                }yield break;
            }
            if(s.effect=="execute"||s.effect=="five"||s.effect=="light"){
                CareerPaintEffect.Beam(s,origin,dir,n.range,n.radius,s.effect=="five"?.85f:.4f,owner);
                foreach(var e in Corridor(origin,dir,n.range,n.radius))StrikeEnemy(s,e,n.damage,origin,dir,authority);
                yield break;
            }
            if(s.effect=="heal"){
                CareerAreaView.Show(s,origin,n.radius,.5f,owner);CareerEffect.Play(s,origin,2,dir,.65f,owner);
                foreach(var p in AlliesAt(origin,n.radius)){CareerPresentation.Link(s,origin,p.Center,owner,.35f);if(authority)Heal(p,n.damage,s);else CareerEffect.Hit(s,p.Center,dir,owner);}yield break;
            }
            if(s.effect=="hot"){StartCoroutine(Sanctuary(s,n.damage,origin,n.radius,map,authority));yield break;}
            if(s.effect=="storm"){
                var visited=new HashSet<EnemyController>();var e=Enemies(origin,n.range).Find(x=>Vector2.Dot((x.Center-origin).normalized,dir)>.35f);Vector2 last=origin;
                for(int i=0;i<s.hits&&e!=null;i++){
                    if(!Valid(map))yield break;
                    visited.Add(e);CareerPresentation.Link(s,last,e.Center,owner,.22f);CareerEffect.Play(s,e.Center,.85f,dir,.25f,owner);
                    StrikeEnemy(s,e,n.damage,last,dir,authority,elemental);last=e.Center;
                    yield return new WaitForSeconds(.12f);e=Enemies(last,n.radius).Find(x=>!visited.Contains(x));
                }yield break;
            }
            if(s.effect=="orbit"){
                for(int i=0;i<s.hits;i++){
                    if(!Valid(map))yield break;
                    var target=Enemies(origin,n.range).Find(x=>Vector2.Dot((x.Center-origin).normalized,dir)>.15f);
                    StartCoroutine(SeekingStar(s,n.damage,origin,target,center,dir,map,authority,i));
                    if(i<s.hits-1)yield return new WaitForSeconds(.18f);
                }yield break;
            }
            if(s.effect=="rift"||s.effect=="eclipse"){
                float interval=s.effect=="rift"?s.duration/s.hits:.38f;
                CareerAreaView.Show(s,center,n.radius,s.effect=="rift"?s.duration:1.25f,owner);
                CareerPaintEffect.Ground(s,center,n.radius,s.effect=="rift"?s.duration:1.4f,owner);
                for(int i=0;i<s.hits;i++){
                    if(s.effect=="rift")yield return new WaitForSeconds(interval);
                    if(!Valid(map))yield break;
                    CareerAreaView.Pulse(owner,s.id);
                    var art=s.effect=="eclipse"?CareerCatalog.Get(i==0?"m_fire":i==1?"m_ice":"m_storm"):s;
                    CareerEffect.Play(art,center,Mathf.Min(n.radius,2.7f),dir,.5f,owner);
                    foreach(var e in Enemies(center,n.radius))StrikeEnemy(s,e,n.damage,origin,dir,authority,1,i);
                    if(s.effect=="eclipse"&&i<s.hits-1)yield return new WaitForSeconds(interval);
                }yield break;
            }
            // Meteor impact and ice eruption occur precisely when the cast completes.
            CareerAreaView.Show(s,center,n.radius,.5f,owner);CareerEffect.Play(s,center,n.radius,dir,.65f,owner);
            foreach(var e in Enemies(center,n.radius))StrikeEnemy(s,e,n.damage,origin,dir,authority,elemental);
        }
        IEnumerator SeekingStar(CareerSkill s,int damage,Vector2 start,EnemyController target,Vector2 fallback,Vector2 dir,string map,bool authority,int index)
        {
            var star=CareerPaintEffect.Star(s,start,owner);Vector2 previous=start;
            const float flight=.4f;
            for(float t=0;t<flight;t+=Time.deltaTime){
                if(!Valid(map)){if(star!=null)Destroy(star.gameObject);yield break;}
                Vector2 end=target!=null&&!target.IsDead?target.Center:fallback;
                float u=Mathf.Clamp01((t+Time.deltaTime)/flight);
                Vector2 side=new Vector2(-dir.y,dir.x);
                previous=Vector2.Lerp(start,end,u)+side*Mathf.Sin(u*Mathf.PI)*(index-1)*.8f;
                if(star!=null)star.transform.position=previous;yield return null;
            }
            if(star!=null)Destroy(star.gameObject);
            if(Valid(map)&&target!=null&&!target.IsDead)StrikeEnemy(s,target,damage,start,dir,authority);
        }
        IEnumerator Sanctuary(CareerSkill s,int healing,Vector2 center,float radius,string map,bool authority)
        {
            sanctuaryVersions.TryGetValue(s.id,out int previous);int version=previous+1;sanctuaryVersions[s.id]=version;float interval=s.duration/s.hits;
            CareerAreaView.Show(s,center,radius,s.duration,owner);CareerPaintEffect.Ground(s,center,radius,s.duration,owner);
            for(int i=0;i<s.hits;i++){
                yield return new WaitForSeconds(interval);
                if(!Valid(map)||sanctuaryVersions[s.id]!=version)yield break;
                CareerAreaView.Pulse(owner,s.id);
                foreach(var p in AlliesAt(center,radius)){
                    if(authority){var state=For(p);state.hotEnd=Time.time+interval;state.hotSource=owner;Heal(p,healing,s);}
                    else CareerEffect.Hit(s,p.Center,Vector2.up,owner);
                }
            }
        }
    }
}
