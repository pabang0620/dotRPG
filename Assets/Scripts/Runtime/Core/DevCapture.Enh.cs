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

        /// <summary>Success % for +0→+1 … +19→+20 (reference table).</summary>
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

    }
}
