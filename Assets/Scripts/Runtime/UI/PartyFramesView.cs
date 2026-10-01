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
        public const float Left = 150f, Top = -152f, Width = 206f, Height = 50f, Gap = 6f;
        const float BarW = 150f;

        sealed class Frame
        {
            public RectTransform root;
            public Image stripe, hpFill, mpFill, downed;
            public Text name, info, downedText;
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

        /// <summary>Number of frames on screen (for automated checks).</summary>
        public int DevVisible { get; private set; }

        void Update()
        {
            var party = Game.Party;
            int shown = 0;
            for (int i = 0; i < frames.Count; i++)
            {
                var f = frames[i];
                var m = party != null && i + 1 < party.Members.Count ? party.Members[i + 1] : null;
                bool on = m != null && m.gameObject.activeInHierarchy;
                if (f.root.gameObject.activeSelf != on) f.root.gameObject.SetActive(on);
                if (!on) continue;
                shown++;
                var def = MercenaryDatabase.Get(m.Data.MercenaryId);
                Color cc = ClassColor(m);
                f.stripe.color = cc;
                f.name.text = $"<color=#{ColorUtility.ToHtmlStringRGB(cc)}>{m.DisplayName}</color>";
                f.info.text = $"Lv.{m.Data.Level} {(def != null ? def.roleName.Split(' ')[0] : "")}";
                var hp = m.Health;
                SetFill(f.hpFill, hp.Max > 0 ? (float)hp.Current / hp.Max : 0f);
                int maxMp = m.MaxMana;
                SetFill(f.mpFill, maxMp > 0 ? m.Data.Mana / maxMp : 0f);
                bool down = m.IsDead;
                if (f.downed.gameObject.activeSelf != down) f.downed.gameObject.SetActive(down);
                if (down)
                {
                    float left = party.ReviveRemaining(m);
                    f.downedText.text = left > 0f ? $"쓰러짐  {Mathf.CeilToInt(left)}초" : "쓰러짐";
                }
            }
            DevVisible = shown;
        }
    }
}
