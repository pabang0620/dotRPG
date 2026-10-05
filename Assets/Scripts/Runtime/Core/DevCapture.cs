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
        static readonly string[] Modes = { "-dotrpgCapture", "-dotrpgFx", "-dotrpgMap", "-dotrpgTown", "-dotrpgCanyon", "-dotrpgWinter", "-dotrpgChars", "-dotrpgUi", "-dotrpgDepth", "-dotrpgStairs", "-dotrpgSilver",
            "-dotrpgParty", "-dotrpgDungeon", "-dotrpgMonster", "-dotrpgBalance", "-dotrpgOnline", "-dotrpgHouse", "-dotrpgMobility", "-dotrpgNature", "-dotrpgWater", "-dotrpgStory", "-dotrpgVillageArt", "-dotrpgNetPair", "-dotrpgOnlinePause", "-dotrpgPresentation", "-dotrpgHunting", "-dotrpgPerf", "-dotrpgCareer", "-dotrpgCareerDemo" }; // [PARTY] [DUNGEON] [MONSTER] [CONTENT]

        /// <summary>Test runs keep their saves next to their report, so the player's own save slot is never overwritten.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void RedirectSaves()
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (Array.IndexOf(Modes, args[i]) >= 0)
                {
                    SaveSystem.DirectoryOverride = Path.Combine(args[i + 1], "saves");
                    if (args[i] == "-dotrpgCareerDemo") SaveSystem.SlotCount = 4;
                    GameFlow.PauseOnFocusLoss = false;
                    return;
                }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Init()
        {
            var args = Environment.GetCommandLineArgs();
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
            if (mode == "-dotrpgCareerDemo") { yield return CareerDemoRun(); Log("career demo ready: four level-40 characters"); log.Close(); log=null; Destroy(this); yield break; }
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
            foreach (var id in new[] { "eq_staff_crystal", "eq_neck_leaf", "eq_ring_wind", "eq_ring_copper", "eq_top_leather", "eq_bot_cloth", "eq_sword_iron" })
                bag.Add(id, 1);
            Game.Flow.OpenInventory();
            yield return Wait(0.5f);
            yield return Shot("03b_inventory_before");
            foreach (var id in new[] { "eq_staff_crystal", "eq_neck_leaf", "eq_ring_wind", "eq_ring_copper", "eq_top_leather", "eq_bot_cloth" })
                Log($"equip {id}: {eq.Equip(id, Game.Player.Class)}");
            Log($"equip eq_sword_iron as mage (should be false): {eq.Equip("eq_sword_iron", Game.Player.Class)}");
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
            foreach (var id in new[] { "Lt0", "Lt1", "Ld1", "LD", "Lc1", "LC", "Rt0", "Rt1", "UM" })
                Log($"allocate {id}: {prog.Allocate(PassiveTree.Get(id))}");
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
            int ironBefore = Game.Session.Inventory.Count("eq_top_iron");
            if (chest != null) chest.Interact(Game.Player);
            yield return Wait(1.5f);
            Log($"chest: found={chest != null} opened={(chest != null && !chest.CanInteract)} ironPlate {ironBefore}->{Game.Session.Inventory.Count("eq_top_iron")}");
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

        /// <summary>
        /// Village town and forest hunting ground: whole-map renders at 32 px per tile (1:1 with the new
        /// art), close-ups at 64 px per tile, in-game screenshots with the HUD and the minimap.
        /// </summary>
        IEnumerator TownShowcase()
        {
            yield return Wait(1f);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Game.Flow.NewGame(CharacterClass.Mage);
            yield return Wait(1.5f);
            Log($"new game: map={Game.World.MapId} size={Game.World.Bounds.width}x{Game.World.Bounds.height} spawn={Game.World.PlayerSpawn} objects={Game.World.ObjectsRoot.childCount} minimap={Game.World.Minimap.width}x{Game.World.Minimap.height} gold={Game.Session.Gold}");
            yield return Shot("town_ingame_start");
            SaveTexture(Game.World.Minimap, Path.Combine(folder, "town_minimap_texture.png"));
            yield return RenderMap("town", new[]
            {
                ("plaza", 28.5f, 20.5f), ("north", 28f, 31f), ("east", 44f, 22f), ("farm", 10f, 13f), ("pond", 24f, 7f), ("exit", 51f, 21f),
                ("houses", 13.5f, 25.5f),
            });
            yield return ServiceTests();

            // The forest hunting ground.
            yield return Teleport(new Vector2(56.2f, 21.5f));
            Game.Player.Spawn(new Vector2(57.6f, 21.5f), Facing.Right, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            sw.Restart();
            yield return Wait(2.5f);
            Log($"via east exit: map={Game.World.MapId} player={Game.Player.Position} monsters={EnemyController.Active.Count}");
            if (Game.World.MapId != MapRegistry.Forest)
            {
                Game.Flow.TravelTo(MapRegistry.Forest);
                yield return Wait(2.5f);
            }
            Log($"forest: size={Game.World.Bounds.width}x{Game.World.Bounds.height} objects={Game.World.ObjectsRoot.childCount}");
            yield return Shot("forest_ingame_arrival");
            yield return RenderMap("forest", new[]
            {
                ("entrance", 9f, 22f), ("graveyard", 33f, 36f), ("pond", 30f, 8f), ("east", 52f, 30f), ("rocks", 55f, 26f),
            });
            yield return ForestCombatTests();

            // Timing of a fresh paint (cache cleared) for both maps.
            Log($"paint timing: village={TimeLoad(MapRegistry.Village)}ms forest={TimeLoad(MapRegistry.Forest)}ms (cached reload={TimeLoad(MapRegistry.Forest, false)}ms)");
            MigrationCheck();
            Game.World.Load(MapRegistry.Forest);
            Game.Player.Spawn(new Vector2(8f, 22f), Facing.Right, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            Game.Camera.SetTarget(Game.Player.transform, true);
            yield return Wait(0.5f);
        }

        /// <summary>
        /// Forest tests: brings up to four live skeletons onto the open trail a few tiles east of
        /// <paramref name="player"/> and puts the player there (map "forest", west of the first fork).
        /// </summary>
        IEnumerator StageSkeletons(Vector2 player)
        {
            var spots = new[] { new Vector2(4f, 0f), new Vector2(4.9f, 1f), new Vector2(5.2f, -0.9f), new Vector2(6.2f, 0.2f) };
            int k = 0;
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead || !e.isActiveAndEnabled || k >= spots.Length) continue;
                Vector2 q = player + spots[k++];
                var rb = e.GetComponent<Rigidbody2D>();
                if (rb != null) rb.position = q;
                e.transform.position = q;
            }
            Game.Player.Spawn(player, Facing.Right, int.MaxValue, 0);
            Game.Camera.SetTarget(Game.Player.transform, true);
            yield return Wait(0.4f);
        }

        static NpcController FindNpc(string id)
        {
            foreach (var npc in FindObjectsByType<NpcController>(FindObjectsInactive.Exclude))
                if (npc.Definition != null && npc.Definition.npcId == id) return npc;
            return null;
        }

        /// <summary>Village services: general store (buy / sell), storage, blacksmith, shop door, bag consumables, potions.</summary>
        IEnumerator ServiceTests()
        {
            var bag = Game.Session.Inventory;
            var shop = Game.UI.Shop;
            string town = MapRegistry.IsTown(Game.World.MapId) ? Game.World.MapId : MapRegistry.Village;
            void EnterService(NpcService service)
            {
                string room = MapRegistry.InteriorFor(town, service);
                Game.World.Load(room); Game.Session.MapId = room; Game.Player.Place(Game.World.PlayerSpawn, Facing.Up);
                Game.Camera.SetTarget(Game.Player.transform, true);
            }
            EnterService(NpcService.Shop);
            var merchant = FindNpc("merchant");
            Log($"services: merchant={(merchant != null)} smith={(FindNpc("smith") != null)} keeper={(FindNpc("keeper") != null)} serviceIcons={NpcController.Services.Count}");
            if (merchant != null)
            {
                yield return Teleport((Vector2)merchant.transform.position + Vector2.down * 1.2f);
                Log($"merchant prompt='{merchant.Prompt}'");
                merchant.Interact(Game.Player);
                yield return Wait(0.5f);
                Log($"shop open: state={Game.State.Current} top={(Game.UI.Top != null ? Game.UI.Top.name : "-")}");
                int gold0 = Game.Session.Gold, hp0 = bag.Count(ConsumableDatabase.HpPotion);
                shop.DevSelect(ConsumableDatabase.HpPotion);
                shop.DevTrade(1);
                Log($"buy 1 hp potion: gold {gold0}->{Game.Session.Gold} potions {hp0}->{bag.Count(ConsumableDatabase.HpPotion)} msg='{Strip(shop.DevResult)}'");
                int mp0 = bag.Count(ConsumableDatabase.MpPotion);
                shop.DevSelect(ConsumableDatabase.MpPotion);
                shop.DevTrade(10);
                Log($"buy 10 mp potions with {gold0 - 30} gold: mp {mp0}->{bag.Count(ConsumableDatabase.MpPotion)} gold={Game.Session.Gold} msg='{Strip(shop.DevResult)}'");
                yield return Wait(0.3f);
                yield return Shot("svc_shop_buy");
                bag.Add("eq_sword_iron", 1);
                bag.Add("mat_bone", 12);
                bag.Add(ItemIds.Wood, 3);
                shop.DevMode(true);
                int g1 = Game.Session.Gold;
                shop.DevSelect("eq_sword_iron");
                shop.DevTrade(1);
                shop.DevSelect("mat_bone");
                shop.DevTrade(int.MaxValue);
                Log($"sell iron sword + 12 bone: gold {g1}->{Game.Session.Gold} sword={bag.Count("eq_sword_iron")} bone={bag.Count("mat_bone")} msg='{Strip(shop.DevResult)}'");
                shop.DevSelect(ItemIds.Wood);
                yield return Wait(0.3f);
                yield return Shot("svc_shop_sell");
                Game.Flow.CloseInventory();
                yield return Wait(0.3f);
            }

            EnterService(NpcService.Storage);
            var keeper = FindNpc("keeper");
            if (keeper != null)
            {
                bag.Add("mat_ore", 4);
                bag.Add("mat_essence", 1);
                yield return Teleport((Vector2)keeper.transform.position + Vector2.down * 1.2f);
                keeper.Interact(Game.Player);
                yield return Wait(0.5f);
                var store = Game.UI.Storage;
                Log($"storage open: top={(Game.UI.Top != null ? Game.UI.Top.name : "-")}");
                store.DevDepositMaterials();
                store.DevMove(ConsumableDatabase.HpPotion, true, false);
                Log($"deposit materials + 1 hp potion: bag ore={bag.Count("mat_ore")} wood={bag.Count(ItemIds.Wood)} | storage ore={Game.Session.Storage.Count("mat_ore")} essence={Game.Session.Storage.Count("mat_essence")} wood={Game.Session.Storage.Count(ItemIds.Wood)} hp={Game.Session.Storage.Count(ConsumableDatabase.HpPotion)}");
                yield return Wait(0.3f);
                yield return Shot("svc_storage");
                var saved = Game.Session.Capture(Game.Player.Position, Game.Player.Facing);
                Log($"save: storage entries={saved.storage.Count} version={SaveData.CurrentVersion}");
                store.DevMove(ConsumableDatabase.HpPotion, false, true);
                Log($"withdraw hp potion: bag hp={bag.Count(ConsumableDatabase.HpPotion)} storage hp={Game.Session.Storage.Count(ConsumableDatabase.HpPotion)}");
                Game.Flow.CloseInventory();
                yield return Wait(0.3f);
            }

            EnterService(NpcService.Blacksmith);
            var smith = FindNpc("smith");
            if (smith != null)
            {
                bag.Add("mat_bone", 10);
                bag.Add("mat_ore", 4);
                yield return Teleport((Vector2)smith.transform.position + Vector2.down * 1.2f);
                Log($"smith prompt='{smith.Prompt}'");
                smith.Interact(Game.Player);
                yield return Wait(0.5f);
                Log($"smith window: top={(Game.UI.Top != null ? Game.UI.Top.name : "-")}");
                yield return Shot("svc_smith");
                Game.Flow.CloseInventory();
                yield return Wait(0.3f);
            }

            // Real entrance/keeper/exit behaviour is checked in AtlasChecks. Restore the town for the bag showcase.
            Game.World.Load(town); Game.Session.MapId = town; Game.Player.Place(Game.World.PlayerSpawn, Facing.Down);
            Game.Camera.SetTarget(Game.Player.transform, true);

            // Bag: consumables tab.
            Game.Flow.OpenWindow(Game.UI.Equipment);
            yield return Wait(0.3f);
            var tab = GameObject.Find("Tab4");
            if (tab != null) UnityEngine.EventSystems.ExecuteEvents.Execute(tab, new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current), UnityEngine.EventSystems.ExecuteEvents.pointerClickHandler);
            yield return Wait(0.3f);
            var firstCell = GameObject.Find("Cell");
            if (firstCell != null) UnityEngine.EventSystems.ExecuteEvents.Execute(firstCell, new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current), UnityEngine.EventSystems.ExecuteEvents.pointerEnterHandler);
            yield return Wait(0.3f);
            yield return Shot("bag_consumables");
            Game.Flow.CloseInventory();
            yield return Wait(0.3f);

            // Potions and the scroll (in the village the scroll must refuse).
            var p = Game.Player;
            p.Health.Drain(35);
            int hpBefore = p.Health.Current;
            bool usedHp = p.UseConsumable(ConsumableDatabase.HpPotion);
            Log($"hp potion: used={usedHp} hp {hpBefore}->{p.Health.Current}/{p.Health.Max}");
            Game.Session.PlayerMana = 5f;
            bool usedMp = p.UseConsumable(ConsumableDatabase.MpPotion);
            Log($"mp potion: used={usedMp} mp 5->{Game.Session.PlayerMana:0}/{CharacterStats.MaxMp}");
            bool scrollInTown = p.UseConsumable(ConsumableDatabase.TownScroll);
            Log($"scroll in the village (should be false): {scrollInTown} scrolls={bag.Count(ConsumableDatabase.TownScroll)}");
            yield return Wait(0.5f);
            yield return Teleport(new Vector2(28.5f, 17.5f));
            yield return Shot("town_ingame_plaza_hud");
        }

        /// <summary>Forest: level to 6, cast the ice-lightning orb at skeletons, collect gold, read the return scroll.</summary>
        IEnumerator ForestCombatTests()
        {
            var prog = Game.Session.Progression;
            prog.AddXp(700);
            yield return Wait(0.3f);
            Log($"forest combat: Lv{prog.Level} slot3 open={prog.IsSlotOpen(2)} skill={prog.Active(2)?.id} name={prog.Active(2)?.name}");
            var n = Game.Player.Skills.Numbers(2);
            Log($"frost orb numbers: dmg={n.damage} radius={n.radius:0.##} range={n.range:0.#} zapTargets={n.chains} freeze={n.freeze:0.#}s cd={n.cooldown:0.##} mp={n.manaCost}");
            // Stage it on the open trail west of the first fork: three skeletons ahead, the mage 6 tiles back.
            var spots = new[] { new Vector2(19.5f, 25.5f), new Vector2(20.4f, 26.5f), new Vector2(20.7f, 24.6f) };
            var picked = new List<EnemyController>();
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead && e.isActiveAndEnabled && picked.Count < spots.Length) picked.Add(e);
            if (picked.Count == 0) { Log("no skeleton found"); yield break; }
            for (int k = 0; k < picked.Count; k++)
            {
                var rb = picked[k].GetComponent<Rigidbody2D>();
                if (rb != null) rb.position = spots[k];
                picked[k].transform.position = spots[k];
            }
            var target = picked[0];
            Game.Player.Spawn(new Vector2(13.5f, 25.5f), Facing.Right, int.MaxValue, 0);
            Game.Camera.SetTarget(Game.Player.transform, true);
            yield return Wait(0.6f);
            for (int k = 0; k < picked.Count; k++)
            {
                var rb = picked[k].GetComponent<Rigidbody2D>();
                if (rb != null) rb.position = spots[k];
                picked[k].transform.position = spots[k];
            }
            Game.Session.PlayerMana = CharacterStats.MaxMp;
            int gold0 = Game.Session.Gold, kills0 = CountDead();
            Game.Player.FaceTowards(target.Position);
            Game.Player.Skills.TryCast(2);
            float t0 = Time.realtimeSinceStartup;
            foreach (float at in new[] { 0.12f, 0.3f, 0.5f, 0.68f, 0.86f, 1.05f })
            {
                while (Time.realtimeSinceStartup - t0 < at) yield return null;
                yield return Shot($"fx_frostorb_{Mathf.RoundToInt(at * 100):00}");
            }
            int frozen = 0, encased = 0;
            foreach (var e in EnemyController.Active) { if (e.IsFrozen) frozen++; if (e.GetComponent<IceEncase>() != null) encased++; }
            Log($"frost orb: frozen={frozen} encased={encased} kills +{CountDead() - kills0}");
            // Finish them with basic attacks and orbs, then walk over the drops.
            for (int k = 0; k < 12 && CountDead() - kills0 < 3; k++)
            {
                Game.Session.PlayerMana = CharacterStats.MaxMp;
                if (k % 3 == 2) Game.Player.Skills.TryCast(2); else Game.Player.GetComponent<PlayerCombat>().TryAttack();
                yield return Wait(0.45f);
            }
            Log($"after fight: kills +{CountDead() - kills0}");
            // The fight above is a showcase; finish one skeleton outright so the gold drop is always checked.
            if (CountDead() == kills0)
            {
                EnemyController victim = null;
                foreach (var e in EnemyController.Active)
                    if (e != null && !e.IsDead) { victim = e; break; }
                var victimHp = victim != null ? victim.GetComponent<Health>() : null;
                for (int k = 0; k < 6 && victim != null && !victim.IsDead; k++)
                {
                    victim.TakeDamage(new DamageInfo((victimHp != null ? victimHp.Current : 0) + 999, Game.Player.transform.position, 0f, Team.Player));
                    yield return Wait(0.3f);
                }
                yield return Wait(0.7f);
                Log($"finisher: kills +{CountDead() - kills0}");
            }
            foreach (var pu in FindObjectsByType<Pickup>(FindObjectsInactive.Exclude))
            {
                if (pu == null) continue;
                Game.Player.Spawn(pu.transform.position, Facing.Down, Game.Session.PlayerHealth, 0);
                yield return Wait(0.6f);
            }
            yield return Wait(0.8f);
            int killsNow = CountDead() - kills0, gained = Game.Session.Gold - gold0;
            Log($"gold after pickups: {gold0}->{Game.Session.Gold} (+{gained} from {killsNow} kills, expect {8 * killsNow}-{16 * killsNow}) ok={(killsNow > 0 && gained >= 8 * killsNow && gained <= 16 * killsNow)} bone={Game.Session.Inventory.Count("mat_bone")}");
            yield return Shot("forest_after_fight");

            // Return scroll: light column, then back to the village square.
            int scrolls = Game.Session.Inventory.Count(ConsumableDatabase.TownScroll);
            if (scrolls == 0) Game.Session.Inventory.Add(ConsumableDatabase.TownScroll, 1);
            bool used = Game.Player.UseConsumable(ConsumableDatabase.TownScroll);
            yield return Wait(0.5f);
            yield return Shot("scroll_cast");
            yield return Wait(2.5f);
            Log($"return scroll: used={used} map={Game.World.MapId} player={Game.Player.Position} spawn={Game.World.PlayerSpawn} scrolls left={Game.Session.Inventory.Count(ConsumableDatabase.TownScroll)}");
            yield return Shot("scroll_arrival");
        }

        static string Strip(string rich) => System.Text.RegularExpressions.Regex.Replace(rich ?? "", "<.*?>", "");

        /// <summary>
        /// A version-2 save (before gold, potions and the rebuilt village) standing somewhere in the old
        /// village must load with the starter pack added and the start point reset. Uses slot 7, never the player's own save.
        /// </summary>
        void MigrationCheck()
        {
            const int slot = 7;
            string path = SaveSystem.SlotPath(slot);
            var old = new SaveData { version = 2, mapId = MapRegistry.Village, playerClass = "mage", playerX = 30f, playerY = 12f, playerHealth = 50, playerMaxHealth = 60, level = 3 };
            old.inventory.Add(new ItemStack(ItemIds.Wood, 4));
            Directory.CreateDirectory(SaveSystem.SaveDirectory);
            File.WriteAllText(path, JsonUtility.ToJson(old));
            var loaded = Game.Saves.Read(slot);
            int Count(string id) { int n = 0; foreach (var s in loaded.inventory) if (s.id == id) n += s.count; return n; }
            Log($"migration v2->v{loaded.version}: gold={Count(ConsumableDatabase.Gold)} hp={Count(ConsumableDatabase.HpPotion)} mp={Count(ConsumableDatabase.MpPotion)} scroll={Count(ConsumableDatabase.TownScroll)} wood={Count(ItemIds.Wood)} startReset={(loaded.playerX < 0f)} storage={(loaded.storage != null)}");
            Game.Saves.Delete(slot);
        }

        long TimeLoad(string mapId, bool fresh = true)
        {
            if (fresh) WorldBuilder.ClearPaintedGroundCache();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Game.World.Load(mapId);
            return sw.ElapsedMilliseconds;
        }

        /// <summary>Whole map at 32 px per tile, then close-ups (1280x720 at 64 px per tile) around the given points.</summary>
        IEnumerator RenderMap(string prefix, (string name, float x, float y)[] closeups)
        {
            SetPlayerVisible(false);
            SetExitMarkersVisible(false);
            SetCharactersVisible(false);
            yield return new WaitForEndOfFrame();
            RenderRegion(Path.Combine(folder, prefix + "_overview.png"), Game.World.Bounds, 32);
            float hw = 10f, hh = 5.625f;
            foreach (var (name, x, y) in closeups)
                RenderRegion(Path.Combine(folder, $"{prefix}_{name}.png"), new Rect(x - hw, y - hh, hw * 2f, hh * 2f), 64);
            SetCharactersVisible(true);
            SetExitMarkersVisible(true);
            SetPlayerVisible(true);
            Log($"rendered {prefix}: overview + {closeups.Length} close-ups");
        }

        static void SetCharactersVisible(bool visible)
        {
            foreach (var npc in Game.World.ObjectsRoot.GetComponentsInChildren<NpcController>(true))
                foreach (var r in npc.GetComponentsInChildren<SpriteRenderer>(true)) r.forceRenderingOff = !visible;
            foreach (var e in Game.World.ObjectsRoot.GetComponentsInChildren<EnemyController>(true))
                foreach (var r in e.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = !visible;
        }

        static void SaveTexture(Texture2D tex, string file)
        {
            if (tex == null) return;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(file, copy.EncodeToPNG());
            Destroy(copy);
        }

        /// <summary>
        /// Winter map: renders the whole map at 2x (32px per tile) and some close-ups at 3x straight
        /// from the camera into textures (no HUD, player hidden), then a couple of in-game screenshots.
        /// </summary>
        IEnumerator MapShowcase()
        {
            yield return Wait(1f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.2f);
            // Walk into the canyon's north portal (top edge, x 28-31) to reach the winter village.
            Game.Flow.TravelTo(MapRegistry.Canyon);
            yield return Wait(2f);
            yield return Teleport(new Vector2(29.5f, 58.3f));
            Game.Player.Spawn(new Vector2(29.5f, 59.4f), Facing.Up, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            yield return Wait(3f);
            Log($"via canyon portal: map={Game.World.MapId} player={Game.Player.Position}");
            if (Game.World.MapId != MapRegistry.Winter)
            {
                Game.Flow.TravelTo(MapRegistry.Winter);
                yield return Wait(3f);
            }
            var b = Game.World.Bounds;
            Log($"map={Game.World.MapId} size={b.width}x{b.height} spawn={Game.World.PlayerSpawn} objects={Game.World.ObjectsRoot.childCount}");
            SetPlayerVisible(false);
            SetExitMarkersVisible(false);
            yield return new WaitForEndOfFrame();
            RenderRegion(Path.Combine(folder, "winter_overview.png"), b, 32);
            float hw = 13.35f, hh = 7.5f; // 1280x720 at 48px per tile
            foreach (var (name, x, y) in new[]
            {
                ("winter_plaza", 23.5f, 25.5f), ("winter_house", 13.4f, 23.5f), ("winter_camp", 35.5f, 25.5f), ("winter_farm", 14f, 7.5f),
                ("winter_bridge", 38.6f, 23.5f), ("winter_north", 19.5f, 38.5f), ("winter_lookout", 38.6f, 38.5f), ("winter_chest", 38.6f, 31.5f),
            })
                RenderRegion(Path.Combine(folder, name + ".png"), new Rect(x - hw, y - hh, hw * 2f, hh * 2f), 48);
            SetExitMarkersVisible(true);
            SetPlayerVisible(true);
            yield return Wait(0.3f);
            yield return Shot("winter_ingame_start");
            yield return Teleport(new Vector2(23.5f, 21.5f));
            yield return Wait(0.3f);
            yield return Shot("winter_ingame_plaza");
            Log($"after walk: player={Game.Player.Position}");
        }

        static void SetPlayerVisible(bool visible)
        {
            foreach (var r in Game.Player.GetComponentsInChildren<SpriteRenderer>(true)) r.forceRenderingOff = !visible;
        }

        /// <summary>The glowing exit arrows are a gameplay hint, so the clean map renders leave them out.</summary>
        static void SetExitMarkersVisible(bool visible)
        {
            foreach (var r in Game.World.ObjectsRoot.GetComponentsInChildren<SpriteRenderer>(true))
                if (r.gameObject.name.StartsWith("arrow_")) r.forceRenderingOff = !visible;
        }

        /// <summary>Renders a world rectangle to a PNG at a fixed number of pixels per tile (crisp pixel art).</summary>
        static void RenderRegion(string file, Rect region, int pixelsPerTile)
        {
            var cam = Game.Camera.Camera;
            int w = Mathf.RoundToInt(region.width * pixelsPerTile), h = Mathf.RoundToInt(region.height * pixelsPerTile);
            var rt = new RenderTexture(w, h, 24) { filterMode = FilterMode.Point };
            var prevTarget = cam.targetTexture;
            float prevSize = cam.orthographicSize;
            var prevPos = cam.transform.position;
            cam.targetTexture = rt;
            cam.aspect = region.width / region.height;
            cam.orthographicSize = region.height * 0.5f;
            cam.transform.position = new Vector3(region.center.x, region.center.y, prevPos.z);
            var prevActive = RenderTexture.active;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prevTarget;
            cam.ResetAspect();
            cam.orthographicSize = prevSize;
            cam.transform.position = prevPos;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex);
            rt.Release();
            Destroy(rt);
        }

        IEnumerator Teleport(Vector2 position)
        {
            Game.Player.Spawn(position, Facing.Down, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            Game.Camera.SetTarget(Game.Player.transform, true);
            yield return Wait(0.4f);
        }

        /// <summary>
        /// Writes a contact sheet: one row per look (with and without gear), columns = the 8 facings
        /// (idle and a walk frame each), then the held weapons. Pixel art scaled 4x.
        /// </summary>
        static void SaveCharacterSheet(string file)
        {
            var looks = new List<CharacterLook>
            {
                CharacterLook.Player,
                CharacterLook.WithGear(CharacterLook.Player, "eq_top_cloth", "eq_bot_cloth"),
                CharacterLook.WithGear(CharacterLook.Player, "eq_top_leather", "eq_bot_leather"),
                CharacterLook.WithGear(CharacterLook.Player, "eq_top_iron", "eq_bot_leather"),
                CharacterLook.Mage,
                CharacterLook.WithGear(CharacterLook.Mage, "eq_top_iron", "eq_bot_cloth"),
                CharacterLook.Skeleton,
                CharacterLook.Chief,
                CharacterLook.Farmer,
            };
            Facing[] facings = { Facing.Down, Facing.DownRight, Facing.Right, Facing.UpRight, Facing.Up, Facing.UpLeft, Facing.Left, Facing.DownLeft };
            string[] frames = { "idle0", "walk0" };
            // Cells fit the largest frame (16px, 32px or 64px art alike); the scale keeps the sheet readable.
            int maxW = 0, maxH = 0;
            foreach (var look in looks)
                foreach (var f in facings)
                {
                    var s = Game.Art.GetCharacter(look, f.SpriteKey(), "idle0");
                    if (s != null) { maxW = Mathf.Max(maxW, (int)s.rect.width); maxH = Mathf.Max(maxH, (int)s.rect.height); }
                }
            for (int t = 0; t < 4; t++)
                foreach (var key in new[] { $"wpn_sword_{t}", $"wpn_staff_{t}" })
                {
                    var s = Game.Art.Get(key);
                    if (s != null) { maxW = Mathf.Max(maxW, (int)s.rect.width); maxH = Mathf.Max(maxH, (int)s.rect.height); }
                }
            int cellW = maxW + 2, cellH = maxH + 7, scale = Mathf.Clamp(108 / Mathf.Max(1, cellH), 1, 4);
            int cols = facings.Length * frames.Length, rows = looks.Count + 1;
            var sheet = new Texture2D(cols * cellW * scale, rows * cellH * scale, TextureFormat.RGBA32, false);
            int sheetW = sheet.width, sheetH = sheet.height;
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(58, 74, 92, 255);

            void Blit(Sprite s, int col, int row, bool flip)
            {
                if (s == null) return;
                var tex = s.texture;
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                int ox = col * cellW * scale + (cellW - w) / 2 * scale, oy = (rows - 1 - row) * cellH * scale + scale;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var c = px[y * w + (flip ? w - 1 - x : x)];
                        if (c.a == 0) continue;
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                            {
                                int sx = ox + x * scale + dx, sy = oy + y * scale + dy;
                                if (sx >= 0 && sy >= 0 && sx < sheetW && sy < sheetH) fill[sy * sheetW + sx] = c;
                            }
                    }
            }

            for (int r = 0; r < looks.Count; r++)
                for (int f = 0; f < facings.Length; f++)
                    for (int k = 0; k < frames.Length; k++)
                        Blit(Game.Art.GetCharacter(looks[r], facings[f].SpriteKey(), frames[k]), f * frames.Length + k, r, facings[f].IsLeft());
            for (int t = 0; t < 4; t++)
            {
                Blit(Game.Art.Get($"wpn_sword_{t}"), t, looks.Count, false);
                Blit(Game.Art.Get($"wpn_staff_{t}"), 4 + t, looks.Count, false);
            }
            sheet.SetPixels32(fill);
            sheet.Apply();
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            Destroy(sheet);
        }

        /// <summary>
        /// Warrior then mage: level up, socket both skills, stand next to a canyon skeleton, cast each
        /// skill and grab frames mid-effect. Also checks that Frost Nova freezes and encases monsters.
        /// </summary>
        IEnumerator FxShowcase()
        {
            yield return Wait(1f);
            SaveCharacterSheet(Path.Combine(folder, "sheet_characters.png"));
            Log("character sheet saved");
            foreach (var cls in new[] { CharacterClass.Warrior, CharacterClass.Mage })
            {
                Game.Flow.NewGame(cls);
                yield return Wait(1.2f);
                var prog = Game.Session.Progression;
                prog.AddXp(2600); // Lv10: all five slots open
                bool mage = cls == CharacterClass.Mage;
                prog.SetGem(0, 1, mage ? "sup_chain" : "sup_aoe");
                prog.SetGem(1, 1, "sup_aoe");
                Game.Flow.TravelTo(MapRegistry.Forest);
                yield return Wait(4f); // let the level-up toasts and the map banner clear
                Log($"fx class={Game.Player.Class} Lv{prog.Level} map={Game.World.MapId} monsters={EnemyController.Active.Count}");
                for (int slot = 0; slot <= SkillGems.Slots; slot++)
                {
                    yield return StageSkeletons(new Vector2(15.5f, 25.5f));
                    // Slots 0-4 are the skills (4 = awakening); the last round is the class's basic attack.
                    bool basic = slot == SkillGems.Slots;
                    string id = !basic ? prog.Active(slot)?.id ?? "none" : mage ? "bolt" : "sword";
                    EnemyController target = null;
                    foreach (var e in EnemyController.Active)
                        if (e != null && !e.IsDead && e.isActiveAndEnabled) { target = e; break; }
                    if (target == null) { Log($"fx {id}: no monster found"); continue; }
                    // Gather a few more monsters around the target so chains and area hits show.
                    var offsets = new[] { new Vector2(1.5f, 0.9f), new Vector2(2.3f, -0.5f), new Vector2(0.9f, -1.3f) };
                    int placed = 0;
                    foreach (var e in EnemyController.Active)
                    {
                        if (e == target || e == null || e.IsDead || placed >= offsets.Length) continue;
                        Vector2 p = target.Position + offsets[placed++];
                        var rb = e.GetComponent<Rigidbody2D>();
                        if (rb != null) rb.position = p;
                        e.transform.position = p;
                    }
                    Game.Player.Spawn(target.Position + Vector2.left * 1.4f, Facing.Right, int.MaxValue, 0);
                    Game.Camera.SetTarget(Game.Player.transform, true);
                    Game.Session.PlayerMana = CharacterStats.MaxMp;
                    yield return Wait(0.35f);
                    Game.Player.FaceTowards(target.Position);
                    int kills = CountDead();
                    if (!basic) Game.Player.Skills.TryCast(slot);
                    else Game.Player.GetComponent<PlayerCombat>().TryAttack();
                    float t0 = Time.realtimeSinceStartup;
                    bool ult = slot == SkillGems.UltimateSlot;
                    foreach (float at in ult ? new[] { 0.2f, 0.62f, 1.05f } : new[] { 0.05f, 0.14f, 0.3f })
                    {
                        while (Time.realtimeSinceStartup - t0 < at) yield return null;
                        yield return Shot($"fx_{id}_{Mathf.RoundToInt(at * 100):00}");
                    }
                    int frozen = 0, encased = 0;
                    foreach (var e in EnemyController.Active)
                    {
                        if (e.IsFrozen) frozen++;
                        if (e.GetComponent<IceEncase>() != null) encased++;
                    }
                    Log($"fx {id}: frozen={frozen} encased={encased} liveFx={(Fx.Root != null ? Fx.Root.childCount : -1)}");
                    yield return Wait(1.3f);
                    Log($"fx {id}: kills +{CountDead() - kills} liveFx after={(Fx.Root != null ? Fx.Root.childCount : -1)}");
                }
                // Worn gear shows on the character: put on a full set, then close-ups facing three ways.
                var bag = Game.Session.Inventory;
                var worn = Game.Session.Equipment;
                foreach (var gid in mage ? new[] { "eq_staff_star", "eq_top_cloth", "eq_bot_leather" } : new[] { "eq_sword_dragon", "eq_top_iron", "eq_bot_leather" })
                {
                    bag.Add(gid, 1);
                    Log($"fx equip {gid}: {worn.Equip(gid, cls)}");
                }
                var cam = Game.Camera.Camera;
                float camSize = cam.orthographicSize;
                cam.orthographicSize = camSize * 0.45f;
                foreach (var f in new[] { Facing.Down, Facing.DownRight, Facing.UpLeft })
                {
                    Game.Player.FaceTowards(Game.Player.Position + f.ToVector());
                    yield return Wait(0.25f);
                    yield return Shot($"fx_look_{(mage ? "mage" : "warrior")}_{f}");
                }
                cam.orthographicSize = camSize;
            }
        }

        static IEnumerator Wait(float seconds) => new WaitForSecondsRealtime(seconds);

        static int CountDead()
        {
            return killed;
        }

        static int killed;

        void OnEnable() => GameEvents.EnemyKilled += OnKilled;

        static void OnKilled(string id) => killed++;

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            // ScreenCapture lives in an optional module this project does not include; read the back buffer instead.
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            yield return null;
            Log("shot " + name);
        }

        void Log(string message) => log?.WriteLine($"[{Time.realtimeSinceStartup:0.00}] {message}");

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
                Log($"{type}: {condition}");
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            GameEvents.EnemyKilled -= OnKilled;
        }
    }
}
