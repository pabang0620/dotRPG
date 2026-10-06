using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        // Explicit local preview mode: never reads or replaces the normal three save slots.
        IEnumerator CareerDemoRun()
        {
            ApplyRequestedResolution();
            yield return Wait(1);
            Game.Config.autosave=false;
            var careers=new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop};
            var loadouts=new[]{new[]{1,2,5,3},new[]{2,5,6,7},new[]{1,2,3,5},new[]{5,1,2,6}};
            for(int slot=0;slot<careers.Length;slot++)
            {
                var career=careers[slot];
                if(Game.Saves.HasSave(slot)){Log("kept demo slot "+slot);continue;}
                SaveSystem.ActiveSlot=slot;
                var cls=CareerCatalog.Base(career);
                Game.Flow.NewGame(cls,CareerCatalog.Name(career)+" 체험");
                yield return Wait(.1f);
                while(Game.Flow.IsTransitioning)yield return null;
                var p=Game.Session.Progression;
                p.SetFromServer(40,0);
                p.Promote(career);
                var skills=CareerCatalog.For(career);
                // All eight ordinary nodes at rank 2 cost 24/29 points; leave 5 for experimentation.
                foreach(var skill in skills)if(skill.kind!=CareerSkillKind.Awakening){p.Learn(skill.id);p.Learn(skill.id);}
                for(int stage=0;stage<5;stage++)p.AdvanceAwakening(stage);
                for(int key=0;key<4;key++)p.EquipSkill(key,skills[loadouts[slot][key]].id);
                var bag=Game.Session.Inventory;
                foreach(var gear in new[]{cls==CharacterClass.Warrior?"eq_sword_10_u+7":"eq_staff_10_u+7",cls==CharacterClass.Warrior?(career==Career.Guardian?"eq_plate_10_e":"eq_plate_1_u"):(career==Career.Guardian?"eq_robe_10_e":"eq_robe_1_u"),cls==CharacterClass.Warrior?"eq_greaves_1_u":"eq_skirt_1_u","eq_neck_1_c","eq_ring_10_r"})
                {bag.Add(gear,1);if(!Game.Session.Equipment.Equip(gear,cls))throw new InvalidOperationException("Demo equipment rejected: "+gear);}
                bag.Add(ConsumableDatabase.Gold,100000);
                bag.Add(ConsumableDatabase.HpPotion,100);
                bag.Add(ConsumableDatabase.MpPotion,100);
                bag.Add(ConsumableDatabase.TownScroll,20);
                Game.Player.HealFull();
                var save=Game.Session.Capture(Game.Player.Position,Game.Player.Facing);
                if(!Game.Saves.Write(save,slot))throw new IOException("Could not save demo slot "+slot);
                var read=Game.Saves.Read(slot);var restored=new Progression();restored.Restore(read,cls);
                if(restored.Level!=40||restored.Career!=career||!restored.Awakened||restored.CareerPoints!=5||skills.Where(x=>x.kind!=CareerSkillKind.Awakening).Any(x=>restored.Rank(x.id)!=2))
                    throw new InvalidOperationException("Invalid demo slot "+slot);
                Log("created and verified slot "+slot+": "+career+", level40, awakened, eight rank2 nodes, five spare points");
            }
            Game.Config.autosave=true;
            Game.Flow.ReturnToTitle();yield return Wait(.1f);
            while(Game.Flow.IsTransitioning)yield return null;
            SaveSystem.ActiveSlot=0;
            Game.UI.Slots.Open(true);
            yield return Wait(.2f);
        }

        /// <summary>
        /// Headless check of every career skill: each one is cast through the real coroutine at a pack of dummies,
        /// and the run logs the damage, healing and states it actually produced (no screenshots).
        /// </summary>
        IEnumerator CareerRun()
        {
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-regionalMonsterOnly")>=0)
            {
                dgnPassed=dgnFailed=0;ApplyRequestedResolution();yield return Wait(1);
                Game.Config.autosave=false;Game.Flow.NewGame(CharacterClass.Warrior);yield return Wait(1.5f);Game.Player.Input=new ScriptedInput();
                yield return RegionalMonsterChecks();yield break;
            }
            dgnPassed=dgnFailed=0;
            foreach(Career c in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop})
            {
                Game.Flow.NewGame(CareerCatalog.Base(c));yield return Wait(.4f);
                while(Game.Flow.IsTransitioning)yield return null;
                var player=Game.Player;player.Input=new ScriptedInput();
                var p=player.Data.Progression;p.SetFromServer(40,0);p.Promote(c);
                foreach(var s in CareerCatalog.For(c))if(s.kind!=CareerSkillKind.Awakening)p.Learn(s.id);
                for(int stage=0;stage<5;stage++)p.AdvanceAwakening(stage);
                var at=player.Position;
                var es=Instantiate(Game.Config.skeletonStats);es.maxHealth=100000;es.attackDamage=0;es.xpReward=0;es.wanderSpeed=es.chaseSpeed=es.knockbackSpeed=0;
                var foes=new EnemyController[3];
                for(int i=0;i<3;i++)foes[i]=EnemyController.Create(es,CharacterLook.Skeleton,at+new Vector2(1.8f+i*.6f,(i-1)*.6f),Game.World.ObjectsRoot);
                foreach(var s in CareerCatalog.For(c))
                {
                    if(s.kind==CareerSkillKind.Passive)continue;
                    var combat=CareerCombat.For(player);combat.ResetState();
                    player.Place(at,Facing.Right);player.FaceTowards(at+Vector2.right);
                    for(int i=0;i<3;i++){foes[i].transform.position=at+new Vector2(1.8f+i*.6f,(i-1)*.6f);foes[i].GetComponent<Rigidbody2D>().position=foes[i].transform.position;foes[i].Health.Heal(1000000);}
                    player.Health.Drain(player.Health.Max/2);
                    if(s.effect=="cleanse"||s.effect=="dawn")combat.AddCurse(10);
                    int hp=player.Health.Current,enemyHp=foes.Sum(f=>f.Health.Current);
                    var numbers=player.Data.Stats.Skill(player.Class,0,s.Gem,new SkillGem[0]);
                    StartCoroutine(combat.Cast(s,numbers));
                    yield return Wait(s.cast+CareerMoves.Recovery(s)+Mathf.Max(1.2f,s.duration)+.4f);
                    int dealt=enemyHp-foes.Sum(f=>f.Health.Current),healed=player.Health.Current-hp;
                    Log($"{s.id} {s.name}: damage {dealt} (one hit {numbers.damage}), healed {healed}, shield {combat.Shield}, frozen {foes.Count(f=>f.IsFrozen)}");
                    bool support=s.effect=="oath"||s.effect=="wings"||s.effect=="bless"||s.effect=="heal"||s.effect=="bloom";
                    if(!support)DCheck(s.id+" deals damage",dealt>0);
                    if(s.effect=="heal"||s.effect=="bloom"||s.effect=="cleanse"||s.effect=="dawn")DCheck(s.id+" heals",healed>0);
                    if(s.effect=="cleanse"||s.effect=="dawn")DCheck(s.id+" cleanses",!combat.Cursed);
                    if(s.effect=="taunt"||s.effect=="aegis")DCheck(s.id+" taunts",ThreatTable.For(foes[0]).Forced==player);
                    if(s.effect=="shieldthrow"||s.effect=="wings"||s.effect=="aegis"||s.effect=="dawn"||s.effect=="oath"||s.effect=="blink")DCheck(s.id+" shields",combat.Shield>0);
                    player.HealFull();
                }
                foreach(var f in foes)if(f!=null)Destroy(f.gameObject);Destroy(es);
            }
            Log($"career checks: {dgnPassed} passed, {dgnFailed} failed");
        }
    }
}
