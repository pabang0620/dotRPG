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
        RectTransform questPanel;
        RectTransform prompt;
        Text promptText;
        Text controlsHint;
        bool lastGamepad;

        RectTransform toastRoot;
        readonly List<Toast> toasts = new List<Toast>();

        class Toast
        {
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
            var items = UIFactory.Place(UIFactory.Rect(root, "Items"), new Vector2(0, 1), new Vector2(0, 1), new Vector2(20, -100), new Vector2(360, 40));
            var bg = UIFactory.Panel(items, "Bg", true);
            UIFactory.Stretch(bg.rectTransform);
            float x = 14f;
            var shown = new List<(string id, string icon)> { (ConsumableDatabase.Gold, "icon_gold") };
            foreach (var def in Game.Config.items) shown.Add((def.id, def.iconKey));
            foreach (var (id, iconKey) in shown)
            {
                bool gold = id == ConsumableDatabase.Gold;
                var icon = UIFactory.Image(items, "Icon_" + id, Game.Art.Get(iconKey), Color.white);
                UIFactory.Place(icon.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x, 0), new Vector2(30, 30));
                var count = UIFactory.Text(items, "Count_" + id, "0", 22, gold ? (Color)new Color32(255, 216, 74, 255) : UIColors.Cream, TextAnchor.MiddleLeft, true);
                UIFactory.Place(count.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(x + 34, 0), new Vector2(gold ? 96 : 70, 34));
                itemCounts[id] = count;
                x += gold ? 138f : 100f;
            }
            items.sizeDelta = new Vector2(x, 44);
            QuickItemBar.Create(root);

            // Round minimap (top-right) with the quest tracker underneath.
            SideMenuView.Create(root);
            MinimapView.Create(root);
            questPanel = UIFactory.Place(UIFactory.Rect(root, "Quest"), new Vector2(1, 1), new Vector2(1, 1), new Vector2(-20, -16 - MinimapView.Diameter - 32), new Vector2(360, 130));
            var qbg = UIFactory.Panel(questPanel, "Bg", true);
            UIFactory.Stretch(qbg.rectTransform);
            questTitle = UIFactory.Text(questPanel, "Title", "", 22, UIColors.Highlight, TextAnchor.UpperLeft, true);
            UIFactory.Stretch(questTitle.rectTransform, 18, 0, 14, 12);
            questBody = UIFactory.Text(questPanel, "Body", "", 18, UIColors.Cream, TextAnchor.UpperLeft, true);
            questBody.lineSpacing = 1.15f;
            UIFactory.Stretch(questBody.rectTransform, 18, 10, 14, 44);

            // Control hints (bottom-left).
            controlsHint = UIFactory.Text(root, "Controls", "", 16, new Color(1, 1, 1, 0.85f), TextAnchor.LowerLeft, true);
            UIFactory.Place(controlsHint.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(20, 14), new Vector2(900, 30));

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
            foreach (var t in toasts) if (t.text != null) Destroy(t.text.gameObject);
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
            questTitle.text = "의뢰 · " + Game.Quest.Config.title;
            var sb = new StringBuilder();
            foreach (var o in Game.Quest.GetObjectives())
            {
                string color = o.done ? "#8fe28f" : "#f6e7c8";
                sb.Append($"<color={color}>{(o.done ? "●" : "○")} {o.text}</color>\n");
            }
            questBody.text = sb.ToString().TrimEnd('\n');
            int lines = Game.Quest.GetObjectives().Count;
            questPanel.sizeDelta = new Vector2(questPanel.sizeDelta.x, 58 + lines * 26);
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
                $"가방 {input.GetBindingLabel(GameAction.Inventory)}   메뉴 {input.GetBindingLabel(GameAction.Pause)}";
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
                    return;
                }
            }
            var text = UIFactory.Text(toastRoot, "Toast", message, 22, Color.white, TextAnchor.MiddleCenter, true);
            var outline = text.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.1f, 0.06f, 0.04f, 0.9f);
            outline.effectDistance = new Vector2(2, -2);
            UIFactory.Place(text.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), Vector2.zero, new Vector2(700, 32));
            toasts.Add(new Toast { text = text, message = message, count = 1, bornAt = now });
            while (toasts.Count > 4)
            {
                Destroy(toasts[0].text.gameObject);
                toasts.RemoveAt(0);
            }
        }

        void Update()
        {
            if (!built) return;
            float now = Time.unscaledTime;
            // Toast layout & fade.
            for (int i = toasts.Count - 1; i >= 0; i--)
            {
                var t = toasts[i];
                float age = now - t.bornAt;
                if (age > ToastLife)
                {
                    Destroy(t.text.gameObject);
                    toasts.RemoveAt(i);
                }
            }
            for (int i = 0; i < toasts.Count; i++)
            {
                var t = toasts[i];
                float age = now - t.bornAt;
                int fromBottom = toasts.Count - 1 - i;
                var rt = t.text.rectTransform;
                rt.anchoredPosition = Vector2.Lerp(rt.anchoredPosition, new Vector2(0, fromBottom * 34f), 1f - Mathf.Exp(-14f * Time.unscaledDeltaTime));
                var c = t.text.color;
                c.a = age < ToastLife - 0.5f ? 1f : Mathf.Clamp01((ToastLife - age) / 0.5f);
                t.text.color = c;
            }

            // Item count pulse.
            foreach (var pair in itemCounts)
            {
                float s = 1f;
                if (itemPulse.TryGetValue(pair.Key, out float at)) s = 1f + 0.35f * Mathf.Max(0f, 1f - (now - at) / 0.25f);
                pair.Value.rectTransform.localScale = new Vector3(s, s, 1f);
            }

            UpdatePrompt();
            RefreshControls(false);
        }

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
