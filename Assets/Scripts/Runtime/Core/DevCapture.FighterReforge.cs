using System.Collections;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator FighterReforgeLifecycleChecks()
        {
            yield return RebornSetup(Career.Fighter);var p=Game.Player;var state=CareerCombat.For(p);var skill=CareerCatalog.Get("f_break");var foe=RevisionDummy(p.Center+Vector2.right*3);
            StartCoroutine(state.Cast(skill,RebornNumbers(skill.id)));yield return Wait(skill.cast+.05f);string map=Game.Session.MapId;Game.Session.MapId="reforge_map_cancel";yield return Wait(.6f);
            DCheck("map change cancels falling sword before impact",foe.Health.Current==foe.Health.Max&&CareerRenewalFx.Count==0&&CareerAreaView.Count==0);Game.Session.MapId=map;Destroy(foe.gameObject);
            yield return RebornSetup(Career.Fighter);p=Game.Player;state=CareerCombat.For(p);foe=RevisionDummy(p.Center+Vector2.right*3);
            StartCoroutine(state.Cast(skill,RebornNumbers(skill.id)));yield return Wait(skill.cast+.05f);
            p.Health.Init(p.Health.Max,p.Health.Max,0);p.TakeDamage(new DamageInfo(999999,p.Position,0,Team.Enemy,null){unblockable=true});yield return Wait(.6f);
            DCheck("death cancels pending sword and artwork",p.IsDead&&foe.Health.Current==foe.Health.Max&&CareerRenewalFx.Count==0&&CareerPaintEffect.ActiveCount==0);Destroy(foe.gameObject);
            yield return RebornSetup(Career.Fighter);p=Game.Player;state=CareerCombat.For(p);foe=RevisionDummy(p.Center+Vector2.right*2);
            foe.TakeDamage(new DamageInfo(10,p.Position,0,Team.Player,p.gameObject));DCheck("basic hit charges new passive",state.ComboStacks==1);yield return Wait(6.15f);DCheck("unused sword force expires",state.ComboStacks==0);Destroy(foe.gameObject);
            Game.Flow.NewGame(CharacterClass.Warrior);yield return Wait(.5f);p=Game.Player;p.Input=new ScriptedInput();int starts=CareerRenewalFx.Started;
            yield return CareerCombat.For(p).Cast(CareerCatalog.Get("f_awake"),RebornNumbers("f_awake"));
            DCheck("base warrior cannot cast new Fighter awakening",CareerRenewalFx.Started==starts&&!p.Data.Progression.Awakened);
        }
        IEnumerator FighterReforgeChecks()
        {
            DCheck("reforge verification muted",AudioListener.volume==0);
            var all=CareerCatalog.For(Career.Fighter);
            DCheck("Fighter six attacks two passives one awakening",all.Count(s=>s.kind==CareerSkillKind.Active)==6&&all.Count(s=>s.kind==CareerSkillKind.Passive)==2&&all.Count(s=>s.kind==CareerSkillKind.Awakening)==1);
            DCheck("all former Fighter deliveries replaced",all.Where(s=>s.kind!=CareerSkillKind.Passive).All(s=>s.effect.StartsWith("f_")));
            for(int row=0;row<8;row++)for(int cel=0;cel<4;cel++){
                var art=FighterReforgeArt.Frame(row,cel);DCheck("new Fighter cel "+row+"/"+cel,art!=null&&art.texture.name.StartsWith("FighterReforge")&&art.texture.filterMode==FilterMode.Point&&art.texture.mipmapCount==1&&!art.texture.isReadable);
            }
            foreach(var s in all)DCheck(s.name+" new node icon",CareerArt.Get(s.Icon)!=null&&CareerArt.Get(s.Icon).texture.width==64);
            for(int facing=0;facing<8;facing++){
                yield return RebornSetup(Career.Fighter);var p=Game.Player;var origin=p.Position;var dir=((Facing)facing).ToVector();var side=new Vector2(-dir.y,dir.x);
                foreach(var s in all.Where(x=>x.kind!=CareerSkillKind.Passive)){
                    CareerCombat.For(p).ResetState();p.Place(origin,(Facing)facing);yield return null;
                    float first=s.effect=="f_eruption"?1.25f:s.effect=="f_echo"?1.5f:s.effect=="f_dash"?.4f:s.effect=="f_convergence"?3:2;
                    float second=s.effect=="f_wave"?4:s.effect=="f_eruption"?4.25f:s.effect=="f_echo"?4.2f:9;
                    var a=RevisionDummy(p.Center+dir*first);var b=RevisionDummy(p.Center+dir*second);var rear=RevisionDummy(p.Center-dir*3);var flank=RevisionDummy(p.Center+side*6);
                    int hits=0;a.Health.Damaged+=info=>hits++;int bHits=0;b.Health.Damaged+=info=>bHits++;
                    if(s.effect=="f_drop"){
                        StartCoroutine(CareerCombat.For(p).Cast(s,RebornNumbers(s.id)));yield return Wait(s.cast+.09f);
                        DCheck("falling sword waits for ground "+facing,hits==0);yield return Wait(.7f);
                    }else{yield return CareerCombat.For(p).Cast(s,RebornNumbers(s.id));yield return Wait(.6f);}
                    int expected=s.effect=="f_vortex"?3:s.effect=="f_convergence"?4:1;
                    bool blockedDash=s.effect=="f_dash"&&Vector2.Distance(p.Position,origin)<.2f;
                    DCheck(s.name+" actual hit cadence "+facing,blockedDash?hits==0:hits==expected);
                    DCheck(s.name+" far path and outside excluded "+facing,bHits==((s.effect=="f_wave"||s.effect=="f_eruption"||s.effect=="f_echo")?1:0)&&rear.Health.Current==rear.Health.Max&&flank.Health.Current==flank.Health.Max);
                    DCheck(s.name+" source facing and cleanup "+facing,p.Facing==(Facing)facing&&CareerRenewalFx.Count==0&&CareerPaintEffect.ActiveCount==0);
                    foreach(var e in new[]{a,b,rear,flank})Destroy(e.gameObject);yield return null;
                }
            }
            yield return RebornSetup(Career.Fighter);var player=Game.Player;var state=CareerCombat.For(player);var foe=RevisionDummy(player.Center+Vector2.right*2);
            for(int i=0;i<3;i++){foe.TakeDamage(new DamageInfo(10,player.Position,0,Team.Player,player.gameObject));yield return Wait(.2f);}
            DCheck("three basic hits store three charges",state.ComboStacks==3&&CareerCombat.SpeedFor(player.Data)==0);
            var wave=CareerCatalog.Get("f_cross");int before=foe.Health.Current;
            yield return state.Cast(wave,RebornNumbers(wave.id));yield return Wait(.2f);
            int charged=before-foe.Health.Current;DCheck("skill consumes charges without recharging itself",state.ComboStacks==0);
            before=foe.Health.Current;yield return state.Cast(wave,RebornNumbers(wave.id));yield return Wait(.2f);int normal=before-foe.Health.Current;
            DCheck("stored force increases real next skill damage",charged>normal&&charged<=Mathf.CeilToInt(normal*1.25f));
            int high=state.ModifyDamage(foe,100);foe.Health.Drain(foe.Health.Max/2);int low=state.ModifyDamage(foe,100);
            DCheck("opening passive uses target health threshold",high==115&&low==100);
            Destroy(foe.gameObject);
            DCheck("new wave equips through existing save key",player.Data.Progression.EquipSkill(0,wave.id));float mp=player.Data.Mana;
            player.Skills.TryCast(0);player.Skills.TryCast(0);DCheck("one key press pays once",Mathf.Abs(mp-player.Data.Mana-player.Skills.Numbers(0).manaCost)<.01f);
            yield return Wait(1);player.Skills.CooldownProgress(0,out float remain);DCheck("new cooldown retained",remain>0&&!player.Skills.IsCasting);
            foreach(string id in new[]{"f_break","f_flurry","f_focus","f_execute","f_awake"}){
                var skill=CareerCatalog.Get(id);StartCoroutine(state.Cast(skill,RebornNumbers(id)));yield return Wait(skill.cast+.04f);state.ResetState();yield return Wait(1.3f);
                DCheck(id+" reset removes pending hits and effects",CareerRenewalFx.Count==0&&CareerPaintEffect.ActiveCount==0&&CareerAreaView.Count==0);
            }
            var saved=new SaveData();player.Data.Progression.Capture(saved);int rank=player.Data.Progression.Rank("f_cross");player.Data.Progression.Restore(saved,player.Class);
            DCheck("saved ranks loadout awakening preserved",player.Data.Progression.Rank("f_cross")==rank&&player.Data.Progression.Active(0)?.name=="월아검"&&player.Data.Progression.Awakened);
            saved.career.awakened=false;saved.career.questStage=2;player.Data.Progression.Restore(saved,player.Class);
            DCheck("new awakening locked until quest complete",!player.Data.Progression.CareerUnlocked(CareerCatalog.Get("f_awake")));
            yield return CareerTrialCheck(Career.Fighter);
            Game.Flow.OpenWindow(Game.UI.Skills);yield return Wait(.2f);yield return PresentationShot("reforge_skill_tree");Game.Flow.CloseInventory();
            Log("FIGHTER REFORGE: new deliveries, original atlases/icons, eight facings, passive loop, saves, cooldowns and quest gate.");
        }
    }
}
