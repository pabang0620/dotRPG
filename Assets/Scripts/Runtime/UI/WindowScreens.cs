using System;
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
            var bg = UIFactory.Overlay(root, "Bg", new Color32(34, 52, 76, 255));
            bg.raycastTarget = true;
            var header = Img(root, "Header", "ui_white", new Color32(22, 31, 46, 255));
            UIFactory.Place(header.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(4000f, 76f));
            Button(root, "Back", "◀", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(22f, -12f), new Vector2(56f, 52f), w.Close, 26);
            if (!string.IsNullOrEmpty(icon))
            {
                var ic = UIFactory.Image(root, "Icon", Game.Art.Get(icon), Color.white);
                UIFactory.Place(ic.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(92f, -14f), new Vector2(48f, 48f));
            }
            var t = UIFactory.Text(root, "Title", title, 40, Color.white, TextAnchor.MiddleLeft, true);
            UIFactory.Place(t.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(150f, -12f), new Vector2(600f, 52f));
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

        public void Close() => Game.Flow.CloseInventory();

        protected static Image Img(Transform parent, string name, string sprite, Color color)
        {
            var img = UIFactory.Image(parent, name, Game.Art.Get(sprite), color);
            img.preserveAspect = false;
            return img;
        }

        protected static Image Panel(Transform parent, string name, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, Color color)
        {
            var img = Img(parent, name, "ui_white", color);
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
            var text = UIFactory.Text(img.transform, "Text", label, font, Color.white, TextAnchor.MiddleCenter, true);
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

        protected virtual void Update()
        {
            if (Time.frameCount == shownFrame || Game.State.ChangedThisFrame) return;
            var input = Game.Input;
            if (input.InventoryPressed || input.CancelPressed)
            {
                Game.Audio.PlaySfx("cancel");
                Close();
            }
        }
    }

    // =====================================================================================

    /// <summary>Big map of the current region with the player, exits and a region list.</summary>
    public class WorldMapScreen : WindowScreen
    {
        RawImage map;
        RectTransform mapRect, playerDot;
        readonly List<RectTransform> exits = new List<RectTransform>();
        Text regions;

        public static WorldMapScreen Create(Transform canvas)
        {
            var w = CreateWindow<WorldMapScreen>(canvas, "WorldMap", "지도", "menuicon_map");
            var frame = Panel(w.content, "Frame", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(760f, 590f), new Color32(18, 26, 40, 255));
            w.map = new GameObject("Map", typeof(RectTransform)).AddComponent<RawImage>();
            w.map.transform.SetParent(frame.transform, false);
            w.mapRect = w.map.rectTransform;
            w.mapRect.anchorMin = w.mapRect.anchorMax = new Vector2(0.5f, 0.5f);
            var dot = UIFactory.Image(w.mapRect, "Player", Game.Art.Get("ui_dot"), new Color32(120, 220, 255, 255));
            w.playerDot = dot.rectTransform;
            w.playerDot.sizeDelta = new Vector2(18f, 18f);
            var side = Panel(w.content, "Side", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(420f, 590f), new Color32(24, 36, 54, 235));
            w.regions = Label(side.transform, "Regions", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -16f), new Vector2(380f, 560f));
            return w;
        }

        protected override void Refresh()
        {
            var world = Game.World;
            if (world == null || world.Minimap == null) return;
            map.texture = world.Minimap;
            float scale = Mathf.Min(740f / world.Bounds.width, 570f / world.Bounds.height);
            mapRect.sizeDelta = new Vector2(world.Bounds.width * scale, world.Bounds.height * scale);
            Vector2 Place(Vector2 p) => new Vector2((p.x - world.Bounds.width * 0.5f) * scale, (p.y - world.Bounds.height * 0.5f) * scale);
            playerDot.anchoredPosition = Place(Game.Player.Position);
            playerDot.SetAsLastSibling();
            foreach (var e in exits) Destroy(e.gameObject);
            exits.Clear();
            foreach (var p in world.PortalPoints)
            {
                var d = UIFactory.Image(mapRect, "Exit", Game.Art.Get("ui_dot"), new Color32(255, 211, 74, 255)).rectTransform;
                d.sizeDelta = new Vector2(12f, 12f);
                d.anchoredPosition = Place(p);
                exits.Add(d);
            }
            var sb = new StringBuilder("<size=26><b>지역</b></size>\n\n");
            foreach (var m in MapRegistry.All)
            {
                bool here = world.MapId == m.id;
                // The current map's line keeps only the location marker, so it never wraps; the others show safe / monster.
                string tag = m.safe ? "  <color=#8fe28f>(안전 지역)</color>" : "  <color=#ff9f7a>(몬스터 출현)</color>";
                sb.Append(here ? $"<color=#ffe066>▶ {m.displayName}  (현재 위치)</color>\n" : $"<color=#00000000>▶</color> {m.displayName}{tag}\n");
            }
            sb.Append("<color=#00000000>▶</color> <color=#8c96a8>눈 덮인 봉우리  (잠김)</color>\n\n");
            sb.Append((world.Map?.hint ?? "") + "\n\n");
            sb.Append("<color=#78dcff>●</color> 나    <color=#ffd34a>●</color> 출구\n");
            regions.text = sb.ToString();
        }
    }

    // =====================================================================================

    /// <summary>Quest log: current main quest, objectives and rewards.</summary>
    public class QuestScreen : WindowScreen
    {
        Text body;

        public static QuestScreen Create(Transform canvas)
        {
            var w = CreateWindow<QuestScreen>(canvas, "QuestLog", "퀘스트", "menuicon_quest");
            var panel = Panel(w.content, "Panel", new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1000f, 580f), new Color32(24, 36, 54, 235));
            w.body = Label(panel.transform, "Body", "", 22, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -24f), new Vector2(940f, 540f));
            return w;
        }

        protected override void Refresh()
        {
            var q = Game.Quest;
            string stage = q.Stage == QuestStage.NotStarted ? "<color=#ff9f43>수락 전</color>"
                : q.Stage == QuestStage.Completed ? "<color=#8fe28f>완료</color>" : "<color=#78dcff>진행 중</color>";
            var sb = new StringBuilder();
            sb.Append($"<size=30><b>[메인] {q.Config.title}</b></size>   {stage}\n\n");
            sb.Append("<color=#b8c4d8>의뢰인: 촌장 모리 (해골 숲 옆 작은 마을)</color>\n\n");
            sb.Append("<b>목표</b>\n");
            foreach (var o in q.GetObjectives()) sb.Append(o.done ? $"<color=#8fe28f>● {o.text}</color>\n" : $"○ {o.text}\n");
            sb.Append($"\n<b>보상</b>\n최대 체력 +{EquipmentDatabase.Hearts(q.Config.rewardMaxHealth)}   ·   <color={EquipmentDatabase.RarityColor(ItemRarity.Unique)}>[유니크] 루비 반지</color>\n\n");
            sb.Append("<b>도움말</b>\n나무와 바위를 캐서 목재·돌을 모으고, 동쪽 큰길 옆 공사장에 전달하자.\n해골은 마을 동쪽 큰길 끝의 사냥터 '해골 숲'에 있다. 쓰러뜨리면 골드와 장비, 강화 재료를 얻는다.");
            body.text = sb.ToString();
        }
    }

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
    /// Blacksmith (opened at an anvil): pick a piece of gear, pay monster materials and try to raise
    /// it to the next +level. Failure keeps the level but uses up the materials.
    /// </summary>
    public class EnhanceScreen : WindowScreen
    {
        const int Cols = 5, RowsN = 4;
        const float Cell = 92f, Gap = 8f;

        sealed class Cell_
        {
            public Image bg, icon, frame;
            public Text level;
            public string id;
        }

        readonly List<Cell_> cells = new List<Cell_>();
        Image cursor, bigIcon;
        Text title, statsText, costText, chanceText, resultText;
        Button enhanceButton;
        Text enhanceLabel;
        int selected;
        bool dirty;

        public static EnhanceScreen Create(Transform canvas)
        {
            var w = CreateWindow<EnhanceScreen>(canvas, "Enhance", "대장간 · 장비 강화", "anvil");
            float gridW = Cols * Cell + (Cols - 1) * Gap;
            var left = Panel(w.content, "Left", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(gridW + 40f, 590f), new Color32(24, 36, 54, 235));
            Label(left.transform, "Hint", "강화할 장비를 고르세요 (착용 중 + 가방)", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(500f, 30f));
            for (int i = 0; i < Cols * RowsN; i++)
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
                c.level = UIFactory.Text(c.bg.transform, "Level", "", 20, new Color32(255, 224, 102, 255), TextAnchor.UpperRight, true);
                UIFactory.Stretch(c.level.rectTransform, 4f, 2f, 6f, 2f);
                var relay = c.bg.gameObject.AddComponent<PointerRelay>();
                relay.onClick = _ => { w.selected = index; w.dirty = true; Game.Audio.PlaySfx("select"); };
                w.cells.Add(c);
            }
            w.cursor = Img(left.transform, "Cursor", "ui_frame", new Color32(255, 211, 74, 255));

            var right = Panel(w.content, "Right", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(660f, 590f), new Color32(24, 36, 54, 235));
            var iconBg = Img(right.transform, "IconBg", "ui_slotblue", Color.white);
            UIFactory.Place(iconBg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -24f), new Vector2(128f, 128f));
            w.bigIcon = UIFactory.SharpIcon(iconBg.transform, "Icon", Color.white);
            UIFactory.Place(w.bigIcon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(96f, 96f));
            w.title = Label(right.transform, "Title", "", 26, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(172f, -24f), new Vector2(470f, 128f));
            w.statsText = Label(right.transform, "Stats", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -160f), new Vector2(610f, 140f));
            w.costText = Label(right.transform, "Cost", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -296f), new Vector2(610f, 120f));
            w.chanceText = Label(right.transform, "Chance", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -432f), new Vector2(610f, 30f));
            w.resultText = Label(right.transform, "Result", "", 22, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 88f), new Vector2(620f, 30f), TextAnchor.MiddleCenter);
            w.enhanceButton = Button(right.transform, "Go", "강화", "ui_btn", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(300f, 64f), w.TryEnhance, 30);
            w.enhanceLabel = w.enhanceButton.GetComponentInChildren<Text>();
            return w;
        }

        List<string> Owned()
        {
            var eq = Game.Session.Equipment;
            var bag = Game.Session.Inventory;
            var ids = new List<string>();
            for (int i = 0; i < Equipment.SlotCount; i++)
            {
                string id = eq[(EquipSlot)i];
                if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);
            }
            foreach (var item in EquipmentDatabase.All)
                if (bag.Count(item.id) > 0 && !ids.Contains(item.id)) ids.Add(item.id);
            return ids;
        }

        public override void Show()
        {
            resultText.text = "";
            base.Show();
        }

        protected override void Refresh()
        {
            var eq = Game.Session.Equipment;
            var ids = Owned();
            selected = Mathf.Clamp(selected, 0, Mathf.Max(0, ids.Count - 1));
            for (int i = 0; i < cells.Count; i++)
            {
                var c = cells[i];
                c.id = i < ids.Count ? ids[i] : null;
                var item = EquipmentDatabase.Get(c.id);
                c.icon.enabled = item != null;
                if (item != null) c.icon.sprite = Game.Art.Get(item.iconKey);
                c.frame.color = item != null ? EquipmentDatabase.RarityTint(item.rarity) : Color.clear;
                int lv = eq.LevelOf(c.id);
                c.level.text = lv > 0 ? $"+{lv}" : "";
            }
            var sel = cells[selected];
            cursor.rectTransform.anchorMin = cursor.rectTransform.anchorMax = sel.bg.rectTransform.anchorMin;
            cursor.rectTransform.pivot = new Vector2(0f, 1f);
            cursor.rectTransform.anchoredPosition = sel.bg.rectTransform.anchoredPosition + new Vector2(-5f, 5f);
            cursor.rectTransform.sizeDelta = new Vector2(Cell + 10f, Cell + 10f);
            cursor.transform.SetAsLastSibling();

            var gear = EquipmentDatabase.Get(sel.id);
            if (gear == null)
            {
                bigIcon.enabled = false;
                title.text = "강화할 장비가 없다.";
                statsText.text = costText.text = chanceText.text = "";
                enhanceButton.interactable = false;
                dirty = false;
                return;
            }
            int level = eq.LevelOf(gear.id);
            bigIcon.enabled = true;
            bigIcon.sprite = Game.Art.Get(gear.iconKey);
            string color = EquipmentDatabase.RarityColor(gear.rarity);
            title.text = $"<color={color}><b>{gear.NameAt(level)}</b></color>\n<color=#b8c4d8>[{EquipmentDatabase.RarityName(gear.rarity)}] {gear.CategoryName}</color>\n" +
                         (level >= EquipmentDatabase.MaxEnhance ? "<color=#ffe066>최대 강화 달성!</color>" : $"+{level}  →  <color=#ffe066>+{level + 1}</color>");
            if (level >= EquipmentDatabase.MaxEnhance)
            {
                statsText.text = $"<b>현재 능력치</b>\n{gear.StatLine(level)}";
                costText.text = chanceText.text = "";
                enhanceButton.interactable = false;
                enhanceLabel.text = "최대";
                dirty = false;
                return;
            }
            statsText.text = $"<b>능력치 변화</b>\n현재<color=#00000000> 후</color>  {gear.StatLine(level)}\n강화 후  <color=#8fe28f>{gear.StatLine(level + 1)}</color>\n" +
                             $"<color=#b8c4d8>전투력 {gear.StatsAt(level).Score:N0} → {gear.StatsAt(level + 1).Score:N0}</color>";
            var cost = EquipmentDatabase.CostFor(gear, level);
            var bag = Game.Session.Inventory;
            string Need(string id, int n)
            {
                if (n <= 0) return "";
                int have = bag.Count(id);
                string name = EquipmentDatabase.GetMaterial(id).name;
                return $"{name}  {(have >= n ? "<color=#8fe28f>" : "<color=#ff7070>")}{have}</color> / {n}\n";
            }
            costText.text = "<b>필요 재료</b>\n" + Need("mat_bone", cost.bone) + Need("mat_ore", cost.ore) + Need("mat_essence", cost.essence);
            chanceText.text = $"성공 확률  <color=#ffe066>{cost.successPercent}%</color>   <color=#8c96a8>(실패 시 강화 수치는 유지, 재료만 소모)</color>";
            bool can = bag.Count("mat_bone") >= cost.bone && bag.Count("mat_ore") >= cost.ore && bag.Count("mat_essence") >= cost.essence;
            enhanceButton.interactable = can;
            enhanceLabel.text = can ? "강화" : "재료 부족";
            dirty = false;
        }

        void TryEnhance()
        {
            var gear = EquipmentDatabase.Get(cells[selected].id);
            if (gear == null) return;
            var eq = Game.Session.Equipment;
            int level = eq.LevelOf(gear.id);
            if (level >= EquipmentDatabase.MaxEnhance) return;
            var cost = EquipmentDatabase.CostFor(gear, level);
            var bag = Game.Session.Inventory;
            if (bag.Count("mat_bone") < cost.bone || bag.Count("mat_ore") < cost.ore || bag.Count("mat_essence") < cost.essence)
            {
                Game.Audio.PlaySfx("cancel");
                resultText.text = "<color=#ff7070>재료가 부족하다. 해골을 더 쓰러뜨리자.</color>";
                return;
            }
            bag.Remove("mat_bone", cost.bone);
            bag.Remove("mat_ore", cost.ore);
            bag.Remove("mat_essence", cost.essence);
            bool success = UnityEngine.Random.Range(0, 100) < cost.successPercent;
            if (success)
            {
                eq.SetLevel(gear.id, level + 1);
                Game.Audio.PlaySfx("build_complete");
                resultText.text = $"<color=#8fe28f>강화 성공!  {gear.NameAt(level + 1)}</color>";
                GameEvents.RaiseToast($"강화 성공! {gear.NameAt(level + 1)}");
            }
            else
            {
                Game.Audio.PlaySfx("hurt");
                resultText.text = "<color=#ff7070>강화 실패… 재료가 사라졌다.</color>";
            }
            Game.Flow.Autosave();
            dirty = true;
        }

        protected override void Update()
        {
            base.Update();
            if (!gameObject.activeSelf) return;
            if (Time.frameCount != shownFrame && !Game.State.ChangedThisFrame)
            {
                var nav = Game.Input.NavigateStep;
                if (nav != Vector2Int.zero)
                {
                    int count = Mathf.Max(1, Owned().Count);
                    int next = selected + nav.x - nav.y * Cols;
                    selected = Mathf.Clamp(next, 0, count - 1);
                    Game.Audio.PlaySfx("select", 0.5f);
                    dirty = true;
                }
                if (Game.Input.SubmitPressed) TryEnhance();
            }
            if (dirty) Refresh();
        }
    }
}
