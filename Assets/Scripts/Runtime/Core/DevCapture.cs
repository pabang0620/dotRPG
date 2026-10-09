using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Developer smoke test, inactive unless the game is started with
    /// <c>-dotrpgCapture &lt;folder&gt;</c>. It walks through title → character select → new game as the
    /// mage → casts a spell → travels to the canyon → takes screenshots and writes a small report,
    /// then quits. Useful for checking a build on a machine without opening the editor.
    /// </summary>
    public partial class DevCapture : MonoBehaviour
    {
        string folder;
        string mode;
        bool fxOnly;
        StreamWriter log;

        /// <summary>
        /// Command-line switches that start an automated run; each is followed by the output folder.
        /// -dotrpgCapture: full smoke test. -dotrpgFx: skill-effect showcase. -dotrpgMap: winter map renders.
        /// -dotrpgTown: village town + forest hunting ground renders and service tests.
        /// -dotrpgCanyon / -dotrpgWinter / -dotrpgChars / -dotrpgUi: 32px canyon, winter village, characters
        /// and monsters, and window/HUD showcases (DevCapture.*.cs).
        /// </summary>
        static readonly string[] Modes = { "-dotrpgSurface", "-surfaceWorldExport", "-dotrpgCapture", "-dotrpgFx", "-dotrpgMap", "-dotrpgTown", "-dotrpgCanyon", "-dotrpgWinter", "-dotrpgChars", "-dotrpgUi", "-dotrpgDepth", "-dotrpgStairs", "-dotrpgSilver",
            "-dotrpgParty", "-dotrpgDungeon", "-dotrpgMonster", "-dotrpgBalance", "-dotrpgOnline", "-dotrpgHouse", "-dotrpgMobility", "-dotrpgNature", "-dotrpgWater", "-dotrpgStory", "-dotrpgVillageArt", "-dotrpgNetPair", "-dotrpgOnlinePause", "-dotrpgPresentation", "-dotrpgHunting", "-dotrpgPerf", "-dotrpgCareer", "-dotrpgCareerDemo", "-dotrpgMonsterDemo", "-dotrpgSanctum", "-dotrpgRoutes", "-dotrpgSanctumFields", "-dotrpgWorldLayers", "-dotrpgWeaponAppearance" }; // [PARTY] [DUNGEON] [MONSTER] [CONTENT]

        /// <summary>Test runs keep their saves next to their report, so the player's own save slot is never overwritten.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RedirectSaves()
        {
#if !DOTRPG_RELEASE
            var args = Environment.GetCommandLineArgs();
#if !DOTRPG_RELEASE
            if (SoakRequested(out _, out var soakFolder)) // [SOAK] own save folder, silent
            {
                SaveSystem.DirectoryOverride = Path.Combine(soakFolder, "saves");
                AudioListener.volume = 0f;
                GameFlow.PauseOnFocusLoss = false;
                return;
            }
#endif
            for (int i = 0; i < args.Length - 1; i++)
                if (Array.IndexOf(Modes, args[i]) >= 0)
                {
                    SaveSystem.DirectoryOverride = Path.Combine(args[i + 1], "saves");
                    bool demo = args[i] == "-dotrpgCareerDemo" || args[i] == "-dotrpgMonsterDemo";
                    if (demo) { SaveSystem.SlotCount = 4; SkillCaster.NoCooldown = true; GameFlow.DemoSlots = true; } // [DEMO] no cooldowns, local trial slots
                    // Automated verification is silent; the interactive demos retain player settings.
                    if (!demo || Array.IndexOf(args, "-batchmode") >= 0) AudioListener.volume = 0f;
                    GameFlow.PauseOnFocusLoss = false;
                    return;
                }
#endif
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
#if !DOTRPG_RELEASE // [RELEASE] the capture / check modes are not in a release build (anyone could start them from Steam)
            var args = Environment.GetCommandLineArgs();
            if (SoakRequested(out _, out var soakFolder)) // [SOAK]
            {
                var soakGo = new GameObject("DevCapture");
                DontDestroyOnLoad(soakGo);
                SoakAttach(soakGo, soakFolder);
                return;
            }
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (Array.IndexOf(Modes, args[i]) < 0) continue;
                var go = new GameObject("DevCapture");
                DontDestroyOnLoad(go);
                var capture = go.AddComponent<DevCapture>();
                capture.folder = args[i + 1];
                capture.mode = args[i];
                capture.fxOnly = args[i] == "-dotrpgFx";
                capture.mapOnly = args[i] == "-dotrpgMap";
                capture.townOnly = args[i] == "-dotrpgTown";
                capture.partyOnly = args[i] == "-dotrpgParty"; // [PARTY]
                capture.dungeonOnly = args[i] == "-dotrpgDungeon"; // [DUNGEON]
                capture.monsterOnly = args[i] == "-dotrpgMonster"; // [MONSTER]
                capture.balanceOnly = args[i] == "-dotrpgBalance"; // [CONTENT]
                capture.onlineOnly = args[i] == "-dotrpgOnline"; // [ONLINE]
                QuestManager.StoryEnabled = args[i] == "-dotrpgStory"; // [STORY] older checks play without the prologue scenes
                return;
            }
#endif
        }

        bool mapOnly, townOnly;

        /// <summary>The showcase coroutine of the 32px modes, or null for the older modes.</summary>
        IEnumerator HdShowcase()
        {
            switch (mode)
            {
                case "-dotrpgCanyon": return CanyonHdShowcase();
                case "-dotrpgWinter": return WinterHdShowcase();
                case "-dotrpgChars": return CharsShowcase();
                case "-dotrpgUi": return UiShowcase();
                case "-dotrpgDepth": return DepthShowcase();
                case "-dotrpgStairs": return StairsShowcase();
                case "-dotrpgSilver": return SilverShowcase();
                case "-dotrpgMobility": return MobilityShowcase();
                case "-dotrpgNature": return NatureShowcase();
                case "-dotrpgWater": return WaterShowcase();
                case "-dotrpgHouse": return HouseScaleShowcase();
                case "-dotrpgVillageArt": return VillageArtShowcase(); // [ART]
            }
            return null;
        }

        IEnumerator Start()
        {
            Directory.CreateDirectory(folder);
            log = new StreamWriter(Path.Combine(folder, "report.txt")) { AutoFlush = true };
            Application.logMessageReceived += OnLog;
            Log("capture started");
            bool interactiveDemo = mode == "-dotrpgCareerDemo" || mode == "-dotrpgMonsterDemo";
            bool automatedDemo = interactiveDemo && Array.IndexOf(Environment.GetCommandLineArgs(), "-batchmode") >= 0;
            if (!interactiveDemo || automatedDemo) { AudioListener.volume = 0f; Game.Audio?.SetVolumes(0f, 0f); Log("verification audio volume: 0"); }
            if (mode == SoakFlag) { yield return SoakRun(); log.Close(); log = null; Application.Quit(); yield break; } // [SOAK]
            if (mode == "-dotrpgWeaponAppearance") { yield return WeaponAppearanceRun(); log.Close(); log = null; Application.Quit(); yield break; }
            if (mode == "-dotrpgSurface" || mode == "-surfaceWorldExport") { yield return SurfaceWorldRun(); log.Close(); log = null; Application.Quit(); yield break; }
            if (mode == "-dotrpgWorldLayers") { yield return WorldLayersRun(); log.Close(); log = null; if (Array.IndexOf(Environment.GetCommandLineArgs(), "-batchmode") >= 0 || Array.IndexOf(Environment.GetCommandLineArgs(), "-worldLayersVerify") >= 0) Application.Quit(); else Destroy(this); yield break; }
            if(mode=="-dotrpgSanctumFields"){yield return SanctumFieldsRun();log.Close();log=null;if(Array.IndexOf(Environment.GetCommandLineArgs(),"-batchmode")>=0)Application.Quit();else Destroy(this);yield break;}
            if (mode == "-dotrpgRoutes") { yield return RoutesRun(); log.Close(); Application.Quit(); yield break; }
            if (mode == "-dotrpgSanctum") { yield return SanctumRun(); log.Close(); log=null; if(Array.IndexOf(Environment.GetCommandLineArgs(), "-batchmode")>=0 || Array.IndexOf(Environment.GetCommandLineArgs(), "-sanctumVerify")>=0)Application.Quit();else Destroy(this); yield break; }
            if (mode == "-dotrpgMonsterDemo") { yield return MonsterDemoRun(); Log("monster demo ready: four region characters"); log.Close(); log=null; if(automatedDemo)Application.Quit();else Destroy(this); yield break; }
            if (mode == "-dotrpgCareerDemo") { yield return CareerDemoRun(); Log("career demo ready: four level-40 characters"); log.Close(); log=null; if(automatedDemo)Application.Quit();else Destroy(this); yield break; }
            if (mode == "-dotrpgCareer") { yield return CareerRun(); Log("capture finished"); log.Close(); Application.Quit(); yield break; }
            if (mode == "-dotrpgStory") { yield return StoryRun(); Log("capture finished"); log.Close(); Application.Quit(); yield break; } // [STORY]
            if (mode == "-dotrpgPerf") { yield return PerfRun(); Log("capture finished"); log.Close(); Application.Quit(); yield break; } // [P5]
            if (mode == "-dotrpgNetPair") { yield return NetPairRun(); Log("capture finished"); log.Close(); Application.Quit(); yield break; } // [F2]
            if (mode == "-dotrpgOnlinePause") { yield return OnlinePauseRun(); Log("capture finished"); log.Close(); Application.Quit(); yield break; }
            if (mode == "-dotrpgPresentation") { yield return PresentationRun(); Log("capture finished"); log.Close(); Application.Quit(); yield break; }
            if (mode == "-dotrpgHunting") { yield return HuntingRun(); Log("capture finished"); log.Close(); Application.Quit(); yield break; }
            if (onlineOnly) { yield return OnlineRun(); Log("capture finished"); log.Close(); Application.Quit(); yield break; } // [ONLINE]
            if (partyOnly) { yield return PartyRun(); Log("capture finished"); log.Close(); Application.Quit(); yield break; } // [PARTY]
            if (dungeonOnly) { yield return DungeonRunCapture(); Log("capture finished"); log.Close(); Application.Quit(); yield break; } // [DUNGEON]
            if (balanceOnly) { yield return BalanceRun(); Log("capture finished"); log.Close(); Application.Quit(); yield break; } // [CONTENT]
            if (monsterOnly) { yield return MonsterRun(); Log("capture finished"); log.Close(); Application.Quit(); yield break; } // [MONSTER]
            var hd = HdShowcase();
            if (hd != null)
            {
                yield return hd;
                Log("capture finished");
                log.Close();
                Application.Quit();
                yield break;
            }
            if (townOnly)
            {
                yield return TownShowcase();
                Log("capture finished");
                log.Close();
                Application.Quit();
                yield break;
            }
            if (mapOnly)
            {
                yield return MapShowcase();
                Log("capture finished");
                log.Close();
                Application.Quit();
                yield break;
            }
            if (fxOnly)
            {
                yield return FxShowcase();
                Log("capture finished");
                log.Close();
                Application.Quit();
                yield break;
            }
            yield return Wait(1.5f);
            yield return Shot("01_title");

            Game.UI.Push(Game.UI.CharacterSelect);
            yield return Wait(0.6f);
            yield return Shot("02_character_select");

            Game.Flow.NewGame(CharacterClass.Mage);
            yield return Wait(1.5f);
            Log($"state={Game.State.Current} map={Game.World.MapId} class={Game.Player.Class}");
            MagicBolt.Fire(Game.Player.gameObject, Game.Player.Center + Vector2.right * 0.6f, Vector2.right, CharacterClassInfo.Get(CharacterClass.Mage));
            yield return Wait(0.15f);
            yield return Shot("03_village_mage");

            // Equipment: put gear in the bag, open the window, wear it, check the stats changed.
            var eq = Game.Session.Equipment;
            var bag = Game.Session.Inventory;
            Log($"before gear: atk+{eq.AttackBonus} hp={Game.Player.Health.Max} block={eq.BlockChance}% speed+{eq.SpeedBonus}% weapon={eq[EquipSlot.Weapon]}");
            Game.Session.Progression.SetFromServer(10, 0); // the Lv.10 staff and ring below need it
            foreach (var id in new[] { "eq_staff_10_u", "eq_neck_1_c", "eq_ring_10_r", "eq_ring_1_c", "eq_robe_1_u", "eq_skirt_1_c", "eq_sword_10_u" })
                bag.Add(id, 1);
            Game.Flow.OpenInventory();
            yield return Wait(0.5f);
            yield return Shot("03b_inventory_before");
            foreach (var id in new[] { "eq_staff_10_u", "eq_neck_1_c", "eq_ring_10_r", "eq_ring_1_c", "eq_robe_1_u", "eq_skirt_1_c" })
                Log($"equip {id}: {eq.Equip(id, Game.Player.Class)}");
            Log($"equip eq_sword_10_u as mage (should be false): {eq.Equip("eq_sword_10_u", Game.Player.Class)}");
            yield return Wait(0.5f);
            yield return Shot("03c_inventory_after");
            // Mouse hover → tooltip (sent through the real UI event system).
            var wornWeapon = GameObject.Find("Worn_Weapon");
            if (wornWeapon != null)
                UnityEngine.EventSystems.ExecuteEvents.Execute(wornWeapon, new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current),
                    UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            yield return Wait(0.3f);
            var tip = GameObject.Find("Tooltip");
            Log($"hover worn weapon: slotFound={wornWeapon != null} tooltipVisible={(tip != null && tip.activeInHierarchy)}");
            yield return Shot("03d_hover_tooltip");
            // Auto-equip: take everything off, then let the button put the best gear back on.
            for (int i = 0; i < Equipment.SlotCount; i++) eq.Unequip((EquipSlot)i);
            int autoChanged = eq.AutoEquip(Game.Player.Class);
            Log($"auto-equip: changed={autoChanged} weapon={eq[EquipSlot.Weapon]} rings={eq[EquipSlot.Ring1]},{eq[EquipSlot.Ring2]} atk+{eq.AttackBonus} hp={Game.Player.Health.Max}");
            Log($"after gear: atk+{eq.AttackBonus} hp={Game.Player.Health.Max}/{Game.Player.Health.Current} block={eq.BlockChance}% speed+{eq.SpeedBonus}% bagStaffOak={bag.Count("eq_staff_oak")}");
            var saved = Game.Session.Capture(Game.Player.Position, Game.Player.Facing);
            Log($"save equipped=[{string.Join(",", saved.equipped)}] baseMaxHp={saved.playerMaxHealth}");
            Game.Flow.CloseInventory();
            yield return Wait(0.3f);
            Log($"state after close={Game.State.Current}");

            // Side menu: open it, then each window in turn.
            var prog = Game.Session.Progression;
            int hpBefore = Game.Player.Health.Max, mpBefore = CharacterStats.MaxMp;
            prog.AddXp(2000);
            yield return Wait(0.3f);
            Log($"level up: Lv={prog.Level} xp={prog.Xp}/{prog.XpNeeded} points={prog.PointsLeft} hp {hpBefore}->{Game.Player.Health.Max} mp {mpBefore}->{CharacterStats.MaxMp}");
            var arcBefore = Game.Player.Skills.Numbers(0);
            Log($"after tree: points={prog.PointsLeft} dmg={CharacterStats.AttackDamage(Game.Player.Class)} inc={CharacterStats.IncDamage}% hp={Game.Player.Health.Max} mp={CharacterStats.MaxMp} speed+{CharacterStats.SpeedBonus}% refund LD allowed={prog.CanRefund(PassiveTree.Get("LD"))} refund Ld1 allowed={prog.CanRefund(PassiveTree.Get("Ld1"))}");
            prog.SetGem(0, 1, "sup_dmg"); prog.SetGem(0, 2, "sup_chain");
            prog.SetGem(1, 1, "sup_aoe"); prog.SetGem(1, 2, "sup_multi");
            var arcN = Game.Player.Skills.Numbers(0);
            var novaN = Game.Player.Skills.Numbers(1);
            Log($"slots open: {prog.IsSlotOpen(0)} {prog.IsSlotOpen(1)} {prog.IsSlotOpen(2)} {prog.IsSlotOpen(3)} ult={prog.IsSlotOpen(4)} | skill1 before tree dmg={arcBefore.damage} cd={arcBefore.cooldown:0.##}");
            Log($"gems: arc dmg={arcN.damage} cd={arcN.cooldown:0.##} cost={arcN.manaCost} chains={arcN.chains} | nova dmg={novaN.damage} cost={novaN.manaCost} radius={novaN.radius:0.##} repeats={novaN.repeats}");
            yield return Wait(0.2f);
            yield return Shot("03d2_hud_bars");

            var menuBtn = GameObject.Find("Btn_메뉴");
            menuBtn?.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
            yield return Wait(0.5f);
            yield return Shot("03e_side_menu");
            foreach (var (label, shot) in new[] { ("스킬", "03f_skills"), ("지도", "03g_map"), ("퀘스트", "03h_quest"), ("요일던전", "03i_dungeon"), ("레이드", "03j_raid") })
            {
                if (!GameObject.Find("Column")) menuBtn?.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
                var b = GameObject.Find("Btn_" + label);
                b?.GetComponent<UnityEngine.UI.Button>()?.onClick.Invoke();
                yield return Wait(0.4f);
                Log($"menu {label}: button={(b != null)} state={Game.State.Current} top={(Game.UI.Top != null ? Game.UI.Top.name : "-")}");
                yield return Shot(shot);
                if (label == "스킬")
                {
                    var gemTab = GameObject.Find("Tab1");
                    if (gemTab != null) UnityEngine.EventSystems.ExecuteEvents.Execute(gemTab, new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current), UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
                    yield return Wait(0.3f);
                    yield return Shot("03f2_skill_gems");
                }
                Game.Flow.CloseInventory();
                yield return Wait(0.3f);
            }

            // Materials and a protection ticket → bag "기타" tab.
            bag.Add("mat_bone", 40); bag.Add("mat_ore", 20); bag.Add("mat_essence", 6); bag.Add(ConsumableDatabase.ProtectTicket, 1);
            Game.Flow.OpenWindow(Game.UI.Equipment);
            yield return Wait(0.3f);
            var etcTab = GameObject.Find("Tab5");
            if (etcTab != null) UnityEngine.EventSystems.ExecuteEvents.Execute(etcTab, new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current), UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            yield return Wait(0.3f);
            yield return Shot("03k_bag_etc_tab");
            Game.Flow.CloseInventory();
            yield return Wait(0.3f);

            // Enhancement rules: logic checks, then the blacksmith window. Bag and gear are put back afterwards.
            yield return EnhanceChecks();

            // Loot table: ~40% drop rate, never the other class's weapon.
            int none = 0, wrongClass = 0;
            var seen = new System.Collections.Generic.Dictionary<string, int>();
            for (int i = 0; i < 2000; i++)
            {
                string id = EquipmentDatabase.RollDrop(CharacterClass.Mage, 0.4f);
                if (id == null) { none++; continue; }
                if (!EquipmentDatabase.Get(id).UsableBy(CharacterClass.Mage)) wrongClass++;
                seen[id] = seen.TryGetValue(id, out int c) ? c + 1 : 1;
            }
            Log($"loot x2000 (mage): dropRate={(2000 - none) / 20f:0.#}% wrongClass={wrongClass} kinds={seen.Count}");

            Game.Flow.TravelTo(MapRegistry.Canyon);
            yield return Wait(1.5f);
            Log($"after travel: map={Game.World.MapId} player={Game.Player.Position}");
            yield return BgmChecks(); // [BGM]
            yield return Shot("04_canyon_arrival");

            yield return Teleport(new Vector2(24f, 38f));
            yield return Shot("05_canyon_plaza");

            yield return Teleport(new Vector2(28f, 26f));
            // 8-way aim: no monsters near here, so bolts must fly along the diagonal aim.
            foreach (var d in new[] { new Vector2(1, 1), new Vector2(-1, 1), new Vector2(1, -1), new Vector2(-1, -1) })
            {
                Game.Player.FaceTowards(Game.Player.Position + d);
                Game.Player.GetComponent<PlayerCombat>().TryAttack();
                yield return Wait(0.4f);
                var bolt = FindAnyObjectByType<MagicBolt>();
                Vector2 off = bolt != null ? (Vector2)bolt.transform.position - Game.Player.Center : Vector2.zero;
                Log($"aim {d}: bolt offset=({off.x:0.00},{off.y:0.00})");
                if (d.x < 0 && d.y > 0) yield return Shot("05b_diagonal_cast");
                yield return Wait(0.5f);
            }
            yield return Teleport(new Vector2(24f, 47f));
            yield return Shot("06_canyon_stairs");

            // East side path: lookout deck + treasure chest.
            yield return Teleport(new Vector2(44f, 39f));
            yield return Shot("06b_lookout");
            var chest = FindAnyObjectByType<TreasureChest>();
            int ironBefore = Game.Session.Inventory.Count("eq_neck_10_r");
            if (chest != null) chest.Interact(Game.Player);
            yield return Wait(1.5f);
            Log($"chest: found={chest != null} opened={(chest != null && !chest.CanInteract)} chestNeck {ironBefore}->{Game.Session.Inventory.Count("eq_neck_10_r")}");
            yield return Teleport(new Vector2(17f, 14f));
            yield return Shot("06c_bridge");
            yield return Teleport(new Vector2(24f, 53f));
            yield return Shot("07_canyon_south");
            Log($"canyon is a safe town: monsters={EnemyController.Active.Count}");

            // Whole-map overview.
            var cam = Game.Camera.Camera;
            float size = cam.orthographicSize;
            cam.orthographicSize = Game.World.Bounds.height * 0.5f + 0.5f;
            yield return Wait(0.3f);
            yield return Shot("08_canyon_overview");
            cam.orthographicSize = size;

            // South portal → the hunting ground (skeleton forest).
            yield return Teleport(new Vector2(29f, 2.6f));
            yield return Wait(0.5f);
            Game.Player.Spawn(new Vector2(29f, 0.5f), Facing.Down, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            yield return Wait(2.5f);
            Log($"canyon south portal: map={Game.World.MapId} player={Game.Player.Position} monsters={EnemyController.Active.Count}");
            yield return StageSkeletons(new Vector2(15.5f, 25.5f));

            // Auto-targeting: face away from the skeletons and cast; bolts should still find them.
            var combat = Game.Player.GetComponent<PlayerCombat>();
            int deadBefore = CountDead();
            for (int i = 0; i < 10; i++)
            {
                Game.Player.HealFull();
                Game.Player.FaceTowards(Game.Player.Position + Vector2.up);
                combat.TryAttack();
                yield return Wait(i == 1 ? 0.5f : 0.6f);
                if (i == 1) yield return Shot("07b_autotarget");
            }
            yield return Wait(0.5f);
            Log($"autotarget: enemies={EnemyController.Active.Count} deadBefore={deadBefore} deadAfter={CountDead()}");

            // Skills: cast the arc and the frost nova next to the skeletons.
            yield return StageSkeletons(new Vector2(15.5f, 25.5f));
            int killsBefore = CountDead();
            int lvBefore = Game.Session.Progression.Level, xpBefore = Game.Session.Progression.Xp;
            Game.Session.PlayerMana = CharacterStats.MaxMp;
            foreach (var e in EnemyController.Active) { if (!e.IsDead) { Game.Player.Spawn(e.Position + Vector2.left * 1.5f, Facing.Right, int.MaxValue, 0); break; } }
            Game.Camera.SetTarget(Game.Player.transform, true);
            yield return Wait(0.2f);
            float mpStart = Game.Session.PlayerMana;
            Game.Player.Skills.TryCast(1);
            yield return Wait(0.12f);
            yield return Shot("07c_frost_nova");
            int frozen = 0;
            foreach (var e in EnemyController.Active) if (e.IsFrozen) frozen++;
            yield return Wait(0.6f);
            Game.Player.Skills.TryCast(0);
            yield return Wait(0.05f);
            yield return Shot("07d_arc");
            yield return Wait(1.2f);
            Log($"skills: frozen={frozen} mp {mpStart:0}->{Game.Session.PlayerMana:0} kills +{CountDead() - killsBefore} xp Lv{lvBefore}:{xpBefore} -> Lv{Game.Session.Progression.Level}:{Game.Session.Progression.Xp}");
            int drops = 0;
            foreach (var p in FindObjectsByType<Pickup>(FindObjectsInactive.Exclude)) if (p.name.StartsWith("Pickup_eq_")) drops++;
            Log($"equipment pickups on the ground after kills: {drops}");

            // Walk back through the forest's west portal into the village.
            yield return Teleport(new Vector2(2.5f, 21.5f));
            yield return Wait(0.5f);
            Game.Player.Spawn(new Vector2(0.4f, 21.5f), Facing.Left, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            yield return Wait(2.5f);
            Log($"after portal: map={Game.World.MapId} player={Game.Player.Position}");
            yield return Shot("09_back_in_village");

            Log("capture finished");
            log.Close();
            Application.Quit();
        }

    }
}
