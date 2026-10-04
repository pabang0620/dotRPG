using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Top-left status: a round level badge and three framed bars (HP, MP, EXP) on one grid. [UI] Every bar
    /// is a 9-slice groove (ui_bar) with the fill inset by the same padding, and every label fits inside its bar.
    /// </summary>
    public class StatusBarsView : MonoBehaviour
    {
        const float BarW = 296f, BarX = 76f, Pad = 5f; // [UI] 18 + 76 + 296 < UiTheme.HudCurrencyMaxRight (centred boss bar)
        // The ui_bar frame (96x24, inner dark slot x 9..86, y 6..17) is drawn with half the usual slice thickness, so one
        // source pixel is 0.75 UI px: the slot starts 7 px in from the sides and 5 px from top and bottom. The fill sits
        // exactly in that slot (it used to start 5 px in and cover the gold rim).
        const float FrameThin = 2f, InsetX = 7f, InsetY = 5f;

        Image hpFill, hpLag, mpFill, xpFill;
        Text hpText, mpText, levelText, xpText;
        float hpShown = 1f, hpLagShown = 1f;
        float fillW;

        public static StatusBarsView Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "Status"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -12f), new Vector2(BarX + BarW, 86f));
            var v = root.gameObject.AddComponent<StatusBarsView>();
            v.fillW = BarW - InsetX * 2f;

            // [ART] Generated gold-rimmed medallion (ui_level_badge); "LV" and the number sit on its dark centre.
            var badge = Img(root, "Badge", "ui_level_badge", Color.white);
            badge.preserveAspect = true;
            UIFactory.Place(badge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-4f, 2f), new Vector2(80f, 80f));
            var lvCaption = UIFactory.Text(badge.transform, "Lv", "LV", 11, new Color32(255, 214, 110, 255), TextAnchor.MiddleCenter, true);
            UIFactory.Place(lvCaption.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 12f), new Vector2(40f, 14f));
            v.levelText = UIFactory.Text(badge.transform, "Level", "1", 22, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Place(v.levelText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, -4f), new Vector2(48f, 26f));

            v.hpFill = Bar(root, "HP", 0f, 30f, 17, new Color32(214, 48, 49, 255), out v.hpLag, out v.hpText, v.fillW);
            v.mpFill = Bar(root, "MP", -31f, 26f, 15, new Color32(52, 120, 230, 255), out _, out v.mpText, v.fillW);
            v.xpFill = Bar(root, "XP", -58f, 24f, 14, new Color32(255, 196, 54, 255), out _, out v.xpText, v.fillW);
            return v;
        }

        static Image Img(Transform parent, string name, string sprite, Color color)
        {
            var img = UIFactory.Image(parent, name, Game.Art.Get(sprite), color);
            img.preserveAspect = false;
            return img;
        }

        static Image Bar(Transform parent, string name, float y, float h, int fontSize, Color color, out Image lag, out Text label, float fillW)
        {
            var frame = Img(parent, name, "ui_bar", Color.white);
            frame.pixelsPerUnitMultiplier *= FrameThin; // thinner rim so short bars keep a usable slot
            UIFactory.Place(frame.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(BarX, y), new Vector2(BarW, h));
            float fillH = h - InsetY * 2f;
            lag = Img(frame.transform, "Lag", "ui_white", new Color32(255, 235, 200, 170));
            UIFactory.Place(lag.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(InsetX, 0f), new Vector2(fillW, fillH));
            var fill = Img(frame.transform, "Fill", "ui_white", color);
            UIFactory.Place(fill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(InsetX, 0f), new Vector2(fillW, fillH));
            var shine = Img(fill.transform, "Shine", "ui_white", new Color(1f, 1f, 1f, 0.22f));
            shine.rectTransform.anchorMin = new Vector2(0f, 1f);
            shine.rectTransform.anchorMax = new Vector2(1f, 1f);
            shine.rectTransform.pivot = new Vector2(0.5f, 1f);
            shine.rectTransform.anchoredPosition = Vector2.zero;
            shine.rectTransform.sizeDelta = new Vector2(0f, Mathf.Max(2f, fillH * 0.35f));
            label = UIFactory.Text(frame.transform, "Text", "", fontSize, Color.white, TextAnchor.MiddleCenter, true);
            label.horizontalOverflow = HorizontalWrapMode.Overflow; // one line, centred on the bar
            UIFactory.Stretch(label.rectTransform, Pad, 0f, Pad, 1f);
            return fill;
        }

        void SetFill(Image fill, float ratio) => fill.rectTransform.sizeDelta = new Vector2(fillW * Mathf.Clamp01(ratio), fill.rectTransform.sizeDelta.y);

        void Update()
        {
            var player = Game.Player;
            if (player == null || Game.Session == null) return;
            var hp = player.Health;
            float hpRatio = hp.Max > 0 ? (float)hp.Current / hp.Max : 0f;
            hpShown = hpRatio;
            hpLagShown = hpLagShown > hpShown ? Mathf.MoveTowards(hpLagShown, hpShown, Time.unscaledDeltaTime * 0.6f) : hpShown;
            SetFill(hpFill, hpShown);
            hpFill.color = UiTheme.ColorBlind ? new Color32(240, 140, 30, 255) : new Color32(214, 48, 49, 255);
            SetFill(hpLag, hpLagShown);
            // [P5] Format only when a number changes (no string garbage every frame).
            if (hp.Current != shownHp || hp.Max != shownHpMax) { shownHp = hp.Current; shownHpMax = hp.Max; hpText.text = $"HP {hp.Current} / {hp.Max}"; }

            int maxMp = player.MaxMana;
            SetFill(mpFill, maxMp > 0 ? Game.Session.PlayerMana / maxMp : 0f);
            int mpNow = player.Mana;
            if (mpNow != shownMp || maxMp != shownMpMax) { shownMp = mpNow; shownMpMax = maxMp; mpText.text = maxMp > 0 ? $"MP {mpNow} / {maxMp}" : "MP 없음 (피의 마법)"; }

            var prog = Game.Session.Progression;
            if (prog.Level != shownLevel) { shownLevel = prog.Level; levelText.text = prog.Level.ToString(); }
            int need = prog.XpNeeded;
            float xpRatio = need > 0 ? (float)prog.Xp / need : 1f;
            SetFill(xpFill, xpRatio);
            int xpKey = need > 0 ? Mathf.FloorToInt(xpRatio * 1000f) : -2;
            if (xpKey != shownXp) { shownXp = xpKey; xpText.text = need > 0 ? $"EXP {xpRatio * 100f:0.0}%" : "EXP MAX"; }
        }

        int shownHp = -1, shownHpMax = -1, shownMp = -1, shownMpMax = -1, shownLevel = -1, shownXp = -1;
    }
}
