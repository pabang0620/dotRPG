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
            var loadouts=new[]{new[]{1,2,3,7},new[]{1,2,5,7},new[]{1,2,3,7},new[]{1,3,5,6}};
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
                foreach(var gear in new[]{cls==CharacterClass.Warrior?"eq_sword_iron+7":"eq_staff_crystal+7",career==Career.Guardian?"eq_top_iron":"eq_top_leather","eq_bot_leather","eq_neck_leaf","eq_ring_wind"})
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
            yield return PresentationShot("career_demo_selection");
        }

        IEnumerator CareerRun()
        {
            dgnPassed=dgnFailed=0;ApplyRequestedResolution();yield return Wait(1);
            Game.Config.autosave=false;Game.Flow.NewGame(CharacterClass.Warrior);yield return Wait(1.5f);Game.Player.Input=new ScriptedInput();
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-careerPreviewOnly")>=0){yield return CareerVisualCaptures();yield break;}
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-careerUiOnly")>=0){yield return CareerUiChecks();yield break;}
            DCheck("catalog exactly 36",CareerCatalog.All.Length==36);
            foreach(Career c in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop})
            {
                var list=CareerCatalog.For(c);DCheck(c+" 2 passives / 6 actives / 1 awakening",list.Count(x=>x.kind==CareerSkillKind.Passive)==2&&list.Count(x=>x.kind==CareerSkillKind.Active)==6&&list.Count(x=>x.kind==CareerSkillKind.Awakening)==1);
                var p=new Progression();p.Reset(CareerCatalog.Base(c));p.SetFromServer(14,0);DCheck(c+" reject level14",!p.Promote(c));p.SetFromServer(15,0);
                DCheck(c+" base cannot allocate",!p.Learn(list[0].id));DCheck(c+" base awakening locked",!p.EquipSkill(4,list[8].id)&&p.Active(4)==null);
                DCheck(c+" wrong base rejected",!p.Promote(CareerCatalog.Base(c)==CharacterClass.Warrior?Career.Bishop:Career.Guardian));
                DCheck(c+" promotion accepted",p.Promote(c));DCheck(c+" duplicate rejected",!p.Promote(c)&&p.CareerPoints==4);
                DCheck(c+" basic retained",p.Active(0)!=null&&CareerCatalog.Get(p.Active(0).id)==null);
                DCheck(c+" own root",p.Learn(list[0].id));DCheck(c+" own active",p.Learn(list[1].id));DCheck(c+" rank costs",p.CareerPoints==2);
                DCheck(c+" prerequisite reject",!p.Learn(list[3].id));DCheck(c+" quest required",!p.Learn(list[8].id)&&!p.EquipSkill(4,list[8].id));
                DCheck(c+" equip",p.EquipSkill(0,list[1].id));p.SetFromServer(40,0);
                for(int i=0;i<5;i++)DCheck(c+" quest stage "+i,p.AdvanceAwakening(i));
                DCheck(c+" reward once",!p.AdvanceAwakening(4)&&p.Awakened);
                DCheck(c+" awakening equip",p.EquipSkill(4,list[8].id));
                var other=CareerCatalog.For(c==Career.Fighter?Career.Guardian:Career.Fighter);
                DCheck(c+" wrong career node rejected",!p.Learn(other[0].id));
                DCheck(c+" wrong awakening rejected",!p.EquipSkill(4,other[8].id));
                var save=new SaveData();p.Capture(save);var round=new Progression();round.Restore(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(save)),CareerCatalog.Base(c));
                DCheck(c+" save restore",round.Career==c&&round.Awakened&&round.Active(0)?.id==list[1].id&&round.CareerPoints==p.CareerPoints);
                round.Restore(save,CareerCatalog.Base(c));DCheck(c+" repeated restore no reward",round.CareerPoints==p.CareerPoints);
                var card=new MemberCard{cls=CareerCatalog.Base(c),level=40,career=save.career,gemSlots=save.gemSlots};
                using(var stream=new MemoryStream()){var writer=new BinaryWriter(stream);card.Write(writer);stream.Position=0;var read=MemberCard.Read(new BinaryReader(stream));var data=read.ToData();DCheck(c+" network card roundtrip",data.Progression.Career==c&&data.Progression.Awakened&&data.Progression.Active(0)?.id==list[1].id);}
            }
            var legacy=new SaveData{level=40,passives=new System.Collections.Generic.List<string>{"start","Lt0","Lt1"},gemSlots=new System.Collections.Generic.List<string>{"crush","sup_dmg","","whirl","","","cry","","","charge","","","blades","",""}};
            var migrated=new Progression();migrated.Restore(legacy,CharacterClass.Warrior);DCheck("legacy awakening removed",migrated.Active(4)==null&&!migrated.Awakened);DCheck("support retained",migrated.SlotGem(0,1)=="sup_dmg");
            var kept=new SaveData();migrated.Capture(kept);int refund=migrated.RefundedPoints;migrated.Restore(kept,CharacterClass.Warrior);DCheck("refund idempotent",migrated.RefundedPoints==refund);
            var prog=Game.Session.Progression;prog.SetFromServer(40,0);prog.Promote(Career.Fighter);foreach(var s in CareerCatalog.For(Career.Fighter))if(s.kind!=CareerSkillKind.Awakening)prog.Learn(s.id);
            Game.Flow.OpenWindow(Game.UI.Skills);yield return Wait(.2f);yield return PresentationShot("career_fighter_ui");Game.Flow.CloseInventory();
            var status=CareerCombat.For(Game.Player);status.AddShield(50,5);DCheck("shield absorb",status.Absorb(30)==0&&status.Shield==20);DCheck("shield overflow",status.Absorb(30)==10&&status.Shield==0);status.AddCurse(5);DCheck("cleanse status",status.Cleanse()&&!status.Cursed);DCheck("cleanse idempotent",!status.Cleanse());
            prog.EquipSkill(0,"f_cross");Game.Player.Data.Mana=0;Game.Player.Skills.TryCast(0);DCheck("resource gate",!Game.Player.Skills.IsCasting);Game.Player.Data.Mana=Game.Player.MaxMana;Game.Player.Skills.TryCast(0);DCheck("cast starts",Game.Player.Skills.IsCasting);yield return Wait(.6f);DCheck("cooldown gate",!Game.Player.Skills.IsReady(0));prog.EquipSkill(0,"crush");prog.EquipSkill(1,"f_cross");DCheck("move slot keeps cooldown",!Game.Player.Skills.IsReady(1));
            string artFolder=Path.Combine(folder,"icons");string fxFolder=Path.Combine(folder,"effects");Directory.CreateDirectory(artFolder);Directory.CreateDirectory(fxFolder);
            var contact=new PixelCanvas(9*72,4*72);int row=0;
            foreach(Career c in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop})
            {
                foreach(var s in CareerCatalog.For(c))
                {
                    var icon=CareerPaintArt.Icon(s);contact.Blit(icon,s.index*72+4,row*72+4);WritePixel(icon,Path.Combine(artFolder,s.Icon+".png"));
                    DCheck(s.id+" icon resource",CareerArt.Get(s.Icon)!=null);
                    if(s.kind==CareerSkillKind.Passive)continue;
                    var sheet=new PixelCanvas(CareerRaster.Size*12,CareerRaster.Size);for(int i=0;i<12;i++)sheet.Blit(CareerArt.Effect(s,i),i*CareerRaster.Size,0);WritePixel(sheet,Path.Combine(fxFolder,s.id+".png"));
                    DCheck(s.id+" effect12",CareerArt.Frame(s,11)!=null);
                    bool inside=true,split=true;int visible=0;
                    string layers=Path.Combine(folder,"layered");Directory.CreateDirectory(layers);
                    var backSheet=new PixelCanvas(CareerRaster.Size*12,CareerRaster.Size);var frontSheet=new PixelCanvas(CareerRaster.Size*12,CareerRaster.Size);
                    for(int f=0;f<12;f++){
                        var art=CareerArt.Effect(s,f);var back=CareerVfxArt.Plane(art,true);var front=CareerVfxArt.Plane(art,false);
                        backSheet.Blit(back,CareerRaster.Size*f,0);frontSheet.Blit(front,CareerRaster.Size*f,0);
                        for(int y=0;y<CareerRaster.Size;y++)for(int x=0;x<CareerRaster.Size;x++){
                            int at=y*CareerRaster.Size+x;var pixel=art.Pixels[at];if(pixel.a>0){if(f==0)visible++;if((x-CareerRaster.Size/2)*(x-CareerRaster.Size/2)+(y-CareerRaster.Size/2)*(y-CareerRaster.Size/2)>87.5f*87.5f)inside=false;}
                            var reconstructed=y<CareerRaster.Size/2?back.Pixels[at]:front.Pixels[at];if(!pixel.Equals(reconstructed)||(y<CareerRaster.Size/2?front.Pixels[at].a:back.Pixels[at].a)!=0)split=false;
                        }
                    }
                    WritePixel(backSheet,Path.Combine(layers,s.id+"_back.png"));WritePixel(frontSheet,Path.Combine(layers,s.id+"_front.png"));
                    DCheck(s.id+" artwork inside combat radius",inside);
                    DCheck(s.id+" split artwork lossless",split);
                    DCheck(s.id+" visible on release frame",visible>0);
                }row++;
            }
            string details=Path.Combine(folder,"details");Directory.CreateDirectory(details);
            foreach(Career c in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop})foreach(bool charge in new[]{true,false}){
                var sheet=new PixelCanvas(CareerRaster.Size*12,CareerRaster.Size);for(int i=0;i<12;i++)sheet.Blit(CareerVfxArt.Detail(c,charge,i),CareerRaster.Size*i,0);
                WritePixel(sheet,Path.Combine(details,c+(charge?"_charge":"_hit")+".png"));
                DCheck(c+(charge?" charge":" hit")+" detail resource",CareerArt.DetailFrame(c,charge,11)!=null);
            }
            string presentation=Path.Combine(folder,"presentation");Directory.CreateDirectory(presentation);
            foreach(var key in CareerVfxArt.VisualKeys){var sheet=new PixelCanvas(CareerRaster.Size*12,CareerRaster.Size);for(int i=0;i<12;i++)sheet.Blit(CareerVfxArt.Visual(key,i),CareerRaster.Size*i,0);WritePixel(sheet,Path.Combine(presentation,key+".png"));DCheck(key+" presentation resource",CareerArt.VisualFrame(key,11)!=null);}
            WritePixel(contact,Path.Combine(folder,"career_icons_36.png"));
            File.WriteAllText(Path.Combine(folder,"skills.json"),JsonUtility.ToJson(new CareerExport{skills=CareerCatalog.All},true));
            if(Array.IndexOf(Environment.GetCommandLineArgs(),"-careerArtPreview")>=0){yield return CareerVisualCaptures();yield break;}
            yield return CareerCombatChecks();
            yield return CareerRebornChecks();
            yield return CareerPresentationChecks();
            yield return CareerVisualCaptures();
            Log($"CAREER RESULTS: {dgnPassed} passed, {dgnFailed} failed");
        }
        IEnumerator CareerPresentationChecks()
        {
            Game.Flow.NewGame(CharacterClass.Warrior);yield return Wait(.5f);var player=Game.Player;player.Input=new ScriptedInput();
            var state=CareerCombat.For(player);state.ResetState();yield return Wait(1);
            var skill=CareerCatalog.Get("g_wall");int hp=player.Health.Current;float mp=player.Data.Mana,scale=Time.timeScale;
            var random=UnityEngine.Random.state;float expected=UnityEngine.Random.value;UnityEngine.Random.state=random;
            CareerEffect.Play(skill,player.Center,1,Vector2.up,.15f,player);
            CareerAreaView.Show(skill,player.Center,2.4f,.2f,player);CareerPresentation.Link(skill,player.Center,player.Center+Vector2.right,player,.2f);
            DCheck("presentation preserves combat RNG",UnityEngine.Random.value==expected);UnityEngine.Random.state=random;
            var area=UnityEngine.Object.FindAnyObjectByType<CareerAreaView>();DCheck("boundary matches exact radius",area!=null&&Mathf.Abs(area.Radius-2.4f)<.0001f&&Mathf.Abs(area.transform.localScale.x-2.4f*48/43)<.0001f);
            yield return Wait(.4f);
            DCheck("presentation no HP MP time-scale mutation",hp==player.Health.Current&&mp==player.Data.Mana&&scale==Time.timeScale);
            DCheck("expired transient objects removed",CareerEffect.ActiveCount==0&&CareerAreaView.Count==0&&CareerLinkView.Count==0);
            state.AddShield(50,.5f,CareerCatalog.Get("b_wing"));yield return Wait(.03f);var view=player.GetComponent<CareerStatusView>();
            DCheck("shield uses source career and maintains boundary",state.ShieldCareer==Career.Bishop&&view.VisibleCount==1);
            state.Absorb(50);yield return Wait(.28f);DCheck("consumed shield fades out",state.Shield==0&&view.VisibleCount==0);
            state.AddShield(50,.12f,skill);yield return Wait(.04f);DCheck("renewed shield visible",view.VisibleCount==1);yield return Wait(.4f);DCheck("shield expiry hides boundary",view.VisibleCount==0);
            for(int i=0;i<160;i++){CareerEffect.Play(skill,player.Center,1,Vector2.up,.15f,player);CareerAreaView.Show(skill,player.Center,2,.15f,player);CareerPresentation.Link(skill,player.Center,player.Center+Vector2.up,player,.15f);}
            DCheck("simultaneous FX hard caps",CareerEffect.ActiveCount<=96&&CareerAreaView.Count<=24&&CareerLinkView.Count<=40);
            yield return Wait(.35f);DCheck("stress cleanup",CareerEffect.ActiveCount==0&&CareerAreaView.Count==0&&CareerLinkView.Count==0);
            CareerEffect.Play(skill,player.Center,1,Vector2.up,9,player);CareerAreaView.Show(skill,player.Center,2,9,player);CareerPresentation.Link(skill,player.Center,player.Center+Vector2.up,player,9);
            string map=Game.Session.MapId;Game.Session.MapId="vfx_cleanup_test";yield return null;yield return null;
            DCheck("map change cleans all transients",CareerEffect.ActiveCount==0&&CareerAreaView.Count==0&&CareerLinkView.Count==0);Game.Session.MapId=map;
            CareerEffect.Play(skill,player.Center,1,Vector2.up,9,player);CareerAreaView.Show(skill,player.Center,2,9,player);CareerPresentation.Link(skill,player.Center,player.Center+Vector2.up,player,9);
            player.Health.Init(player.Health.Max,player.Health.Max,0);player.TakeDamage(new DamageInfo(999999,player.Position+Vector2.up,0,Team.Enemy,null){unblockable=true});
            yield return null;yield return null;
            DCheck("death cleans all transients",player.IsDead&&CareerEffect.ActiveCount==0&&CareerAreaView.Count==0&&CareerLinkView.Count==0);
            Game.Flow.NewGame(CharacterClass.Warrior);yield return Wait(.5f);
        }
        IEnumerator CareerVisualCaptures()
        {
            string dest=Path.Combine(folder,"comparison");Directory.CreateDirectory(dest);
            foreach(string id in new[]{"f_cross","f_awake","g_wall","g_awake","m_fire","m_awake","b_heal","b_awake"}){
                var s=CareerCatalog.Get(id);Game.Flow.NewGame(CareerCatalog.Base(s.career));yield return Wait(.4f);
                var player=Game.Player;player.Input=new ScriptedInput();var p=player.Data.Progression;p.SetFromServer(40,0);p.Promote(s.career);
                foreach(var node in CareerCatalog.For(s.career))if(node.kind!=CareerSkillKind.Awakening)p.Learn(node.id);for(int stage=0;stage<5;stage++)p.AdvanceAwakening(stage);
                // Let level-up/reward particles finish so the comparison contains only the requested skill.
                yield return Wait(3);
                var at=player.Position;player.Place(at,Facing.Right);player.Health.Drain(150);
                var stats=Instantiate(Game.Config.skeletonStats);stats.maxHealth=100000;stats.attackDamage=stats.xpReward=0;stats.wanderSpeed=stats.chaseSpeed=stats.knockbackSpeed=0;stats.invulnerableTime=.01f;
                var enemy=EnemyController.Create(stats,CharacterLook.Skeleton,at+Vector2.right*1.8f,Game.World.ObjectsRoot);
                var rect=new Rect(at.x-4,at.y-3,8,6);
                // Remove only setup particles before casting; keep all particles produced by the actual skill.
                foreach(var setup in UnityEngine.Object.FindObjectsByType<FxParticle>(FindObjectsSortMode.None))Destroy(setup.gameObject);
                yield return null;
                StartCoroutine(CareerCombat.For(player).Cast(s,player.Data.Stats.Skill(player.Class,0,s.Gem,new SkillGem[0])));
                for(int frame=0;frame<18;frame++){RenderRegion(Path.Combine(dest,id+"_"+frame.ToString("00")+".png"),rect,48);yield return Wait(.1f);}
                Destroy(enemy.gameObject);Destroy(stats);
            }
        }
        IEnumerator CareerCombatChecks()
        {
            foreach(Career c in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop})
            {
                Game.Flow.NewGame(CareerCatalog.Base(c));yield return Wait(.4f);var player=Game.Player;player.Input=new ScriptedInput();
                var p=player.Data.Progression;p.SetFromServer(40,0);p.Promote(c);foreach(var s in CareerCatalog.For(c))if(s.kind!=CareerSkillKind.Awakening)p.Learn(s.id);
                var at=player.Position;player.FaceTowards(at+Vector2.right);player.Health.Init(player.Data.Stats.MaxHp,player.Data.Stats.MaxHp,0);
                var es=Instantiate(Game.Config.skeletonStats);es.maxHealth=100000;es.attackDamage=0;es.xpReward=0;es.wanderSpeed=es.chaseSpeed=es.knockbackSpeed=0;es.invulnerableTime=.01f;
                var foe=EnemyController.Create(es,CharacterLook.Skeleton,at+Vector2.right*1.8f,Game.World.ObjectsRoot);
                // Every active executes through the actual coroutine and effect renderer.
                foreach(var s in CareerCatalog.For(c))
                {
                    if(s.kind==CareerSkillKind.Passive)continue;
                    if(s.kind==CareerSkillKind.Awakening)for(int stage=0;stage<5;stage++)p.AdvanceAwakening(stage);
                    player.Place(at,Facing.Right);foe.transform.position=at+Vector2.right*1.8f;foe.GetComponent<Rigidbody2D>().position=foe.transform.position;
                    player.Health.Drain(150);int hp=player.Health.Current,enemyHp=foe.Health.Current;
                    var numbers=player.Data.Stats.Skill(player.Class,0,s.Gem,new SkillGem[0]);
                    if(s.effect=="cleanse")CareerCombat.For(player).AddCurse(10);
                    StartCoroutine(CareerCombat.For(player).Cast(s,numbers));yield return Wait(s.cast+.13f);
                    if(s.kind==CareerSkillKind.Awakening)RenderRegion(Path.Combine(folder,"combat_"+s.id+".png"),new Rect(at.x-5,at.y-4,10,8),64);
                    yield return Wait(s.effect=="hot"||s.effect=="rift"?s.duration+.2f:s.effect=="counter"?3.2f:1.1f);
                    bool heal=s.effect=="heal"||s.effect=="hot"||s.effect=="cleanse"||s.effect=="dawn";
                    bool attack=s.power>0&&s.effect!="focus"&&s.effect!="guard"&&s.effect!="blink"&&s.effect!="shield"&&s.effect!="ward"&&s.effect!="citadel"&&s.effect!="wings"&&s.effect!="bless"&&!heal;
                    if(attack)DCheck(s.id+" actual damage",foe.Health.Current<enemyHp);
                    if(heal)DCheck(s.id+" actual heal",player.Health.Current>hp);
                    if(s.effect=="cleanse")DCheck("cleanse actual curse",!CareerCombat.For(player).Cursed);
                    if(s.effect=="taunt")DCheck("AI forced target",ThreatTable.For(foe).Select(Game.Party)==player);
                    if(s.effect=="shield"||s.effect=="ward"||s.effect=="citadel"||s.effect=="wings")DCheck(s.id+" actual shield",CareerCombat.For(player).Shield>0);
                    if(s.effect=="ice")DCheck("ice has freeze",foe.IsFrozen);
                    player.HealFull();
                }
                Destroy(foe.gameObject);Destroy(es);
                // UI comparison and all four completed trees are captured at the shipped resolution.
                Game.Flow.OpenWindow(Game.UI.Skills);yield return Wait(.1f);yield return PresentationShot("career_"+c+"_tree");Game.Flow.CloseInventory();
                yield return CareerTrialCheck(c);
            }
        }
        IEnumerator CareerTrialCheck(Career c)
        {
            var player=Game.Player;var p=player.Data.Progression;var save=new SaveData();p.Capture(save);save.career.questStage=2;save.career.awakened=false;p.Restore(save,player.Class);
            CareerCombat.For(player).ResetState();player.HealFull();player.Place(player.Position,Facing.Up);
            DCheck(c+" trial start",CareerTrials.Begin()=="");yield return Wait(.2f);
            if(CareerTrials.Running==null)yield break;
            if(c==Career.Fighter){string map=Game.Session.MapId;Game.Session.MapId="trial_exit_test";yield return Wait(.05f);DCheck("trial failure retains progress",p.AwakeningStage==2&&!p.Awakened&&CareerTrials.Running==null);Game.Session.MapId=map;DCheck("trial retry",CareerTrials.Begin()=="");yield return Wait(.2f);}
            var echo=CareerTrials.Running.GetComponentInChildren<EnemyController>();
            if(c==Career.Fighter)
            {
                // Five real damage events in the advertised opening, not a quest-counter mutation.
                for(int i=0;i<5;i++){echo.TakeDamage(new DamageInfo(1,player.Position,0,Team.Player,player.gameObject));yield return Wait(.19f);}
            }
            else if(c==Career.Arcanist)
            {
                foreach(var id in new[]{"m_fire","m_ice","m_storm"}) {var s=CareerCatalog.Get(id);player.FaceTowards(echo.Center);yield return CareerCombat.For(player).Cast(s,player.Data.Stats.Skill(player.Class,0,s.Gem,new SkillGem[0]));yield return Wait(.2f);}
            }
            else
            {
                float until=Time.time+22,next=Time.time;
                while(Time.time<until&&CareerTrials.Running!=null)
                {
                    if(Time.time>=next){next=Time.time+5;
                        foreach(var id in c==Career.Guardian?new[]{"g_wall","g_taunt"}:new[]{"b_cleanse","b_heal","b_wing"}){
                            var s=CareerCatalog.Get(id);yield return CareerCombat.For(player).Cast(s,player.Data.Stats.Skill(player.Class,0,s.Gem,new SkillGem[0]));
                        }
                    }yield return Wait(.25f);
                }
            }
            yield return Wait(.3f);DCheck(c+" role trial completion",p.AwakeningStage==3);
            if(CareerTrials.Running!=null)Destroy(CareerTrials.Running.gameObject);
            DCheck(c+" quest finishing dialogue",p.AdvanceAwakening(3)&&p.AdvanceAwakening(4)&&p.Awakened);
        }
        [Serializable] sealed class CareerExport {public CareerSkill[] skills;}
        IEnumerator CareerUiChecks()
        {
            foreach(var c in new[]{Career.Fighter,Career.Guardian,Career.Arcanist,Career.Bishop})
            {
                Game.Flow.NewGame(CareerCatalog.Base(c));yield return Wait(1);var p=Game.Session.Progression;p.SetFromServer(40,0);p.Promote(c);
                foreach(var s in CareerCatalog.For(c))if(s.kind!=CareerSkillKind.Awakening)p.Learn(s.id);
                Game.Flow.OpenWindow(Game.UI.Skills);yield return Wait(.1f);yield return PresentationShot("ui_"+c);
                var node=Game.UI.Skills.GetComponentsInChildren<PointerRelay>().First(x=>x.name=="Node_"+CareerCatalog.For(c)[3].id);node.onClick(UnityEngine.EventSystems.PointerEventData.InputButton.Left);yield return Wait(.1f);yield return PresentationShot("ui_"+c+"_detail");
                Game.UI.Skills.ShowAwakening();yield return Wait(.1f);yield return PresentationShot("ui_"+c+"_quest");Game.Flow.CloseInventory();
            }
        }
        void WritePixel(PixelCanvas p,string path){var tex=new Texture2D(p.Width,p.Height,TextureFormat.RGBA32,false);tex.SetPixels32(p.ToTexturePixels());tex.Apply();File.WriteAllBytes(path,tex.EncodeToPNG());Destroy(tex);}
    }
}
