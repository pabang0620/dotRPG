using System.Collections;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        EnemyController RevisionDummy(Vector2 at)
        {
            var e=RebornDummy(at);e.GetComponent<Rigidbody2D>().simulated=false;
            e.Health.Init(e.Health.Max,e.Health.Max,.15f);return e;
        }
        IEnumerator FighterRevisionChecks(){yield return FighterReforgeChecks();}
        IEnumerator CareerRenewalChecks()
        {
            DCheck("renewal tests muted",AudioListener.volume==0);
            foreach(var c in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop})foreach(var s in CareerCatalog.For(c))
                DCheck(s.id+" delivery metadata",!string.IsNullOrEmpty(s.delivery));
            for(int i=0;i<8;i++){
                float angle=i*Mathf.PI/4;Vector2 dir=new Vector2(Mathf.Cos(angle),Mathf.Sin(angle));
                DCheck("swept blade facing "+i,CareerCombat.BladeSector(dir*2,Vector2.zero,dir,3,-30,30,.1f)&&!CareerCombat.BladeSector(-dir*2,Vector2.zero,dir,3,-30,30,.1f));
                DCheck("spin includes rear "+i,CareerCombat.BladeSector(-dir*2,Vector2.zero,dir,3,-180,180,.1f));
            }
            yield return RebornSetup(Career.Fighter);var p=Game.Player;var at=p.Center;
            EnemyController near=RebornDummy(at+Vector2.right*2),rear=RebornDummy(at+Vector2.left*2);
            yield return CareerCombat.For(p).Cast(CareerCatalog.Get("f_flurry"),RebornNumbers("f_flurry"));
            DCheck("single circle hits front and rear",rear.Health.Current<rear.Health.Max&&near.Health.Current<near.Health.Max);
            Destroy(near.gameObject);Destroy(rear.gameObject);
            yield return RebornSetup(Career.Arcanist);p=Game.Player;at=p.Center;
            var target=RebornDummy(at+Vector2.right*5);var fire=CareerCatalog.Get("m_fire");
            StartCoroutine(CareerCombat.For(p).Cast(fire,RebornNumbers(fire.id)));yield return Wait(fire.cast+.05f);
            DCheck("fire has actual flight time",target.Health.Current==target.Health.Max&&CareerRenewalFx.Count>0);
            yield return Wait(.55f);DCheck("fire hits on arrival",target.Health.Current<target.Health.Max);
            Destroy(target.gameObject);CareerCombat.For(p).ResetState();yield return null;yield return null;
            EnemyController close=RebornDummy(p.Center+Vector2.right*2),outside=RebornDummy(p.Center+Vector2.up*4);
            var ice=CareerCatalog.Get("m_ice");yield return CareerCombat.For(p).Cast(ice,RebornNumbers(ice.id));yield return Wait(.5f);
            DCheck("ice spears pierce and freeze",close.Health.Current<close.Health.Max&&close.IsFrozen);
            DCheck("ice excludes side outside cone",outside.Health.Current==outside.Health.Max);
            Destroy(close.gameObject);Destroy(outside.gameObject);CareerCombat.For(p).ResetState();yield return Wait(.2f);
            yield return RebornSetup(Career.Bishop);p=Game.Player;var prog=p.Data.Progression;var save=new SaveData();prog.Capture(save);save.career.questStage=2;save.career.awakened=false;prog.Restore(save,p.Class);
            DCheck("renewal party fixture",CareerTrials.Begin()=="");yield return Wait(.1f);var ally=CareerTrials.Companion;
            if(ally!=null){
                CareerTrials.Running.enabled=false;CareerCombat.For(ally).Cleanse();ally.Health.Drain(200);ally.Place(p.Position+Vector2.right*3.5f,Facing.Left);int hp=ally.Health.Current;
                var heal=CareerCatalog.Get("b_heal");StartCoroutine(CareerCombat.For(p).Cast(heal,RebornNumbers(heal.id)));yield return Wait(heal.cast+.05f);
                DCheck("healing feather waits for arrival",ally.Health.Current==hp);yield return Wait(.45f);
                DCheck("healing feather reaches ally",ally.Health.Current>hp);
                ally.Place(p.Position+Vector2.right*3,Facing.Left);
                yield return CareerCombat.For(p).Cast(CareerCatalog.Get("b_bloom"),RebornNumbers("b_bloom"));yield return Wait(6.3f);
                DCheck("healing wave status ends with field",!CareerCombat.For(ally).HotVisible&&CareerRenewalFx.Count==0);
            }
            if(CareerTrials.Running!=null)Destroy(CareerTrials.Running.gameObject);
            yield return Wait(.2f);yield return RebornSetup(Career.Guardian);p=Game.Player;
            var wall=CareerCatalog.Get("g_wall");StartCoroutine(CareerCombat.For(p).Cast(wall,RebornNumbers(wall.id)));yield return Wait(wall.cast+.05f);
            DCheck("returning shield protects caster",CareerCombat.For(p).Shield>0&&CareerRenewalFx.Count>0);
            yield return Wait(.8f);DCheck("returning shield terminates",CareerRenewalFx.Count==0);
            var oath=CareerCatalog.Get("g_oath");yield return CareerCombat.For(p).Cast(oath,RebornNumbers(oath.id));yield return Wait(.1f);p.Place(p.Position+Vector2.right,Facing.Right);yield return null;
            var boundary=UnityEngine.Object.FindAnyObjectByType<CareerAreaView>();
            DCheck("moving bastion boundary follows caster",boundary!=null&&Vector2.Distance(boundary.transform.position,p.Center)<.01f);
            CareerCombat.For(p).ResetState();yield return null;yield return null;
            DCheck("reset removes renewal field",CareerRenewalFx.Count==0);
            var foe=RebornDummy(p.Center+Vector2.right*2);
            yield return CareerCombat.For(p).Cast(CareerCatalog.Get("g_awake"),RebornNumbers("g_awake"));yield return Wait(.2f);
            DCheck("guardian awakening deals area damage without old protection",foe.Health.Current<foe.Health.Max&&CareerCombat.For(p).Shield==0&&!CareerCombat.For(p).GuardVisible&&ThreatTable.For(foe).Forced==null);
            yield return Wait(1.3f);
            DCheck("guardian awakening artwork expires",GuardianAwakeningFx.Count==0&&CareerRenewalFx.Count==0);
            Destroy(foe.gameObject);
            string map=Game.Session.MapId;CareerRenewalFx.Projectile(wall,p.Center,0,Vector2.one,Vector2.right,5,p);Game.Session.MapId="renewal_cancel";yield return null;yield return null;
            DCheck("map cancels travelling renewal effect",CareerRenewalFx.Count==0);Game.Session.MapId=map;
            Log("RENEWAL: swept blades, travelling projectiles, delivery-gated healing, moving fields, cancellation; peak "+CareerRenewalFx.Peak+"/80 combined cap");
            Game.Flow.NewGame(CharacterClass.Warrior);yield return Wait(.5f);Game.Player.Input=new ScriptedInput();
        }
    }
}
