using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
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
                bag.Add("eq_sword_10_u", 1);
                bag.Add("mat_bone", 12);
                bag.Add(ItemIds.Wood, 3);
                shop.DevMode(true);
                int g1 = Game.Session.Gold;
                shop.DevSelect("eq_sword_10_u");
                shop.DevTrade(1);
                shop.DevSelect("mat_bone");
                shop.DevTrade(int.MaxValue);
                Log($"sell iron sword + 12 bone: gold {g1}->{Game.Session.Gold} sword={bag.Count("eq_sword_10_u")} bone={bag.Count("mat_bone")} msg='{Strip(shop.DevResult)}'");
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
    }
}
