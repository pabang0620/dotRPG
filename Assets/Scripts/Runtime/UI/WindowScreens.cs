using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{

    // =====================================================================================

    // =====================================================================================

    // =====================================================================================

    /// <summary>
    /// Blacksmith (anvil or 대장장이): pick a piece of gear - worn slots first, then the bag - pay gold and
    /// monster materials and try to raise it one +level: the chance falls from 100%
    /// to 10%, a weapon failing from +10 / +11 drops 3 levels, and from +12 (other gear +10) a failure
    /// destroys the piece unless a protection ticket in the bag saves it at +0. Risky attempts ask first,
    /// then the hammer falls twice (unscaled time: windows pause the game). Rules: <see cref="EnhanceRules"/>.
    /// </summary>
    public partial class EnhanceScreen : WindowScreen
    {
        const int Cols = 5, RowsN = 4, PerPage = Cols * RowsN;
        const float Cell = 92f, Gap = 8f;
        /// <summary>Hammer time before the result.</summary>
        const float SuspenseSeconds = 0.9f;

        /// <summary>One thing that can be enhanced: a worn slot (each slot on its own) or a key in the bag.</summary>
        struct Entry
        {
            public EquipSlot? slot;
            public string key;

            public EnhanceTarget Target => slot.HasValue ? EnhanceTarget.Worn(slot.Value) : EnhanceTarget.Bag(key);
        }

        sealed class Cell_
        {
            public Image bg, icon, frame;
            public Text level, count, worn;
        }

        readonly List<Cell_> cells = new List<Cell_>();
        readonly List<Entry> entries = new List<Entry>();
        readonly Text[] costCells = new Text[4];
        Image cursor, bigIcon, bigFrame;
        Text title, statsText, costTitle, chanceText, failText, resultText, pageText;
        Button enhanceButton, pagePrev, pageNext;
        Text enhanceLabel;
        int selected;
        bool dirty, busy;
        /// <summary>Set after a piece was destroyed: 강화 / Enter do nothing until the player picks again (click or navigation).</summary>
        bool pickRequired;
        float busyTime;

        CharacterClass Class => Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;

        int PageCount => Mathf.Max(1, (entries.Count + PerPage - 1) / PerPage);

        public static EnhanceScreen Create(Transform canvas)
        {
            var w = CreateWindow<EnhanceScreen>(canvas, "Enhance", "대장간 · 강화 · 승급", "menuicon_enhance");
            float gridW = Cols * Cell + (Cols - 1) * Gap;
            var left = Panel(w.content, "Left", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(gridW + 40f, 590f), new Color32(24, 36, 54, 235));
            Label(left.transform, "Hint", "강화할 장비를 고르세요 (착용 중 + 가방)", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(500f, 30f));
            for (int i = 0; i < PerPage; i++)
            {
                int index = i;
                var c = new Cell_();
                c.bg = Img(left.transform, "Cell" + i, "ui_slot", Color.white);
                c.bg.raycastTarget = true;
                UIFactory.Place(c.bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(20f + (i % Cols) * (Cell + Gap), -52f - (i / Cols) * (Cell + Gap)), new Vector2(Cell, Cell));
                c.icon = UIFactory.SharpIcon(c.bg.transform, "Icon", Color.white);
                UIFactory.Place(c.icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(66f, 66f));
                c.frame = Img(c.bg.transform, "Frame", "ui_frame", Color.clear);
                UIFactory.Stretch(c.frame.rectTransform);
                c.level = UIFactory.Text(c.bg.transform, "Level", "", 20, Color.white, TextAnchor.UpperRight, true);
                UIFactory.Stretch(c.level.rectTransform, 4f, 2f, 6f, 2f);
                c.count = UIFactory.Text(c.bg.transform, "Count", "", 18, Color.white, TextAnchor.LowerRight, true);
                UIFactory.Stretch(c.count.rectTransform, 4f, 2f, 6f, 2f);
                c.worn = UIFactory.Text(c.bg.transform, "Worn", "", UiTheme.FontMin, new Color32(120, 220, 255, 255), TextAnchor.LowerLeft, true);
                UIFactory.Stretch(c.worn.rectTransform, 8f, 6f, 4f, 2f);
                var relay = c.bg.gameObject.AddComponent<PointerRelay>();
                relay.onClick = _ => w.Click(index);
                w.cells.Add(c);
            }
            w.cursor = Img(left.transform, "Cursor", "ui_frame", new Color32(255, 211, 74, 255));
            // "+N" colour bands, then paging (only shown with more than 20 entries).
            Label(left.transform, "Legend", $"{Band(1, 6)}  {Band(7, 8)}  {Band(9, 12)}  {Band(13, 14)}  {Band(15, 16)}  {Band(17, EquipmentDatabase.MaxEnhance)}", 16,
                new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 76f), new Vector2(gridW, 26f), TextAnchor.MiddleCenter);
            w.pagePrev = Button(left.transform, "Prev", "◀", "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f, 14f), new Vector2(56f, 46f), () => w.Page(-1), 22);
            w.pageNext = Button(left.transform, "Next", "▶", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-20f, 14f), new Vector2(56f, 46f), () => w.Page(1), 22);
            w.pageText = Label(left.transform, "Page", "", 20, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(200f, 46f), TextAnchor.MiddleCenter);

            var right = Panel(w.content, "Right", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(660f, 590f), new Color32(24, 36, 54, 235));
            var iconBg = Img(right.transform, "IconBg", "ui_slotblue", Color.white);
            UIFactory.Place(iconBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(128f, 128f));
            w.bigIcon = UIFactory.SharpIcon(iconBg.transform, "Icon", Color.white);
            UIFactory.Place(w.bigIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96f, 96f));
            w.bigFrame = Img(iconBg.transform, "Frame", "ui_frame", Color.clear);
            UIFactory.Stretch(w.bigFrame.rectTransform);
            var topLeft = new Vector2(0f, 1f);
            w.title = Label(right.transform, "Title", "", 26, topLeft, topLeft, new Vector2(172f, -24f), new Vector2(470f, 128f));
            w.statsText = Label(right.transform, "Stats", "", 19, topLeft, topLeft, new Vector2(24f, -158f), new Vector2(610f, 92f));
            w.costTitle = Label(right.transform, "CostTitle", "", 19, topLeft, topLeft, new Vector2(24f, -252f), new Vector2(610f, 30f));
            for (int i = 0; i < w.costCells.Length; i++)
                w.costCells[i] = Label(right.transform, "Cost" + i, "", 19, topLeft, topLeft, new Vector2(24f + (i % 2) * 305f, -282f - (i / 2) * 30f), new Vector2(300f, 30f));
            w.chanceText = Label(right.transform, "Chance", "", 20, topLeft, topLeft, new Vector2(24f, -346f), new Vector2(610f, 32f));
            w.failText = Label(right.transform, "Fail", "", 19, topLeft, topLeft, new Vector2(24f, -379f), new Vector2(610f, 30f));
            w.resultText = Label(right.transform, "Result", "", 22, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 80f), new Vector2(620f, 64f), TextAnchor.MiddleCenter);
            w.enhanceButton = Button(right.transform, "Go", "강화", "ui_btn", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(300f, 64f), w.OnEnhancePressed, 30);
            w.enhanceLabel = w.enhanceButton.GetComponentInChildren<Text>();
            w.BuildPromote(right.transform);
            w.BuildTicket(right.transform); // [CASH] 강화권
            return w;
        }

        static string Band(int from, int to) => $"<color={EquipmentDatabase.LevelColor(from)}>+{from}~{to}</color>";

        /// <summary>Worn slots first (each slot on its own, so two identical rings are two entries), then the bag keys in bag order.</summary>
        void BuildEntries()
        {
            entries.Clear();
            var eq = Game.Session.Equipment;
            for (int i = 0; i < Equipment.SlotCount; i++)
            {
                var slot = (EquipSlot)i;
                if (EquipmentDatabase.IsEquipment(eq[slot])) entries.Add(new Entry { slot = slot, key = eq[slot] });
            }
            foreach (var key in EquipmentDatabase.GearKeys(Game.Session.Inventory)) entries.Add(new Entry { key = key });
        }

        public override void Show()
        {
            // Always open on the first page, first entry (the bag may have changed since the last visit).
            selected = 0;
            pickRequired = false;
            resultText.text = "";
            bigIcon.rectTransform.anchoredPosition = Vector2.zero;
            if (ticketPending)
            {
                // [UX] Reopened before the server answered a 강화권: stay locked (no hammer / promote / second ticket) until it does.
                busy = true;
                resultText.text = TicketPendingText;
            }
            base.Show();
        }

        public override void Hide()
        {
            if (busy && ticketPending)
            {
                // [UX] Closed while the server answers a 강화권: the answer still lands (toast, bag) when it comes.
                busy = false;
                bigIcon.rectTransform.anchoredPosition = Vector2.zero;
            }
            else if (busy)
            {
                // Closed mid-swing (e.g. a state change): the attempt is called off. Nothing is paid before the hammer lands.
                StopAllCoroutines();
                busy = false;
                devRoll = null;
                bigIcon.rectTransform.anchoredPosition = Vector2.zero;
            }
            base.Hide();
        }

        /// <summary>
        /// The window stays open until the hammer lands. Only a 강화권 waiting on the server may be closed: nothing else
        /// starts while it is pending, so busy with ticketPending is never a hammer swing or a 승급.
        /// </summary>
        public override void Close()
        {
            if (!busy || ticketPending) base.Close();
        }

        protected override void Refresh()
        {
            BuildEntries();
            var bag = Game.Session.Inventory;
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, entries.Count - 1));
            int page = selected / PerPage, pages = PageCount;
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                int index = page * PerPage + i;
                var item = index < entries.Count ? EquipmentDatabase.Get(entries[index].key) : null;
                c.icon.enabled = item != null;
                c.frame.color = item != null ? EquipmentDatabase.RarityTint(item.rarity) : Color.clear;
                if (item == null)
                {
                    c.level.text = c.count.text = c.worn.text = "";
                    continue;
                }
                var e = entries[index];
                c.icon.sprite = Game.Art.Get(item.iconKey);
                int lv = EquipmentDatabase.LevelOfKey(e.key);
                c.level.text = lv > 0 ? $"+{lv}" : "";
                c.level.color = EquipmentDatabase.LevelTint(lv);
                int n = e.slot.HasValue ? 0 : bag.Count(e.key);
                c.count.text = n > 1 ? n.ToString() : "";
                c.worn.text = e.slot.HasValue ? "착용" : "";
            }
            // The cursor sits on the selected cell of the shown page (never past the 20 cells).
            var at = cells[selected - page * PerPage].bg.rectTransform;
            cursor.rectTransform.anchorMin = cursor.rectTransform.anchorMax = at.anchorMin;
            cursor.rectTransform.pivot = new Vector2(0f, 1f);
            cursor.rectTransform.anchoredPosition = at.anchoredPosition + new Vector2(-5f, 5f);
            cursor.rectTransform.sizeDelta = new Vector2(Cell + 10f, Cell + 10f);
            cursor.transform.SetAsLastSibling();
            bool paged = pages > 1;
            pageText.text = $"{page + 1} / {pages}";
            pageText.gameObject.SetActive(paged);
            pagePrev.gameObject.SetActive(paged);
            pageNext.gameObject.SetActive(paged);
            pagePrev.interactable = !busy && page > 0;
            pageNext.interactable = !busy && page < pages - 1;

            if (entries.Count == 0) ShowNothing();
            else ShowDetails(entries[selected]);
            RefreshPromote(entries.Count == 0 ? null : entries[selected].key);
            dirty = false;
        }

        void ShowNothing()
        {
            bigIcon.enabled = false;
            bigFrame.color = Color.clear;
            title.text = "강화할 장비가 없습니다.";
            statsText.text = costTitle.text = chanceText.text = failText.text = "";
            foreach (var t in costCells) t.text = "";
            enhanceButton.interactable = false;
            enhanceLabel.text = "강화";
            RefreshTicket(null); // no gear: the 강화권 button goes too
        }

        void ShowDetails(Entry e)
        {
            var eq = Game.Session.Equipment;
            var bag = Game.Session.Inventory;
            var gear = EquipmentDatabase.Get(e.key);
            int level = EquipmentDatabase.LevelOfKey(e.key);
            bool max = level >= EquipmentDatabase.MaxEnhance;
            RefreshTicket(e.key);
            bigIcon.enabled = true;
            bigIcon.sprite = Game.Art.Get(gear.iconKey);
            bigFrame.color = EquipmentDatabase.RarityTint(gear.rarity);
            string where = e.slot.HasValue ? $"{EquipmentDatabase.SlotName(e.slot.Value)} 착용 중" : $"가방 {bag.Count(e.key)}개";
            title.text = $"<b>{EquipmentDatabase.RichName(e.key)}</b>\n<size=19><color=#b8c4d8>[{EquipmentDatabase.RarityName(gear.rarity)}] {gear.CategoryName} · {where}</color></size>\n" +
                         (max ? "<color=#ffe066>최대 강화 달성!</color>" : $"{EquipmentDatabase.LevelTag(level)}  →  {EquipmentDatabase.LevelTag(level + 1)}");
            var now = gear.StatsAt(level);
            if (max)
            {
                statsText.text = $"<b>현재 능력치</b>   <color=#b8c4d8>전투력 {now.Score:N0}</color>\n{gear.StatLine(level)}";
                costTitle.text = chanceText.text = failText.text = "";
                foreach (var t in costCells) t.text = "";
                enhanceButton.interactable = false;
                enhanceLabel.text = "최대 강화";
                return;
            }
            var next = gear.StatsAt(level + 1);
            // Low tiers can pay for a level whose bonus still rounds to the same stat: say so instead of "A → A".
            bool flat = next.enhanceAttack == now.enhanceAttack && next.enhanceHealth == now.enhanceHealth;
            string after = flat ? NoStatChange : $"강화 후  <color=#8fe28f>{gear.StatLine(level + 1)}</color>";
            statsText.text = $"<b>능력치 변화</b>   <color=#b8c4d8>전투력 {now.Score:N0} → </color><color=#8fe28f>{next.Score:N0} (+{next.Score - now.Score:N0})</color>\n" +
                             $"현재<color=#00000000> 후</color>  {gear.StatLine(level)}\n{after}";

            var cost = eq.CostFor(e.key);
            costTitle.text = "<b>필요 재료</b>";
            int cell = 0;
            costCells[cell++].text = Need("골드", Game.Session.Gold, cost.gold);
            if (cost.bone > 0) costCells[cell++].text = Need(MaterialName(EnhanceRules.Bone), bag.Count(EnhanceRules.Bone), cost.bone);
            if (cost.ore > 0) costCells[cell++].text = Need(MaterialName(EnhanceRules.Ore), bag.Count(EnhanceRules.Ore), cost.ore);
            if (cost.essence > 0) costCells[cell++].text = Need(MaterialName(EnhanceRules.Essence), bag.Count(EnhanceRules.Essence), cost.essence);
            while (cell < costCells.Length) costCells[cell++].text = "";

            string chance = $"성공 확률  <color=#ffe066><b>{cost.successPercent}%</b></color>";
            if (cost.pityBonus > 0) chance += $"   <color=#b8c4d8>(기본 {cost.basePercent}% + 보정 {cost.pityBonus}%p)</color>";
            else if (EnhanceRules.HasPity(gear, level)) chance += "   <color=#8c96a8>(실패할 때마다 +1%p 보정)</color>";
            chanceText.text = chance;
            failText.text = FailureLine(cost, bag.Count(ConsumableDatabase.ProtectTicket), gear.starter);

            bool can = eq.CanAfford(cost);
            enhanceButton.interactable = can && !busy;
            enhanceLabel.text = busy ? "강화 중…" : can ? "강화" : Game.Session.Gold < cost.gold ? "골드 부족" : "재료 부족";
        }

        static string Need(string name, int have, int need) =>
            $"{name}  {(have >= need ? "<color=#8fe28f>" : "<color=#ff7070>")}{have:N0}</color> / {need:N0}";

        static string MaterialName(string id) => EquipmentDatabase.GetMaterial(id)?.name ?? id;

        const string NoStatChange = "<color=#8c96a8>이번 단계는 능력치 변화가 없습니다 (높은 단계일수록 크게 오릅니다)</color>";
        const string PickAgain = "강화할 장비를 다시 골라 주세요.";

        static string FailureLine(EnhanceCost cost, int tickets, bool starter)
        {
            switch (cost.failure)
            {
                case EnhanceFailure.Keep: return "실패 시: <color=#8fe28f>강화 수치 유지</color>";
                case EnhanceFailure.Drop3: return $"실패 시: <color=#ff9f43>강화 수치 3 하락 (+{cost.level} → +{cost.DroppedLevel})</color>";
                default:
                    if (cost.usesTicket) return $"실패 시: <color=#ffd84a>장비 보호권 1장 자동 사용 → +0 초기화 (보유 {tickets}장)</color>";
                    return starter && tickets > 0
                        ? "실패 시: <color=#ff5050><b>장비 파괴!</b></color>  <color=#8c96a8>(기본 장비에는 보호권을 쓰지 않습니다)</color>"
                        : "실패 시: <color=#ff5050><b>장비 파괴!</b></color>";
            }
        }

        /// <summary>Risk sentence for attempts that can lose levels or the item (null = safe, no question).</summary>
        static string RiskWarning(EnhanceCost cost)
        {
            switch (cost.failure)
            {
                case EnhanceFailure.Drop3: return $"실패하면 강화 수치가 3 내려갑니다 (+{cost.level} → +{cost.DroppedLevel}).";
                case EnhanceFailure.Destroy:
                    return cost.usesTicket ? "실패하면 장비 보호권 1장을 사용해 +0으로 초기화됩니다." : "실패하면 장비가 파괴됩니다.";
                default: return null;
            }
        }

        /// <summary>The risk confirm: which piece, where it goes, the chance, then the risk.</summary>
        static string ConfirmText(string key, EnhanceCost cost, string risk) =>
            $"<b>{EquipmentDatabase.NameOfKey(key)}</b> → +{cost.level + 1}  (성공 {cost.successPercent}%)\n{risk} 강화할까요?";

        // ---------- Actions ----------

        /// <summary>The player picked something (click, page, arrows): the old result line and the pick lock go.</summary>
        void Picked()
        {
            pickRequired = false;
            resultText.text = "";
            dirty = true;
        }

        void Click(int cellIndex)
        {
            if (busy || !IsTop) return;
            int index = selected / PerPage * PerPage + cellIndex;
            if (index >= entries.Count) return;
            selected = index;
            Picked();
            Game.Audio.PlaySfx("select");
        }

        void Page(int d)
        {
            if (busy || !IsTop) return;
            int page = selected / PerPage;
            int next = Mathf.Clamp(page + d, 0, PageCount - 1);
            if (next == page) return;
            selected = Mathf.Min(next * PerPage, entries.Count - 1);
            Picked();
            Game.Audio.PlaySfx("select", 0.5f);
        }

        /// <summary>Button / Enter: checks the price, asks first when the attempt is risky, then swings the hammer.</summary>
        void OnEnhancePressed()
        {
            if (busy || ticketPending || entries.Count == 0 || !IsTop) return;
            if (pickRequired)
            {
                // After a destroy the cursor sits on a piece the player never chose.
                Game.Audio.PlaySfx("cancel");
                resultText.text = PickAgain;
                return;
            }
            var e = entries[selected];
            var eq = Game.Session.Equipment;
            if (EquipmentDatabase.LevelOfKey(e.key) >= EquipmentDatabase.MaxEnhance)
            {
                Game.Audio.PlaySfx("cancel");
                return;
            }
            var cost = eq.CostFor(e.key);
            if (!eq.CanAfford(cost))
            {
                Game.Audio.PlaySfx("cancel");
                resultText.text = Game.Session.Gold < cost.gold
                    ? "<color=#ff7070>골드가 부족합니다. 해골을 쓰러뜨리거나 물건을 팔아 모으세요.</color>"
                    : "<color=#ff7070>재료가 부족합니다. 해골을 더 쓰러뜨리세요.</color>";
                return;
            }
            string warning = RiskWarning(cost);
            if (warning == null)
            {
                StartAttempt(e);
                return;
            }
            // The confirm is drawn over this window (it stays visible, dimmed, and takes no input meanwhile).
            Game.Audio.PlaySfx("select");
            Game.UI.Confirm(ConfirmText(e.key, cost, warning), () => StartAttempt(e), true);
        }

    }
}
