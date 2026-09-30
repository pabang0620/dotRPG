using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Bottom-centre skill bar: five slots (the last, larger one is the awakening skill) with key,
    /// icon, MP cost, a radial cooldown with the seconds left, and a lock with the level a slot opens at.
    /// </summary>
    public class SkillBarView : MonoBehaviour
    {
        const float Size = 70f, UltSize = 84f, Gap = 10f;
        static readonly Color UltGold = new Color(1f, 0.84f, 0.42f, 1f);

        readonly Image[] frames = new Image[SkillGems.Slots];
        readonly Image[] icons = new Image[SkillGems.Slots];
        readonly Image[] cooldowns = new Image[SkillGems.Slots];
        readonly Image[] locks = new Image[SkillGems.Slots];
        readonly Text[] keys = new Text[SkillGems.Slots];
        readonly Text[] costs = new Text[SkillGems.Slots];
        readonly Text[] timers = new Text[SkillGems.Slots];
        readonly Text[] lockTexts = new Text[SkillGems.Slots];

        public static SkillBarView Create(Transform parent)
        {
            float width = (SkillGems.Slots - 1) * (Size + Gap) + UltSize;
            var root = UIFactory.Place(UIFactory.Rect(parent, "SkillBar"), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(width, UltSize + 22f));
            var v = root.gameObject.AddComponent<SkillBarView>();
            float x = 0f;
            for (int i = 0; i < SkillGems.Slots; i++)
            {
                bool ult = i == SkillGems.UltimateSlot;
                float s = ult ? UltSize : Size;
                var bg = UIFactory.Image(root, "Slot" + i, Game.Art.Get("ui_slot"), ult ? UltGold : Color.white);
                bg.preserveAspect = false;
                UIFactory.Place(bg.rectTransform, Vector2.zero, Vector2.zero, new Vector2(x, 0f), new Vector2(s, s));
                v.frames[i] = bg;

                v.icons[i] = UIFactory.Image(bg.transform, "Icon", null, Color.white);
                UIFactory.Place(v.icons[i].rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(s * 0.7f, s * 0.7f));

                var cd = UIFactory.Image(bg.transform, "Cooldown", Game.Art.Get("ui_white"), new Color(0f, 0f, 0f, 0.6f));
                cd.preserveAspect = false;
                cd.type = Image.Type.Filled;
                cd.fillMethod = Image.FillMethod.Radial360;
                cd.fillOrigin = (int)Image.Origin360.Top;
                cd.fillClockwise = false;
                UIFactory.Stretch(cd.rectTransform, 3f, 3f, 3f, 3f);
                v.cooldowns[i] = cd;

                v.locks[i] = UIFactory.Image(bg.transform, "Lock", Game.Art.Get("ui_white"), new Color(0.02f, 0.03f, 0.06f, 0.62f));
                v.locks[i].preserveAspect = false;
                UIFactory.Stretch(v.locks[i].rectTransform, 3f, 3f, 3f, 3f);
                v.lockTexts[i] = UIFactory.Text(bg.transform, "LockText", "", 17, new Color32(220, 226, 240, 255), TextAnchor.MiddleCenter, true);
                UIFactory.Stretch(v.lockTexts[i].rectTransform, 0f, 0f, 0f, 0f);

                v.timers[i] = UIFactory.Text(bg.transform, "Timer", "", ult ? 24 : 21, Color.white, TextAnchor.MiddleCenter, true);
                UIFactory.Stretch(v.timers[i].rectTransform);
                v.keys[i] = UIFactory.Text(bg.transform, "Key", "", 16, new Color32(255, 224, 102, 255), TextAnchor.UpperLeft, true);
                UIFactory.Stretch(v.keys[i].rectTransform, 6f, 2f, 2f, 3f);
                v.costs[i] = UIFactory.Text(bg.transform, "Cost", "", 14, new Color32(140, 190, 255, 255), TextAnchor.LowerRight, true);
                UIFactory.Stretch(v.costs[i].rectTransform, 2f, 3f, 6f, 2f);
                x += s + Gap;
            }
            // Caption above the awakening slot.
            var caption = UIFactory.Text(root, "UltCaption", "각성", 15, new Color32(255, 214, 110, 255), TextAnchor.LowerCenter, true);
            UIFactory.Place(caption.rectTransform, Vector2.zero, new Vector2(0.5f, 0f), new Vector2(width - UltSize * 0.5f, UltSize + 1f), new Vector2(UltSize + 20f, 20f));
            return v;
        }

        void Update()
        {
            var player = Game.Player;
            if (player == null || player.Skills == null) return;
            var prog = Game.Session.Progression;
            for (int i = 0; i < SkillGems.Slots; i++)
            {
                var gem = prog.Active(i);
                var shown = gem ?? SkillGems.ForSlot(player.Class, i);
                keys[i].text = Game.Input.GetBindingLabel(SkillGems.ActionFor(i));
                icons[i].enabled = shown != null;
                if (shown != null) icons[i].sprite = Game.Art.Get(shown.icon);
                bool open = gem != null;
                locks[i].enabled = !open;
                lockTexts[i].enabled = !open;
                if (!open)
                {
                    lockTexts[i].text = $"Lv.{Progression.SlotLevel(i)}";
                    icons[i].color = new Color(0.45f, 0.45f, 0.52f, 1f);
                    cooldowns[i].fillAmount = 0f;
                    costs[i].text = "";
                    timers[i].text = "";
                    if (i == SkillGems.UltimateSlot) frames[i].color = Color.Lerp(UltGold, Color.gray, 0.5f);
                    continue;
                }
                var n = player.Skills.Numbers(i);
                float p = player.Skills.CooldownProgress(i, out float left);
                cooldowns[i].fillAmount = 1f - p;
                timers[i].text = left > 0.05f ? (left >= 10f ? $"{left:0}" : $"{left:0.0}") : "";
                bool affordable = n.usesLife ? player.Health.Current > n.manaCost : player.Mana >= n.manaCost;
                icons[i].color = affordable ? Color.white : new Color(0.5f, 0.5f, 0.6f, 1f);
                costs[i].text = n.usesLife ? $"<color=#ff8080>{n.manaCost}</color>" : n.manaCost.ToString();
                if (i == SkillGems.UltimateSlot)
                {
                    // The awakening slot glows when it is ready.
                    float glow = left <= 0f && affordable ? 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 5f) : 0f;
                    frames[i].color = Color.Lerp(UltGold, Color.white, glow * 0.6f);
                }
            }
        }
    }

    /// <summary>Big banner across the screen when an awakening skill is cast ("각성 · 천검 강림").</summary>
    public class AwakeningBanner : MonoBehaviour
    {
        const float Life = 1.7f;
        CanvasGroup group;
        Image lineTop, lineBottom;
        Text caption, title;
        float shownAt = -10f;
        Color color = Color.white;

        public static AwakeningBanner Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "Awakening"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 150f), new Vector2(1400f, 112f));
            var b = root.gameObject.AddComponent<AwakeningBanner>();
            b.group = root.gameObject.AddComponent<CanvasGroup>();
            b.group.blocksRaycasts = false;
            b.group.interactable = false;
            b.group.alpha = 0f;
            UIFactory.Overlay(root, "Band", new Color(0.02f, 0.02f, 0.06f, 0.5f));
            b.lineTop = UIFactory.Image(root, "LineTop", Game.Art.Get("ui_white"), Color.white);
            b.lineTop.preserveAspect = false;
            UIFactory.Place(b.lineTop.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(1400f, 3f));
            b.lineBottom = UIFactory.Image(root, "LineBottom", Game.Art.Get("ui_white"), Color.white);
            b.lineBottom.preserveAspect = false;
            UIFactory.Place(b.lineBottom.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), Vector2.zero, new Vector2(1400f, 3f));
            b.caption = UIFactory.Text(root, "Caption", "— 각 성 —", 20, new Color32(255, 236, 180, 255), TextAnchor.UpperCenter, true);
            UIFactory.Place(b.caption.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -10f), new Vector2(600f, 28f));
            b.title = UIFactory.Text(root, "Title", "", 46, Color.white, TextAnchor.MiddleCenter, true);
            var outline = b.title.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0.1f, 0.05f, 0.02f, 0.95f);
            outline.effectDistance = new Vector2(3f, -3f);
            UIFactory.Place(b.title.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 12f), new Vector2(900f, 62f));
            return b;
        }

        void OnEnable() => GameEvents.Awakening += Show;

        void OnDisable() => GameEvents.Awakening -= Show;

        void Show(string skillName, Color themeColor)
        {
            title.text = skillName;
            color = themeColor;
            shownAt = Time.unscaledTime;
        }

        void Update()
        {
            float t = Time.unscaledTime - shownAt;
            if (t < 0f || t > Life)
            {
                if (group.alpha != 0f) group.alpha = 0f;
                return;
            }
            group.alpha = t < 0.12f ? t / 0.12f : t > Life - 0.4f ? (Life - t) / 0.4f : 1f;
            // The name slides in from the right, then drifts slowly.
            float k = Mathf.Clamp01(t / 0.2f);
            float slide = t < 0.2f ? Mathf.Lerp(260f, 0f, 1f - (1f - k) * (1f - k)) : -(t - 0.2f) * 18f;
            title.rectTransform.anchoredPosition = new Vector2(slide, 12f);
            title.color = Color.Lerp(Color.white, color, 0.5f);
            lineTop.color = lineBottom.color = color;
        }
    }
}
