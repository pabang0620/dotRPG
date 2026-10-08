using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
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
