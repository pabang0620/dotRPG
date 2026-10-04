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
        Text autoLabel, autoStatus; // [QUEST] auto-progress toggle under the tracker
        RectTransform questPanel;
        // [CONTENT] for capture checks
        public bool DevQuestVisible => questPanel != null && questPanel.gameObject.activeInHierarchy;
        RectTransform prompt;
        Text promptText;
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
            SkillBarView.Create(root);
            AwakeningBanner.Create(root);

            // Items under the bars (gold first).
            var items = UIFactory.Place(UIFactory.Rect(root, "Items"), new Vector2(0, 1), new Vector2(0, 1), new Vector2(18, -104), new Vector2(380, 40));
            var bg = UIFactory.Panel(items, "Bg", true);
            UIFactory.Stretch(bg.rectTransform);
            // [UI] Compact steps keep the bar left of the centred boss bar (UiTheme.HudCurrencyMaxRight).
            float x = 12f;
            var shown = new List<(string id, string icon)> { (ConsumableDatabase.Gold, "icon_gold") };
            foreach (var def in Game.Config.items) shown.Add((def.id, def.iconKey));
            foreach (var (id, iconKey) in shown)
            {
                bool gold = id == ConsumableDatabase.Gold;
                var icon = UIFactory.Image(items, "Icon_" + id, Game.Art.Get(iconKey), Color.white);
                UIFactory.Place(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(26, 26));
                var count = UIFactory.Text(items, "Count_" + id, "0", 20, gold ? (Color)new Color32(255, 216, 74, 255) : UIColors.Cream, TextAnchor.MiddleLeft, true);
                UIFactory.Place(count.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 30, 0), new Vector2(gold ? 108 : 52, 34));
                itemCounts[id] = count;
                x += gold ? 146f : 88f; // [UI] room for the wider pixel-font digits
            }
            items.sizeDelta = new Vector2(Mathf.Min(x + 10f, UiTheme.HudCurrencyMaxRight), 44); // [UI] the last count stays inside the rim
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
            questPanel = UIFactory.Place(UIFactory.Rect(root, "Quest"), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -16 - MinimapView.Diameter - 32), new Vector2(360, 130));
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
            UIFactory.Stretch(autoLabel.rectTransform);
            autoStatus = UIFactory.Text(questPanel, "AutoStatus", "", 16, new Color32(143, 226, 143, 255), TextAnchor.UpperRight, true);
            UIFactory.Place(autoStatus.rectTransform, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, -44f), new Vector2(360f, 44f));
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
            prompt.gameObject.SetActive(false);

            // Toasts (bottom-centre, above the dialogue box area).
            toastRoot = UIFactory.Place(UIFactory.Rect(root, "Toasts"), new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 200), new Vector2(700, 200));
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
            foreach (var pair in itemCounts) pair.Value.text = Game.Session.Inventory.Count(pair.Key).ToString("N0");
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
                text.text = count.ToString("N0");
                if (delta > 0) itemPulse[id] = Time.unscaledTime;
            }
            RefreshQuest();
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
            float titleH = Mathf.Max(24f, questTitle.preferredHeight);
            questBody.rectTransform.offsetMax = new Vector2(questBody.rectTransform.offsetMax.x, -(16f + titleH));
            questPanel.sizeDelta = new Vector2(questPanel.sizeDelta.x, 30f + titleH + Mathf.Max(22f, questBody.preferredHeight));
        }

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
                $"가방 {input.GetBindingLabel(GameAction.Inventory)}   지도 M   메뉴 {input.GetBindingLabel(GameAction.Pause)}";
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

        static void FitToast(Toast t)
        {
            t.plate.rectTransform.sizeDelta = new Vector2(Mathf.Min(700f, t.text.preferredWidth + 32f), ToastHeight);
        }

        void Update()
        {
            if (!built) return;
            // [CONTENT] The quest tracker is hidden inside dungeons (room map + boss bar own that space).
            bool showQuest = Game.Dungeon == null || !Game.Dungeon.InRun;
            if (questPanel.gameObject.activeSelf != showQuest) questPanel.gameObject.SetActive(showQuest);
            if (autoLabel != null)
            {
                bool auto = QuestAutoPilot.Active;
                autoLabel.text = auto ? "자동 중지" : "자동 진행";
                autoStatus.text = auto ? QuestAutoPilot.Status : "";
            }
            float now = Time.unscaledTime;
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
            promptText.text = $"<color=#ffd34a>[{Game.Input.GetBindingLabel(GameAction.Interact)}]</color> {target.Prompt}";
            prompt.sizeDelta = new Vector2(Mathf.Max(150f, promptText.preferredWidth + 36f), 40f);
            Vector3 screen = Game.Camera.Camera.WorldToScreenPoint(target.PromptWorldPosition);
            prompt.position = screen + new Vector3(0f, Mathf.Sin(Time.unscaledTime * 4f) * 3f, 0f);
        }
    }
}
