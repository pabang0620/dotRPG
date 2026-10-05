using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator CareerMotionChecks()
        {
            DCheck("motion verification is silent",AudioListener.volume==0);
            var skills=CareerCatalog.All.Where(s=>s.kind!=CareerSkillKind.Passive).ToArray();
            DCheck("28 individual motion scores",skills.Length==28&&skills.All(s=>CareerSkillMotion.Profile(s.id).duration>0));
            foreach(var s in skills)for(int f=0;f<8;f++){
                var score=CareerSkillMotion.Profile(s.id);var dir=((Facing)f).ToVector();
                var a=CareerSkillMotion.Sample(score,dir,0,false);var b=CareerSkillMotion.Sample(score,dir,1,false);
                bool good=a.sqrMagnitude<.00001f&&b.sqrMagnitude<.00001f;
                for(int i=0;i<=20;i++){var v=CareerSkillMotion.Sample(score,dir,i/20f,false);good&=!float.IsNaN(v.x)&&!float.IsNaN(v.y)&&v.magnitude<.3f;}
                DCheck(s.id+" bounded visual motion direction "+f,good);
            }
            string dest=Path.Combine(folder,"motion");Directory.CreateDirectory(dest);
            foreach(var career in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop}){
                yield return RebornSetup(career);yield return Wait(3);
                var player=Game.Player;var origin=player.Position;var rig=player.GetComponent<CharacterAnimator>();
                var enemy=RebornDummy(player.Center+Vector2.right*1.8f);
                foreach(var s in CareerCatalog.For(career).Where(x=>x.kind!=CareerSkillKind.Passive)){
                    CareerCombat.For(player).ResetState();player.Place(origin,Facing.Right);player.Health.Drain(20);
                    enemy.transform.position=player.Center+Vector2.right*1.8f;enemy.GetComponent<Rigidbody2D>().position=enemy.transform.position;
                    int meshBefore=CareerLivingFx.MeshUpdates;
                    StartCoroutine(CareerCombat.For(player).Cast(s,RebornNumbers(s.id)));
                    float maxOffset=0;var frames=new System.Collections.Generic.HashSet<string>();bool running=false;
                    for(int i=0;i<44;i++){
                        yield return Wait(.06f);var motion=player.GetComponent<CareerSkillMotion>();
                        if(motion!=null){running|=motion.Active;maxOffset=Mathf.Max(maxOffset,motion.VisualOffset.magnitude);}
                        frames.Add(rig.FrameKey);
                        RenderRegion(Path.Combine(dest,s.id+"_"+i.ToString("00")+".png"),new Rect(origin.x-3,origin.y-3,9,7),48);
                    }
                    var m=player.GetComponent<CareerSkillMotion>();
                    if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-careerLivingOnly")>=0)DCheck(s.id+" animated mesh executes in actual cast",CareerLivingFx.MeshUpdates>meshBefore);
                    DCheck(s.id+" live body motion connected",running&&maxOffset>.001f&&frames.Count>=2);
                    DCheck(s.id+" pose returns to neutral",m!=null&&!m.Active&&m.VisualOffset.sqrMagnitude<.00001f);
                    bool moving=s.id=="f_rush"||s.id=="g_bash"||s.id=="m_veil";
                    DCheck(s.id+" collider motion unchanged",moving||Vector2.Distance(origin,player.Position)<.01f);
                    Log(s.id+" frames="+frames.Count+" max visual offset="+maxOffset);
                }
                Destroy(enemy.gameObject);
                var first=CareerCatalog.For(career).First(s=>s.kind==CareerSkillKind.Active);
                CareerSkillMotion.Begin(player,first,Vector2.right);yield return null;CareerCombat.For(player).ResetState();
                DCheck(career+" reset clears motion",!player.GetComponent<CareerSkillMotion>().Active);
                CareerSkillMotion.Begin(player,first,Vector2.right);player.Skills.ResetCooldowns();
                string basic=career==Career.Fighter||career==Career.Guardian?"crush":"lance";
                bool equipped=player.Data.Progression.EquipSkill(0,basic);player.Skills.TryCast(0);yield return Wait(.1f);
                DCheck(career+" base skill interrupts promoted pose",equipped&&!player.GetComponent<CareerSkillMotion>().Active);
                CareerSkillMotion.Begin(player,first,Vector2.right);string map=Game.Session.MapId;Game.Session.MapId="motion_cancel";yield return null;yield return null;
                DCheck(career+" map change clears motion",!player.GetComponent<CareerSkillMotion>().Active&&player.GetComponent<CareerSkillMotion>().VisualOffset==Vector3.zero);Game.Session.MapId=map;
            }
            Log($"CAREER MOTION RESULTS: {dgnPassed} passed, {dgnFailed} failed");
        }
    }
}
