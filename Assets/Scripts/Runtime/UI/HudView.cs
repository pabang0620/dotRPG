using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// In-game HUD: hearts, item counts, quest tracker, context prompt ("[E] 대화하기"),
    /// control hints and toast messages. Purely reactive: it listens to events and polls
    /// the interactor, never changes game state.
    /// </summary>
    public class HudView : MonoBehaviour
    {
        RectTransform heartsRoot;
        readonly List<Image> hearts = new List<Image>();
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
            var hud = root.gameObject.AddComponent<HudView>();
            hud.Build(root);
            hud.built = true;
            hud.RefreshAll();
            return hud;
        }

        void Build(RectTransform root)
        {
            // Level + HP / MP / EXP bars (top-left) and the skill bar (bottom-centre).
            heartsRoot = UIFactory.Place(UIFactory.Rect(root, "Hearts"), new Vector2(0, 1), new Vector2(0, 1), new Vector2(24, -20), new Vector2(10, 10));
            heartsRoot.gameObject.SetActive(false);
            StatusBarsView.Create(root);
            BuffBarView.Create(root); // [UI] buffs and debuffs under the currency line
            SkillBarView.Create(root);
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
            QuickItemBar.Create(root);

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
            float room = questFitHeight > 0f ? questFitHeight - QuestTop - 88f - 136f : wanted;
            float height = Mathf.Min(wanted, Mathf.Max(30f + titleH + 22f, room));
            questBody.verticalOverflow = height < wanted ? VerticalWrapMode.Truncate : VerticalWrapMode.Overflow;
            questPanel.sizeDelta = new Vector2(questPanel.sizeDelta.x, height);
        }

        /// <summary>Top of the quest tracker under the minimap (TipView uses the same column on narrow canvases).</summary>
        public const float QuestTop = 16f + MinimapView.Diameter + 42f;
        /// <summary>[UI] Quest tracker width; narrowed to the auto buttons' column while a boss bar shows on a narrow canvas.</summary>
        const float QuestWidth = 360f, QuestNarrowWidth = 272f;
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
            string key = input == null || input.UsingGamepad ? "" : input.GetBindingLabel(action);
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

        void Update()
        {
            if (!built) return;
            // [CONTENT] The quest tracker is hidden inside dungeons (room map + boss bar own that space).
            bool inRun = Game.Dungeon != null && Game.Dungeon.InRun;
            var hudRect = ((RectTransform)transform).rect;
            var bossBar = BossHpBarView.Instance;
            bool bossShown = bossBar != null && bossBar.DevVisible;
            // [UI] On narrow canvases (UI size 1.15 / 1.3: W < 1160) the bottom-centre boss bar (W/2 +- 200) reaches the
            // quest column (W-380): a field boss fight owns that space, but only while the player is in it (a field boss
            // stays bound until killed). A first-time tip in the column hides it too.
            bool showQuest = !inRun && !TipView.InQuestColumn && !(bossShown && bossBar.Engaged && hudRect.width < 1160f);
            if (questPanel.gameObject.activeSelf != showQuest) questPanel.gameObject.SetActive(showQuest);
            // [UI] A bound but not engaged boss bar on a narrow canvas: the tracker narrows to the auto buttons' column
            // (W-292..W-20) so it stays clear of the bar's right end (its "xN" count), and widens back afterwards.
            float questWidth = bossShown && hudRect.width < 1160f ? QuestNarrowWidth : QuestWidth;
            if (!Mathf.Approximately(questPanel.sizeDelta.x, questWidth)) questPanel.sizeDelta = new Vector2(questWidth, questPanel.sizeDelta.y);
            if (showQuest && (!Mathf.Approximately(questWidth, questFitWidth) || !Mathf.Approximately(hudRect.height, questFitHeight))) RefreshQuest(); // UI size / width changed
            // [AUTO] Hotkeys for 자동 진행 / 자동 사냥 (rebindable, default F6 / F7), only in normal play with no window open.
            var keys = Game.Input;
            if (keys != null && Game.IsPlaying && (!inRun || QuestAutoPilot.Active))
            {
                if (keys.HotkeyPressed(GameAction.AutoQuest)) QuestAutoPilot.Toggle();
                else if (keys.HotkeyPressed(GameAction.AutoHunt)) QuestAutoPilot.ToggleHunt();
            }
            if (autoLabel != null)
            {
                bool auto = QuestAutoPilot.Active;
                bool hunting = QuestAutoPilot.Hunting;
                autoLabel.text = auto && !hunting ? "자동 중지" : QuestAutoPilot.Targets.Count > 0 ? $"자동 진행 ({QuestAutoPilot.Targets.Count})" : "자동 진행";
                if (huntLabel != null) huntLabel.text = hunting ? "사냥 중지" : "자동 사냥";
                autoStatus.text = auto ? QuestAutoPilot.Status : "";
                if (huntBtn != null)
                {
                    bool canHunt = hunting || QuestAutoPilot.CanHuntHere;
                    if (huntBtn.interactable != canHunt) huntBtn.interactable = canHunt;
                }
                ShowKey(autoKey, GameAction.AutoQuest, ref autoKeyShown);
                ShowKey(huntKey, GameAction.AutoHunt, ref huntKeyShown);
            }
            // [UI] Toasts move up over the dialogue name plate and over the boss bar.
            var dialogue = Game.Dialogue;
            // A boss warning lifted over the GROGGY line (300..340) pushes them higher still.
            float toastY = bossShown ? Mathf.Max(ToastBaseBoss, bossBar.TopEdge + 4f) : dialogue != null && dialogue.IsOpen ? ToastBaseDialogue : ToastBase;
            if (!Mathf.Approximately(toastRoot.anchoredPosition.y, toastY)) toastRoot.anchoredPosition = new Vector2(0f, toastY);
            float now = Time.unscaledTime;
            // [UI] Over the boss bar (a warning lifts the base to 344) the stack must stay under the party status line and
            // the currency row (top 212 px): at UI size 1.3 only 1 toast fits, at 1.15 3. Older ones fade out early.
            int maxToasts = bossShown ? Mathf.Max(1, Mathf.FloorToInt((hudRect.height - 212f - toastY - ToastHeight) / ToastStep) + 1) : 4;
            // Toast layout & fade.
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                float age = now - t.bornAt;
                if (age > ToastLife)
                {
                    Destroy(t.root.gameObject);
                    toasts.RemoveAt(i);
                }
            }
            for (int i = 0; i < toasts.Count - maxToasts; i++)
            {
                // Pushed past the limit: jump to the last 0.5 s (the normal fade) instead of covering the HUD above.
                var t = toasts[i];
                if (now - t.bornAt < ToastLife - 0.5f) t.bornAt = now - (ToastLife - 0.5f);
            }
            for (int i = 0; i < toasts.Count; i++)
            {
                var t = toasts[i];
                float age = now - t.bornAt;
                int fromBottom = toasts.Count - 1 - i;
                var rt = t.root;
                rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, new Vector2(0, fromBottom * ToastStep), 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                float alpha = age < ToastLife - 0.5f ? 1f : Mathf.Clamp01((ToastLife - age) / 0.5f);
                var c = t.text.color;
                c.a = alpha;
                t.text.color = c;
                var pc = UiTheme.HudPlate;
                pc.a *= alpha;
                t.plate.color = pc;
            }

            // Item count pulse.
            foreach (var pair in itemCounts)
            {
                float s = 1f;
                if (itemPulse.TryGetValue(pair.Key, out float at)) s = 1f + 0.35f * Mathf.Max(0f, 1f - (now - at) / 0.25f);
                pair.Value.rectTransform.localScale = new Vector3(s, s, 1f);
            }

            var mobilityPlayer = Game.Player;
            if (mobilityPlayer != null)
            {
                float remaining = mobilityPlayer.MobilityCooldownRemaining;
                string key = Game.Input.GetBindingLabel(GameAction.Mobility);
                int stateKey = mobilityPlayer.IsDashing ? -2 : Mathf.CeilToInt(remaining * 10f);
                if (stateKey != mobilityStateShown || !ReferenceEquals(key, mobilityKeyShown) || !ReferenceEquals(mobilityPlayer.MobilityName, mobilityNameShown))
                {
                    mobilityStateShown = stateKey; mobilityKeyShown = key; mobilityNameShown = mobilityPlayer.MobilityName;
                    string state = mobilityPlayer.IsDashing ? "이동 중" : remaining > 0f ? $"{remaining:0.0}초" : "준비";
                    mobilityHint.text = $"[{key}] {mobilityPlayer.MobilityName}  {state}";
                }
                mobilityHint.color = remaining > 0f ? new Color(.7f, .74f, .8f) : new Color(.65f, .94f, 1f);
            }
            UpdatePrompt();
            RefreshControls(false);
        }

        // [P5] Last values shown in the mobility hint (format only on change).
        int mobilityStateShown = int.MinValue;
        string mobilityKeyShown, mobilityNameShown;

        void UpdatePrompt()
        {
            var player = Game.Player;
            var target = player != null && Game.IsPlaying ? player.Interactor.Current : null;
            if (target == null || Game.Camera == null)
            {
                if (prompt.gameObject.activeSelf) prompt.gameObject.SetActive(false);
                return;
            }
            if (!prompt.gameObject.activeSelf) prompt.gameObject.SetActive(true);
            var padIcon = Game.Input.UsingGamepad ? Game.Art.Optional("pad_" + Game.Input.GetBindingLabel(GameAction.Interact).ToLowerInvariant()) : null;
            promptPad.enabled = padIcon != null;
            promptPad.sprite = padIcon;
            promptText.text = padIcon != null ? target.Prompt : $"<color=#ffd34a>[{Game.Input.GetBindingLabel(GameAction.Interact)}]</color> {target.Prompt}";
            promptText.rectTransform.offsetMin = new Vector2(padIcon != null ? 38f : 8f, 0f);
            prompt.sizeDelta = new Vector2(Mathf.Max(150f, promptText.preferredWidth + (padIcon != null ? 66f : 36f)), 40f);
            Vector3 screen = Game.Camera.Camera.WorldToScreenPoint(target.PromptWorldPosition);
            prompt.position = screen + new Vector3(0f, Mathf.Sin(Time.unscaledTime * 4f) * 3f, 0f);
        }
    }
}
