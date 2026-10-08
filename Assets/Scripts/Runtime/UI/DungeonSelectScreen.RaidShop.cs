using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [RAID] 레이드 상점 (Docs/server/phase13_raid_rewards.md §7.5): a button on the 레이드 tab (the 소탕 button's place) opens a panel
    /// over the detail with the four weapon boxes of 해골왕 and 수호자 그라흐. A box is paid with that raid's own material and
    /// opened on purchase: the class's weapon comes straight into the bag, shown as one flipping card.
    /// Online the server decides (OnlineEconomy.BuyRaidShop); offline the same table is rolled here.
    /// </summary>
    public partial class DungeonSelectScreen
    {
        const float RsColX = 24f, RsColStep = 402f, RsColW = 390f, RsCellH = 190f;

        sealed class RsCell
        {
            public RaidShopProduct product;
            public Image box, gear;
            public Text name, cost, result, gearName;
            public Button buy;
        }

        Button raidShopButton;
        RectTransform raidShopPanel, rsPopup;
        readonly List<RsCell> rsCells = new List<RsCell>();
        readonly Image[] rsColIcon = new Image[2];
        readonly Text[] rsColHead = new Text[2];
        Text rsPopupTitle;
        Button rsPopupOk;
        RaidCardView rsCard;
        List<OnlineEconomy.RaidShopOffer> rsOffers;
        bool rsBusy, rsPopupDone;
        readonly System.Random rsRng = new System.Random();

        bool RaidShopOpen => raidShopPanel != null && raidShopPanel.gameObject.activeSelf;
        bool RsPopupOpen => rsPopup != null && rsPopup.gameObject.activeSelf;

        void BuildRaidShop(Transform d)
        {
            raidShopButton = Button(d, "RaidShop", "레이드 상점", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-276f, 20f), new Vector2(180f, 62f), OpenRaidShop, 22);

            var panel = Panel(d, "RaidShopPanel", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero, new Color32(18, 26, 40, 250));
            raidShopPanel = panel.rectTransform;
            raidShopPanel.anchorMin = Vector2.zero; raidShopPanel.anchorMax = Vector2.one; raidShopPanel.offsetMin = raidShopPanel.offsetMax = Vector2.zero;
            panel.raycastTarget = true; // blocks the detail underneath
            var t = raidShopPanel.transform;
            var tl = new Vector2(0f, 1f);
            Label(t, "Head", "<b>레이드 상점</b>", 26, tl, tl, new Vector2(24f, -14f), new Vector2(400f, 38f));
            Label(t, "Sub", "<color=#b8c4d8>레이드 재료로 무기 상자를 삽니다. 산 즉시 열려 내 직업 무기가 가방에 들어옵니다.</color>", 16, tl, tl, new Vector2(24f, -52f), new Vector2(700f, 26f));
            Button(t, "Close", "닫기", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-16f, -14f), new Vector2(110f, 40f), CloseRaidShop, 18);

            for (int col = 0; col < 2; col++)
            {
                float x0 = RsColX + col * RsColStep;
                rsColIcon[col] = UIFactory.SharpIcon(t, "ColIcon" + col, Color.white);
                UIFactory.Place(rsColIcon[col].rectTransform, tl, tl, new Vector2(x0, -84f), new Vector2(40f, 40f));
                rsColHead[col] = Label(t, "ColHead" + col, "", 19, tl, tl, new Vector2(x0 + 50f, -82f), new Vector2(RsColW - 50f, 44f));
            }
            for (int i = 0; i < RaidShop.Products.Length; i++)
            {
                var product = RaidShop.Products[i];
                int col = i / 2, row = i % 2;
                var cell = new RsCell { product = product };
                float x0 = RsColX + col * RsColStep, y0 = -134f - row * (RsCellH + 8f);
                var bg = Panel(t, "Cell" + i, tl, tl, new Vector2(x0, y0), new Vector2(RsColW, RsCellH), new Color32(14, 20, 32, 255));
                var ct = bg.transform;
                cell.box = UIFactory.SharpIcon(ct, "Box", Color.white);
                cell.box.sprite = Game.Art.Get(product.IsLegendary ? "icon_box_enh_gold" : "icon_box_sealed");
                UIFactory.Place(cell.box.rectTransform, tl, tl, new Vector2(14f, -14f), new Vector2(56f, 56f));
                cell.name = Label(ct, "Name", $"<b>{product.name}</b>", 19, tl, tl, new Vector2(82f, -12f), new Vector2(RsColW - 94f, 28f));
                cell.cost = Label(ct, "Cost", "", 17, tl, tl, new Vector2(82f, -42f), new Vector2(RsColW - 94f, 26f));
                cell.result = Label(ct, "Result", "", 16, tl, tl, new Vector2(82f, -68f), new Vector2(RsColW - 94f, 48f));
                if (product.IsLegendary)
                {
                    cell.gear = UIFactory.SharpIcon(ct, "Gear", Color.white);
                    UIFactory.Place(cell.gear.rectTransform, tl, tl, new Vector2(14f, -124f), new Vector2(44f, 44f));
                    cell.gearName = Label(ct, "GearName", "", 17, tl, tl, new Vector2(66f, -134f), new Vector2(160f, 28f));
                    var capture = cell;
                    GearTooltip.Hook(cell.gear, () => capture.gear.enabled ? RaidShop.ResultKey(capture.product, RaidClass(), ItemRarity.Legendary) : null);
                }
                cell.buy = Button(ct, "Buy", "구매", "ui_btn", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-14f, 12f), new Vector2(140f, 42f), () => RequestBuy(product), 20);
                rsCells.Add(cell);
            }

            // Box result popup: one card flipping on a dark layer over the panel.
            var dim = Panel(t, "Popup", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, Vector2.zero, new Color(0f, 0f, 0f, 0.8f));
            rsPopup = dim.rectTransform;
            rsPopup.anchorMin = Vector2.zero; rsPopup.anchorMax = Vector2.one; rsPopup.offsetMin = rsPopup.offsetMax = Vector2.zero;
            dim.raycastTarget = true;
            rsPopupTitle = Label(rsPopup, "Title", "", 24, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -14f), new Vector2(640f, 36f), TextAnchor.UpperCenter);
            rsCard = RaidCardView.Create(rsPopup, "BoxCard", new Vector2(0.5f, 0.5f), new Vector2(0f, -6f), 1.5f, null, () => rsCard != null && rsCard.Flipped ? rsCard.Content.itemId : null);
            rsPopupOk = Button(rsPopup, "Ok", "확인", "ui_btn", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(200f, 46f), CloseBoxPopup, 22);
            rsPopup.gameObject.SetActive(false);
            raidShopPanel.gameObject.SetActive(false);
        }

        CharacterClass RaidClass() => Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;

        // ---------------- open / close ----------------

        void OpenRaidShop()
        {
            if (!raidTab) return;
            rsBusy = false;
            rsPopup.gameObject.SetActive(false);
            raidShopPanel.gameObject.SetActive(true);
            raidShopPanel.SetAsLastSibling();
            Game.Audio.PlaySfx("select");
            rsOffers = null;
            if (OnlineEconomy.On)
                OnlineEconomy.LoadRaidShop(list =>
                {
                    rsOffers = list;
                    if (this != null && RaidShopOpen) RefreshRaidShop();
                });
            RefreshRaidShop();
        }

        void CloseRaidShop()
        {
            if (!RaidShopOpen || RsPopupOpen) return;
            raidShopPanel.gameObject.SetActive(false);
            Game.Audio.PlaySfx("cancel");
        }

        /// <summary>The window closed or switched away: the panel and the popup go without a sound.</summary>
        void CloseRaidShopQuiet()
        {
            if (raidShopPanel != null) raidShopPanel.gameObject.SetActive(false);
            if (rsPopup != null) rsPopup.gameObject.SetActive(false);
        }

        void CloseBoxPopup()
        {
            if (!RsPopupOpen || !rsPopupDone) return;
            rsPopup.gameObject.SetActive(false);
            Game.Audio.PlaySfx("confirm");
            RefreshRaidShop();
        }

        /// <summary>Refresh() hook: the button sits on the raid tab only, where 소탕 would be.</summary>
        void RefreshRaidShopButton()
        {
            if (raidShopButton == null) return;
            bool on = raidTab && Selected != null && Selected.isRaid;
            raidShopButton.gameObject.SetActive(on);
            TextOf(raidShopButton).text = $"레이드 상점 <size=15><color=#b8c4d8>[{Game.Input.GetBindingLabel(GameAction.UseItem)}]</color></size>";
            if (!on && RaidShopOpen) CloseRaidShopQuiet();
            else if (RaidShopOpen) RefreshRaidShop();
        }

        string RaidShopKeyHint() => raidShopButton != null && raidShopButton.gameObject.activeSelf ? $"   {Game.Input.GetBindingLabel(GameAction.UseItem)} 레이드 상점" : "";

        /// <summary>Update() hook while the panel is open: Esc closes the popup (once the card is open) or the panel.</summary>
        void RaidShopInput()
        {
            if (!TakesInput) return;
            var input = Game.Input;
            if (RsPopupOpen)
            {
                if (rsPopupDone && (input.SubmitPressed || input.CancelPressed || input.InventoryPressed)) CloseBoxPopup();
                return;
            }
            if (input.CancelPressed || input.InventoryPressed) CloseRaidShop();
        }

        // ---------------- panel text ----------------

        OnlineEconomy.RaidShopOffer OfferOf(string productId)
        {
            if (rsOffers == null) return null;
            foreach (var o in rsOffers) if (o.id == productId) return o;
            return null;
        }

        /// <summary>"에픽 88% / 유니크 12%" or "레전더리 확정"; the server's table when it has answered, else the local one.</summary>
        string RatesOf(RaidShopProduct p)
        {
            var offer = OfferOf(p.id);
            if (offer == null || offer.outcomes.Count == 0) return RaidShop.RatesText(p);
            var parts = new List<string>();
            foreach (var o in offer.outcomes)
            {
                string name = System.Enum.TryParse(o.rarity, out ItemRarity r) ? EquipmentDatabase.RarityName(r) : o.rarity;
                parts.Add(offer.outcomes.Count == 1 && o.percent >= 100 ? $"{name} 확정" : $"{name} {o.percent}%");
            }
            return string.Join(" / ", parts);
        }

        void RefreshRaidShop()
        {
            if (raidShopPanel == null || !RaidShopOpen) return;
            var bag = Game.Session.Inventory;
            var cls = RaidClass();
            string[] raids = { DungeonDatabase.Raid, DungeonDatabase.RaidGrah };
            for (int col = 0; col < 2; col++)
            {
                var raid = DungeonDatabase.Get(raids[col]);
                string material = RaidShop.Products[col * 2].materialItem;
                int lv = GearCatalog.TierLevels[RaidShop.Products[col * 2].Tier];
                rsColIcon[col].sprite = Game.Art.Get(DungeonDatabase.ItemIcon(material));
                rsColHead[col].text = $"<b>{raid.name} (Lv.{lv})</b>\n<color=#b8c4d8>{DungeonDatabase.ItemName(material)}</color> <color=#ffe066>보유 {bag.Count(material):N0}</color>";
            }
            foreach (var cell in rsCells)
            {
                var p = cell.product;
                var offer = OfferOf(p.id);
                int price = offer != null && offer.price > 0 ? offer.price : p.price;
                bool afford = bag.Count(p.materialItem) >= price;
                cell.cost.text = $"재료 <color={(afford ? "#ffffff" : "#ff9f7a")}>{price:N0}개</color>";
                cell.result.text = $"내 직업 무기 · {RatesOf(p)}";
                cell.buy.interactable = !rsBusy;
                cell.buy.image.color = afford ? Color.white : new Color(1f, 1f, 1f, 0.5f);
                if (cell.gear == null) continue;
                string key = RaidShop.ResultKey(p, cls, ItemRarity.Legendary);
                cell.gear.enabled = key != null;
                cell.gearName.text = key != null ? EquipmentDatabase.RichName(key) : "";
                if (key != null) cell.gear.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(key));
            }
        }

        // ---------------- buying ----------------

        void RequestBuy(RaidShopProduct p)
        {
            if (rsBusy || RsPopupOpen) return;
            var offer = OfferOf(p.id);
            int price = offer != null && offer.price > 0 ? offer.price : p.price;
            if (Game.Session.Inventory.Count(p.materialItem) < price)
            {
                GameEvents.RaiseToast($"{DungeonDatabase.ItemName(p.materialItem)}이(가) 모자랍니다. ({Game.Session.Inventory.Count(p.materialItem):N0}/{price:N0})");
                Game.Audio.PlaySfx("cancel");
                return;
            }
            Game.UI.Confirm($"재료 {price:N0}개를 써서 {p.name}을 엽니다.\n<size=18>내 직업 무기 · {RatesOf(p)}</size>", () => DoBuy(p, price), defaultYes: true, overlay: true);
        }

        void DoBuy(RaidShopProduct p, int price)
        {
            if (rsBusy) return;
            rsBusy = true; // one request per press: the buttons stay off until the answer (or the failure) is in
            RefreshRaidShop();
            if (OnlineEconomy.On)
            {
                OnlineEconomy.BuyRaidShop(p.id, (key, rarity) =>
                {
                    if (this == null) return;
                    rsBusy = false;
                    if (key == null) { Game.Audio.PlaySfx("cancel"); RefreshRaidShop(); return; }
                    ShowBoxResult(p, key);
                });
                return;
            }
            var bag = Game.Session.Inventory;
            if (!bag.Remove(p.materialItem, price)) { rsBusy = false; RefreshRaidShop(); return; }
            var (rarity2, key2) = RaidShop.Roll(p, RaidClass(), rsRng);
            rsBusy = false;
            if (key2 == null)
            {
                bag.Add(p.materialItem, price); // nothing to hand out: give the material back
                GameEvents.RaiseToast("지급할 장비를 찾지 못했습니다.");
                RefreshRaidShop();
                return;
            }
            bag.Add(key2, 1);
            ShowBoxResult(p, key2);
        }

        /// <summary>The opened box: one face-down card on a dark layer that flips to the weapon (the raid result's staging).</summary>
        void ShowBoxResult(RaidShopProduct p, string key)
        {
            rsPopupDone = false;
            rsPopup.gameObject.SetActive(true);
            rsPopup.SetAsLastSibling();
            rsPopupOk.gameObject.SetActive(false);
            rsPopupTitle.text = $"<b>{p.name}</b>";
            rsCard.SetActive(true);
            rsCard.ResetBack();
            RefreshRaidShop();
            StartCoroutine(BoxRoutine(key));
        }

        IEnumerator BoxRoutine(string key)
        {
            yield return rsCard.Appear(0f);
            yield return new WaitForSecondsRealtime(0.4f);
            yield return rsCard.Flip(new RewardCard(key, 1));
            rsCard.Who.text = "<color=#8fe28f>획득</color>";
            rsPopupOk.gameObject.SetActive(true);
            rsPopupDone = true;
        }
    }
}
