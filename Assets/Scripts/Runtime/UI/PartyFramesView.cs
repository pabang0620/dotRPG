using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Left party frames: one small card per companion (the local player has the big bars) with a class
    /// colour stripe, name, level, role, HP and MP bars, and a "쓰러짐" countdown while downed. Sits right
    /// of the side-menu button and its fold-out grid so the two never overlap. Hidden when solo.
    /// </summary>
    public class PartyFramesView : MonoBehaviour
    {
        // Layout (reference pixels, 1280x720 canvas). The side menu (button + 2-column grid) ends at x≈136.
        public const float Left = 18f, Top = -214f, Width = 206f, Height = 50f, Gap = 6f; // left-aligned with the status bars, under the party status line
        const float BarW = 150f;

        sealed class Frame
        {
            public RectTransform root;
            public Image stripe, hpFill, mpFill, downed;
            public Text name, info, downedText;
            public PlayerController shownMember;
            public int shownLevel = -1, shownDowned = -1;
            public Color shownColor;
        }

        readonly List<Frame> frames = new List<Frame>();

        public static PartyFramesView Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "PartyFrames"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(Left, Top),
                new Vector2(Width, (Height + Gap) * PartyManager.MaxCompanions));
            var v = root.gameObject.AddComponent<PartyFramesView>();
            for (int i = 0; i < PartyManager.MaxCompanions; i++) v.frames.Add(v.Build(root, i));
            return v;
        }

        static Image Img(Transform parent, string name, Color color)
        {
            var img = UIFactory.Image(parent, name, Game.Art.Get("ui_white"), color);
            img.preserveAspect = false;
            img.raycastTarget = false;
            return img;
        }

        Frame Build(RectTransform parent, int index)
        {
            var f = new Frame();
            var bg = Img(parent, "Member" + index, new Color32(14, 18, 28, 205));
            f.root = UIFactory.Place(bg.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -index * (Height + Gap)), new Vector2(Width, Height));
            f.stripe = Img(f.root, "Stripe", Color.white);
            UIFactory.Place(f.stripe.rectTransform, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(5f, Height));
            f.name = UIFactory.Text(f.root, "Name", "", 16, Color.white, TextAnchor.UpperLeft, true);
            UIFactory.Place(f.name.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, -3f), new Vector2(90f, 22f));
            f.info = UIFactory.Text(f.root, "Info", "", UiTheme.FontMin, new Color(1f, 1f, 1f, 0.88f), TextAnchor.UpperRight, true);
            UIFactory.Place(f.info.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-6f, -5f), new Vector2(104f, 20f));
            f.hpFill = Bar(f.root, "HP", -27f, 10f, new Color32(214, 48, 49, 255));
            f.mpFill = Bar(f.root, "MP", -40f, 6f, new Color32(52, 120, 230, 255));
            f.downed = Img(f.root, "Downed", new Color32(40, 10, 10, 190));
            UIFactory.Stretch(f.downed.rectTransform);
            f.downedText = UIFactory.Text(f.downed.transform, "Text", "", 16, new Color32(255, 150, 130, 255), TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(f.downedText.rectTransform);
            f.root.gameObject.SetActive(false);
            return f;
        }

        static Image Bar(Transform parent, string name, float y, float h, Color color)
        {
            var frame = Img(parent, name, new Color32(4, 6, 10, 230));
            UIFactory.Place(frame.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(12f, y), new Vector2(BarW + 2f, h + 2f));
            var fill = Img(frame.transform, "Fill", color);
            UIFactory.Place(fill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(1f, 0f), new Vector2(BarW, h));
            return fill;
        }

        static void SetFill(Image fill, float ratio) => fill.rectTransform.sizeDelta = new Vector2(BarW * Mathf.Clamp01(ratio), fill.rectTransform.sizeDelta.y);

        /// <summary>Stripe colour: the mercenary's colour (warriors warm, mages cool).</summary>
        public static Color ClassColor(PlayerController member)
        {
            var def = member != null && member.Data != null ? MercenaryDatabase.Get(member.Data.MercenaryId) : null;
            if (def != null) return def.color;
            return member != null && member.Class == CharacterClass.Mage ? new Color32(160, 110, 240, 255) : new Color32(230, 110, 60, 255);
        }

        /// <summary>A party member who is not on this map: name, level and class, bars empty, where they are (same map, another map, offline).</summary>
        static void ShowAway(Frame frame, PartyMemberView v)
        {
            Color classColor = v.cls == CharacterClass.Mage ? new Color32(160, 110, 240, 255) : new Color32(230, 110, 60, 255);
            frame.stripe.color = classColor;
            frame.shownMember = null;
            frame.shownLevel = -1;
            frame.name.text = $"<color=#{ColorUtility.ToHtmlStringRGB(classColor)}>{v.name}</color>";
            frame.info.text = $"Lv.{v.level} {CharacterClassInfo.Get(v.cls).displayName}";
            SetFill(frame.hpFill, 0f);
            SetFill(frame.mpFill, 0f);
            if (!frame.downed.gameObject.activeSelf) frame.downed.gameObject.SetActive(true);
            frame.shownDowned = -2;
            frame.downedText.text = WhereText(v);
        }

        /// <summary>Where a party member without a body here is, from the server's presence (map they last reported).</summary>
        public static string WhereText(PartyMemberView v)
        {
            if (!v.online) return "<color=#8c96a8>접속 안 함</color>";
            if (string.IsNullOrEmpty(v.mapId)) return "<color=#b8c4d8>위치 확인 중</color>";
            if (Game.Session != null && v.mapId == Game.Session.MapId) return "<color=#8fe28f>같은 곳에 있음</color>";
            var map = MapRegistry.Get(v.mapId);
            return $"<color=#b8c4d8>{(map != null ? map.displayName : "다른 곳")}에 있음</color>";
        }

        /// <summary>Number of frames on screen (for automated checks).</summary>
        public int DevVisible { get; private set; }

        readonly List<PlayerController> bodies = new List<PlayerController>();
        readonly List<PartyMemberView> away = new List<PartyMemberView>();

        void Update()
        {
            var party = Game.Party;
            int visibleCount = 0;
            // Everyone with a body here (AI in a dungeon, friends hunting or fighting with me), then the people of my
            // online party who are somewhere else: a party member is always listed, not only the AI.
            bodies.Clear();
            away.Clear();
            if (party != null)
                for (int k = 1; k < party.Members.Count; k++)
                    if (party.Members[k] != null && party.Members[k].gameObject.activeInHierarchy) bodies.Add(party.Members[k]);
            var pc = PartyClient.Instance;
            if (pc != null && pc.InParty)
                foreach (var v in pc.Members)
                {
                    if (v.me) continue;
                    bool here = false;
                    foreach (var b in bodies) if (b.DisplayName == v.name) here = true;
                    if (!here) away.Add(v);
                }
            for (int i = 0; i < frames.Count; i++)
            {
                var frame = frames[i];
                var member = i < bodies.Count ? bodies[i] : null;
                var elsewhere = member == null && i - bodies.Count < away.Count ? away[i - bodies.Count] : null;
                bool isVisible = member != null || elsewhere != null;
                if (frame.root.gameObject.activeSelf != isVisible) frame.root.gameObject.SetActive(isVisible);
                if (!isVisible) continue;
                visibleCount++;
                if (elsewhere != null) { ShowAway(frame, elsewhere); continue; }
                var mercenary = MercenaryDatabase.Get(member.Data.MercenaryId);
                Color classColor = ClassColor(member);
                frame.stripe.color = classColor;
                // [P5] Name / level text only when the member, colour or level changes.
                if (frame.shownMember != member || frame.shownLevel != member.Data.Level || frame.shownColor != classColor)
                {
                    frame.shownMember = member;
                    frame.shownLevel = member.Data.Level;
                    frame.shownColor = classColor;
                    frame.name.text = $"<color=#{ColorUtility.ToHtmlStringRGB(classColor)}>{member.DisplayName}</color>";
                    frame.info.text = $"Lv.{member.Data.Level} {(mercenary != null ? mercenary.roleName.Split(' ')[0] : "")}";
                }
                var health = member.Health;
                SetFill(frame.hpFill, health.Max > 0 ? (float)health.Current / health.Max : 0f);
                int maxMana = member.MaxMana;
                SetFill(frame.mpFill, maxMana > 0 ? member.Data.Mana / maxMana : 0f);
                bool isDowned = member.IsDead;
                if (frame.downed.gameObject.activeSelf != isDowned) frame.downed.gameObject.SetActive(isDowned);
                if (isDowned)
                {
                    float reviveRemaining = party.ReviveRemaining(member);
                    int reviveSeconds = reviveRemaining > 0f ? Mathf.CeilToInt(reviveRemaining) : 0;
                    if (reviveSeconds != frame.shownDowned)
                    {
                        frame.shownDowned = reviveSeconds;
                        frame.downedText.text = reviveRemaining > 0f ? $"쓰러짐  {reviveSeconds}초" : "쓰러짐";
                    }
                }
            }
            DevVisible = visibleCount;
        }
    }
}
