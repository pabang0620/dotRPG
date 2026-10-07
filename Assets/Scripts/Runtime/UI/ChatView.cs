using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [F5] HUD chat box (bottom left, above the mobility bar). Enter opens the input line, Enter sends,
    /// Esc closes. "/p" party, "/g" general, "/w 이름" whisper. Keys 4~8 send the quick signals
    /// (PLAN_ONLINE §2.3) to the party with a speech bubble over the hero.
    /// </summary>
    public class ChatView : MonoBehaviour
    {
        public static ChatView Instance { get; private set; }

        // [UI] Width = Min(400, W/2 - 224): the right edge stays 10 px left of the centred skill bar (W/2 - 202) at every UI
        // size (1.0: 400, 1.15: 332, 1.3: 268). Short canvases (H < 600, UI size 1.3) use 112 px from y 90 (top 202): under
        // the party frames, which shrink there (PartyFramesView, bottom 207 from the bottom with three members).
        const float Width = 400f, Height = 154f, Left = 12f, Bottom = 96f, InputH = 30f;
        const float CompactHeight = 112f, CompactBottom = 90f, CompactBelow = 600f, SkillBarClear = 224f;
        const int ShownLines = 6;
        static readonly KeyCode[] SignalKeys = { KeyCode.Alpha4, KeyCode.Alpha5, KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8 };
        static readonly string[] Tabs = { "전체", "파티", "귓속말" };

        Image bg;
        CanvasGroup group;
        Text log, tabText, channelText;
        GameObject inputRoot;
        InputField field;
        RectTransform canvasRect, bubble, root, inputBox;
        Vector2 laidOutFor;
        Text bubbleText;
        float idle = 99f, bubbleTime;
        int tab, openedFrame;
        ChatChannel channel = ChatChannel.General;
        bool channelChosen;
        string whisperTo;

        public bool Typing => inputRoot != null && inputRoot.activeSelf;
        static IChatService Service => OnlineServices.Chat;

        public static ChatView Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "Chat"), Vector2.zero, Vector2.zero, new Vector2(Left, Bottom), new Vector2(Width, Height));
            var v = root.gameObject.AddComponent<ChatView>();
            Instance = v;
            v.root = root;
            v.group = root.gameObject.AddComponent<CanvasGroup>();
            v.canvasRect = (RectTransform)parent; // HUD root: stretched over the whole canvas

            v.bg = UIFactory.Image(root, "Bg", Game.Art.Get("ui_white"), new Color(0.04f, 0.05f, 0.08f, 0.2f));
            v.bg.preserveAspect = false;
            UIFactory.Stretch(v.bg.rectTransform);
            // The log grows upward from the input line; its own mask keeps old lines off the tab row.
            var logArea = UIFactory.Stretch(UIFactory.Rect(v.bg.transform, "LogArea"), 8f, InputH + 4f, 8f, 24f);
            logArea.gameObject.AddComponent<RectMask2D>();
            v.log = UIFactory.Text(logArea, "Log", "", 16, UiTheme.TextPrimary, TextAnchor.LowerLeft, true);
            UIFactory.Stretch(v.log.rectTransform);

            // Tab label (click to switch 전체 / 파티 / 귓속말).
            v.tabText = UIFactory.Text(root, "Tab", "", 16, UiTheme.Accent, TextAnchor.MiddleLeft, true);
            UIFactory.Place(v.tabText.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(8f, -2f), new Vector2(Width - 16f, 22f));
            v.tabText.raycastTarget = true;
            v.tabText.gameObject.AddComponent<Button>().onClick.AddListener(() => { v.tab = (v.tab + 1) % Tabs.Length; v.idle = 0f; v.Redraw(); });

            v.BuildInput(root);
            v.BuildBubble();
            GameEvents.Toast += v.OnToast;
            Service.Received += v.OnReceived;
            v.bound = Service;
            OnlineServices.ChatChanged += v.Rebind; // [SERVER 5] online / offline swaps the service
            v.Redraw();
            return v;
        }

        void BuildInput(RectTransform root)
        {
            var box = UIFactory.Panel(root, "Input", true);
            UIFactory.Place(box.rectTransform, Vector2.zero, Vector2.zero, Vector2.zero, new Vector2(Width, InputH));
            inputBox = box.rectTransform;
            box.raycastTarget = true;
            channelText = UIFactory.Text(box.transform, "Channel", "", 16, Color.white, TextAnchor.MiddleLeft, false);
            UIFactory.Place(channelText.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(92f, InputH));
            var text = UIFactory.Text(box.transform, "Text", "", 16, Color.white, TextAnchor.MiddleLeft, false);
            UIFactory.Stretch(text.rectTransform, 98f, 2f, 8f, 2f);
            text.supportRichText = false;
            var placeholder = UIFactory.Text(box.transform, "Placeholder", "Enter 보내기 · /p /g /w", 16, new Color(1f, 1f, 1f, 0.35f), TextAnchor.MiddleLeft, false);
            UIFactory.Stretch(placeholder.rectTransform, 98f, 2f, 8f, 2f);
            placeholder.resizeTextForBestFit = true; // the narrow box at UI size 1.3 shrinks the hint instead of wrapping it
            placeholder.resizeTextMinSize = 12;
            placeholder.resizeTextMaxSize = placeholder.fontSize;
            field = box.gameObject.AddComponent<InputField>();
            field.textComponent = text;
            field.placeholder = placeholder;
            field.characterLimit = ChatRules.MaxLength + 24; // room for "/w 이름 "
            field.lineType = InputField.LineType.SingleLine;
            field.targetGraphic = box;
            inputRoot = box.gameObject;
            inputRoot.SetActive(false);
        }

        void BuildBubble()
        {
            var img = UIFactory.Image(canvasRect, "ChatBubble", Game.Art.Get("ui_white"), new Color(1f, 1f, 1f, 0.92f));
            img.preserveAspect = false;
            bubble = UIFactory.Place(img.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(150f, 30f));
            bubbleText = UIFactory.Text(bubble, "Text", "", 16, UiTheme.PanelDeep, TextAnchor.MiddleCenter, false);
            UIFactory.Stretch(bubbleText.rectTransform, 6f, 2f, 6f, 2f);
            bubble.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            GameEvents.Toast -= OnToast;
            OnlineServices.ChatChanged -= Rebind;
            if (bound != null) bound.Received -= OnReceived;
            InputReader.TextInputActive = false;
            if (Instance == this) Instance = null;
        }

        // Server-wide notices (+10 enhance etc.) also land in the system channel.
        void OnToast(string message)
        {
            if (message != null && message.Contains("[알림]")) Service.PostSystem(message.Replace("<color=#ffd84a>[알림]</color> ", "").Replace("[알림] ", ""));
        }

        IChatService bound;

        void Rebind()
        {
            if (bound != null) bound.Received -= OnReceived;
            bound = Service;
            bound.Received += OnReceived;
            Redraw();
        }

        void OnReceived(ChatLine line)
        {
            idle = 0f;
            Redraw();
        }

        bool PlayingNow => Game.State != null && Game.State.Current == GameState.Playing;

        /// <summary>[UI] Fits the box to the canvas (UI size changes): left of the skill bar, under the party frames.</summary>
        void Layout()
        {
            var size = canvasRect.rect.size;
            if (size == laidOutFor || size.x <= 0f) return;
            laidOutFor = size;
            bool compact = size.y < CompactBelow;
            float w = Mathf.Min(Width, size.x * 0.5f - SkillBarClear);
            root.anchoredPosition = new Vector2(Left, compact ? CompactBottom : Bottom);
            root.sizeDelta = new Vector2(w, compact ? CompactHeight : Height);
            tabText.rectTransform.sizeDelta = new Vector2(w - 16f, tabText.rectTransform.sizeDelta.y);
            inputBox.sizeDelta = new Vector2(w, InputH);
        }

        void Update()
        {
            Layout();
            Service.Tick(Time.unscaledDeltaTime);
            idle += Time.unscaledDeltaTime;
            if (Typing)
            {
                InputReader.TextInputActive = true;
                if (!PlayingNow || Input.GetKeyDown(KeyCode.Escape)) Close();
                else if (Time.frameCount > openedFrame && (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))) SendTyped();
                else if (!field.isFocused) field.ActivateInputField();
            }
            else if (PlayingNow)
            {
                if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)) Open(null);
                else
                    for (int i = 0; i < SignalKeys.Length; i++)
                        if (Input.GetKeyDown(SignalKeys[i]) && !KeyTakenByAction(SignalKeys[i])) { QuickSignal(i); break; }
            }
            Fade();
            UpdateBubble();
            // The menu grid now folds out at the top next to the status bars: the chat stays visible.
            group.alpha = 1f;
            group.blocksRaycasts = true;
        }

        static bool KeyTakenByAction(KeyCode k)
        {
            foreach (var a in InputReader.Rebindable) if (InputReader.KeyboardKey(a) == k) return true;
            return false;
        }

        /// <summary>Opens the input line; <paramref name="prefill"/> e.g. "/w 검은뿔 " from the friends window.</summary>
        public void Open(string prefill)
        {
            if (!channelChosen) channel = Game.Dungeon != null && Game.Dungeon.InRun ? ChatChannel.Party : ChatChannel.General;
            inputRoot.SetActive(true);
            field.text = prefill ?? "";
            field.ActivateInputField();
            field.caretPosition = field.text.Length;
            openedFrame = Time.frameCount;
            InputReader.TextInputActive = true;
            idle = 0f;
            RedrawChannel();
        }

        public void Close()
        {
            field.DeactivateInputField();
            inputRoot.SetActive(false);
            InputReader.TextInputActive = false;
        }

        void SendTyped()
        {
            string text = field.text ?? "";
            Close();
            var target = channel;
            string to = whisperTo;
            if (text.StartsWith("/"))
            {
                int sp = text.IndexOf(' ');
                string cmd = (sp < 0 ? text : text.Substring(0, sp)).ToLowerInvariant();
                string rest = sp < 0 ? "" : text.Substring(sp + 1);
                if (cmd == "/p" || cmd == "/파티") target = ChatChannel.Party;
                else if (cmd == "/g" || cmd == "/일반") target = ChatChannel.General;
                else if (cmd == "/w" || cmd == "/귓")
                {
                    int sp2 = rest.IndexOf(' ');
                    to = sp2 < 0 ? rest : rest.Substring(0, sp2);
                    rest = sp2 < 0 ? "" : rest.Substring(sp2 + 1);
                    target = ChatChannel.Whisper;
                }
                else { GameEvents.RaiseToast("모르는 명령입니다. /p 파티 · /g 일반 · /w 이름 내용"); return; }
                channel = target;
                whisperTo = to;
                channelChosen = true;
                text = rest;
            }
            if (text.Trim().Length == 0) { RedrawChannel(); return; }
            string error = Service.Send(target, text, to);
            if (!string.IsNullOrEmpty(error)) { GameEvents.RaiseToast(error); Game.Audio.PlaySfx("cancel"); }
        }

        /// <summary>[F5] Quick signal <paramref name="index"/> (0~4) to the party, with a bubble over the hero.</summary>
        public bool QuickSignal(int index)
        {
            string text = ChatRules.QuickSignals[index];
            string error = Service.Send(ChatChannel.Party, text);
            if (!string.IsNullOrEmpty(error)) { GameEvents.RaiseToast(error); return false; }
            ShowBubble(text);
            Game.Audio.PlaySfx("select");
            return true;
        }

        public void ShowBubble(string text)
        {
            bubbleText.text = text;
            bubble.sizeDelta = new Vector2(Mathf.Max(70f, bubbleText.preferredWidth + 20f), 30f);
            bubbleTime = 2.5f;
            bubble.gameObject.SetActive(true);
            UpdateBubble();
        }

        void UpdateBubble()
        {
            if (!bubble.gameObject.activeSelf) return;
            bubbleTime -= Time.unscaledDeltaTime;
            var cam = Camera.main;
            if (bubbleTime <= 0f || Game.Player == null || cam == null || !PlayingNow) { bubble.gameObject.SetActive(false); return; }
            Vector3 sp = cam.WorldToScreenPoint(Game.Player.transform.position + Vector3.up * 1.7f);
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, sp, null, out var local)) bubble.anchoredPosition = local;
        }

        void Fade()
        {
            bool lit = Typing || idle < 8f;
            var c = bg.color;
            float a = Mathf.MoveTowards(c.a, lit ? 0.5f : 0.18f, Time.unscaledDeltaTime * 1.5f);
            if (!Mathf.Approximately(a, c.a)) bg.color = new Color(c.r, c.g, c.b, a);
            log.color = new Color(1f, 1f, 1f, Mathf.Lerp(0.6f, 1f, (a - 0.18f) / 0.32f));
        }

        void RedrawChannel()
        {
            string name = channel == ChatChannel.Whisper && !string.IsNullOrEmpty(whisperTo) ? $"▶{whisperTo}" : ChatRules.ChannelName(channel);
            channelText.text = $"<color={ChatRules.ChannelColor(channel)}>[{name}]</color>";
        }

        void Redraw()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Tabs.Length; i++)
                sb.Append(i == tab ? $"<color=#ffd84a>[{Tabs[i]}]</color> " : $"<color=#8a94a8>{Tabs[i]}</color> ");
            sb.Append("<size=12><color=#8a94a8> Enter 채팅</color></size>");
            tabText.text = sb.ToString();

            var lines = Service.Lines;
            var picked = new System.Collections.Generic.List<string>();
            for (int i = lines.Count - 1; i >= 0 && picked.Count < ShownLines; i--)
            {
                var l = lines[i];
                if (tab == 1 && l.channel != ChatChannel.Party) continue;
                if (tab == 2 && l.channel != ChatChannel.Whisper) continue;
                picked.Add(Format(l));
            }
            picked.Reverse();
            log.text = string.Join("\n", picked);
        }

        /// <summary>Player text never carries rich-text tags into the log.</summary>
        static string Safe(string s) => (s ?? "").Replace("<", "＜").Replace(">", "＞");

        public static string Format(ChatLine l)
        {
            string col = ChatRules.ChannelColor(l.channel);
            if (l.channel == ChatChannel.System) return $"<color={col}>[시스템] {l.text}</color>";
            if (l.channel == ChatChannel.Whisper)
                return l.mine ? $"<color={col}>▶{Safe(l.to)}: {Safe(l.text)}</color>" : $"<color={col}>{Safe(l.from)}▶: {Safe(l.text)}</color>";
            string who = l.mine ? $"<color=#ffe066>{Safe(l.from)}</color>" : Safe(l.from);
            // [칭호][이름] : 말 (general chat shows no channel tag; party keeps [파티])
            string title = string.IsNullOrEmpty(l.title) ? "" : $"<color=#ffd34a>[{Safe(l.title)}]</color>";
            string tag = l.channel == ChatChannel.General ? "" : $"<color={col}>[{ChatRules.ChannelName(l.channel)}]</color> ";
            return $"{tag}{title}[{who}] : {Safe(l.text)}";
        }
    }
}
