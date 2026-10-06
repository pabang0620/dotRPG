using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 잡화점 일괄 판매: tick the kinds to sell (gear by rarity, consumables, materials) and sell every matching bag
    /// stack at once. The ticks are remembered on this PC. Enhanced gear (+1 and up), protection tickets, seal
    /// keys and anything without a sell price are never part of it.
    /// </summary>
    public class BulkSellPanel : MonoBehaviour
    {
        enum Kind { Common, Uncommon, Rare, Consumable, Material, LowTierOnly }

        static readonly (Kind kind, string label)[] Options =
        {
            (Kind.Common, "커먼 장비"), (Kind.Uncommon, "언커먼 장비"), (Kind.Rare, "레어 장비"),
            (Kind.Consumable, "소비 아이템 (물약 · 주문서)"), (Kind.Material, "재료"),
            (Kind.LowTierOnly, "장비는 내 레벨 단계보다 낮은 것만"),
        };
        const string PrefKey = "dotrpg.bulkSell";

        readonly bool[] ticked = new bool[Options.Length];
        readonly List<Text> boxes = new List<Text>();
        Text summary;
        Action changed;
        bool busy;

        public static BulkSellPanel Create(Transform parent, Action onSold)
        {
            var bg = UIFactory.Panel(parent, "BulkSell", true);
            bg.raycastTarget = true; // clicks stop here, not on the list underneath
            UIFactory.Place(bg.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(560f, 520f));
            var p = bg.gameObject.AddComponent<BulkSellPanel>();
            p.changed = onSold;
            var title = UIFactory.Text(bg.transform, "Title", "일괄 판매", 28, UIColors.Highlight, TextAnchor.UpperCenter, true);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -16f), new Vector2(520f, 40f));
            for (int i = 0; i < Options.Length; i++)
            {
                int index = i;
                var row = UIFactory.Image(bg.transform, "Opt" + i, Game.Art.Get("ui_white"), new Color32(30, 45, 66, 235));
                row.raycastTarget = true;
                UIFactory.Place(row.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -70f - i * 52f), new Vector2(500f, 46f));
                row.gameObject.AddComponent<PointerRelay>().onClick = _ => p.Toggle(index);
                var t = UIFactory.Text(row.transform, "Text", "", 22, Color.white, TextAnchor.MiddleLeft, true);
                UIFactory.Stretch(t.rectTransform, 16f, 0f, 10f, 0f);
                p.boxes.Add(t);
            }
            var note = UIFactory.Text(bg.transform, "Note", "<color=#8c96a8>강화된 장비(+1 이상), 보호권, 봉인 열쇠는 팔지 않습니다. 착용 중인 장비는 가방에 없어 제외됩니다.</color>", 16, Color.white, TextAnchor.UpperCenter, true);
            UIFactory.Place(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -386f), new Vector2(500f, 44f));
            p.summary = UIFactory.Text(bg.transform, "Summary", "", 20, UIColors.Cream, TextAnchor.MiddleCenter, true);
            UIFactory.Place(p.summary.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 76f), new Vector2(520f, 30f));
            MakeButton(bg.transform, "판매", "ui_btn", new Vector2(-6f, 16f), new Vector2(1f, 0f), p.AskSell);
            MakeButton(bg.transform, "닫기", "ui_btngray", new Vector2(6f, 16f), new Vector2(0f, 0f), () => p.gameObject.SetActive(false));
            p.Load();
            bg.gameObject.SetActive(false);
            return p;
        }

        static void MakeButton(Transform parent, string label, string sprite, Vector2 pos, Vector2 pivot, Action onClick)
        {
            var img = UIFactory.Image(parent, "Btn_" + label, Game.Art.Get(sprite), Color.white);
            img.preserveAspect = false;
            img.raycastTarget = true;
            UIFactory.Place(img.rectTransform, new Vector2(0.5f, 0f), pivot, pos, new Vector2(200f, 54f));
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => onClick());
            UiButton.Attach(b);
            var t = UIFactory.Text(img.transform, "Text", label, UiTheme.ButtonFont, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(t.rectTransform);
        }

        public void Open()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();
        }

        void Load()
        {
            string saved = "";
            try { saved = PlayerPrefs.GetString(PrefKey, "110001"); } catch { }
            for (int i = 0; i < ticked.Length; i++) ticked[i] = i < saved.Length && saved[i] == '1';
        }

        void Save()
        {
            var chars = new char[ticked.Length];
            for (int i = 0; i < ticked.Length; i++) chars[i] = ticked[i] ? '1' : '0';
            try { PlayerPrefs.SetString(PrefKey, new string(chars)); PlayerPrefs.Save(); } catch { }
        }

        void Toggle(int i)
        {
            if (busy) return;
            ticked[i] = !ticked[i];
            Save();
            Game.Audio.PlaySfx("select");
            Refresh();
        }

        /// <summary>Which kind a bag id is, or null when bulk selling never touches it.</summary>
        static Kind? KindOf(string id)
        {
            if (ItemPrices.SellPrice(id) <= 0) return null;
            var gear = EquipmentDatabase.Get(id);
            if (gear != null)
            {
                if (EquipmentDatabase.LevelOfKey(id) > 0) return null; // enhanced: real effort went into it
                switch (gear.rarity)
                {
                    case ItemRarity.Common: return Kind.Common;
                    case ItemRarity.Uncommon: return Kind.Uncommon;
                    case ItemRarity.Rare: return Kind.Rare;
                    default: return null; // epic and above: sell one by one on purpose
                }
            }
            var c = ConsumableDatabase.Get(id);
            if (c != null) return ConsumableDatabase.IsUsable(id) ? Kind.Consumable : (Kind?)null; // tickets, keys, gold stay
            return Kind.Material;
        }

        List<(string id, int count)> Picked(out long total)
        {
            total = 0;
            var list = new List<(string, int)>();
            var bag = Game.Session.Inventory;
            foreach (var id in new List<string>(bag.Ids))
            {
                var k = KindOf(id);
                if (k == null || !ticked[(int)k.Value]) continue;
                // [GEAR] Optionally keep gear of my current tier and above (sell only what I have outgrown).
                var g = EquipmentDatabase.Get(id);
                if (g != null && ticked[(int)Kind.LowTierOnly] && g.levelTier >= GearCatalog.TierOfLevel(Game.Session.Progression.Level)) continue;
                int n = bag.Count(id);
                if (n <= 0) continue;
                list.Add((id, n));
                total += (long)ItemPrices.SellPrice(id) * n;
            }
            return list;
        }

        void Refresh()
        {
            for (int i = 0; i < Options.Length; i++) boxes[i].text = (ticked[i] ? "<color=#8fe28f>■</color>  " : "<color=#8c96a8>□</color>  ") + Options[i].label;
            var list = Picked(out long total);
            int items = 0;
            foreach (var e in list) items += e.count;
            summary.text = busy ? "판매 중..." : list.Count == 0 ? "<color=#8c96a8>팔 아이템이 없습니다.</color>" : $"{list.Count}종 {items}개  ·  <color=#ffd84a>{total:N0} G</color>";
        }

        void AskSell()
        {
            if (busy) return;
            var list = Picked(out long total);
            if (list.Count == 0) { Game.Audio.PlaySfx("cancel"); return; }
            Game.UI.Confirm($"{list.Count}종의 아이템을 {total:N0} G에 모두 판매할까요?", () => Sell(list), true);
        }

        void Sell(List<(string id, int count)> list)
        {
            busy = true;
            Refresh();
            if (!OnlineEconomy.On)
            {
                var bag = Game.Session.Inventory;
                foreach (var (id, count) in list)
                {
                    int n = Mathf.Min(count, bag.Count(id));
                    if (n > 0 && bag.Remove(id, n)) bag.Add(ConsumableDatabase.Gold, ItemPrices.SellPrice(id) * n);
                }
                Finish(list.Count, list.Count);
                return;
            }
            // [SERVER] One sale per stack, one after another (the server prices and pays each).
            int index = 0, ok = 0;
            void Next()
            {
                if (index >= list.Count) { Finish(ok, list.Count); return; }
                var (id, count) = list[index++];
                int n = Mathf.Min(count, Game.Session.Inventory.Count(id));
                if (n <= 0) { Next(); return; }
                OnlineEconomy.ShopSell(id, n, success => { if (success) ok++; Next(); });
            }
            Next();
        }

        void Finish(int ok, int all)
        {
            busy = false;
            Game.Audio.PlaySfx(ok > 0 ? "pickup" : "cancel");
            GameEvents.RaiseToast(ok == all ? $"{ok}종의 아이템을 팔았습니다." : $"{all}종 중 {ok}종을 팔았습니다.");
            changed?.Invoke();
            if (this != null && gameObject.activeSelf) Refresh();
        }
    }
}
