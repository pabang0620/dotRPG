using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [TOUCH] Keeps the phone HUD clean (added by HudView.BuildMobile, never on PC). Always shown: stick, attack / skill
    /// buttons, HP / MP, the small minimap, the folded quest line. Folded behind one small button each, remembered per
    /// device: the party (status line and full frames), the chat (a bubble button with the unread count). The PC side
    /// menu and the currency row move into the touch menu sheet (TouchControls.Menu). A tap on the minimap enlarges it
    /// (the M key on PC). Runs in LateUpdate so the views' own Update has already placed them.
    /// </summary>
    public sealed class MobileHud : MonoBehaviour
    {
        // Left column on the 1024x576 phone canvas, from the top: status bars (12..98), buffs (104..132), the party
        // button with the status line beside it (140..184), the party frames from 190.
        const float BuffTop = 104f, PartyTop = 140f, PartyChipW = 96f, FramesTop = 190f;
        const float FramesOpenScale = 0.82f, FramesFoldedScale = 0.6f, StatusLineW = 270f;
        const float ChatChipW = 72f, ChatGap = 6f, QuestTop = 72f, BossGap = 6f;

        RectTransform hud, quest, partyStatus, partyFrames, chat, items, buffs, minimap, minimapHint;
        GameObject partyStatusPlate;
        Button partyChip, chatChip, chatInput;
        Text partyChipLabel, chatChipLabel, chatBadge;
        bool partyOpen, chatOpen;
        int unread;
        IChatService bound;

        public static MobileHud Attach(RectTransform hud, RectTransform quest)
        {
            var m = hud.gameObject.AddComponent<MobileHud>();
            m.hud = hud;
            m.quest = quest;
            m.Build();
            return m;
        }

        RectTransform Child(string name) => hud.Find(name) as RectTransform;

        void Build()
        {
            partyOpen = MobileTouch.GetFlag("party", false);
            chatOpen = MobileTouch.GetFlag("chat", false);

            // The touch menu sheet replaces the PC side menu. It stays active (its Update polls mail / rewards / daily jobs
            // that the sheet's red dots read) but draws and catches nothing.
            var side = Child("SideMenu");
            if (side != null)
            {
                var group = side.gameObject.AddComponent<CanvasGroup>();
                group.alpha = 0f;
                group.interactable = false;
                group.blocksRaycasts = false;
            }
            items = Child("Items"); // gold and items: shown in the menu sheet instead
            buffs = Child("Buffs");
            if (buffs != null) buffs.anchoredPosition = new Vector2(buffs.anchoredPosition.x, -BuffTop);

            partyStatus = Child("PartyStatus");
            partyStatusPlate = partyStatus != null && partyStatus.Find("Plate") != null ? partyStatus.Find("Plate").gameObject : null;
            partyFrames = Child("PartyFrames");
            partyChip = MobileTouch.HudButton(hud, "MobilePartyButton", "", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -PartyTop),
                new Vector2(PartyChipW, MobileTouch.HudTap - 4f), out partyChipLabel);
            partyChip.onClick.AddListener(() => { partyOpen = !partyOpen; MobileTouch.SetFlag("party", partyOpen); });

            chat = Child("Chat");
            chatChip = MobileTouch.HudButton(hud, "MobileChatButton", L.T("채팅"), Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(ChatChipW, MobileTouch.HudTap), out chatChipLabel);
            chatChip.onClick.AddListener(() => { chatOpen = !chatOpen; MobileTouch.SetFlag("chat", chatOpen); if (chatOpen) unread = 0; });
            chatBadge = MobileTouch.Badge((RectTransform)chatChip.transform);
            chatInput = MobileTouch.HudButton(hud, "MobileChatInput", L.T("입력"), Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(ChatChipW, MobileTouch.HudTap), out _);
            chatInput.onClick.AddListener(() => { if (Game.IsPlaying && ChatView.Instance != null) ChatView.Instance.Open(null); });
            Rebind();
            OnlineServices.ChatChanged += Rebind;
            // Under the touch controls, so their menu sheet's dim layer covers these buttons too.
            var touch = Child("TouchControls");
            if (touch != null)
                foreach (var b in new[] { partyChip, chatChip, chatInput }) b.transform.SetSiblingIndex(touch.GetSiblingIndex());

            // The minimap is its own button: a tap enlarges it (M on PC), a tap on the big map shrinks it back.
            minimap = Child("Minimap");
            if (minimap != null)
            {
                var tap = UIFactory.Image(minimap, "MobileTap", Game.Art.Get("ui_circle"), Color.clear);
                tap.raycastTarget = true;
                UIFactory.Stretch(tap.rectTransform);
                var button = tap.gameObject.AddComponent<Button>();
                button.transition = Selectable.Transition.None;
                button.onClick.AddListener(() => { if (Game.IsPlaying) { Game.Audio.PlaySfx("select"); MinimapView.SetExpanded(!MinimapView.Expanded); } });
                var hint = UIFactory.Image(minimap, "MobileZoomHint", Game.Art.Get("ui_circle"), new Color(0.07f, 0.09f, 0.14f, 0.8f));
                hint.raycastTarget = false;
                minimapHint = UIFactory.Place(hint.rectTransform, Vector2.zero, Vector2.zero, new Vector2(6f, 6f), new Vector2(36f, 36f));
                var plus = UIFactory.Text(minimapHint, "Plus", "+", 26, Color.white, TextAnchor.MiddleCenter, true);
                plus.raycastTarget = false;
                UIFactory.Stretch(plus.rectTransform);
            }
        }

        void OnDestroy()
        {
            OnlineServices.ChatChanged -= Rebind;
            if (bound != null) bound.Received -= OnChat;
        }

        void Rebind()
        {
            if (bound != null) bound.Received -= OnChat;
            bound = OnlineServices.Chat;
            if (bound != null) bound.Received += OnChat;
        }

        void OnChat(ChatLine line)
        {
            if (line == null || line.mine || line.channel == ChatChannel.System || chatOpen) return;
            unread++;
        }

        static readonly string PartyOpenLabel = L.T("파티") + " ▼", PartyFoldedLabel = L.T("파티") + " ▶";
        static readonly string ChatOpenLabel = L.T("채팅") + " ▼", ChatFoldedLabel = L.T("채팅");

        static void SetLabel(Text t, string s)
        {
            if (!ReferenceEquals(t.text, s)) t.text = s;
        }

        static void Show(Component c, bool on)
        {
            if (c != null && c.gameObject.activeSelf != on) c.gameObject.SetActive(on);
        }

        static void Scale(Transform t, float s)
        {
            if (t != null && !Mathf.Approximately(t.localScale.x, s)) t.localScale = new Vector3(s, s, 1f);
        }

        static void Pos(RectTransform rt, Vector2 p)
        {
            if (rt != null && rt.anchoredPosition != p) rt.anchoredPosition = p;
        }

        void LateUpdate()
        {
            if (items != null && items.gameObject.activeSelf) items.gameObject.SetActive(false);
            bool playing = Game.IsPlaying;
            LayoutParty(playing);
            LayoutChat(playing);
            if (minimapHint != null) Show(minimapHint, !MinimapView.Expanded);
            AvoidBossBar();
        }

        void LayoutParty(bool playing)
        {
            bool online = partyStatusPlate != null && partyStatusPlate.activeInHierarchy;
            bool frames = false;
            if (partyFrames != null)
                for (int i = 0; i < partyFrames.childCount; i++)
                    if (partyFrames.GetChild(i).gameObject.activeSelf) { frames = true; break; }
            Show(partyChip, playing && (online || frames));
            SetLabel(partyChipLabel, partyOpen ? PartyOpenLabel : PartyFoldedLabel);
            if (partyStatus != null)
            {
                // Open: the status line sits right of the button (thumb height). Folded: hidden (scale 0 keeps its Update).
                Scale(partyStatus, partyOpen ? 1f : 0f);
                Pos(partyStatus, new Vector2(18f + PartyChipW + 6f, -PartyTop));
                if (partyStatus.sizeDelta.x != StatusLineW) partyStatus.sizeDelta = new Vector2(StatusLineW, MobileTouch.HudTap - 4f);
            }
            if (partyFrames != null)
            {
                // Folded: small cards (name, HP / MP) under the button; open: the full frames.
                Scale(partyFrames, partyOpen ? FramesOpenScale : FramesFoldedScale);
                Pos(partyFrames, new Vector2(partyFrames.anchoredPosition.x, -FramesTop));
            }
        }

        void LayoutChat(bool playing)
        {
            var view = ChatView.Instance;
            bool typing = view != null && view.Typing;
            if (typing && !chatOpen) { chatOpen = true; unread = 0; } // the input line always shows its box
            if (chat != null) Scale(chat, chatOpen ? 1f : 0f); // scale 0: the box draws and catches nothing, its Update keeps running
            Show(chatChip, playing || typing);
            Show(chatInput, playing && chatOpen && !typing);
            SetLabel(chatChipLabel, chatOpen ? ChatOpenLabel : ChatFoldedLabel);
            MobileTouch.SetBadge(chatBadge, chatOpen ? 0 : unread);
            if (chat == null) return;
            // Folded: the bubble sits where the box starts. Open: right of the box, with 입력 above it.
            float x = chatOpen ? chat.anchoredPosition.x + chat.sizeDelta.x + ChatGap : chat.anchoredPosition.x;
            Pos((RectTransform)chatChip.transform, new Vector2(x, chat.anchoredPosition.y));
            Pos((RectTransform)chatInput.transform, new Vector2(x, chat.anchoredPosition.y + MobileTouch.HudTap + ChatGap));
        }

        /// <summary>The boss bar takes the top centre (BossHpBarView.Mobile): the quest tracker moves under it.</summary>
        void AvoidBossBar()
        {
            if (quest == null) return;
            var bar = BossHpBarView.Instance;
            float top = bar != null && bar.DevVisible ? Mathf.Max(QuestTop, bar.MobileBottom + BossGap) : QuestTop;
            Pos(quest, new Vector2(quest.anchoredPosition.x, -top));
        }
    }
}
