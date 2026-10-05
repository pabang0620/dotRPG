using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class CareerCombat
    {
        readonly Dictionary<string,int> renewalVersions=new Dictionary<string,int>();
        int NewField(string id){renewalVersions.TryGetValue(id,out int n);renewalVersions[id]=n+1;return n+1;}
        bool FieldValid(string map,string id,int version)=>Valid(map)&&renewalVersions.TryGetValue(id,out int n)&&n==version;
        static Vector2 Rotate(Vector2 d,float angle){float a=angle*Mathf.Deg2Rad;return new Vector2(d.x*Mathf.Cos(a)-d.y*Mathf.Sin(a),d.x*Mathf.Sin(a)+d.y*Mathf.Cos(a));}
        static float DistanceToSegment(Vector2 p,Vector2 a,Vector2 b){var d=b-a;float t=d.sqrMagnitude>.00001f?Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude):0;return Vector2.Distance(p,a+d*t);}
        public static bool BladeSector(Vector2 point,Vector2 origin,Vector2 dir,float range,float from,float to,float thickness)
        {
            var d=point-origin;if(d.magnitude>range+thickness)return false;
            float angle=Vector2.SignedAngle(dir,d),lo=Mathf.Min(from,to),hi=Mathf.Max(from,to);
            for(int i=-1;i<=1;i++)if(angle+i*360>=lo&&angle+i*360<=hi)return true;
            return DistanceToSegment(point,origin,origin+Rotate(dir,from)*range)<=thickness||DistanceToSegment(point,origin,origin+Rotate(dir,to)*range)<=thickness;
        }
        void Pose(float seconds,int stage=0){owner.GetComponent<CharacterAnimator>()?.BeginCareerPose(seconds,stage);}
        bool OpenPosition(Vector2 at)
        {foreach(var c in Physics2D.OverlapCircleAll(at+Vector2.up*.22f,.29f))if(!c.isTrigger&&c.GetComponentInParent<PlayerController>()==null&&c.GetComponentInParent<EnemyController>()==null)return false;return true;}
        IEnumerator Step(float distance,float seconds,string map,bool authority)
        {
            Vector2 start=owner.Position,end=Travel(distance);
            for(float t=0;t<seconds;t+=Time.deltaTime){if(!Valid(map))yield break;float u=Mathf.Clamp01((t+Time.deltaTime)/seconds);var next=Vector2.Lerp(start,end,u*u*(3-2*u));if(!OpenPosition(next))yield break;if(authority)owner.Place(next,owner.Facing);yield return null;}
        }
        IEnumerator MovingBolt(CareerSkill s,int damage,Vector2 start,Vector2 dir,float range,float width,float speed,int row,string map,bool authority,HashSet<EnemyController> shared=null)
        {
            var hit=shared??new HashSet<EnemyController>();float travelled=0,elemental=ElementPower(s);
            var fx=CareerRenewalFx.Projectile(s,start,row,new Vector2(s.career==Career.Guardian?1.2f:1.5f,s.career==Career.Guardian?1.2f:width*2.2f),dir,range/speed+.15f,owner);
            while(travelled<range){
                if(!Valid(map)){if(fx!=null)Destroy(fx.gameObject);yield break;}
                float next=Mathf.Min(range,travelled+speed*Time.deltaTime);Vector2 a=start+dir*travelled,b=start+dir*next;
                if(fx!=null)fx.Place(b,CareerRenewalFx.Angle(dir));
                foreach(var e in EnemyController.Active)if(e!=null&&!e.IsDead&&!hit.Contains(e)&&DistanceToSegment(e.Center,a,b)<=width){hit.Add(e);StrikeEnemy(s,e,damage,a,dir,authority,elemental);}
                travelled=next;yield return null;
            }
            if(fx!=null){if(s.career==Career.Fighter)fx.Release(.12f);else Destroy(fx.gameObject);}
        }
        IEnumerator RadialWave(CareerSkill s,Vector2 at,float radius,int damage,string map,bool authority,bool taunt=false,bool secondary=false)
        {
            const float seconds=.36f;float previous=0;var hit=new HashSet<EnemyController>();
            var fx=CareerRenewalFx.Make(s,at,1,Vector2.one*.2f,seconds+.12f,owner,0);
            for(float t=0;t<seconds;t+=Time.deltaTime){
                if(!Valid(map)){if(fx!=null)Destroy(fx.gameObject);yield break;}
                float r=radius*Mathf.Clamp01((t+Time.deltaTime)/seconds);if(fx!=null)fx.Resize(new Vector2(r*2,r*1.45f));
                foreach(var e in Enemies(at,r+.1f))if(!hit.Contains(e)){
                    hit.Add(e);if(authority){
                        if(taunt){float duration=e.IsBoss?1.2f:4;ThreatTable.For(e).Force(owner,duration);CareerPresentation.Taunted(e,duration,owner);CareerTrials.Record(owner,"taunted",1);}
                        int hp=e.Health.Current;Damage(e,damage,secondary);if(e.Health.Current<hp)CareerImpact.Show(s,e,(e.Center-at).normalized,owner);
                    }else CareerEffect.Hit(s,e.Center,(e.Center-at).normalized,owner);
                }
                previous=r;yield return null;
            }
        }
        void ApplyShield(PlayerController p,CareerSkill s,int remaining,float scale)
        {ApplyProtection(p,s,remaining,scale);}
        void ApplyProtection(PlayerController p,CareerSkill s,float remaining,float scale)
        {
            var state=For(p);int rank=Prog.Rank("b_grace");float bonus=rank>0?1+(12+4*(rank-1))/100f:1;
            state.AddShield(Mathf.RoundToInt(p.Health.Max*s.power*scale*bonus),Mathf.Max(.05f,remaining),s);
            if(s.effect=="ward"||s.effect=="citadel")state.AddGuard(s.effect=="ward"?20:35,remaining);
            CareerEffect.Hit(s,p.Center,Vector2.up,owner);
        }
        IEnumerator ReturningShield(CareerSkill s,SkillNumbers n,Vector2 dir,string map,bool authority)
        {
            Pose(.3f);Vector2 start=owner.Center,end=start+dir*n.radius;float scale=n.careerPotency>0?n.careerPotency:1;
            var allies=new HashSet<PlayerController>();var enemies=new HashSet<EnemyController>();if(authority)ApplyProtection(owner,s,s.duration,scale);allies.Add(owner);
            var fx=CareerRenewalFx.Projectile(s,start,0,Vector2.one*1.25f,dir,.75f,owner);Vector2 previous=start;
            for(float t=0;t<.65f;t+=Time.deltaTime){
                if(!Valid(map)){if(fx!=null)Destroy(fx.gameObject);yield break;}
                float u=Mathf.Clamp01((t+Time.deltaTime)/.65f);Vector2 at=u<.5f?Vector2.Lerp(start,end,u*2):Vector2.Lerp(end,owner.Center,(u-.5f)*2);
                if(fx!=null)fx.Place(at,t*720);
                foreach(var p in Allies(float.MaxValue))if(p!=null&&!p.IsDead&&!allies.Contains(p)&&DistanceToSegment(p.Center,previous,at)<.95f){allies.Add(p);if(authority)ApplyProtection(p,s,s.duration,scale);else CareerEffect.Hit(s,p.Center,dir,owner);}
                foreach(var e in EnemyController.Active)if(e!=null&&!e.IsDead&&!enemies.Contains(e)&&DistanceToSegment(e.Center,previous,at)<.75f){enemies.Add(e);if(authority)e.Stun(e.IsBoss?.1f:.3f);CareerEffect.Hit(s,e.Center,dir,owner);}
                previous=at;yield return null;
            }
            if(fx!=null)Destroy(fx.gameObject);
        }
        IEnumerator ProtectionField(CareerSkill s,SkillNumbers n,Vector2 origin,string map,bool authority,bool moving)
        {
            int version=NewField(s.id);float elapsed=0,scale=n.careerPotency>0?n.careerPotency:1;var protectedAllies=new HashSet<PlayerController>();
            var fx=CareerRenewalFx.Zone(s,origin,n.radius,s.duration,owner,moving);float nextTaunt=0;
            while(elapsed<s.duration&&FieldValid(map,s.id,version)){
                var at=moving?owner.Center:origin;
                foreach(var p in AlliesAt(at,n.radius))if(protectedAllies.Add(p)){if(authority)ApplyProtection(p,s,s.duration-elapsed,scale);else CareerEffect.Hit(s,p.Center,Vector2.up,owner);}
                if(s.effect=="citadel"&&elapsed>=nextTaunt){nextTaunt=elapsed+2;foreach(var e in Enemies(at,n.radius)){float duration=Mathf.Min(s.duration-elapsed,e.IsBoss?1.2f:4);if(authority){ThreatTable.For(e).Force(owner,duration);CareerTrials.Record(owner,"taunted",1);}CareerPresentation.Taunted(e,duration,owner);}}
                if(elapsed==0)CareerAreaView.Show(s,at,n.radius,s.duration,owner,moving);
                yield return new WaitForSeconds(.2f);elapsed+=.2f;
            }
            if(fx!=null)Destroy(fx.gameObject);
        }
        IEnumerator FireLance(CareerSkill s,SkillNumbers n,Vector2 at,Vector2 target,string map,bool authority)
        {
            Vector2 delta=target-at,dir=delta.normalized;float length=Mathf.Min(n.range,delta.magnitude),travelled=0;Vector2 center=at;
            var fx=CareerRenewalFx.Projectile(s,at,0,new Vector2(1.5f,.7f),dir,length/12+.2f,owner);
            bool impact=false;
            while(travelled<length&&!impact){
                if(!Valid(map)){if(fx!=null)Destroy(fx.gameObject);yield break;}
                float next=Mathf.Min(length,travelled+12*Time.deltaTime);Vector2 from=at+dir*travelled;center=at+dir*next;
                EnemyController nearest=null;float best=float.MaxValue;
                foreach(var e in EnemyController.Active)if(e!=null&&!e.IsDead&&DistanceToSegment(e.Center,from,center)<.4f&&(e.Center-from).sqrMagnitude<best){nearest=e;best=(e.Center-from).sqrMagnitude;}
                if(nearest!=null){center=nearest.Center;impact=true;}if(fx!=null)fx.Place(center,CareerRenewalFx.Angle(dir));travelled=next;yield return null;
            }
            if(fx!=null)Destroy(fx.gameObject);if(!Valid(map))yield break;
            CareerRenewalFx.Burst(s,center,0,n.radius,owner);CareerAreaView.Show(s,center,n.radius,.25f,owner);
            foreach(var e in Enemies(center,n.radius))StrikeEnemy(s,e,n.damage,at,dir,authority,ElementPower(s));
        }
        IEnumerator Lightning(CareerSkill s,SkillNumbers n,Vector2 dir,string map,bool authority)
        {
            var seen=new HashSet<EnemyController>();Vector2 from=owner.Center;var enemy=Enemies(from,n.range).Find(e=>Vector2.Dot((e.Center-from).normalized,dir)>.25f);
            for(int i=0;i<s.hits&&enemy!=null;i++){
                seen.Add(enemy);Vector2 start=from;float duration=Mathf.Clamp(Vector2.Distance(start,enemy.Center)/26,.05f,.18f);
                var bolt=CareerRenewalFx.Projectile(s,start,2,new Vector2(.9f,.5f),dir,duration+.12f,owner);
                for(float t=0;t<duration;t+=Time.deltaTime){
                    if(!Valid(map)||enemy==null||enemy.IsDead){if(bolt!=null)Destroy(bolt.gameObject);yield break;}
                    from=Vector2.Lerp(start,enemy.Center,Mathf.Clamp01((t+Time.deltaTime)/duration));if(bolt!=null)bolt.Place(from,CareerRenewalFx.Angle(enemy.Center-start)+45);yield return null;
                }
                if(bolt!=null)Destroy(bolt.gameObject);if(!Valid(map)||enemy==null||enemy.IsDead)yield break;
                CareerPresentation.Link(s,start,enemy.Center,owner,.12f);StrikeEnemy(s,enemy,n.damage,start,(enemy.Center-start).normalized,authority,ElementPower(s));from=enemy.Center;
                yield return new WaitForSeconds(.07f);enemy=Enemies(from,n.radius).Find(e=>!seen.Contains(e));
            }
        }
        IEnumerator AstralSalvo(CareerSkill s,SkillNumbers n,Vector2 dir,Vector2 target,string map,bool authority)
        {
            var side=new Vector2(-dir.y,dir.x);
            for(int i=0;i<3;i++){
                if(!Valid(map))yield break;Vector2 start=owner.Center+side*(i-1)*.45f,end=target+side*(i-1)*n.radius*.4f;
                StartCoroutine(FallingStar(s,n.damage,start,end,.35f+i*.035f,n.radius,map,authority));yield return new WaitForSeconds(.13f);
            }
        }
        IEnumerator FallingStar(CareerSkill s,int damage,Vector2 start,Vector2 end,float seconds,float radius,string map,bool authority)
        {
            var fx=CareerRenewalFx.Projectile(s,start,3,Vector2.one*.75f,(end-start).normalized,seconds+.1f,owner);
            for(float t=0;t<seconds;t+=Time.deltaTime){if(!Valid(map)){if(fx!=null)Destroy(fx.gameObject);yield break;}float u=Mathf.Clamp01((t+Time.deltaTime)/seconds);var at=Vector2.Lerp(start,end,u)+Vector2.up*Mathf.Sin(u*Mathf.PI)*.7f;if(fx!=null)fx.Place(at,t*300);yield return null;}
            if(fx!=null)Destroy(fx.gameObject);if(!Valid(map))yield break;
            CareerRenewalFx.Burst(s,end,3,radius,owner);foreach(var e in Enemies(end,radius))StrikeEnemy(s,e,damage,start,(end-start).normalized,authority);
        }
        IEnumerator Vortex(CareerSkill s,SkillNumbers n,Vector2 start,Vector2 target,string map,bool authority)
        {
            int version=NewField(s.id);var fx=CareerRenewalFx.Zone(s,start,n.radius,s.duration,owner);
            var boundary=CareerAreaView.Show(s,start,n.radius,s.duration,owner);
            for(int i=0;i<s.hits;i++){
                float interval=s.duration/s.hits;
                for(float t=0;t<interval;t+=Time.deltaTime){if(!FieldValid(map,s.id,version)){if(fx!=null)Destroy(fx.gameObject);if(boundary!=null)Destroy(boundary.gameObject);yield break;}var at=Vector2.Lerp(start,target,(i*interval+t)/s.duration);if(fx!=null)fx.Place(at,0);if(boundary!=null)boundary.transform.position=at;yield return null;}
                Vector2 center=Vector2.Lerp(start,target,(i+1)/(float)s.hits);
                CareerRenewalFx.Burst(s,center,3,n.radius*.65f,owner);CareerAreaView.Show(s,center,n.radius,.22f,owner);
                foreach(var e in Enemies(center,n.radius))StrikeEnemy(s,e,n.damage,start,(center-start).normalized,authority);
            }
            if(fx!=null)Destroy(fx.gameObject);
            if(boundary!=null)Destroy(boundary.gameObject);
        }
        IEnumerator HealingFeather(CareerSkill s,PlayerController target,int amount,bool shield,string map,bool authority,float scale=1)
        {
            if(target==null||target.IsDead)yield break;Vector2 start=owner.Center;float duration=Mathf.Clamp(Vector2.Distance(start,target.Center)/9,.03f,.4f);
            var fx=CareerRenewalFx.Projectile(s,start,0,new Vector2(.7f,.5f),Vector2.up,duration+.1f,owner);
            for(float t=0;t<duration;t+=Time.deltaTime){if(!Valid(map)||target==null||target.IsDead){if(fx!=null)Destroy(fx.gameObject);yield break;}float u=Mathf.Clamp01((t+Time.deltaTime)/duration);var at=Vector2.Lerp(start,target.Center,u)+Vector2.up*Mathf.Sin(u*Mathf.PI)*.25f;if(fx!=null)fx.Place(at,CareerRenewalFx.Angle(target.Center-start)+180);yield return null;}
            if(fx!=null)Destroy(fx.gameObject);if(!Valid(map)||target==null||target.IsDead)yield break;
            if(authority){if(shield)ApplyProtection(target,s,s.duration,scale);else Heal(target,amount,s);}else CareerEffect.Hit(s,target.Center,Vector2.up,owner);
        }
        IEnumerator HealingWave(CareerSkill s,int amount,Vector2 at,float radius,float seconds,string map,bool authority,bool cleanse=false,bool follows=false,int version=0)
        {
            var recipients=new HashSet<PlayerController>();var fx=CareerRenewalFx.Make(s,at,2,Vector2.one*.2f,seconds+.15f,owner,0);
            for(float t=0;t<seconds;t+=Time.deltaTime){
                if(!Valid(map)||(version>0&&!FieldValid(map,s.id,version))){if(fx!=null)Destroy(fx.gameObject);yield break;}
                if(follows)at=owner.Center;
                if(fx!=null)fx.Place(at,0);
                float r=radius*Mathf.Clamp01((t+Time.deltaTime)/seconds);if(fx!=null)fx.Resize(new Vector2(r*2,r*1.5f));
                foreach(var p in AlliesAt(at,r))if(recipients.Add(p)){
                    if(authority){if(cleanse&&For(p).Cleanse())CareerTrials.Record(owner,"cleanse",1);if(s.effect=="hot"||s.effect=="dawn"){For(p).hotSource=owner;For(p).hotEnd=Time.time+Mathf.Max(0,seconds-t);}Heal(p,amount,s);}
                    else CareerEffect.Hit(s,p.Center,Vector2.up,owner);
                }yield return null;
            }
            if(fx!=null)Destroy(fx.gameObject);
        }
        IEnumerator RenewalSanctuary(CareerSkill s,SkillNumbers n,Vector2 origin,string map,bool authority,bool follows)
        {
            int version=NewField(s.id);var fx=CareerRenewalFx.Zone(s,origin,n.radius,s.duration,owner,follows);
            CareerAreaView.Show(s,origin,n.radius,s.duration,owner,follows);
            float interval=s.duration/s.hits;int amount=s.effect=="dawn"?n.damage/6:n.damage;
            for(int i=0;i<s.hits;i++){
                if(!FieldValid(map,s.id,version))break;
                var center=follows?owner.Center:origin;CareerRenewalFx.Pulse(owner,s.id);
                yield return HealingWave(s,amount,center,n.radius,interval,map,authority,false,follows,version);
            }
            if(fx!=null)Destroy(fx.gameObject);
        }
        IEnumerator RenewalCast(CareerSkill s,SkillNumbers n,Vector2 dir,string map,bool authority,Vector2 aimed)
        {
            Vector2 origin=owner.Center;float scale=n.careerPotency>0?n.careerPotency:1;
            switch(s.effect){
                case "guard":if(authority)AddGuard(Mathf.RoundToInt(30*scale),s.duration);CareerRenewalFx.Make(s,owner.Center+dir*.3f,0,new Vector2(1.25f,1.65f),.4f,owner,0,0,true);yield break;
                case "shield":yield return ReturningShield(s,n,dir,map,authority);yield break;
                case "ward":StartCoroutine(ProtectionField(s,n,origin,map,authority,true));yield break;
                case "shieldquake":yield return GuardianQuake(s,n,map,authority);yield break;
                case "citadel":StartCoroutine(ProtectionField(s,n,origin,map,authority,false));yield break;
                case "taunt":CareerAreaView.Show(s,origin,n.radius,.4f,owner);yield return RadialWave(s,origin,n.radius,n.damage,map,authority,true);yield break;
                case "bash":yield return Step(.75f,.12f,map,authority);if(!Valid(map))yield break;Pose(.22f,2);CareerRenewalFx.Make(s,owner.Center+dir*.75f,0,new Vector2(1.9f,1.9f),.32f,owner);foreach(var e in Enemies(owner.Center,n.range))if(CareerGeometry.Fan(e.Center,owner.Center,dir,n.range,110))StrikeEnemy(s,e,n.damage,owner.Center,dir,authority);yield break;
                case "counter":if(authority){AddGuard(25,s.duration);counterEnd=Time.time+s.duration;counterDamage=n.damage;}CareerRenewalFx.Make(s,origin,0,Vector2.one*1.3f,.35f,owner);yield break;
                case "fire":yield return FireLance(s,n,origin,aimed,map,authority);yield break;
                case "ice":var shared=new HashSet<EnemyController>();for(int i=-1;i<=1;i++){if(!Valid(map))yield break;var ray=Rotate(dir,i*22);StartCoroutine(MovingBolt(s,n.damage,origin,ray,n.range,.34f,11,1,map,authority,shared));yield return new WaitForSeconds(.065f);}yield break;
                case "storm":yield return Lightning(s,n,dir,map,authority);yield break;
                case "orbit":yield return AstralSalvo(s,n,dir,aimed,map,authority);yield break;
                case "blink":if(authority){owner.Place(Travel(n.range),owner.Facing);AddShield(Mathf.RoundToInt(owner.Health.Max*s.power*scale),s.duration,s);}CareerRenewalFx.Burst(s,origin,3,1.1f,owner);yield return new WaitForSeconds(.18f);if(Valid(map)){CareerRenewalFx.Burst(s,origin,3,1.5f,owner);foreach(var e in Enemies(origin,1.5f))StrikeEnemy(s,e,n.damage,origin,dir,authority);}yield break;
                case "rift":StartCoroutine(Vortex(s,n,origin,aimed,map,authority));yield break;
                case "eclipse":
                    CareerRenewalFx.Zone(s,aimed,n.radius,1.5f,owner);
                    for(int i=0;i<3;i++){if(!Valid(map))yield break;Vector2 point=aimed+Rotate(dir,90+i*120)*n.radius*.35f;CareerRenewalFx.Burst(CareerCatalog.Get(i==0?"m_fire":i==1?"m_ice":"m_storm"),point,i,n.radius*.65f,owner);foreach(var e in Enemies(point,n.radius*.65f))StrikeEnemy(s,e,n.damage,origin,(point-origin).normalized,authority,1,i);if(i<2)yield return new WaitForSeconds(.35f);}yield break;
                case "heal":foreach(var p in AlliesAt(origin,n.radius))StartCoroutine(HealingFeather(s,p,n.damage,false,map,authority));yield return new WaitForSeconds(.42f);yield break;
                case "hot":StartCoroutine(RenewalSanctuary(s,n,origin,map,authority,false));yield break;
                case "cleanse":yield return HealingWave(s,n.damage,origin,n.radius,.32f,map,authority,true);yield break;
                case "light":yield return MovingBolt(s,n.damage,origin,dir,n.range,n.radius,20,0,map,authority);yield break;
                case "wings":foreach(var p in AlliesAt(origin,n.radius))StartCoroutine(HealingFeather(s,p,0,true,map,authority,scale));yield return new WaitForSeconds(.42f);yield break;
                case "bless":Vector2 from=origin;foreach(var p in AlliesAt(origin,n.radius)){if(!Valid(map))yield break;CareerPresentation.Link(s,from,p.Center,owner,.18f);if(authority){var st=For(p);st.bless=Mathf.RoundToInt(s.power*scale);st.blessEnd=Time.time+s.duration;}CareerRenewalFx.Make(s,p.Center,1,new Vector2(1.5f,1.25f),.3f,owner);from=p.Center;yield return new WaitForSeconds(.085f);}yield break;
                case "dawn":
                    CareerRenewalFx.Make(s,origin,3,new Vector2(3.5f,4.2f),.8f,owner);
                    foreach(var p in AlliesAt(origin,n.radius)){if(authority){if(For(p).Cleanse())CareerTrials.Record(owner,"cleanse",1);Heal(p,n.damage,s);int grace=Prog.Rank("b_grace");float bonus=grace>0?1+(12+4*(grace-1))/100f:1;For(p).AddShield(Mathf.RoundToInt(p.Health.Max*.25f*bonus),s.duration,s);}CareerPresentation.Link(s,origin,p.Center,owner,.25f);}
                    StartCoroutine(RenewalSanctuary(s,n,origin,map,authority,true));yield break;
            }
        }
    }
}
