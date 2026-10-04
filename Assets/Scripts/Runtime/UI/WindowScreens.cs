using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Full-screen window in the bag's style: blue background, header with back button + title.
    /// Esc / Cancel / the bag key close it and return to the game.
    /// </summary>
    public abstract class WindowScreen : MenuScreen
    {
        protected RectTransform content;

        protected static T CreateWindow<T>(Transform canvas, string name, string title, string icon) where T : WindowScreen
        {
            var root = CreateRoot(canvas, name, false);
            var w = root.gameObject.AddComponent<T>();
            // [UI] Colours / sizes from UiTheme; the back button names its key (Esc) so keyboard and pad
            // players see how to close every window, and a thin accent line separates header and content.
            var bg = UIFactory.Overlay(root, "Bg", UiTheme.Background);
            bg.raycastTarget = true;
            var header = Img(root, "Header", "ui_header", Color.white); // [UI] wooden header strip (9-slice)
            UIFactory.Place(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(4000f, UiTheme.HeaderHeight));
            var headerLine = Img(root, "HeaderLine", "ui_white", new Color(UiTheme.Accent.r, UiTheme.Accent.g, UiTheme.Accent.b, 0.35f));
            UIFactory.Place(headerLine.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -UiTheme.HeaderHeight), new Vector2(4000f, 2f));
            var back = Button(root, "Back", "◀", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -12f), new Vector2(96f, 52f), w.Close, 24);
            var backText = back.GetComponentInChildren<Text>();
            backText.text = "◀ <size=17>Esc</size>";
            if (!string.IsNullOrEmpty(icon))
            {
                var ic = UIFactory.Image(root, "Icon", Game.Art.Get(icon), Color.white);
                UIFactory.Place(ic.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(134f, -14f), new Vector2(48f, 48f));
            }
            var t = UIFactory.Text(root, "Title", title, UiTheme.FontTitle, Color.white, TextAnchor.MiddleLeft, true);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(192f, -12f), new Vector2(560f, 52f));
            w.keeperLine = UIFactory.Text(root, "Keeper", "", 19, new Color32(246, 231, 200, 255), TextAnchor.MiddleRight, true);
            UIFactory.Place(w.keeperLine.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-28f, -12f), new Vector2(760f, 52f));
            w.content = UIFactory.Stretch(UIFactory.Rect(root, "Content"), 30f, 30f, 30f, 96f);
            return w;
        }

        Text keeperLine;

        /// <summary>Shows who runs this window and what they say (empty = nothing).</summary>
        public void SetKeeper(string npcName, string line)
        {
            if (keeperLine == null) return;
            keeperLine.text = string.IsNullOrEmpty(npcName) ? "" : $"<color=#ffe066>{npcName}</color>  “{line}”";
        }

        public virtual void Close() => Game.Flow.CloseInventory();

        protected static Image Img(Transform parent, string name, string sprite, Color color)
        {
            var img = UIFactory.Image(parent, name, Game.Art.Get(sprite), color);
            img.preserveAspect = false;
            return img;
        }

        protected static Image Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            // [UI] Dark content panels get the framed dark panel (9-slice); light or translucent fills stay flat.
            float lum = color.r * 0.3f + color.g * 0.59f + color.b * 0.11f;
            bool framed = color.a > 0.8f && lum < 0.3f && size.x >= 80f && size.y >= 60f;
            var img = framed ? Img(parent, name, "ui_dark", new Color(1f, 1f, 1f, color.a)) : Img(parent, name, "ui_white", color);
            UIFactory.Place(img.rectTransform, anchor, pivot, pos, size);
            return img;
        }

        protected static Button Button(Transform parent, string name, string label, string sprite, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Action onClick, int font)
        {
            var img = Img(parent, name, sprite, Color.white);
            img.raycastTarget = true;
            UIFactory.Place(img.rectTransform, anchor, pivot, pos, size);
            var button = img.gameObject.AddComponent<Button>();
            button.targetGraphic = img;
            button.onClick.AddListener(() => onClick());
            UiButton.Attach(button); // [UI] hover / press / disabled feedback + hover sound
            var text = UIFactory.Text(img.transform, "Text", label, UiTheme.Size(font), Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(text.rectTransform);
            return button;
        }

        protected static Text Label(Transform parent, string name, string text, int size, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 box, TextAnchor align = TextAnchor.UpperLeft)
        {
            var t = UIFactory.Text(parent, name, text, size, Color.white, align, true);
            t.lineSpacing = 1.2f;
            UIFactory.Place(t.rectTransform, anchor, pivot, pos, box);
            return t;
        }

        public override void Show()
        {
            base.Show();
            Refresh();
        }

        protected abstract void Refresh();

        /// <summary>False while a dialog is drawn over this window (it then takes no input at all).</summary>
        protected bool IsTop => Game.UI == null || Game.UI.Top == this;

        /// <summary>True when this frame's keys belong to this window (on top, not the frame it was shown or the state changed).</summary>
        protected bool TakesInput => IsTop && Time.frameCount != shownFrame && !Game.State.ChangedThisFrame;

        protected virtual void Update()
        {
            if (!TakesInput) return;
            var input = Game.Input;
            if (input.InventoryPressed || input.CancelPressed || (input.MapPressed && this is WorldMapScreen))
            {
                Game.Audio.PlaySfx("cancel");
                Close();
            }
        }
    }

    // =====================================================================================

    // =====================================================================================

    /// <summary>Mini-dungeon / raid information window (content not open yet).</summary>
    public class ContentScreen : WindowScreen
    {
        Text status;

        public static ContentScreen Create(Transform canvas, string title, string icon, string desc, string req, string reward)
        {
            var w = CreateWindow<ContentScreen>(canvas, "Content_" + title, title, icon);
            var panel = Panel(w.content, "Panel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1000f, 580f), new Color32(24, 36, 54, 235));
            var big = UIFactory.Image(panel.transform, "Art", Game.Art.Get(icon), Color.white);
            UIFactory.Place(big.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(40f, -40f), new Vector2(200f, 200f));
            Label(panel.transform, "Desc", $"<size=30><b>{title}</b></size>\n\n{desc}\n\n<color=#b8c4d8>{req}</color>\n<color=#ffe066>{reward}</color>", 22,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(280f, -40f), new Vector2(680f, 380f));
            var enter = Button(panel.transform, "Enter", "입장", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(260f, 64f),
                () => { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast($"{title}은(는) 아직 준비 중이다."); }, 28);
            w.status = Label(panel.transform, "Status", "<color=#ff9f43>준비 중 — 다음 업데이트에서 열립니다</color>", 20,
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 122f), new Vector2(600f, 30f), TextAnchor.MiddleCenter);
            return w;
        }

        protected override void Refresh() { }
    }

    // =====================================================================================

    /// <summary>
    /// Blacksmith (anvil or 대장장이): pick a piece of gear — worn slots first, then the bag — pay gold and
    /// monster materials and try to raise it one +level, Dungeon&amp;Fighter style: the chance falls from 100%
    /// to 10%, a weapon failing from +10 / +11 drops 3 levels, and from +12 (other gear +10) a failure
    /// destroys the piece unless a protection ticket in the bag saves it at +0. Risky attempts ask first,
    /// then the hammer falls twice (unscaled time: windows pause the game). Rules: <see cref="EnhanceRules"/>.
    /// </summary>
    public class EnhanceScreen : WindowScreen
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
            var w = CreateWindow<EnhanceScreen>(canvas, "Enhance", "대장간 · 장비 강화", "anvil");
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
            base.Show();
        }

        public override void Hide()
        {
            if (busy)
            {
                // Closed mid-swing (e.g. a state change): the attempt is called off. Nothing is paid before the hammer lands.
                StopAllCoroutines();
                busy = false;
                devRoll = null;
                bigIcon.rectTransform.anchoredPosition = Vector2.zero;
            }
            base.Hide();
        }

        /// <summary>The window stays open until the hammer lands.</summary>
        public override void Close()
        {
            if (!busy) base.Close();
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
            dirty = false;
        }

        void ShowNothing()
        {
            bigIcon.enabled = false;
            bigFrame.color = Color.clear;
            title.text = "강화할 장비가 없다.";
            statsText.text = costTitle.text = chanceText.text = failText.text = "";
            foreach (var t in costCells) t.text = "";
            enhanceButton.interactable = false;
            enhanceLabel.text = "강화";
        }

        void ShowDetails(Entry e)
        {
            var eq = Game.Session.Equipment;
            var bag = Game.Session.Inventory;
            var gear = EquipmentDatabase.Get(e.key);
            int level = EquipmentDatabase.LevelOfKey(e.key);
            bool max = level >= EquipmentDatabase.MaxEnhance;
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
                enhanceLabel.text = "최대";
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

        const string NoStatChange = "<color=#8c96a8>이번 단계는 능력치 변화가 없다 (높은 단계일수록 크게 오른다)</color>";
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
                        ? "실패 시: <color=#ff5050><b>장비 파괴!</b></color>  <color=#8c96a8>(기본 장비에는 보호권을 쓰지 않는다)</color>"
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
            if (busy || entries.Count == 0 || !IsTop) return;
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
                    ? "<color=#ff7070>골드가 부족하다. 해골을 쓰러뜨리거나 물건을 팔아 모으자.</color>"
                    : "<color=#ff7070>재료가 부족하다. 해골을 더 쓰러뜨리자.</color>";
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

        void StartAttempt(Entry e)
        {
            if (busy || !gameObject.activeInHierarchy) return;
            StartCoroutine(AttemptRoutine(e));
        }

        IEnumerator AttemptRoutine(Entry e)
        {
            busy = true;
            busyTime = 0f;
            resultText.text = "<color=#b8c4d8>망치질 중…</color>";
            Refresh();
            // [SERVER] Online the server rolls; ask now so the answer arrives during the hammer strikes.
            bool online = OnlineEconomy.On, answered = false;
            Dictionary<string, object> server = null;
            if (online)
                OnlineEconomy.Enhance(e.slot.HasValue ? (int)e.slot.Value : (int?)null, e.slot.HasValue ? null : e.key,
                    d => { server = d; answered = true; });
            // [A] DNF-style suspense: three hammer strikes over a rising charge, the glow grows around the item.
            Game.Audio.PlaySfx("enhance_charge");
            EnhanceFx.Charge(bigIcon.rectTransform, SuspenseSeconds * 1.5f);
            for (int i = 0; i < 3; i++)
            {
                Game.Audio.PlaySfx("hammer");
                EnhanceFx.Sparks(bigIcon.rectTransform, 6, new Color32(255, 220, 120, 255));
                yield return new WaitForSecondsRealtime(SuspenseSeconds * 0.5f);
            }
            float waited = 0f;
            while (online && !answered && waited < 15f) { waited += Time.unscaledDeltaTime; yield return null; }
            EnhanceResult result;
            try
            {
                if (online) result = FromServer(e, server);
                else
                {
                    int roll = devRoll ?? Authority.Current.EnhanceRoll();
                    devRoll = null;
                    result = Game.Session.Equipment.TryEnhance(e.Target, roll, Class);
                }
            }
            finally
            {
                // Even if the attempt (or a Changed handler) throws, the window must not stay locked.
                busy = false;
                bigIcon.rectTransform.anchoredPosition = Vector2.zero;
            }
            ShowResult(result);
            Follow(e, result);
            if (result.kind == EnhanceOutcome.Destroyed) pickRequired = true;
            if (result.Attempted) Game.Flow.Autosave();
            Refresh();
        }

        /// <summary>[SERVER] The server's answer (its delta is already applied) as the local result shape.</summary>
        static EnhanceResult FromServer(Entry e, Dictionary<string, object> d)
        {
            if (d == null) return new EnhanceResult { kind = EnhanceOutcome.Invalid, oldKey = e.key, slot = e.slot };
            EnhanceOutcome kind;
            switch (MiniJson.Str(d, "outcome"))
            {
                case "success": kind = EnhanceOutcome.Success; break;
                case "keep": kind = EnhanceOutcome.Keep; break;
                case "drop3": kind = EnhanceOutcome.Drop3; break;
                case "destroyed": kind = EnhanceOutcome.Destroyed; break;
                case "protected": kind = EnhanceOutcome.Protected; break;
                default: kind = EnhanceOutcome.Invalid; break;
            }
            string oldKey = MiniJson.Str(d, "old_key", e.key);
            Game.Session.Equipment.SetPityFromServer(oldKey, MiniJson.Int(d, "pity"));
            return new EnhanceResult
            {
                kind = kind,
                oldKey = oldKey,
                newKey = MiniJson.Str(d, "new_key"),
                oldLevel = MiniJson.Int(d, "old_level"),
                newLevel = MiniJson.Int(d, "new_level"),
                slot = e.slot,
                roll = MiniJson.Int(d, "roll"),
            };
        }

        void ShowResult(EnhanceResult r)
        {
            switch (r.kind)
            {
                case EnhanceOutcome.Success:
                    bool great = EquipmentDatabase.LevelOfKey(r.newKey) >= 10;
                    Game.Audio.PlaySfx(great ? "enhance_great" : "enhance_success");
                    EnhanceFx.Burst(bigIcon.rectTransform, great ? 40 : 22, new Color32(255, 230, 120, 255), great);
                    if (great) GameEvents.RaiseToast($"<color=#ffd84a>[알림]</color> {Game.Session.Journal.PlayerName}님이 {EquipmentDatabase.NameOfKey(r.newKey)} 강화에 성공했습니다!");
                    resultText.text = $"<color=#8fe28f><b>강화 성공!</b></color>  {EquipmentDatabase.RichName(r.newKey)}";
                    GameEvents.RaiseToast($"강화 성공! {EquipmentDatabase.RichName(r.newKey)}");
                    break;
                case EnhanceOutcome.Keep:
                    Game.Audio.PlaySfx("enhance_fail");
                    EnhanceFx.Fail(bigIcon.rectTransform, false);
                    resultText.text = "<color=#ffb070>강화 실패… 강화 수치는 그대로다.</color>";
                    break;
                case EnhanceOutcome.Drop3:
                    Game.Audio.PlaySfx("enhance_fail");
                    EnhanceFx.Fail(bigIcon.rectTransform, false);
                    resultText.text = $"<color=#ff9f43>강화 실패… 강화 수치가 3 떨어졌다.  (+{r.oldLevel} → +{r.newLevel})</color>";
                    break;
                case EnhanceOutcome.Destroyed:
                    Game.Audio.PlaySfx("enhance_break");
                    EnhanceFx.Fail(bigIcon.rectTransform, true);
                    resultText.text = $"<color=#ff5050><b>강화 실패… 장비가 파괴되었다!</b></color>\n<color=#ff8080>{EquipmentDatabase.NameOfKey(r.oldKey)}</color>";
                    GameEvents.RaiseToast($"<color=#ff5050>장비 파괴: {EquipmentDatabase.NameOfKey(r.oldKey)}</color>");
                    break;
                case EnhanceOutcome.Protected:
                    Game.Audio.PlaySfx("confirm");
                    resultText.text = $"<color=#ffd84a>강화 실패… 장비 보호권이 장비를 지켰다.</color>\n<color=#b8c4d8>{EquipmentDatabase.NameOfKey(r.newKey)} (+0으로 초기화)</color>";
                    break;
                case EnhanceOutcome.NotEnough:
                    Game.Audio.PlaySfx("cancel");
                    resultText.text = "<color=#ff7070>골드나 재료가 부족하다.</color>";
                    break;
                default:
                    Game.Audio.PlaySfx("cancel");
                    resultText.text = "";
                    break;
            }
        }

        /// <summary>Keeps the cursor on the same piece after an attempt (same slot, or the bag key it became).</summary>
        void Follow(Entry e, EnhanceResult r)
        {
            BuildEntries();
            for (int i = 0; i < entries.Count; i++)
            {
                var x = entries[i];
                bool same = e.slot.HasValue ? x.slot == e.slot : !x.slot.HasValue && x.key == r.newKey;
                if (!same) continue;
                selected = i;
                return;
            }
        }

        protected override void Update()
        {
            if (busy)
            {
                // No input while the hammer falls; the piece shakes a little.
                busyTime += Time.unscaledDeltaTime;
                bigIcon.rectTransform.anchoredPosition = new Vector2(Mathf.Sin(busyTime * 70f) * 3f, 0f);
                if (dirty) Refresh();
                return;
            }
            base.Update();
            if (!gameObject.activeSelf) return;
            if (TakesInput)
            {
                var nav = Game.Input.NavigateStep;
                if (nav != Vector2Int.zero && entries.Count > 0)
                {
                    // One continuous list: moving past the edge of a page turns the page.
                    selected = Mathf.Clamp(selected + nav.x - nav.y * Cols, 0, entries.Count - 1);
                    Game.Audio.PlaySfx("select", 0.5f);
                    Picked();
                }
                if (Game.Input.SubmitPressed) OnEnhancePressed();
            }
            if (dirty) Refresh();
        }

        // ---------- Developer automation (DevCapture) ----------

        /// <summary>Selects the first entry holding this key (worn or bag). False when it is not listed.</summary>
        public bool DevSelect(string key)
        {
            BuildEntries();
            int i = entries.FindIndex(x => x.key == key);
            if (i < 0) return false;
            selected = i;
            Picked();
            Refresh();
            return true;
        }

        /// <summary>Selects a worn slot's entry. False when that slot is empty.</summary>
        public bool DevSelectSlot(EquipSlot slot)
        {
            BuildEntries();
            int i = entries.FindIndex(x => x.slot == slot);
            if (i < 0) return false;
            selected = i;
            Picked();
            Refresh();
            return true;
        }

        /// <summary>Presses the enhance button (risky attempts open the confirm dialog first).</summary>
        public void DevPress() => OnEnhancePressed();
        /// <summary>The next attempt uses this 0..99 roll instead of a random one (cleared when used).</summary>
        public void DevForceRoll(int roll) => devRoll = roll;
        int? devRoll;
        public bool DevBusy => busy;
        public bool DevPickRequired => pickRequired;
        public string DevSelectedKey => entries.Count > 0 && selected < entries.Count ? entries[selected].key : null;
        public int DevSelectedIndex => selected;
        public string DevStats => statsText.text;
        public string DevResult => resultText.text;
        public string DevFailLine => failText.text;
        public string DevPage => $"{selected / PerPage + 1} / {PageCount}";
        public int DevEntries => entries.Count;
    }
}
