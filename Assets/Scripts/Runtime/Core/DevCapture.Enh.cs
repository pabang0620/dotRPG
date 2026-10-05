using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ENH] Enhancement checks of the full <c>-dotrpgCapture</c> run: "ENH &lt;what&gt; PASS|FAIL" report lines and
    /// "ENH summary: N passed, M failed". Rules and injected-roll scenarios on the live session, save round
    /// trips and the v3 migration, then the real UI paths (blacksmith with its overlay confirm, bag paging,
    /// shop sale of enhanced gear, storage paging). Autosave is off meanwhile; bag, storage, worn gear, pity
    /// and MP are put back afterwards.
    /// </summary>
    public partial class DevCapture
    {
        int enhPassed, enhFailed;

        /// <summary>Writes "ENH &lt;what&gt; PASS|FAIL" (no timestamp, so the lines start with ENH for easy filtering).</summary>
        void Check(string what, bool ok)
        {
            if (ok) enhPassed++;
            else enhFailed++;
            log?.WriteLine($"ENH {what} {(ok ? "PASS" : "FAIL")}");
        }

        /// <summary>Success % for +0→+1 … +19→+20 disclosed for Dungeon&amp;Fighter KR on 2021-12-02.</summary>
        static readonly int[] ExpectedChances = { 100, 100, 100, 100, 80, 70, 60, 50, 40, 30, 25, 15, 14, 13, 12, 11, 10, 10, 10, 10 };

        const string PickAgainText = "강화할 장비를 다시 골라 주세요.";
        const string NoStatText = "이번 단계는 능력치 변화가 없다";

        IEnumerator EnhanceChecks()
        {
            enhPassed = enhFailed = 0;
            var snapshot = Game.Session.Capture(Game.Player.Position, Game.Player.Facing);
            float mana = Game.Session.PlayerMana;
            // Nothing in here may write scratch state into the capture's save slot.
            bool autosave = Game.Config.autosave;
            Game.Config.autosave = false;
            try
            {
                EnhanceRuleChecks();
                EnhanceScenario();
                EnhanceLoadChecks();
                EnhanceSaveChecks();
            }
            catch (Exception e)
            {
                Check($"logic checks threw {e.GetType().Name}: {e.Message}", false);
            }
            RestoreGear(snapshot);
            yield return EnhanceWindowChecks();
            RestoreGear(snapshot);
            yield return BagPagingChecks();
            RestoreGear(snapshot);
            yield return ShopSellChecks();
            RestoreGear(snapshot);
            yield return StoragePagingChecks();
            RestoreGear(snapshot);
            Game.Session.PlayerMana = mana;
            Game.Config.autosave = autosave;
            Check($"state restored: bag [{Dump(Game.Session.Inventory.ToList())}] worn [{string.Join(",", Game.Session.Equipment.ToList())}] autosave={Game.Config.autosave}",
                Dump(Game.Session.Inventory.ToList()) == Dump(snapshot.inventory) && Dump(Game.Session.Storage.ToList()) == Dump(snapshot.storage)
                && string.Join(",", Game.Session.Equipment.ToList()) == string.Join(",", snapshot.equipped) && Game.Config.autosave == autosave);
            log?.WriteLine($"ENH summary: {enhPassed} passed, {enhFailed} failed");
        }

        /// <summary>Bag, storage, worn gear and enhancement pity back to a captured state (nothing else).</summary>
        static void RestoreGear(SaveData snapshot)
        {
            var s = Game.Session;
            s.Inventory.Load(snapshot.inventory);
            s.Storage.Load(snapshot.storage);
            s.Equipment.LoadPity(snapshot.enhancePity);
            s.Equipment.Load(snapshot.equipped);
        }

        static string Dump(List<ItemStack> stacks)
        {
            var parts = new List<string>();
            foreach (var s in stacks) parts.Add($"{s.id}:{s.count}");
            parts.Sort(string.CompareOrdinal);
            return string.Join(",", parts);
        }

        static int CountIn(List<ItemStack> stacks, string id)
        {
            int n = 0;
            foreach (var s in stacks) if (s.id == id) n += s.count;
            return n;
        }

        static string TopName => Game.UI.Top != null ? Game.UI.Top.name : "-";

        void EnhanceRuleChecks()
        {
            bool table = EnhanceRules.SuccessPercent(EquipmentDatabase.MaxEnhance) == 0;
            for (int l = 0; l < ExpectedChances.Length; l++) table &= EnhanceRules.SuccessPercent(l) == ExpectedChances[l];
            Check($"chance table ({string.Join("/", ExpectedChances)})", table);
        }

        /// <summary>Scripted attempts with injected rolls on the live session (bag and slots are emptied first; see <see cref="RestoreGear"/>).</summary>
        void EnhanceScenario()
        {
            var eq = Game.Session.Equipment;
            var bag = Game.Session.Inventory;
            // The real window passes the player's class: use it, with a weapon of that class for the worn-weapon cases.
            var cls = Game.Player.Class;
            string weapon = cls == CharacterClass.Mage ? "eq_staff_10_u" : "eq_sword_10_u";
            string weapon12 = EquipmentDatabase.KeyFor(weapon, 12);
            string starter = EquipmentDatabase.StarterWeapon(cls), starter12 = EquipmentDatabase.KeyFor(starter, 12);
            const CharacterClass warrior = CharacterClass.Warrior;
            const string iron = "eq_sword_10_u", iron10 = "eq_sword_10_u+10", iron11 = "eq_sword_10_u+11", iron12 = "eq_sword_10_u+12";
            const string top10 = "eq_plate_1_u+10", ring9 = "eq_ring_1_c+9", iron20 = "eq_sword_10_u+20";
            const string gold = ConsumableDatabase.Gold, ticket = ConsumableDatabase.ProtectTicket;

            Check($"keys: KeyFor(iron,10)={EquipmentDatabase.KeyFor(iron, 10)} KeyFor(iron,0)={EquipmentDatabase.KeyFor(iron, 0)} KeyFor(iron,25)={EquipmentDatabase.KeyFor(iron, 25)} " +
                  $"BaseId={EquipmentDatabase.BaseId(iron12)} LevelOfKey={EquipmentDatabase.LevelOfKey(iron12)} name='{Game.Config.GetItem(iron12).displayName}' sprite={EquipmentDatabase.WeaponSprite(iron12, warrior)}",
                EquipmentDatabase.KeyFor(iron, 10) == iron10 && EquipmentDatabase.KeyFor(iron, 0) == iron && EquipmentDatabase.KeyFor(iron, 25) == iron20
                && EquipmentDatabase.BaseId(iron12) == iron && EquipmentDatabase.LevelOfKey(iron12) == 12 && EquipmentDatabase.Get(iron12) == EquipmentDatabase.Get(iron)
                && EquipmentDatabase.IsEquipment(iron12) && EquipmentDatabase.TierOf(iron12) == 1
                && Game.Config.GetItem(iron12).displayName == "뼈손잡이 장검 +12"
                && EquipmentDatabase.Get("eq_sword_10_u+0") == null && EquipmentDatabase.Get("eq_sword_10_u+21") == null);

            // Scratch setup: empty bag and slots, plenty of gold and materials, no protection ticket.
            bag.Clear();
            eq.Clear();
            eq.LoadPity(null);
            bag.Add(gold, 100000);
            bag.Add(EnhanceRules.Bone, 500);
            bag.Add(EnhanceRules.Ore, 200);
            bag.Add(EnhanceRules.Essence, 100);

            // +10 at 25%: roll 25 is the first failing roll without pity → +7, pity(+10) = 1, price paid once.
            bag.Add(iron10, 1);
            var cost = eq.CostFor(iron10);
            Check($"cost from +10: gold {cost.gold} bone {cost.bone} ore {cost.ore} essence {cost.essence} chance {cost.successPercent}% on fail {cost.failure}",
                cost.gold == 90 && cost.bone == 12 && cost.ore == 4 && cost.essence == 1 && cost.successPercent == 25 && cost.failure == EnhanceFailure.Drop3);
            int g0 = bag.Count(gold), b0 = bag.Count(EnhanceRules.Bone), o0 = bag.Count(EnhanceRules.Ore), e0 = bag.Count(EnhanceRules.Essence);
            var r = eq.TryEnhance(EnhanceTarget.Bag(iron10), 25, warrior);
            Check($"+10 roll 25 without pity fails: {r.kind} {r.oldKey} -> {r.newKey}, bag +7={bag.Count("eq_sword_10_u+7")} +10={bag.Count(iron10)} pity(+10)={eq.PityOf(iron10)}",
                r.kind == EnhanceOutcome.Drop3 && r.newKey == "eq_sword_10_u+7" && r.newLevel == 7 && bag.Count("eq_sword_10_u+7") == 1 && bag.Count(iron10) == 0 && eq.PityOf(iron10) == 1);
            Check($"+10 fail paid: gold -{g0 - bag.Count(gold)} bone -{b0 - bag.Count(EnhanceRules.Bone)} ore -{o0 - bag.Count(EnhanceRules.Ore)} essence -{e0 - bag.Count(EnhanceRules.Essence)}",
                g0 - bag.Count(gold) == cost.gold && b0 - bag.Count(EnhanceRules.Bone) == cost.bone
                && o0 - bag.Count(EnhanceRules.Ore) == cost.ore && e0 - bag.Count(EnhanceRules.Essence) == cost.essence);

            // +11 at 15%: roll 15 fails without pity → +8, pity(+11) = 1.
            bag.Add(iron11, 1);
            r = eq.TryEnhance(EnhanceTarget.Bag(iron11), 15, warrior);
            Check($"+11 roll 15 without pity fails: {r.kind} -> {r.newKey}, pity(+11)={eq.PityOf(iron11)}",
                r.kind == EnhanceOutcome.Drop3 && r.newKey == "eq_sword_10_u+8" && bag.Count("eq_sword_10_u+8") == 1 && bag.Count(iron11) == 0 && eq.PityOf(iron11) == 1);

            // The same rolls with 1%p pity succeed: +10 roll 25 → +11 (pity(+10) cleared, +11 keeps its own) ...
            bag.Add(iron10, 1);
            cost = eq.CostFor(iron10);
            r = eq.TryEnhance(EnhanceTarget.Bag(iron10), 25, warrior);
            Check($"+10 roll 25 with pity {cost.basePercent}%+{cost.pityBonus}%p={cost.successPercent}% succeeds: {r.kind} -> {r.newKey}, pity(+10)={eq.PityOf(iron10)} pity(+11)={eq.PityOf(iron11)}",
                cost.basePercent == 25 && cost.pityBonus == 1 && cost.successPercent == 26 && r.kind == EnhanceOutcome.Success && r.newKey == iron11
                && bag.Count(iron11) == 1 && bag.Count(iron10) == 0 && eq.PityOf(iron10) == 0 && eq.PityOf(iron11) == 1);
            // ... and +11 roll 15 → +12.
            cost = eq.CostFor(iron11);
            r = eq.TryEnhance(EnhanceTarget.Bag(iron11), 15, warrior);
            Check($"+11 roll 15 with pity {cost.basePercent}%+{cost.pityBonus}%p={cost.successPercent}% succeeds: {r.kind} -> {r.newKey}, pity(+11)={eq.PityOf(iron11)}",
                cost.successPercent == 16 && r.kind == EnhanceOutcome.Success && r.newKey == iron12 && bag.Count(iron12) == 1 && bag.Count(iron11) == 0 && eq.PityOf(iron11) == 0);
            // One more +11 failure so the save round trip below carries pity.
            bag.Add(iron11, 1);
            eq.TryEnhance(EnhanceTarget.Bag(iron11), 99, warrior);

            // Worn +12 weapon of the player's class without a ticket: destroyed, that class's starter weapon goes in.
            eq.Set(EquipSlot.Weapon, weapon12);
            cost = eq.CostFor(weapon12);
            r = eq.TryEnhance(EnhanceTarget.Worn(EquipSlot.Weapon), 99, cls);
            Check($"worn {weapon12} fail ({cls}), no ticket: risk {cost.failure} ticket={cost.usesTicket} -> {r.kind}, weapon now {eq[EquipSlot.Weapon]}",
                cost.failure == EnhanceFailure.Destroy && !cost.usesTicket && r.kind == EnhanceOutcome.Destroyed && r.newKey == null
                && eq[EquipSlot.Weapon] == starter && bag.Count(weapon12) == 0);

            // The same with one protection ticket: kept at +0, the ticket is used up.
            eq.Set(EquipSlot.Weapon, weapon12);
            bag.Add(ticket, 1);
            cost = eq.CostFor(weapon12);
            r = eq.TryEnhance(EnhanceTarget.Worn(EquipSlot.Weapon), 99, cls);
            Check($"worn {weapon12} fail, 1 ticket: predicted ticket={cost.usesTicket} -> {r.kind}, weapon {eq[EquipSlot.Weapon]}, tickets left {bag.Count(ticket)}",
                cost.usesTicket && r.kind == EnhanceOutcome.Protected && r.newLevel == 0 && eq[EquipSlot.Weapon] == weapon && bag.Count(ticket) == 0);

            // Starter gear never uses a ticket (a free +0 starter replaces it anyway).
            eq.Set(EquipSlot.Weapon, starter12);
            bag.Add(ticket, 1);
            cost = eq.CostFor(starter12);
            r = eq.TryEnhance(EnhanceTarget.Worn(EquipSlot.Weapon), 99, cls);
            Check($"worn {starter12} fail with a ticket: predicted ticket={cost.usesTicket} -> {r.kind}, weapon {eq[EquipSlot.Weapon]}, tickets left {bag.Count(ticket)}",
                !cost.usesTicket && r.kind == EnhanceOutcome.Destroyed && eq[EquipSlot.Weapon] == starter && bag.Count(ticket) == 1);
            bag.Remove(ticket, bag.Count(ticket));

            // Armour from +10 is destroyed (no −3, no pity); a bag piece's destroy never hands out a weapon.
            eq.Set(EquipSlot.Weapon, null);
            bag.Add(top10, 1);
            cost = eq.CostFor(top10);
            r = eq.TryEnhance(EnhanceTarget.Bag(top10), 99, cls);
            Check($"bag leather top +10 fail, weapon slot empty: risk {cost.failure} gold {cost.gold} -> {r.kind}, left {bag.Count(top10)}, weapon slot '{eq[EquipSlot.Weapon]}'",
                cost.failure == EnhanceFailure.Destroy && cost.gold == 48 && r.kind == EnhanceOutcome.Destroyed && bag.Count(top10) == 0 && eq.PityOf(top10) == 0
                && string.IsNullOrEmpty(eq[EquipSlot.Weapon]));
            bag.Add(ring9, 1);
            r = eq.TryEnhance(EnhanceTarget.Bag(ring9), 99, warrior);
            Check($"copper ring +9 fail: {r.kind}, still +9={bag.Count(ring9)}", r.kind == EnhanceOutcome.Keep && bag.Count(ring9) == 1 && eq.PityOf(ring9) == 0);

            // +20 cannot go higher; without gold nothing happens.
            bag.Add(iron20, 1);
            int goldBefore = bag.Count(gold);
            r = eq.TryEnhance(EnhanceTarget.Bag(iron20), 0, warrior);
            Check($"+20: {r.kind}, gold unchanged={bag.Count(gold) == goldBefore}", r.kind == EnhanceOutcome.MaxLevel && bag.Count(gold) == goldBefore && bag.Count(iron20) == 1);
            bag.Remove(gold, bag.Count(gold));
            r = eq.TryEnhance(EnhanceTarget.Bag("eq_sword_10_u+7"), 0, warrior);
            Check($"no gold: {r.kind}, +7 kept={bag.Count("eq_sword_10_u+7")}", r.kind == EnhanceOutcome.NotEnough && bag.Count("eq_sword_10_u+7") == 1);
            bag.Add(gold, 100000);

            // Bag order: database order, then the higher +level first.
            string order = string.Join(",", EquipmentDatabase.GearKeys(bag));
            Check($"bag order: {order}", order == "eq_sword_10_u+20,eq_sword_10_u+12,eq_sword_10_u+8,eq_sword_10_u+7,eq_ring_1_c+9");

            // Growth: ≈ +50% of base + weapon attack at +12, HP for everything else, never block / speed / accessory attack.
            int AttackBonus(string key) => EquipmentDatabase.StatsOfKey(key).attack - EquipmentDatabase.StatsOfKey(EquipmentDatabase.BaseId(key)).attack;
            int HpBonus(string key) => EquipmentDatabase.StatsOfKey(key).maxHealth - EquipmentDatabase.StatsOfKey(EquipmentDatabase.BaseId(key)).maxHealth;
            Check($"iron sword +12 attack +{AttackBonus(iron12)} (expect 8)", AttackBonus(iron12) == 8);
            Check($"dragon sword +12 attack +{AttackBonus("eq_sword_20_l+12")} (expect 13)", AttackBonus("eq_sword_20_l+12") == 13);
            Check($"plate top +10 hp +{HpBonus(top10)} (expect 9)", HpBonus(top10) == 9);
            var topBase = EquipmentDatabase.StatsOfKey("eq_plate_1_u");
            var top = EquipmentDatabase.StatsOfKey("eq_plate_1_u+20");
            var ruby = EquipmentDatabase.StatsOfKey("eq_ring_10_un+20");
            Check($"no block/speed/accessory-attack growth: top block {topBase.block}->{top.block} speed {topBase.speed}->{top.speed}, ruby +20 attack {ruby.attack}",
                top.block == topBase.block && top.speed == topBase.speed && top.attack == topBase.attack && ruby.attack == EquipmentDatabase.Get("eq_ring_10_un").attack);
            string flat = null;
            foreach (var item in EquipmentDatabase.All)
                for (int l = 1; l <= EquipmentDatabase.MaxEnhance && flat == null; l++)
                    if (item.StatsAt(l).Score <= item.StatsAt(l - 1).Score) flat = $"{item.id} +{l}";
            Check($"전투력 rises at every +level for all gear{(flat != null ? " (flat at " + flat + ")" : "")}", flat == null);
            eq.Set(EquipSlot.Weapon, iron12);
            Check($"worn iron +12: AttackBonus {eq.AttackBonus} (expect 16)", eq.AttackBonus == 16);

            // Prices: gear sells for more with its level; the ticket is shop-only.
            Check($"sell iron +0 {ItemPrices.SellPrice(iron)} G / +12 {ItemPrices.SellPrice(iron12)} G (expect 31 / 124)", ItemPrices.SellPrice(iron) == 31 && ItemPrices.SellPrice(iron12) == 124);
            Check($"ticket: buy {ItemPrices.BuyPrice(ticket)} G, sell {ItemPrices.SellPrice(ticket)} G, last on the shelf, not usable by hand",
                ItemPrices.BuyPrice(ticket) == 3000 && ItemPrices.SellPrice(ticket) == 0 && Array.IndexOf(ItemPrices.ShopStock, ticket) == ItemPrices.ShopStock.Length - 1
                && !ConsumableDatabase.IsUsable(ticket) && ConsumableDatabase.IsTicket(ticket));
        }

        /// <summary>Equipment.Load never deletes a worn key: odd spellings are repaired, the rest goes to the bag.</summary>
        void EnhanceLoadChecks()
        {
            var eq = Game.Session.Equipment;
            var bag = Game.Session.Inventory;
            var saved = new List<string>(new string[Equipment.SlotCount]);
            saved[(int)EquipSlot.Weapon] = "eq_staff_10_u+0";
            saved[(int)EquipSlot.Top] = "eq_robe_1_u+25";
            saved[(int)EquipSlot.Bottom] = "eq_skirt_1_c+05";
            saved[(int)EquipSlot.Necklace] = "eq_ring_1_c+3"; // valid key, wrong slot
            saved[(int)EquipSlot.Ring1] = "eq_unknown_thing+2";
            int ring3 = bag.Count("eq_ring_1_c+3"), unknown = bag.Count("eq_unknown_thing+2");
            eq.Load(saved);
            Check($"load odd worn keys: weapon {eq[EquipSlot.Weapon]} top {eq[EquipSlot.Top]} bottom {eq[EquipSlot.Bottom]} necklace '{eq[EquipSlot.Necklace]}' ring1 '{eq[EquipSlot.Ring1]}', " +
                  $"to bag: ring+3 {bag.Count("eq_ring_1_c+3") - ring3} unknown {bag.Count("eq_unknown_thing+2") - unknown}",
                eq[EquipSlot.Weapon] == "eq_staff_10_u" && eq[EquipSlot.Top] == "eq_robe_1_u+20" && eq[EquipSlot.Bottom] == "eq_skirt_1_c+5"
                && eq[EquipSlot.Necklace] == null && eq[EquipSlot.Ring1] == null
                && bag.Count("eq_ring_1_c+3") == ring3 + 1 && bag.Count("eq_unknown_thing+2") == unknown + 1);
            bag.Remove("eq_unknown_thing+2", bag.Count("eq_unknown_thing+2"));
        }

        /// <summary>Save round trips (JSON and a real Write/Read of a spare slot) into a fresh session, then hand-built version-3 saves through <see cref="SaveSystem.Migrate"/>.</summary>
        void EnhanceSaveChecks()
        {
            var session = Game.Session;
            var eq = session.Equipment;
            var bag = session.Inventory;
            // The mage keeps an enhanced staff worn through Restore (EnsureUsable), so worn keys are checked too.
            eq.Set(EquipSlot.Weapon, "eq_staff_10_u+9");
            string bagBefore = Dump(bag.ToList()), pityBefore = Dump(eq.PityToList()), wornBefore = string.Join(",", eq.ToList());
            var data = session.Capture(Game.Player.Position, Game.Player.Facing);
            string json = JsonUtility.ToJson(data);
            // A fresh session, so the live one (MP, quest object) is left alone.
            var copy = new GameSession();
            copy.Restore(JsonUtility.FromJson<SaveData>(json), Game.Config);
            string bagAfter = Dump(copy.Inventory.ToList()), pityAfter = Dump(copy.Equipment.PityToList()), wornAfter = string.Join(",", copy.Equipment.ToList());
            Check($"save round trip v{data.version}: worn [{wornAfter}] pity [{pityAfter}] bag [{bagAfter}]",
                data.version == SaveData.CurrentVersion && bagAfter == bagBefore && wornAfter == wornBefore && pityAfter == pityBefore
                && pityAfter == "eq_sword_10_u+11:1" && json.Contains("\"eq_sword_10_u+8\"") && json.Contains("\"enhancePity\"") && data.enhanceLevels.Count == 0
                && !json.Contains("enhanceCompensation"));

            const int slot = 6;
            bool wrote = Game.Saves.Write(data, slot);
            var back = Game.Saves.Read(slot);
            Game.Saves.Delete(slot);
            var copy2 = new GameSession();
            if (back != null) copy2.Restore(back, Game.Config);
            Check($"SaveSystem Write/Read slot {slot}: wrote={wrote} read={(back != null)} compensation={back?.enhanceCompensation}",
                wrote && back != null && back.enhanceCompensation == 0 && Dump(copy2.Inventory.ToList()) == bagBefore
                && string.Join(",", copy2.Equipment.ToList()) == wornBefore && Dump(copy2.Equipment.PityToList()) == pityBefore);

            var v3 = new SaveData { version = 3, playerClass = "warrior" };
            v3.equipped = new List<string> { "eq_sword_10_u", "", "", "", "", "" };
            v3.enhanceLevels.Add(new ItemStack("eq_sword_10_u", 5));
            v3.enhanceLevels.Add(new ItemStack("eq_ring_1_c", 3));
            v3.inventory.Add(new ItemStack("eq_ring_1_c", 2));
            v3.enhanceLevels.Add(new ItemStack("eq_neck_1_c", 4));
            v3.storage.Add(new ItemStack("eq_neck_1_c", 1));
            v3.enhanceLevels.Add(new ItemStack("eq_plate_1_c", 2));
            v3.inventory.Add(new ItemStack("eq_plate_1_c", 1));
            v3.enhanceLevels.Add(new ItemStack("eq_greaves_1_c", 6)); // held nowhere: nothing converted, no ticket
            var m = SaveSystem.Migrate(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(v3)));
            Check($"v3 -> v{m.version}: worn weapon {m.equipped[0]}, enhanceLevels left {m.enhanceLevels.Count}",
                m.version == SaveData.CurrentVersion && m.equipped[0] == "eq_sword_10_u+5" && m.enhanceLevels.Count == 0);
            Check($"v3 -> v{m.version} kind only in the bag: copper {CountIn(m.inventory, "eq_ring_1_c")} + copper+3 {CountIn(m.inventory, "eq_ring_1_c+3")}",
                CountIn(m.inventory, "eq_ring_1_c") == 1 && CountIn(m.inventory, "eq_ring_1_c+3") == 1);
            Check($"v3 -> v{m.version} kind only in storage: leaf {CountIn(m.storage, "eq_neck_1_c")} + leaf+4 {CountIn(m.storage, "eq_neck_1_c+4")}",
                CountIn(m.storage, "eq_neck_1_c") == 0 && CountIn(m.storage, "eq_neck_1_c+4") == 1);
            Check($"v3 -> v{m.version} compensation: 4 kinds converted -> {m.enhanceCompensation} tickets, bag tickets {CountIn(m.inventory, ConsumableDatabase.ProtectTicket)} (max {SaveSystem.MaxEnhanceCompensation})",
                m.enhanceCompensation == SaveSystem.MaxEnhanceCompensation && CountIn(m.inventory, ConsumableDatabase.ProtectTicket) == SaveSystem.MaxEnhanceCompensation);
            var one = new SaveData { version = 3, playerClass = "mage" };
            one.equipped = new List<string> { "eq_staff_10_u", "", "", "", "", "" };
            one.enhanceLevels.Add(new ItemStack("eq_staff_10_u", 7));
            var m1 = SaveSystem.Migrate(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(one)));
            var m1again = SaveSystem.Migrate(JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(m1))); // as written, then read again
            Check($"v3 -> v{m1.version} one kind: {m1.enhanceCompensation} ticket(s), re-run grants {m1again.enhanceCompensation}",
                m1.enhanceCompensation == 1 && CountIn(m1.inventory, ConsumableDatabase.ProtectTicket) == 1 && m1again.enhanceCompensation == 0
                && CountIn(m1again.inventory, ConsumableDatabase.ProtectTicket) == 1);
        }

        IEnumerator OpenBlacksmith()
        {
            var anvil = FindAnyObjectByType<Anvil>();
            if (anvil != null) anvil.Interact(Game.Player);
            else Game.Flow.OpenWindow(Game.UI.Enhance);
            yield return Wait(0.4f);
        }

        /// <summary>
        /// The blacksmith through its real UI paths: the overlay risk confirm ("아니오", then "예" with a forced roll),
        /// a destroy and the pick lock after it, a safe attempt, closing mid-swing, the no-stat hint and paging.
        /// </summary>
        IEnumerator EnhanceWindowChecks()
        {
            var bag = Game.Session.Inventory;
            const string staff12 = "eq_staff_10_u+12", staff13 = "eq_staff_10_u+13", staff3 = "eq_staff_10_u+3", staff4 = "eq_staff_10_u+4";
            bag.Remove(ConsumableDatabase.ProtectTicket, bag.Count(ConsumableDatabase.ProtectTicket)); // show the destroy risk
            bag.Add(staff12, 1);
            bag.Add(staff3, 1);
            bag.Add(ConsumableDatabase.Gold, 50000);
            bag.Add(EnhanceRules.Bone, 200);
            bag.Add(EnhanceRules.Ore, 100);
            bag.Add(EnhanceRules.Essence, 50);
            yield return OpenBlacksmith();
            var smithy = Game.UI.Enhance;
            var dialog = Game.UI.ConfirmDialog;
            bool open = Game.UI.Top == smithy;
            Check($"blacksmith opens on entry 0: index {smithy.DevSelectedIndex}", open && smithy.DevSelectedIndex == 0);
            bool picked = smithy.DevSelect(staff12);
            yield return Wait(0.2f);
            string failLine = Strip(smithy.DevFailLine);
            Check($"blacksmith window: open={open} +12 staff selected={picked} '{failLine}'", open && picked && failLine == "실패 시: 장비 파괴!");
            Check($"+12 -> +13 shows stat growth: '{Strip(smithy.DevStats).Replace('\n', '|')}'", !smithy.DevStats.Contains(NoStatText));
            yield return Shot("03l_enhance");

            // Risky attempt: the confirm is drawn over the window, names the piece, the level and the chance.
            int gold0 = Game.Session.Gold;
            smithy.DevPress();
            yield return Wait(0.3f);
            bool asked = Game.UI.Top == dialog;
            string question = dialog.DevMessage;
            Check($"+12 attempt asks over the window: top={TopName} blacksmith visible={smithy.gameObject.activeSelf} '{Strip(question).Replace('\n', '|')}'",
                asked && smithy.gameObject.activeSelf && question == "<b>뼈 장식 지팡이 +12</b> → +13  (성공 14%)\n실패하면 장비가 파괴됩니다. 강화할까요?");
            yield return Shot("03l2_enhance_confirm");
            smithy.DevPress(); // the window under the dialog takes no input
            yield return null;
            Check($"window ignores input under the confirm: busy={smithy.DevBusy} top={TopName} gold -{gold0 - Game.Session.Gold}",
                !smithy.DevBusy && Game.UI.Top == dialog && Game.Session.Gold == gold0);
            dialog.DevAnswer(false);
            yield return Wait(0.3f);
            Check($"아니오: top={TopName} +12 staff={bag.Count(staff12)} busy={smithy.DevBusy} still selected {smithy.DevSelectedKey}",
                Game.UI.Top == smithy && smithy.gameObject.activeSelf && bag.Count(staff12) == 1 && !smithy.DevBusy && smithy.DevSelectedKey == staff12 && Game.Session.Gold == gold0);

            // "예" through the dialog's own menu path, forced success roll: exactly one payment, +13.
            var cost = Game.Session.Equipment.CostFor(staff12);
            int bone0 = bag.Count(EnhanceRules.Bone), ore0 = bag.Count(EnhanceRules.Ore), ess0 = bag.Count(EnhanceRules.Essence);
            smithy.DevForceRoll(0);
            smithy.DevPress();
            yield return null;
            dialog.DevAnswer(true);
            yield return null;
            bool busyAfterYes = smithy.DevBusy;
            yield return Wait(1.4f);
            Check($"예 (roll 0): busy after yes={busyAfterYes} result '{Strip(smithy.DevResult)}' +13={bag.Count(staff13)} +12={bag.Count(staff12)} " +
                  $"paid gold {gold0 - Game.Session.Gold}/{cost.gold} bone {bone0 - bag.Count(EnhanceRules.Bone)}/{cost.bone} ore {ore0 - bag.Count(EnhanceRules.Ore)}/{cost.ore} essence {ess0 - bag.Count(EnhanceRules.Essence)}/{cost.essence}",
                busyAfterYes && !smithy.DevBusy && Game.UI.Top == smithy && bag.Count(staff13) == 1 && bag.Count(staff12) == 0 && gold0 - Game.Session.Gold == cost.gold
                && bone0 - bag.Count(EnhanceRules.Bone) == cost.bone && ore0 - bag.Count(EnhanceRules.Ore) == cost.ore && ess0 - bag.Count(EnhanceRules.Essence) == cost.essence);

            // "예" with a forced failure: destroyed; the cursor moved on, so 강화 now asks for a new pick first.
            int gold1 = Game.Session.Gold;
            bool on13 = smithy.DevSelect(staff13);
            smithy.DevForceRoll(99);
            smithy.DevPress();
            yield return null;
            dialog.DevAnswer(true);
            yield return Wait(1.4f);
            bool locked = smithy.DevPickRequired;
            string afterKey = smithy.DevSelectedKey;
            int gold2 = Game.Session.Gold;
            smithy.DevPress();
            yield return null;
            Check($"destroyed (roll 99): +13={bag.Count(staff13)} pickRequired={locked} cursor now {afterKey}, 강화 again -> '{Strip(smithy.DevResult)}' busy={smithy.DevBusy} top={TopName} gold -{gold2 - Game.Session.Gold}",
                on13 && bag.Count(staff13) == 0 && gold1 > gold2 && locked && Strip(smithy.DevResult) == PickAgainText && !smithy.DevBusy
                && Game.UI.Top == smithy && Game.Session.Gold == gold2);

            // Safe attempt: button locked during the unscaled-time swing, then the result stays; picking clears it.
            smithy.DevSelect(staff3);
            Check($"pick after destroy clears the lock and the result: pickRequired={smithy.DevPickRequired} result '{Strip(smithy.DevResult)}'",
                !smithy.DevPickRequired && smithy.DevResult == "");
            int goldBefore = Game.Session.Gold;
            smithy.DevPress();
            yield return null;
            bool busyDuring = smithy.DevBusy;
            yield return Wait(1.4f);
            string result = Strip(smithy.DevResult);
            int paid = goldBefore - Game.Session.Gold;
            Check($"+3 attempt in the window: busy during swing={busyDuring} result '{result}' +4 staff={bag.Count(staff4)} gold -{paid}",
                busyDuring && !smithy.DevBusy && result.StartsWith("강화 성공!") && bag.Count(staff4) == 1 && bag.Count(staff3) == 0
                && paid == EnhanceRules.GoldFor(EquipmentDatabase.Get(staff3), 3));

            // Mid-swing: Close() is refused; leaving the window state calls the attempt off before anything is paid.
            smithy.DevSelect(staff4);
            int gold3 = Game.Session.Gold;
            smithy.DevForceRoll(0);
            smithy.DevPress();
            yield return null;
            bool busyNow = smithy.DevBusy;
            smithy.Close();
            yield return null;
            bool refused = Game.UI.Top == smithy && smithy.DevBusy;
            Game.Flow.CloseInventory();
            yield return Wait(1.4f);
            Check($"mid-swing: busy={busyNow} Close() refused={refused}, hidden -> busy={smithy.DevBusy} gold -{gold3 - Game.Session.Gold} +4={bag.Count(staff4)} +5={bag.Count("eq_staff_10_u+5")}",
                busyNow && refused && !smithy.DevBusy && !smithy.gameObject.activeSelf && Game.Session.Gold == gold3 && bag.Count(staff4) == 1 && bag.Count("eq_staff_10_u+5") == 0);

            // Reopen: first entry again; a +0 → +1 step whose rounded bonus is still 0 says so.
            yield return OpenBlacksmith();
            Check($"reopened on entry 0: index {smithy.DevSelectedIndex} top={TopName}", Game.UI.Top == smithy && smithy.DevSelectedIndex == 0);
            bag.Add("eq_staff_oak", 1);
            bool oak = smithy.DevSelect("eq_staff_oak");
            yield return null;
            Check($"oak staff +0 -> +1 no-stat hint: '{Strip(smithy.DevStats).Replace('\n', '|')}'", oak && smithy.DevStats.Contains(NoStatText));

            // More than 20 entries: a second page instead of running past the grid.
            for (int l = 1; l <= EquipmentDatabase.MaxEnhance; l++) bag.Add(EquipmentDatabase.KeyFor("eq_ring_1_c", l), 1);
            bool last = smithy.DevSelect("eq_ring_1_c+1");
            yield return Wait(0.2f);
            Check($"paging: {smithy.DevEntries} entries, last one selected={last} on page {smithy.DevPage}, result line '{Strip(smithy.DevResult)}'",
                last && smithy.DevEntries > 20 && smithy.DevPage.StartsWith("2 /") && smithy.DevResult == "");
            yield return Shot("03l3_enhance_page2");
            Game.Flow.CloseInventory();
            yield return Wait(0.3f);
        }

        /// <summary>30 extra gear keys: the bag grid pages per tab, and the 25th+ item shows on page 2.</summary>
        IEnumerator BagPagingChecks()
        {
            var bag = Game.Session.Inventory;
            for (int l = 1; l <= EquipmentDatabase.MaxEnhance; l++) bag.Add(EquipmentDatabase.KeyFor("eq_ring_1_c", l), 1);
            for (int l = 1; l <= 10; l++) bag.Add(EquipmentDatabase.KeyFor("eq_sword_10_u", l), 1);
            Game.Flow.OpenWindow(Game.UI.Equipment);
            yield return Wait(0.3f);
            var screen = Game.UI.Equipment;
            screen.DevSelectTab(0);
            var page1 = screen.DevShown();
            string label1 = screen.DevPageLabel;
            screen.DevPage(1);
            var page2 = screen.DevShown();
            string label2 = screen.DevPageLabel;
            int total = ItemText.BagOrder(bag).Count;
            bool disjoint = true;
            foreach (var id in page2) disjoint &= !page1.Contains(id);
            string last = ItemText.BagOrder(bag)[total - 1];
            Check($"bag paging: {total} ids, page 1 {page1.Count} ({label1}), page 2 {page2.Count} ({label2}), last '{last}' on page 2={page2.Contains(last)}, capacity '{screen.DevCapacity}'",
                total > 24 && page1.Count == 24 && page2.Count == total - 24 && disjoint && page2.Contains(last) && label2.StartsWith("2 /") && screen.DevCapacity == $"{total}종");
            yield return Wait(0.2f);
            yield return Shot("03k2_bag_page2");
            screen.DevSelectTab(3); // 장신구: 20 rings + worn-free accessories, on its own page 1
            string accLabel = screen.DevPageLabel;
            var accShown = screen.DevShown();
            screen.DevSelectTab(0);
            string backLabel = screen.DevPageLabel;
            Check($"pages per tab: 장신구 {accLabel} ({accShown.Count} shown), back on 전체 {backLabel}", accLabel.StartsWith("1 /") && backLabel.StartsWith("2 /"));
            Game.Flow.CloseInventory();
            yield return Wait(0.3f);
        }

        /// <summary>The shop's sell tab: enhanced keys last, and selling one asks first over the shop.</summary>
        IEnumerator ShopSellChecks()
        {
            var bag = Game.Session.Inventory;
            const string iron12 = "eq_sword_10_u+12";
            bag.Add(iron12, 1);
            bag.Add("eq_sword_10_u", 1);
            bag.Add("eq_ring_1_c+3", 1);
            bag.Add(EnhanceRules.Bone, 5);
            var shop = Game.UI.Shop;
            shop.SetKeeper("상인", "어서 오세요.");
            Game.Flow.OpenWindow(shop);
            yield return Wait(0.3f);
            shop.DevMode(true);
            var list = shop.DevEntries();
            int firstEnhanced = list.FindIndex(id => EquipmentDatabase.LevelOfKey(id) > 0);
            bool plainAfter = firstEnhanced >= 0 && list.FindLastIndex(id => EquipmentDatabase.LevelOfKey(id) == 0) < firstEnhanced;
            Check($"sell list: enhanced keys after every +0 item: first enhanced #{firstEnhanced} of {list.Count} [{string.Join(",", list)}]", plainAfter && list.Contains(iron12));

            var dialog = Game.UI.ConfirmDialog;
            int gold0 = Game.Session.Gold;
            shop.DevSelect(iron12);
            shop.DevTrade(1);
            yield return Wait(0.3f);
            Check($"selling {iron12} asks over the shop: top={TopName} shop visible={shop.gameObject.activeSelf} '{Strip(dialog.DevMessage)}'",
                Game.UI.Top == dialog && shop.gameObject.activeSelf && dialog.DevMessage == "뼈손잡이 장검 +12 을(를) 124 G에 판매할까요?" && bag.Count(iron12) == 1);
            yield return Shot("03m_shop_sell_confirm");
            dialog.DevAnswer(false);
            yield return Wait(0.2f);
            Check($"아니오 keeps it: top={TopName} +12={bag.Count(iron12)} gold {gold0}->{Game.Session.Gold} still selling={shop.DevEntries().Contains(iron12)}",
                Game.UI.Top == shop && bag.Count(iron12) == 1 && Game.Session.Gold == gold0 && shop.DevEntries().Contains(iron12));

            // 모두 판매 on an enhanced stack always asks; 예 sells the stack once.
            bag.Add(iron12, 1);
            shop.DevSelect(iron12);
            shop.DevTrade(int.MaxValue);
            yield return null;
            string all = dialog.DevMessage;
            dialog.DevAnswer(true);
            yield return null;
            Check($"모두 판매 +12 x2: asked '{Strip(all)}' -> +12={bag.Count(iron12)} gold +{Game.Session.Gold - gold0}",
                all == "뼈손잡이 장검 +12 2개를 248 G에 판매할까요?" && bag.Count(iron12) == 0 && Game.Session.Gold - gold0 == 248 && Game.UI.Top == shop);

            // +0 gear still sells on one press.
            int gold1 = Game.Session.Gold, swords = bag.Count("eq_sword_10_u");
            shop.DevSelect("eq_sword_10_u");
            shop.DevTrade(1);
            yield return null;
            Check($"+0 sword sells without a question: top={TopName} sword {swords}->{bag.Count("eq_sword_10_u")} gold +{Game.Session.Gold - gold1}",
                Game.UI.Top == shop && bag.Count("eq_sword_10_u") == swords - 1 && Game.Session.Gold - gold1 == ItemPrices.SellPrice("eq_sword_10_u"));
            Game.Flow.CloseInventory();
            yield return Wait(0.3f);
        }

        /// <summary>Storage takes 25+ distinct ids (capacity 48) and pages both grids.</summary>
        IEnumerator StoragePagingChecks()
        {
            var bag = Game.Session.Inventory;
            var storage = Game.Session.Storage;
            var keys = new List<string>();
            for (int l = 1; l <= EquipmentDatabase.MaxEnhance; l++) keys.Add(EquipmentDatabase.KeyFor("eq_ring_1_c", l));
            for (int l = 1; l <= 10; l++) keys.Add(EquipmentDatabase.KeyFor("eq_sword_10_u", l));
            foreach (var k in keys) bag.Add(k, 1);
            var screen = Game.UI.Storage;
            screen.SetKeeper(null, null);
            Game.Flow.OpenWindow(screen);
            yield return Wait(0.3f);
            string pagesBefore = screen.DevPages;
            int moved = 0;
            foreach (var k in keys) if (screen.DevMove(k, true, true)) moved++;
            int stored = 0;
            foreach (var k in keys) if (storage.Count(k) == 1 && bag.Count(k) == 0) stored++;
            int storeIds = new List<string>(storage.Ids).Count;
            screen.DevPage(true, 1);
            var page2 = screen.DevShown(true);
            Check($"storage: {moved}/{keys.Count} deposited, {stored} stored, {storeIds} ids (capacity {StorageScreen.Capacity}), before [{pagesBefore}] now [{screen.DevPages}] page 2 shows {page2.Count}",
                StorageScreen.Capacity == 48 && moved == keys.Count && stored == keys.Count && storeIds > 24 && pagesBefore.StartsWith("bag 1/2")
                && screen.DevPages.Contains("storage 2/2") && page2.Count == storeIds - 24);
            yield return Wait(0.2f);
            yield return Shot("03n_storage_page2");
            Game.Flow.CloseInventory();
            yield return Wait(0.3f);
        }
    }
}
