using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 파티 window (side menu → 파티): the current party (the player + up to three mercenaries) on top, the
    /// four mercenaries below with their role, level-synced stats and a 편성 / 해제 button. Mouse or keys:
    /// ←/→ pick a mercenary, Enter/attack toggles it, Esc / the bag key close.
    /// </summary>
    public class PartyScreen : WindowScreen
    {
        /// <summary>[UX] Kept for callers: the party hub's "opened from" window (Esc goes back there).</summary>
        public static WindowScreen ReturnTo { get => PartyHub.ReturnTo; set => PartyHub.ReturnTo = value; }

        public override void Close()
        {
            if (PartyHub.BackOnePage()) return;
            if (!PartyHub.CloseToOrigin()) base.Close();
        }

        const float SlotW = 290f, SlotH = 104f, CardW = 290f, CardH = 372f, Step = 300f;

        sealed class Slot
        {
            public Image bg, portrait, stripe;
            public Text text;
        }

        sealed class Card
        {
            public MercenaryDef def;
            public Image bg, portrait, frame;
            public Text title, body;
            public Button button;
            public Text buttonText;
        }

        readonly List<Slot> slots = new List<Slot>();
        readonly List<Card> cards = new List<Card>();
        Text partyTitle, hint;
        Button kukButton; // [CH3] 쿠크 (story companion offered from 3-3 to 3-끝, StoryCompanions.Chapter3.cs)
        /// <summary>[ROGUE] Six companions no longer fit one row: the cards slide sideways inside this masked strip.</summary>
        RectTransform cardsView, cardsRoot;
        int selected;
        float lastMoveX;

        public static PartyScreen Create(Transform canvas)
        {
            var w = CreateWindow<PartyScreen>(canvas, "Party", L.T("AI 동료 편성"), "menuicon_party");
            PartyHub.Attach(w, w.content, PartyHub.Page.Companions); // [UX] one party window: 내 파티 | 파티 찾기 | AI 동료
            w.partyTitle = Label(w.content, "PartyTitle", "", 24, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(4f, 0f), new Vector2(800f, 32f));
            UIFactory.OneLine(w.partyTitle, true); // [UI 2026-10-30] the "자리가 N개라" warning shrinks the line instead of running under the 쿠크 button
            for (int i = 0; i < PartyManager.MaxMembers; i++) w.slots.Add(w.BuildSlot(i));
            Label(w.content, "MercTitle", L.T("<b>용병 목록</b>   <color=#b8c4d8>레벨은 내 캐릭터와 같고, 장비는 레벨 구간마다 정해져 있습니다.</color>"), 22,
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(4f, -150f), new Vector2(1180f, 30f));
            w.cardsView = UIFactory.Rect(w.content, "MercCards");
            w.cardsView.anchorMin = new Vector2(0f, 1f); w.cardsView.anchorMax = new Vector2(1f, 1f); w.cardsView.pivot = new Vector2(0f, 1f);
            w.cardsView.anchoredPosition = new Vector2(0f, -182f); w.cardsView.sizeDelta = new Vector2(0f, CardH + 8f);
            w.cardsView.gameObject.AddComponent<RectMask2D>();
            w.cardsRoot = UIFactory.Place(UIFactory.Rect(w.cardsView, "Cards"), new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(MercenaryDatabase.All.Count * Step, CardH + 8f));
            foreach (var def in MercenaryDatabase.All) w.cards.Add(w.BuildCard(def, w.cards.Count));
            w.kukButton = Button(w.content, "Kukum", "", "ui_btn", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-4f, 0f), new Vector2(300f, 34f), () => w.ToggleKuk(), 18);
            w.kukButton.gameObject.SetActive(false);
            // [UI] Key hint under the cards (top right it ran into the party title once the "자리가 N개라" warning showed).
            w.hint = Label(w.content, "Hint", "", 17, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-4f, 2f), new Vector2(1180f, 28f), TextAnchor.LowerRight);
            return w;
        }

        Slot BuildSlot(int i)
        {
            var s = new Slot();
            s.bg = Panel(content, "Member" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * Step, -36f), new Vector2(SlotW, SlotH), new Color32(24, 36, 54, 235));
            s.stripe = Panel(s.bg.transform, "Stripe", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(6f, SlotH), Color.white);
            s.portrait = UIFactory.Image(s.bg.transform, "Portrait", null, Color.white);
            s.portrait.preserveAspect = true;
            UIFactory.Place(s.portrait.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(16f, 0f), new Vector2(60f, 90f));
            s.text = Label(s.bg.transform, "Text", "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(88f, -10f), new Vector2(196f, 88f));
            s.text.resizeTextForBestFit = true; s.text.resizeTextMinSize = 14; s.text.resizeTextMaxSize = 18;
            s.text.verticalOverflow = VerticalWrapMode.Truncate;
            return s;
        }

        Card BuildCard(MercenaryDef def, int i)
        {
            var c = new Card { def = def };
            c.bg = Panel(cardsRoot, "Merc_" + def.id, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * Step + 4f, -4f), new Vector2(CardW, CardH), new Color32(24, 36, 54, 235));
            c.bg.raycastTarget = true;
            int index = i;
            var pick = c.bg.gameObject.AddComponent<Button>();
            pick.targetGraphic = c.bg;
            pick.transition = Selectable.Transition.None;
            pick.onClick.AddListener(() => { selected = index; Refresh(); });
            c.frame = Panel(c.bg.transform, "Frame", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(CardW, CardH), new Color(1f, 1f, 1f, 0f));
            c.frame.rectTransform.sizeDelta = new Vector2(CardW + 6f, CardH + 6f);
            c.frame.transform.SetAsFirstSibling();
            var stripe = Panel(c.bg.transform, "Stripe", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(CardW, 6f), def.color);
            stripe.raycastTarget = false;
            c.portrait = UIFactory.Image(c.bg.transform, "Portrait", null, Color.white);
            c.portrait.preserveAspect = true;
            UIFactory.Place(c.portrait.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -18f), new Vector2(72f, 108f));
            c.title = Label(c.bg.transform, "Title", "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(100f, -16f), new Vector2(180f, 112f));
            c.title.resizeTextForBestFit = true; c.title.resizeTextMinSize = 14; c.title.resizeTextMaxSize = 18;
            c.title.verticalOverflow = VerticalWrapMode.Truncate;
            var bodyViewport = UIFactory.Place(UIFactory.Rect(c.bg.transform, "BodyViewport"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(16f, -136f), new Vector2(262f, 164f));
            bodyViewport.gameObject.AddComponent<Image>().color = Color.clear;
            bodyViewport.gameObject.AddComponent<RectMask2D>();
            c.body = Label(bodyViewport, "Body", "", 16, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(262f, 164f));
            c.body.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            var bodyScroll = bodyViewport.gameObject.AddComponent<ScrollRect>();
            bodyScroll.viewport = bodyViewport; bodyScroll.content = c.body.rectTransform; bodyScroll.horizontal = false;
            bodyScroll.movementType = ScrollRect.MovementType.Clamped; bodyScroll.scrollSensitivity = 24f;
            var track = UIFactory.Image(c.bg.transform, "BodyScrollTrack", null, new Color(1f, 1f, 1f, .15f));
            UIFactory.Place(track.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(282f, -136f), new Vector2(6f, 164f));
            var handle = UIFactory.Image(track.transform, "Handle", null, new Color32(184, 196, 216, 255));
            UIFactory.Stretch(handle.rectTransform);
            track.raycastTarget = true; handle.raycastTarget = true;
            var scrollbar = track.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle.rectTransform; scrollbar.targetGraphic = handle;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            bodyScroll.verticalScrollbar = scrollbar;
            bodyScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            c.button = Button(c.bg.transform, "Toggle", "", "ui_btn", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 14f), new Vector2(200f, 50f),
                () => { selected = index; Toggle(def); }, 22);
            c.buttonText = c.button.GetComponentInChildren<Text>();
            return c;
        }

        static string Hex(Color32 c) => ColorUtility.ToHtmlStringRGB(c);

        /// <summary>Hires or dismisses a mercenary (what the card button does). Returns true when the party changed.</summary>
        public bool Toggle(MercenaryDef def)
        {
            var party = Game.Party;
            if (party == null || def == null) return false;
            if (Game.Dungeon != null && Game.Dungeon.InRun) { GameEvents.RaiseToast(L.T("던전 안에서는 파티를 바꿀 수 없습니다.")); Game.Audio.PlaySfx("cancel"); return false; } // [DUNGEON]
            bool ok;
            if (party.Has(def.id) || Game.Session.PartyRoster.Contains(def.id))
            {
                ok = party.RemoveCompanion(def.id);
                string ga = L.Josa(def.name, "이", "가"); // i18n-ignore particle
                if (ok) GameEvents.RaiseToast(L.F("{0}{1} 파티에서 떠났습니다.", L.T(def.name), ga));
            }
            else if (Game.Session.PartyRoster.Count >= PartyManager.CompanionLimit)
            {
                ok = false;
                int people = PartyManager.PeopleInParty;
                GameEvents.RaiseToast(people > 1
                    ? L.F("파티원 {0}명이 있어 AI 동료는 {1}명까지 넣을 수 있습니다. (최대 {2}명)", people, PartyManager.CompanionLimit, PartyManager.MaxMembers)
                    : L.F("파티가 가득 찼습니다. (최대 {0}명)", PartyManager.MaxMembers));
            }
            else
            {
                ok = party.AddCompanion(def.id) != null || Game.Session.PartyRoster.Contains(def.id);
                string ga = L.Josa(def.name, "이", "가"); // i18n-ignore particle
                if (ok) GameEvents.RaiseToast(L.F("{0}{1} 파티에 합류했습니다.", L.T(def.name), ga));
            }
            Game.Audio.PlaySfx(ok ? "confirm" : "cancel");
            Refresh();
            return ok;
        }

        protected override void Refresh()
        {
            var session = Game.Session;
            var party = Game.Party;
            if (session == null || party == null) return;
            int level = session.Progression.Level;
            var roster = session.PartyRoster;
            int people = PartyManager.PeopleInParty, limit = PartyManager.CompanionLimit;
            partyTitle.text = L.F("<b>현재 파티</b>  <color=#ffe066>사람 {0}명 · AI {1}명 / {2}</color>", people, Mathf.Min(roster.Count, limit), PartyManager.MaxMembers)
                + (roster.Count > limit ? L.F("   <color=#ff9f7a>자리가 {0}개라 던전에는 앞의 {1}명만 들어갑니다</color>", limit, limit) : "");

            // Party slots: me first, then the roster in order.
            for (int i = 0; i < slots.Count; i++)
            {
                var s = slots[i];
                if (i == 0)
                {
                    var me = Game.Player;
                    var cls = CharacterClassInfo.Get(session.PlayerClass);
                    s.stripe.color = CharacterClassInfo.ColorOf(session.PlayerClass);
                    s.portrait.enabled = true;
                    s.portrait.sprite = Game.Art.GetCharacter(CharacterPreviewCard.LookFor(session.PlayerClass), "down", "idle0");
                    int hp = me != null && me.Health != null ? me.Health.Max : CharacterStats.MaxHp;
                    s.text.text = L.F("<size=22><b>나</b></size>  <color=#b8c4d8>{0}</color>\nLv.{1}   HP {2}\n<color=#ffe066>리더</color>", L.T(cls.displayName), level, hp);
                    continue;
                }
                var def = i - 1 < roster.Count ? MercenaryDatabase.Get(roster[i - 1]) : null;
                s.portrait.enabled = def != null;
                if (def == null)
                {
                    s.stripe.color = new Color32(60, 70, 90, 255);
                    s.text.text = L.T("<color=#8c96a8>빈 자리\n아래에서 용병을 편성하자</color>");
                    continue;
                }
                var member = party.Find(def.id);
                s.stripe.color = def.color;
                s.portrait.sprite = Portrait(def, level);
                string hpLine = member != null ? $"HP {member.Health.Current} / {member.Health.Max}" : "";
                s.text.text = $"<size=22><b><color=#{Hex(def.color)}>{L.T(def.name)}</color></b></size>  <color=#b8c4d8>{L.T(def.roleName)}</color>\nLv.{level}   {hpLine}";
            }

            selected = Mathf.Clamp(selected, 0, cards.Count - 1);
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                var def = c.def;
                bool inParty = roster.Contains(def.id);
                var preview = MercenaryDatabase.CreateData(def, level);
                var st = preview.Stats;
                c.portrait.sprite = Portrait(def, level);
                c.title.text = L.F("<size=26><b><color=#{0}>{1}</color></b></size>\n{2}\n<color=#b8c4d8>Lv.{3}  HP {4}  공격 {5}</color>", Hex(def.color), L.T(def.name), L.T(def.roleName), level, st.MaxHp, st.AttackDamage(def.cls) * DamageNumber.DisplayScale);
                c.body.text = L.F("{0}\n\n<color=#ffe066>주력</color> {1}\n<color=#b8c4d8>무기</color> {2}", L.T(def.description), SkillLine(def, preview), L.T(EquipmentDatabase.Get(preview.Equipment[EquipSlot.Weapon])?.name) ?? "-");
                c.bg.color = inParty ? new Color32(36, 58, 82, 245) : new Color32(24, 36, 54, 235);
                c.frame.color = i == selected ? (Color)UIColors.Highlight : new Color(1f, 1f, 1f, 0f);
                bool full = !inParty && roster.Count >= PartyManager.CompanionLimit;
                c.buttonText.text = inParty ? L.T("해제") : full ? L.T("자리 없음") : L.T("편성");
                c.button.image.sprite = Game.Art.Get(inParty ? "ui_btngray" : "ui_btn");
                c.button.image.color = full ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            }
            KeepSelectedInView();
            hint.text = L.T("<color=#ffd34a>AI 동료는 요일던전·레이드에서만 함께 싸웁니다</color>   <color=#b8c4d8>←/→ 선택  Enter 편성·해제  ESC 닫기</color>");
            bool kukIn = roster.Contains(StoryCompanions.KukId);
            kukButton.gameObject.SetActive(kukIn || StoryCompanions.KukAvailable);
            if (kukButton.gameObject.activeSelf)
            {
                bool kukFull = !kukIn && roster.Count >= PartyManager.CompanionLimit;
                kukButton.GetComponentInChildren<Text>().text = kukIn ? L.T("쿠크 해제") : kukFull ? L.T("쿠크: 자리 없음") : L.T("쿠크와 함께 가기");
                kukButton.image.sprite = Game.Art.Get(kukIn ? "ui_btngray" : "ui_btn");
                kukButton.image.color = kukFull ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
            }
        }

        void ToggleKuk()
        {
            if (Game.Dungeon != null && Game.Dungeon.InRun) { GameEvents.RaiseToast(L.T("던전 안에서는 파티를 바꿀 수 없습니다.")); Game.Audio.PlaySfx("cancel"); return; }
            bool wasIn = Game.Session.PartyRoster.Contains(StoryCompanions.KukId);
            bool ok = StoryCompanions.ToggleKuk();
            if (ok) GameEvents.RaiseToast(wasIn ? L.T("쿠크가 파티에서 떠났습니다.") : L.T("쿠크가 파티에 합류했습니다."));
            else if (!wasIn) GameEvents.RaiseToast(L.F("파티가 가득 찼습니다. (최대 {0}명)", PartyManager.MaxMembers));
            Game.Audio.PlaySfx(ok ? "confirm" : "cancel");
            Refresh();
        }

        /// <summary>Slides the card strip so the selected card is fully inside the window.</summary>
        void KeepSelectedInView()
        {
            if (cardsView == null || cardsRoot == null || cards.Count == 0) return;
            float view = cardsView.rect.width;
            float total = cards.Count * Step + 8f;
            float offset = -cardsRoot.anchoredPosition.x;
            float left = selected * Step, right = left + CardW + 8f;
            if (left < offset) offset = left;
            else if (right > offset + view) offset = right - view;
            offset = Mathf.Clamp(offset, 0f, Mathf.Max(0f, total - view));
            cardsRoot.anchoredPosition = new Vector2(-offset, 0f);
        }

        static Sprite Portrait(MercenaryDef def, int level)
        {
            var gear = MercenaryDatabase.GearFor(def, level);
            return Game.Art.GetCharacter(CharacterLook.WithGear(MercenaryDatabase.LookOf(def), gear[1], gear[2]), "down", "idle0");
        }

        static string SkillLine(MercenaryDef def, CharacterData data)
        {
            var names = new List<string>();
            foreach (int slot in def.skillPriority)
            {
                var gem = SkillGems.ForSlot(def.cls, slot);
                if (gem == null) continue;
                bool open = data.Progression.IsSlotOpen(slot);
                names.Add(open ? L.T(gem.name) : $"<color=#8c96a8>{L.T(gem.name)}(Lv.{Progression.SlotLevel(slot)})</color>");
                if (names.Count >= 3) break;
            }
            return string.Join(" · ", names);
        }

        protected override void Update()
        {
            base.Update();
            if (!TakesInput || !gameObject.activeInHierarchy) return;
            var input = Game.Input;
            float x = input.Move.x;
            int dir = x > 0.5f && lastMoveX <= 0.5f ? 1 : x < -0.5f && lastMoveX >= -0.5f ? -1 : 0;
            lastMoveX = x;
            if (dir != 0)
            {
                selected = (selected + dir + cards.Count) % cards.Count;
                Game.Audio.PlaySfx("select");
                Refresh();
            }
            if (input.SubmitPressed || input.AttackPressed) Toggle(cards[selected].def);
        }

        // ---------- Automated checks ----------

        public bool DevToggle(string mercId) => Toggle(MercenaryDatabase.Get(mercId));
        public string DevCardButton(string mercId)
        {
            foreach (var c in cards) if (c.def.id == mercId) return c.buttonText.text;
            return null;
        }
    }
}
