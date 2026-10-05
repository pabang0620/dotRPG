using UnityEngine;

namespace DotRPG
{
    // Visual-only choreography. Colliders, movement, damage and cooldown clocks are never written here.
    [DefaultExecutionOrder(85)]
    public sealed class CareerSkillMotion : MonoBehaviour
    {
        public struct Score
        {
            public float reach, lean, lift, duration, beat, twist;
            public int stage;
            public Score(float r,float l,float h,float d,int s=0,float b=0,float t=0)
            {reach=r;lean=l;lift=h;duration=d;stage=s;beat=b;twist=t;}
        }
        public static Score Profile(string id)
        {
            switch(id){
                case "f_cross":return new Score(.14f,9,0,.36f,0);
                case "f_rush":return new Score(.10f,15,.02f,.35f,2);
                case "f_flurry":return new Score(.07f,8,.04f,.68f,0,.24f,1);
                case "f_break":return new Score(.10f,12,.12f,.56f,2);
                case "f_focus":return new Score(.13f,14,.05f,.55f,2,.18f);
                case "f_execute":return new Score(.09f,10,.02f,.55f,0,.18f);
                case "f_awake":return new Score(.08f,13,.16f,1.22f,2,.25f);
                case "g_guard":return new Score(.04f,6,0,.42f,2);
                case "g_wall":return new Score(.16f,12,.02f,.58f,0);
                case "g_oath":return new Score(.02f,5,.05f,.48f,2);
                case "g_taunt":return new Score(.06f,9,.04f,.48f,1);
                case "g_bash":return new Score(.17f,15,.09f,.42f,1);
                case "g_counter":return new Score(.04f,7,0,.45f,2);
                case "g_awake":return new Score(.12f,16,.19f,.72f,2);
                case "m_fire":return new Score(.12f,12,.02f,.44f,0);
                case "m_ice":return new Score(.05f,7,.03f,.42f,1,.065f);
                case "m_storm":return new Score(.04f,9,.06f,.62f,1,.14f);
                case "m_orbit":return new Score(.05f,8,.08f,.75f,0,.18f,1);
                case "m_veil":return new Score(.09f,13,.07f,.40f,1);
                case "m_rift":return new Score(.03f,10,.09f,.55f,0,0,1);
                case "m_awake":return new Score(.05f,12,.20f,1.16f,2,.35f,1);
                case "b_heal":return new Score(.04f,5,.04f,.50f,1);
                case "b_bloom":return new Score(.02f,4,.06f,.62f,1);
                case "b_cleanse":return new Score(.08f,8,.03f,.48f,0);
                case "b_light":return new Score(.12f,11,.02f,.35f,0);
                case "b_wing":return new Score(.03f,7,.08f,.56f,1);
                case "b_bless":return new Score(.03f,5,.06f,.64f,1,.17f);
                case "b_awake":return new Score(.02f,8,.20f,1.10f,2);
                default:return default;
            }
        }
        PlayerController owner;CharacterAnimator rig;PlayerCombat combat;Transform root;
        SpriteRenderer trail;
        CareerSkill skill;Score score;Vector2 direction;string map;float start,release=-1,end,impactUntil,nextImpact;string heldFrame;
        public bool Active=>skill!=null&&owner!=null&&!owner.IsDead&&Time.time<end&&Game.Session.MapId==map;
        public string SkillId=>Active?skill.id:null;
        public Vector3 VisualOffset=>root!=null?root.localPosition:Vector3.zero;
        public float Phase=>release<0?-Mathf.Clamp01((Time.time-start)/Mathf.Max(.01f,skill.cast)):Mathf.Clamp01((Time.time-release)/score.duration);
        public static CareerSkillMotion Begin(PlayerController p,CareerSkill s,Vector2 dir)
        {
            if(Profile(s.id).duration<=0)return null;
            var m=p.GetComponent<CareerSkillMotion>();if(m==null)m=p.gameObject.AddComponent<CareerSkillMotion>();
            m.Bind(p);m.skill=s;m.score=Profile(s.id);m.direction=dir.normalized;m.map=Game.Session.MapId;
            m.start=Time.time;m.release=-1;m.end=Time.time+s.cast+m.score.duration;
            if(p.Class==CharacterClass.Mage)SkillVisuals.CastCircle(p.Position,CareerCatalog.Color(s.career));
            return m;
        }
        void Bind(PlayerController p)
        {
            if(root!=null)return;owner=p;rig=p.GetComponent<CharacterAnimator>();combat=p.GetComponent<PlayerCombat>();
            root=new GameObject("Skill motion pivot").transform;root.SetParent(rig.Renderer.transform.parent,false);
            rig.Renderer.transform.SetParent(root,false);
            if(combat.WeaponRenderer!=null)combat.WeaponRenderer.transform.SetParent(root,false);
            if(combat.GripRenderer!=null)combat.GripRenderer.transform.SetParent(root,false);
        }
        public void Contact(string id)
        {
            if(!Active||skill.id!=id||Time.time<nextImpact)return;
            heldFrame=Frame(owner.Class==CharacterClass.Warrior);impactUntil=Time.time+.035f;nextImpact=Time.time+.12f;
        }
        public void Release(){if(skill==null)return;release=Time.time;end=release+score.duration;
            if(owner.Class==CharacterClass.Mage)SkillVisuals.StaffFlash(owner.Center+direction*.35f,CareerCatalog.Color(skill.career));
        }
        public void Cancel(){skill=null;impactUntil=0;heldFrame=null;if(trail!=null)trail.enabled=false;if(root!=null){root.localPosition=Vector3.zero;root.localRotation=Quaternion.identity;root.localScale=Vector3.one;}}
        public Facing ViewFacing(Facing fallback)
        {
            if(!Active)return fallback;
            if(skill.id!="f_flurry"||release<0)return FacingExtensions.FromVector(direction,fallback);
            float a=(Time.time-release)/.24f*Mathf.PI*2;var d=new Vector2(direction.x*Mathf.Cos(a)-direction.y*Mathf.Sin(a),direction.x*Mathf.Sin(a)+direction.y*Mathf.Cos(a));
            return FacingExtensions.FromVector(d,fallback);
        }
        // Uses real articulated warrior frames; each beat includes its own recovery instead of frozen attack art.
        public string Frame(bool warrior)
        {
            if(!Active)return null;
            if(Time.time<impactUntil&&heldFrame!=null)return heldFrame;
            float t=Phase;
            float lag=skill.id=="f_break"?.22f:skill.id=="g_bash"?.12f:0;
            if(release>=0&&lag>0){if(Time.time-release<lag)return WarriorAttackMotion.Frame(.25f,2);t=Mathf.Clamp01((Time.time-release-lag)/(score.duration-lag));}
            if(warrior&&skill.career==Career.Guardian&&(skill.id=="g_guard"||skill.id=="g_oath"||skill.id=="g_counter")){
                float brace=release<0?Mathf.Abs(t):1-Mathf.SmoothStep(0,1,t);
                return WarriorAttackMotion.Frame(.24f*brace,2);
            }
            if(!warrior)return release<0?(t<-.65f?"attack":"walk1"):(t<.62f?"attack":t<.82f?"walk3":"idle0");
            int stage=score.stage;float u=Mathf.Abs(t);
            if(release<0)return WarriorAttackMotion.Frame(u*WarriorAttackMotion.Contact,stage);
            if(score.beat>0&&Time.time-release<score.duration-.15f){float age=Time.time-release;stage=(stage+Mathf.FloorToInt(age/score.beat))%3;u=(age%score.beat)/score.beat;}
            if(u>.74f)return WarriorAttackMotion.Frame((u-.74f)/.26f,stage,true);
            return WarriorAttackMotion.Frame(Mathf.Lerp(WarriorAttackMotion.Contact,1,u/.74f),stage);
        }
        public static Vector3 Sample(Score s,Vector2 dir,float u,bool preparation)
        {
            u=Mathf.Clamp01(u);float load,hop;
            if(preparation){load=-Mathf.SmoothStep(0,1,u)*.42f;hop=s.lift*Mathf.Sin(u*Mathf.PI)*.4f;}
            else{float strike=Mathf.Exp(-u*5)*Mathf.Sin(u*Mathf.PI*2.2f);load=strike;hop=s.lift*Mathf.Sin(u*Mathf.PI)*Mathf.Max(0,1-u);}
            return new Vector3(dir.x*s.reach*load,dir.y*s.reach*load+hop,0);
        }
        void LateUpdate()
        {
            if(!Active||combat.IsAttacking||!owner.gameObject.activeInHierarchy){Cancel();return;}
            if(Time.time<impactUntil)return;
            float u=Mathf.Abs(Phase);bool prep=release<0;float beat=u;
            if(!prep&&score.beat>0&&Time.time-release<score.duration-.15f)beat=((Time.time-release)%score.beat)/score.beat;
            var at=Sample(score,direction,beat,prep);at.x=Mathf.Round(at.x*96)/96;at.y=Mathf.Round(at.y*96)/96;root.localPosition=at;
            float hand=direction.x==0?(direction.y>0?-1:1):Mathf.Sign(direction.x);
            float swing=prep?Mathf.SmoothStep(0,1,u)*.45f:-Mathf.Sin(beat*Mathf.PI*2)*Mathf.Exp(-beat*3);
            root.localRotation=Quaternion.Euler(0,0,hand*score.lean*swing);
            float squash=prep?Mathf.Sin(u*Mathf.PI)*.025f:Mathf.Sin(beat*Mathf.PI*2)*.035f*(1-u);
            root.localScale=new Vector3(1+squash,1-squash,1);
            // The same hand-attached flame ribbon used by the basic warrior combo.
            bool blade=owner.Class==CharacterClass.Warrior&&skill.career==Career.Fighter&&!prep;
            if(blade){
                if(trail==null){trail=new GameObject("Career basic sword ribbon").AddComponent<SpriteRenderer>();trail.transform.SetParent(root,false);SilverWarriorPresentation.ApplyMaterial(trail);}
                var key=rig.FrameKey;bool swinging=WarriorAttackMotion.IsSwing(key)&&!WarriorAttackMotion.IsRecovery(key);trail.enabled=swinging;
                if(swinging){var view=ViewFacing(owner.Facing);var pose=SilverWarriorArt.Pose(SilverWarriorArt.ViewKey(view),key);float progress=WarriorAttackMotion.Progress(key);int stage=WarriorAttackMotion.Stage(key);
                    trail.sprite=WarriorFlameSlash.Get(stage,progress);trail.transform.localPosition=WarriorRightHandRig.WorldHand(pose);trail.transform.localRotation=Quaternion.Euler(0,0,pose.swordAngle);
                    float sign=WarriorAttackMotion.ReverseCut(view)?-1:1;if(stage==1)sign=-sign;trail.transform.localScale=new Vector3(1,sign,1);trail.color=new Color(1,1,1,WarriorAttackMotion.TrailAlpha(progress)*.65f);trail.sortingOrder=rig.Renderer.sortingOrder+(pose.rightHandBack?-1:3);
                }
            }else if(trail!=null)trail.enabled=false;
            // Staff gesture is independent from torso recoil and follows the same visual pivot.
            if(owner.Class!=CharacterClass.Warrior&&combat.WeaponRenderer!=null){
                var w=combat.WeaponRenderer.transform;float extension=prep?u:Mathf.Max(0,1-u);
                var held=w.localPosition;var raised=new Vector3(0,.35f,0)+(Vector3)(direction*.3f);
                w.localPosition=Vector3.Lerp(held,raised,Mathf.Sin(extension*Mathf.PI*.5f))+new Vector3(-direction.y,direction.x,0)*(.06f*score.twist*Mathf.Sin(u*Mathf.PI*2));
                w.localRotation*=Quaternion.Euler(0,0,hand*(prep?-24*u:32*Mathf.Sin(u*Mathf.PI)*(1-u)));
            }
        }
        void OnDisable(){Cancel();}
    }
}
