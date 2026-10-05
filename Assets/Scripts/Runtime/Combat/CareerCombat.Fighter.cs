using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class CareerCombat
    {
        // All revised Fighter attacks have independent delivery logic; old red-cut dispatch is retired.
        IEnumerator FighterCast(CareerSkill s,SkillNumbers n,Vector2 dir,string map,bool authority,Vector2 target)
        {
            int version=NewField(s.id);int damage=n.damage;var origin=owner.Center;
            if(Prog.Rank("f_rhythm")>0&&ComboStacks>=3){damage=Mathf.RoundToInt(damage*(1.2f+.05f*(Prog.Rank("f_rhythm")-1)));stacks=0;rhythmEnd=0;view?.Passive("f_rhythm");}
            bool Live()=>FieldValid(map,s.id,version);
            void HitArea(Vector2 center,float radius,int amount){foreach(var e in Enemies(center,radius))StrikeEnemy(s,e,amount,origin,dir,authority);}
            CareerRenewalFx Art(int row,Vector2 at,Vector2 size,float life,float angle=0,int mode=0)=>CareerRenewalFx.Make(s,at,row,size,life,owner,mode,angle);
            var rig=owner.GetComponent<CharacterAnimator>();
            try{
                if(s.effect=="f_wave"){
                    rig?.BeginCareerPose(.27f,0,WarriorAttackMotion.Contact,1);
                    // Moving crescent: collision and artwork advance along the same swept segment.
                    var hit=new HashSet<EnemyController>();float travel=0;var fx=Art(0,origin,new Vector2(.85f,n.radius*2),n.range/12+.15f,CareerRenewalFx.Angle(dir),1);
                    while(travel<n.range&&Live()){
                        float next=Mathf.Min(n.range,travel+12*Time.deltaTime);var a=origin+dir*travel;var b=origin+dir*next;
                        if(fx!=null)fx.Place(b,CareerRenewalFx.Angle(dir));
                        foreach(var e in EnemyController.Active)if(e!=null&&!e.IsDead&&Vector2.Dot(e.Center-origin,dir)>=0&&DistanceToSegment(e.Center,a,b)<=n.radius&&hit.Add(e))StrikeEnemy(s,e,damage,a,dir,authority);
                        travel=next;yield return null;
                    }
                    if(fx!=null){if(Live())fx.Release();else Destroy(fx.gameObject);}yield break;
                }
                if(s.effect=="f_dash"){
                    rig?.BeginCareerPose(.28f,2,WarriorAttackMotion.Contact,1);var start=owner.Position;var end=Travel(n.range);var offset=origin-start;var previous=origin;var hit=new HashSet<EnemyController>();
                    var fx=Art(1,origin,new Vector2(2,n.radius*2),.38f,CareerRenewalFx.Angle(dir),1);
                    for(float t=0;t<.25f&&Live();t+=Time.deltaTime){
                        var next=Vector2.Lerp(start,end,1-Mathf.Pow(1-Mathf.Clamp01((t+Time.deltaTime)/.25f),2));if(!OpenPosition(next))break;
                        if(authority)owner.Place(next,owner.Facing);var at=next+offset;if(fx!=null)fx.Place(at+dir*.35f,CareerRenewalFx.Angle(dir));
                        if(Time.frameCount%3==0)CareerPaintEffect.FighterGhost(s,previous,owner,.2f);
                        foreach(var e in EnemyController.Active)if(e!=null&&!e.IsDead&&DistanceToSegment(e.Center,previous,at)<=n.radius&&hit.Add(e))StrikeEnemy(s,e,damage,previous,dir,authority);
                        previous=at;yield return null;
                    }
                    if(fx!=null){if(Live())fx.Release();else Destroy(fx.gameObject);}yield break;
                }
                if(s.effect=="f_vortex"){
                    CareerAreaView.Show(s,origin,n.radius,.8f,owner);var fx=Art(2,origin,new Vector2(n.radius*2,n.radius*1.6f),.85f,0,1);
                    for(int beat=0;beat<3&&Live();beat++){
                        rig?.BeginCareerPose(.2f,beat,WarriorAttackMotion.Contact,1);if(fx!=null)fx.Place(origin,beat*120);
                        HitArea(origin,n.radius,damage);if(beat<2)yield return new WaitForSeconds(.24f);
                    }
                    if(fx!=null){if(Live())fx.Release(.2f);else Destroy(fx.gameObject);}yield break;
                }
                if(s.effect=="f_drop"){
                    CareerAreaView.Show(s,target,n.radius,.55f,owner);var fx=Art(3,target+Vector2.up*3,new Vector2(1.25f,2),.48f,0,1);
                    for(float t=0;t<.22f&&Live();t+=Time.deltaTime){if(fx!=null)fx.Place(target+Vector2.up*Mathf.Lerp(3,.9f,Mathf.Clamp01((t+Time.deltaTime)/.22f)),0);yield return null;}
                    if(fx!=null)Destroy(fx.gameObject);if(!Live())yield break;
                    rig?.BeginCareerPose(.25f,2,WarriorAttackMotion.Contact,1);Art(3,target+Vector2.up*.9f,new Vector2(2,2.4f),.32f);HitArea(target,n.radius,damage);yield break;
                }
                if(s.effect=="f_eruption"){
                    rig?.BeginCareerPose(.35f,2,WarriorAttackMotion.Contact,1);
                    for(int i=0;i<3&&Live();i++){
                        var at=origin+dir*(1.25f+i*1.5f);CareerAreaView.Show(s,at,n.radius,.22f,owner);
                        Art(4,at+Vector2.up*.45f,new Vector2(n.radius*2,2),.33f);HitArea(at,n.radius,damage);
                        if(i<2)yield return new WaitForSeconds(.18f);
                    }yield break;
                }
                if(s.effect=="f_echo"){
                    for(int i=0;i<3&&Live();i++){
                        var at=origin+dir*(1.5f+i*1.35f);rig?.BeginCareerPose(.18f,i,WarriorAttackMotion.Contact,1);
                        CareerPaintEffect.FighterGhost(s,at-dir*.6f,owner,.3f);Art(5,at,new Vector2(n.radius*1.45f,n.radius*1.8f),.25f,CareerRenewalFx.Angle(dir)+(i==1?25:-20));
                        foreach(var e in Enemies(at,n.radius))if(Vector2.Dot(e.Center-origin,dir)>=0)StrikeEnemy(s,e,damage,origin,dir,authority);
                        if(i<2)yield return new WaitForSeconds(.18f);
                    }yield break;
                }
                if(s.effect=="f_convergence"){
                    CareerAreaView.Show(s,target,n.radius,1.3f,owner);Art(6,target,new Vector2(n.radius*2,n.radius*1.6f),1.1f,0,1);
                    for(int i=0;i<3&&Live();i++){
                        Vector2 at=target+Rotate(dir,90+i*120)*n.radius*.38f;var fx=Art(3,at+Vector2.up*2.5f,new Vector2(1,1.8f),.3f,0,1);
                        for(float t=0;t<.16f&&Live();t+=Time.deltaTime){if(fx!=null)fx.Place(at+Vector2.up*Mathf.Lerp(2.5f,.5f,Mathf.Clamp01((t+Time.deltaTime)/.16f)),0);yield return null;}
                        if(fx!=null)Destroy(fx.gameObject);if(!Live())yield break;
                        Art(3,at+Vector2.up*.3f,new Vector2(2,1.8f),.24f);HitArea(at,n.radius*.65f,damage);
                        // Let the last sword's normal enemy hit immunity end before the final cut.
                        yield return new WaitForSeconds(i==2?.18f:.09f);
                    }
                    if(!Live())yield break;rig?.BeginCareerPose(.3f,2,WarriorAttackMotion.Contact,1);
                    Art(6,target,new Vector2(n.radius*2,n.radius*1.65f),.4f);Art(7,target,Vector2.one*2.2f,.24f);HitArea(target,n.radius,Mathf.RoundToInt(damage*1.5f));
                }
            }finally{rig?.EndCareerPose();}
        }
    }
}
