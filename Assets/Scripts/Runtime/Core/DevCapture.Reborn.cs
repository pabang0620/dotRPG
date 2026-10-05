using System.Collections;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator RebornSetup(Career career)
        {
            Game.Flow.NewGame(CareerCatalog.Base(career));yield return Wait(.5f);
            var p=Game.Player;p.Input=new ScriptedInput();p.Place(p.Position,Facing.Right);
            var prog=p.Data.Progression;prog.SetFromServer(40,0);prog.Promote(career);
            foreach(var s in CareerCatalog.For(career))if(s.kind!=CareerSkillKind.Awakening)prog.Learn(s.id);
            for(int stage=0;stage<5;stage++)prog.AdvanceAwakening(stage);
        }
        EnemyController RebornDummy(Vector2 center)
        {
            var stats=Instantiate(Game.Config.skeletonStats);stats.maxHealth=100000;stats.attackDamage=stats.xpReward=0;
            stats.wanderSpeed=stats.chaseSpeed=stats.knockbackSpeed=0;stats.invulnerableTime=.001f;
            var e=EnemyController.Create(stats,CharacterLook.Skeleton,center,Game.World.ObjectsRoot);
            Vector2 correction=center-e.Center;e.transform.position+=(Vector3)correction;e.GetComponent<Rigidbody2D>().position=e.transform.position;
            Destroy(stats,40);return e;
        }
        SkillNumbers RebornNumbers(string id)=>Game.Player.Data.Stats.Skill(Game.Player.Class,0,CareerCatalog.Get(id).Gem,new SkillGem[0]);
        IEnumerator CareerRebornChecks()
        {
            DCheck("reborn verification silent",AudioListener.volume==0);
            foreach(var career in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop})for(int cell=0;cell<4;cell++){
                var sprite=CareerPaintArt.Cell(career,cell);
                DCheck(career+" original atlas cell "+cell,sprite!=null&&sprite.texture.width>=1024&&sprite.texture.filterMode==FilterMode.Point);
            }
            for(int i=0;i<8;i++){
                float a=i*Mathf.PI/4;var dir=new Vector2(Mathf.Cos(a),Mathf.Sin(a));var side=new Vector2(-dir.y,dir.x);
                DCheck("corridor facing "+i,CareerGeometry.Corridor(dir*5+side*.6f,Vector2.zero,dir,8,.7f)&&!CareerGeometry.Corridor(dir*5+side*.8f,Vector2.zero,dir,8,.7f)&&!CareerGeometry.Corridor(-dir,Vector2.zero,dir,8,.7f));
                DCheck("fan facing "+i,CareerGeometry.Fan(dir*2,Vector2.zero,dir,3,120)&&!CareerGeometry.Fan(-dir*2,Vector2.zero,dir,3,120));
            }
            yield return RebornSetup(Career.Fighter);var p=Game.Player;var at=p.Center;
            var landing=RevisionDummy(at+Vector2.right*3);var outside=RevisionDummy(at+Vector2.right*8);
            var drop=CareerCatalog.Get("f_break");StartCoroutine(CareerCombat.For(p).Cast(drop,RebornNumbers(drop.id)));
            yield return Wait(drop.cast+.08f);DCheck("new falling sword waits for landing",landing.Health.Current==landing.Health.Max);
            yield return Wait(.7f);DCheck("new falling sword local impact",landing.Health.Current<landing.Health.Max&&outside.Health.Current==outside.Health.Max);
            Destroy(landing.gameObject);Destroy(outside.gameObject);
            yield return RebornSetup(Career.Arcanist);p=Game.Player;at=p.Center;
            EnemyController first=RebornDummy(at+Vector2.right*2),second=RebornDummy(at+new Vector2(3,1)),third=RebornDummy(at+new Vector2(4,2)),isolated=RebornDummy(at+new Vector2(-5,-5));
            yield return CareerCombat.For(p).Cast(CareerCatalog.Get("m_storm"),RebornNumbers("m_storm"));
            DCheck("lightning connects distinct nearby enemies",first.Health.Current<first.Health.Max&&second.Health.Current<second.Health.Max&&third.Health.Current<third.Health.Max);
            DCheck("lightning cannot jump beyond link range",isolated.Health.Current==isolated.Health.Max);
            int chainDamage=first.Health.Max-first.Health.Current;
            DCheck("lightning never repeats same enemy",chainDamage<=Mathf.CeilToInt(RebornNumbers("m_storm").damage*1.01f));
            foreach(var e in new[]{first,second,third,isolated})Destroy(e.gameObject);

            yield return RebornSetup(Career.Bishop);p=Game.Player;var prog=p.Data.Progression;var save=new SaveData();prog.Capture(save);save.career.questStage=2;save.career.awakened=false;prog.Restore(save,p.Class);
            DCheck("party healing fixture",CareerTrials.Begin()=="");yield return Wait(.1f);
            var friend=CareerTrials.Companion;
            if(friend!=null){
                CareerTrials.Running.enabled=false; // Keep the registered companion; disable unrelated trial hazard timing.
                CareerCombat.For(friend).Cleanse();p.Health.Drain(100);int own=p.Health.Current,ally=friend.Health.Current;
                yield return CareerCombat.For(p).Cast(CareerCatalog.Get("b_heal"),RebornNumbers("b_heal"));
                DCheck("chorus heals both caster and party member",p.Health.Current>own&&friend.Health.Current>ally);
                friend.Health.Drain(80);Vector2 inside=friend.Position;
                yield return CareerCombat.For(p).Cast(CareerCatalog.Get("b_bloom"),RebornNumbers("b_bloom"));
                friend.Place(p.Position+Vector2.right*10,Facing.Left);ally=friend.Health.Current;yield return Wait(1.12f);
                DCheck("sanctuary does not heal outside field",friend.Health.Current==ally);
                friend.Place(inside,Facing.Left);ally=friend.Health.Current;yield return Wait(1.05f);
                DCheck("sanctuary heals when ally reenters",friend.Health.Current>ally);
                CareerCombat.For(p).ResetState();yield return Wait(1.2f);ally=friend.Health.Current;yield return Wait(1.1f);
                DCheck("reset stops healing ticks",friend.Health.Current<=ally);
            }
            if(CareerTrials.Running!=null)Destroy(CareerTrials.Running.gameObject);
            yield return Wait(.3f);
            Log("Reborn original artwork active peak: "+CareerPaintEffect.PeakCount+" / 80");
        }
    }
}
