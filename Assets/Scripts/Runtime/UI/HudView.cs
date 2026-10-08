using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// In-game HUD: item counts, quest tracker, context prompt ("[E] 대화하기"),
    /// control hints and toast messages. Purely reactive: it listens to events and polls
    /// the interactor, never changes game state.
    /// </summary>
    public partial class HudView : MonoBehaviour
    {
        readonly Dictionary<string, Text> itemCounts = new Dictionary<string, Text>();
        readonly Dictionary<string, float> itemPulse = new Dictionary<string, float>();
        Text questTitle;
        Text questBody;
        Text autoLabel, autoStatus, huntLabel; // [QUEST] auto-progress toggle under the tracker
        Text autoKey, huntKey; // [AUTO] hotkey badges (InputReader AutoQuest / AutoHunt)
        string autoKeyShown, huntKeyShown;
        Button huntBtn;
        RectTransform questPanel;
        // [CONTENT] for capture checks
        public bool DevQuestVisible => questPanel != null && questPanel.gameObject.activeInHierarchy;
        RectTransform prompt;
        Text promptText;
        Image promptPad; // [UI] gamepad: the A button picture instead of "[A]"
        Text controlsHint;
        Text mobilityHint;
        Image controlsPlate; // [UI]
        bool lastGamepad;

        RectTransform toastRoot;
        readonly List<Toast> toasts = new List<Toast>();

        class Toast
        {
            public RectTransform root; // [UI] plate + text
            public Image plate;
            public Text text;
            public string message;
            public int count;
            public float bornAt;
        }

        const float ToastLife = 2.4f;

        bool built;

        public static HudView Create(Transform canvas)
        {
            var root = UIFactory.Stretch(UIFactory.Rect(canvas, "HUD"));
            root.gameObject.AddComponent<SafeAreaFitter>(); // [TOUCH] keep the HUD out of notches and rounded corners
            var hud = root.gameObject.AddComponent<HudView>();
            hud.Build(root);
            hud.built = true;
            hud.RefreshAll();
            return hud;
        }

        void Build(RectTransform root)
        {
            // Level + HP / MP / EXP bars (top-left) and the skill bar (bottom-centre).
            StatusBarsView.Create(root);
            BuffBarView.Create(root); // [UI] buffs and debuffs under the currency line
            var skillBar = SkillBarView.Create(root);
            if (TouchUi.Enabled) skillBar.gameObject.SetActive(false); // [TOUCH] the touch skill buttons take its place (UI/TouchControls.cs)
            AwakeningBanner.Create(root);
            AwakeningCutIn.Create(root); // class illustration slides in at the bottom-left on an awakening skill

            // Items under the bars (gold first).
            var items = UIFactory.Place(UIFactory.Rect(root, "Items"), new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -104), new Vector2(380, 40));
            var bg = UIFactory.Panel(items, "Bg", true);
            UIFactory.Stretch(bg.rectTransform);
            // [UI] Compact steps keep the bar left of the centred tip (UiTheme.HudCurrencyMaxRight): gold + 3 items end at
            // 12 + 128 + 3 * 72 = 356, so the frame is 364 wide (18..382) and the last count cell (ends 354) stays inside it.
            float x = 12f;
            var shown = new List<(string id, string icon)> { (ConsumableDatabase.Gold, "icon_gold") };
            foreach (var def in Game.Config.items) shown.Add((def.id, def.iconKey));
            foreach (var (id, iconKey) in shown)
            {
                bool gold = id == ConsumableDatabase.Gold;
                var icon = UIFactory.Image(items, "Icon_" + id, Game.Art.Get(iconKey), Color.white);
                UIFactory.Place(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(24, 24));
                var count = UIFactory.Text(items, "Count_" + id, "0", 20, gold ? (Color)new Color32(255, 216, 74, 255) : UIColors.Cream, TextAnchor.MiddleLeft, true);
                count.horizontalOverflow = HorizontalWrapMode.Overflow; // a long number never breaks onto a second line
                UIFactory.Place(count.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 27, 0), new Vector2(gold ? 98 : 43, 34));
                itemCounts[id] = count;
                x += gold ? 128f : 72f; // [UI] room for the wider pixel-font digits
            }
            items.sizeDelta = new Vector2(Mathf.Min(x + 8f, UiTheme.HudCurrencyMaxRight - 18f), 44); // [UI] the last count stays inside the rim
            var quickBar = QuickItemBar.Create(root);
            if (TouchUi.Enabled) quickBar.gameObject.SetActive(false); // [TOUCH] touch potion buttons

            // Round minimap (top-right) with the quest tracker underneath.
            SideMenuView.Create(root);
            PartyFramesView.Create(root); // [PARTY]
            PartyStatusView.Create(root); // online party state in one line (click: party window)
            BossHpBarView.Create(root); // [MONSTER] boss bar, auto-binds to EnemyController.BossSpawned
            MinimapView.Create(root);
            DungeonHudView.Create(root); // [DUNGEON] clock, room map, CLEAR banner, coin countdown
            TipView.Create(root); // [E5] first-time tips
            ChatView.Create(root); // [F5] chat box + quick signals
            questPanel = UIFactory.Place(UIFactory.Rect(root, "Quest"), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -QuestTop), new Vector2(QuestWidth, 130));
            if (TouchUi.Enabled)
            {
                // [TOUCH] The right column belongs to the touch buttons: the tracker moves to the top, left of the menu buttons.
                questPanel.anchoredPosition = new Vector2(-MobileQuestRight, -MobileQuestTop);
                questPanel.sizeDelta = new Vector2(QuestNarrowWidth, 130);
            }
            var qbg = UIFactory.Panel(questPanel, "Bg", true);
            UIFactory.Stretch(qbg.rectTransform);
            questTitle = UIFactory.Text(questPanel, "Title", "", 22, UIColors.Highlight, TextAnchor.UpperLeft, true);
            UIFactory.Stretch(questTitle.rectTransform, 18, 0, 14, 12);
            questBody = UIFactory.Text(questPanel, "Body", "", 18, UIColors.Cream, TextAnchor.UpperLeft, true);
            questBody.lineSpacing = 1.15f;
            UIFactory.Stretch(questBody.rectTransform, 18, 10, 14, 44);
            // [QUEST] Auto-progress: walks to the next objective, any key takes control back.
            var autoImg = UIFactory.Image(questPanel, "Auto", Game.Art.Get("ui_btn"), Color.white);
            autoImg.raycastTarget = true;
            UIFactory.Place(autoImg.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, -4f), new Vector2(132f, 36f));
            var autoBtn = autoImg.gameObject.AddComponent<Button>();
            autoBtn.targetGraphic = autoImg;
            autoBtn.onClick.AddListener(QuestAutoPilot.Toggle);
            UiButton.Attach(autoBtn);
            autoLabel = UIFactory.Text(autoImg.transform, "Text", "자동 진행", 17, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(autoLabel.rectTransform, 30f, 0f, 24f, 0f);
            FitLabel(autoLabel);
            autoKey = KeyBadge(autoImg.transform);
            ButtonIcon(autoImg.transform, "menuicon_autoquest"); // [ART]
            // [AUTO] 자동 사냥 (hunting grounds only), left of 자동 진행.
            var huntImg = UIFactory.Image(questPanel, "AutoHunt", Game.Art.Get("ui_btn"), Color.white);
            huntImg.raycastTarget = true;
            UIFactory.Place(huntImg.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(-140f, -4f), new Vector2(132f, 36f));
            huntBtn = huntImg.gameObject.AddComponent<Button>();
            huntBtn.targetGraphic = huntImg;
            huntBtn.onClick.AddListener(QuestAutoPilot.ToggleHunt);
            UiButton.Attach(huntBtn);
            huntLabel = UIFactory.Text(huntImg.transform, "Text", "자동 사냥", 17, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(huntLabel.rectTransform, 30f, 0f, 24f, 0f);
            FitLabel(huntLabel);
            huntKey = KeyBadge(huntImg.transform);
            ButtonIcon(huntImg.transform, "menuicon_autohunt"); // [ART]
            // [UI] As wide as the two buttons above it (W-292..W-20): clear of the skill bar and the bottom-centre boss bar.
            autoStatus = UIFactory.Text(questPanel, "AutoStatus", "", 16, new Color32(143, 226, 143, 255), TextAnchor.UpperRight, true);
            UIFactory.Place(autoStatus.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, -44f), new Vector2(272f, 44f));
            autoStatus.raycastTarget = false;

            var mobilityPlate = UIFactory.Panel(root, "MobilityPlate", true);
            UIFactory.Place(mobilityPlate.rectTransform, Vector2.zero, Vector2.zero, new Vector2(20, 46), new Vector2(264, 32));
            mobilityHint = UIFactory.Text(mobilityPlate.transform, "Mobility", "", UiTheme.FontMin, UIColors.Cream, TextAnchor.MiddleLeft, true);
            UIFactory.Stretch(mobilityHint.rectTransform, 10, 0, 10, 0);

            // Control hints (bottom-left).
            // [UI] On a translucent plate so it stays readable over bright ground.
            controlsHint = UIFactory.Text(root, "Controls", "", UiTheme.FontMin, new Color(1, 1, 1, 0.92f), TextAnchor.MiddleLeft, true);
            UIFactory.Place(controlsHint.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(20, 10), new Vector2(900, 26));
            controlsPlate = UIFactory.Image(root, "ControlsPlate", Game.Art.Get("ui_white"), UiTheme.HudPlate);
            controlsPlate.preserveAspect = false;
            controlsPlate.transform.SetSiblingIndex(controlsHint.transform.GetSiblingIndex());

            // Context prompt (follows the target in world space).
            prompt = UIFactory.Place(UIFactory.Rect(root, "Prompt"), new Vector2(0, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(220, 40));
            var pbg = UIFactory.Panel(prompt, "Bg", true);
            UIFactory.Stretch(pbg.rectTransform);
            promptText = UIFactory.Text(prompt, "Text", "", 18, UIColors.Cream, TextAnchor.MiddleCenter);
            UIFactory.Stretch(promptText.rectTransform, 8, 0, 8, 0);
            promptPad = UIFactory.SharpIcon(prompt, "Pad", Color.white);
            promptPad.raycastTarget = false;
            UIFactory.Place(promptPad.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(8, 0), new Vector2(26, 26));
            promptPad.enabled = false;
            prompt.gameObject.SetActive(false);

            // Toasts (bottom-centre, above the dialogue box area; raised in Update over the name plate / boss bar).
            toastRoot = UIFactory.Place(UIFactory.Rect(root, "Toasts"), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, ToastBase), new Vector2(700, 200));
            // [UX] Toasts draw above every window (they used to sit under opaque full-screen windows).
            var toastCanvas = toastRoot.gameObject.AddComponent<Canvas>();
            toastCanvas.overrideSorting = true;
            toastCanvas.sortingOrder = 500;

            if (TouchUi.Enabled)
            {
                // [TOUCH] Keyboard hints make no sense on a phone, and the touch buttons sit where they were.
                mobilityPlate.gameObject.SetActive(false);
                controlsHint.gameObject.SetActive(false);
                controlsPlate.gameObject.SetActive(false);
                TouchControls.Create(root);
            }
        }

        void OnEnable()
        {
            GameEvents.Toast += ShowToast;
            GameEvents.PlayerHealthChanged += OnHealthChanged;
            if (Game.Session != null) Game.Session.Inventory.Changed += OnInventoryChanged;
            if (Game.Quest != null) Game.Quest.Changed += RefreshQuest;
            RefreshAll();
        }

        void OnDisable()
        {
            GameEvents.Toast -= ShowToast;
            GameEvents.PlayerHealthChanged -= OnHealthChanged;
            if (Game.Session != null) Game.Session.Inventory.Changed -= OnInventoryChanged;
            if (Game.Quest != null) Game.Quest.Changed -= RefreshQuest;
        }

        public void RefreshAll()
        {
            if (!built || Game.Session == null) return;
            OnHealthChanged(Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            foreach (var pair in itemCounts) pair.Value.text = CountText(pair.Key, Game.Session.Inventory.Count(pair.Key));
            RefreshQuest();
            RefreshControls(true);
        }

        public void ClearToasts()
        {
            foreach (var t in toasts) if (t.root != null) Destroy(t.root.gameObject);
            toasts.Clear();
        }

        void OnHealthChanged(int current, int max)
        {
            // HP is drawn by StatusBarsView (it polls every frame); nothing to do here.
        }

        void OnInventoryChanged(string id, int count, int delta)
        {
            if (itemCounts.TryGetValue(id, out var text))
            {
                text.text = CountText(id, count);
                if (delta > 0) itemPulse[id] = Time.unscaledTime;
            }
            RefreshQuest();
        }

        /// <summary>
        /// [UI] The gold cell has about 100 px for digits ("999,999" fits, "1,234,567" does not): from a million up the
        /// HUD shows 만 / 억 units, rounded down ("123.4만", "12.34억"), so the bar still ends at UiTheme.HudCurrencyMaxRight.
        /// </summary>
        static string CountText(string id, long count)
        {
            if (id != ConsumableDatabase.Gold || count < 1000000L) return count.ToString("N0");
            if (count < 100000000L) return (System.Math.Floor(count / 1000.0) / 10.0).ToString("#,0.#") + "만";
            return (System.Math.Floor(count / 1000000.0) / 100.0).ToString("#,0.##") + "억";
        }

        void RefreshQuest()
        {
            if (Game.Quest == null) return;
            // [STORY] Main quest on top (gold), the pinned side quest under it (blue), like a quest notifier.
            var main = Game.Quest.CurrentMain();
            var sb = new StringBuilder();
            int lines = 0;
            if (main != null)
            {
                questTitle.text = "메인 · " + main.DisplayTitle;
                foreach (var o in Game.Quest.ObjectivesOf(main)) { AppendObjective(sb, o); lines++; }
            }
            else questTitle.text = "진행할 메인 퀘스트 없음";
            var sub = Game.Quest.Database.Get(Game.Session.Journal.Tracked);
            if (sub != null && Game.Quest.StatusOf(sub.id) != QuestStatus.Completed)
            {
                sb.Append($"<color=#78d6ff>서브 · {sub.title}</color>\n");
                lines++;
                foreach (var o in Game.Quest.ObjectivesOf(sub)) { AppendObjective(sb, o); lines++; }
            }
            questBody.text = sb.ToString().TrimEnd('\n');
            // [UI] Height from the laid-out text (long titles and objectives wrap) instead of a line count.
            // [UI] At most two title lines (a very long title in the narrow panel is cut rather than pushing the buttons down).
            float titleH = Mathf.Clamp(questTitle.preferredHeight, 24f, 56f);
            questTitle.verticalOverflow = questTitle.preferredHeight > 56f ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
            questBody.rectTransform.offsetMax = new Vector2(questBody.rectTransform.offsetMax.x, -(16f + titleH));
            float wanted = 30f + titleH + Mathf.Max(22f, questBody.preferredHeight);
            // [UI] The 자동 사냥 / 자동 진행 buttons and the status line hang 88 px under the panel: keep them above the
            // quick item bar under the right column (top at 128, +8 gap) when the canvas is short (UI size 1.15 / 1.3 =
            // 626 / 554 tall). At 1.3 that leaves 554 - 234 - 88 - 136 = 96 px: title + 2 body lines.
            questFitHeight = ((RectTransform)transform).rect.height;
            questFitWidth = questPanel.sizeDelta.x;
            float room = questFitHeight > 0f ? questFitHeight - (TouchUi.Enabled ? MobileQuestTop : QuestTop) - 88f - 136f : wanted;
            float height = Mathf.Min(wanted, Mathf.Max(30f + titleH + 22f, room));
            questBody.verticalOverflow = height < wanted ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
            questPanel.sizeDelta = new Vector2(questPanel.sizeDelta.x, height);
        }

        /// <summary>Top of the quest tracker under the minimap (TipView uses the same column on narrow canvases).</summary>
        public const float QuestTop = 16f + MinimapView.Diameter + 42f;
        /// <summary>[UI] Quest tracker width; narrowed to the auto buttons' column while a boss bar shows on a narrow canvas.</summary>
        const float QuestWidth = 360f, QuestNarrowWidth = 272f;
        /// <summary>[TOUCH] Tracker position on a phone: below the side menu button, left of the menu / pause buttons.</summary>
        const float MobileQuestRight = 360f, MobileQuestTop = 72f;
        float questFitHeight, questFitWidth;

        static void AppendObjective(StringBuilder sb, QuestObjective o)
        {
            string color = o.done ? "#8fe28f" : "#f6e7c8";
            sb.Append($"<color={color}>{(o.done ? "●" : "○")} {o.text}</color>\n");
        }

        void RefreshControls(bool force)
        {
            var input = Game.Input;
            if (input == null) return;
            if (!force && input.UsingGamepad == lastGamepad) return;
            lastGamepad = input.UsingGamepad;
            controlsHint.text =
                $"이동 {input.GetBindingLabel(GameAction.Move)}   공격 {input.GetBindingLabel(GameAction.Attack)}   " +
                $"상호작용 {input.GetBindingLabel(GameAction.Interact)}   물약 {input.GetBindingLabel(GameAction.UseItem)}/{input.GetBindingLabel(GameAction.UseMana)}   " +
                $"가방 {input.GetBindingLabel(GameAction.Inventory)}   지도 {input.GetBindingLabel(GameAction.Map)}   메뉴 {input.GetBindingLabel(GameAction.Pause)}";
            // [UI] Size the text box (and its plate) to the text.
            controlsHint.rectTransform.sizeDelta = new Vector2(Mathf.Min(900f, controlsHint.preferredWidth + 4f), 26f);
            if (controlsPlate != null)
            {
                var pr = controlsPlate.rectTransform;
                pr.anchorMin = pr.anchorMax = Vector2.zero;
                pr.pivot = Vector2.zero;
                pr.anchoredPosition = new Vector2(12f, 8f);
                pr.sizeDelta = controlsHint.rectTransform.sizeDelta + new Vector2(16f, 4f);
            }
        }

        void ShowToast(string message)
        {
            float now = Time.unscaledTime;
            // Merge repeated messages ("+1 목재" x3).
            if (toasts.Count > 0)
            {
                var last = toasts[toasts.Count - 1];
                if (last.message == message && now - last.bornAt < 1.5f)
                {
                    last.count++;
                    last.bornAt = now;
                    last.text.text = $"{message}  x{last.count}";
                    FitToast(last);
                    return;
                }
            }
            // [UI] Each toast sits on its own translucent plate sized to the text (readable over bright ground).
            var box = UIFactory.Place(UIFactory.Rect(toastRoot, "Toast"), new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(700, ToastHeight));
            var plate = UIFactory.Image(box, "Plate", Game.Art.Get("ui_white"), UiTheme.HudPlate);
            plate.preserveAspect = false;
            UIFactory.Place(plate.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200, ToastHeight));
            var text = UIFactory.Text(box, "Text", message, UiTheme.FontSubheading, Color.white, TextAnchor.MiddleCenter, true);
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.1f, 0.06f, 0.04f, 0.9f);
            outline.effectDistance = new Vector2(2, -2);
            UIFactory.Stretch(text.rectTransform);
            var toast = new Toast { root = box, plate = plate, text = text, message = message, count = 1, bornAt = now };
            FitToast(toast);
            toasts.Add(toast);
            while (toasts.Count > 4)
            {
                Destroy(toasts[0].root.gameObject);
                toasts.RemoveAt(0);
            }
        }

        const float ToastHeight = 32f, ToastStep = 36f; // [UI]
        // [UI] Toast base heights: normal / over the dialogue name plate (box 24..194, plate top 226) / over the
        // bottom-centre boss bar (166..258) and its groggy line (262..298).
        const float ToastBase = 200f, ToastBaseDialogue = 240f, ToastBaseBoss = 306f;

        static void FitToast(Toast t)
        {
            t.plate.rectTransform.sizeDelta = new Vector2(Mathf.Min(700f, t.text.preferredWidth + 32f), ToastHeight);
        }

        /// <summary>[UI] Long labels ("자동 진행 (3)") shrink to fit next to the key badge instead of wrapping.</summary>
        static void FitLabel(Text t)
        {
            t.resizeTextForBestFit = true;
            t.resizeTextMinSize = 12;
            t.resizeTextMaxSize = t.fontSize;
        }

        /// <summary>[AUTO] The hotkey ("F6") at the right end of a HUD button.</summary>
        static Text KeyBadge(Transform button)
        {
            var t = UIFactory.Text(button, "Key", "", 14, new Color32(255, 211, 74, 255), TextAnchor.MiddleRight, true);
            t.horizontalOverflow = HorizontalWrapMode.Overflow; // a rebound "Space" grows to the left, never wraps
            UIFactory.Place(t.rectTransform, new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-5f, 0f), new Vector2(24f, 20f));
            return t;
        }

        static void ShowKey(Text badge, GameAction action, ref string shown)
        {
            var input = Game.Input;
            string key = input == null || input.UsingGamepad || TouchUi.Enabled ? "" : input.GetBindingLabel(action);
            if (key == "?") key = "";
            if (ReferenceEquals(key, shown)) return;
            shown = key;
            badge.text = key;
        }

        /// <summary>[ART] A small icon at the left end of a HUD button.</summary>
        static void ButtonIcon(Transform button, string sprite)
        {
            var ic = UIFactory.Image(button, "Icon", Game.Art.Get(sprite), Color.white);
            ic.preserveAspect = true;
            ic.raycastTarget = false;
            UIFactory.Place(ic.rectTransform, new Vector2(0f, .5f), new Vector2(0f, .5f), new Vector2(5f, 0f), new Vector2(26f, 26f));
        }
    }
}
