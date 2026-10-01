using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Top-left status: level badge, HP bar, MP bar and a thin experience bar.</summary>
    public class StatusBarsView : MonoBehaviour
    {
        const float BarW = 300f;

        Image hpFill, hpLag, mpFill, xpFill;
        Text hpText, mpText, levelText, xpText;
        float hpShown = 1f, hpLagShown = 1f;

        public static StatusBarsView Create(Transform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "Status"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -14f), new Vector2(390f, 80f));
            var v = root.gameObject.AddComponent<StatusBarsView>();

            var badge = Img(root, "Badge", "ui_btn", Color.white);
            UIFactory.Place(badge.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(70f, 70f));
            var lvCaption = UIFactory.Text(badge.transform, "Lv", "레벨", UiTheme.FontMin, new Color(1f, 1f, 1f, 0.85f), TextAnchor.UpperCenter, true);
            UIFactory.Stretch(lvCaption.rectTransform, 0f, 0f, 0f, 6f);
            v.levelText = UIFactory.Text(badge.transform, "Level", "1", 30, Color.white, TextAnchor.LowerCenter, true);
            UIFactory.Stretch(v.levelText.rectTransform, 0f, 6f, 0f, 0f);

            v.hpFill = Bar(root, "HP", new Vector2(80f, -2f), 24f, new Color32(214, 48, 49, 255), out v.hpLag, out v.hpText);
            v.mpFill = Bar(root, "MP", new Vector2(80f, -32f), 20f, new Color32(52, 120, 230, 255), out _, out v.mpText);
            v.xpFill = Bar(root, "XP", new Vector2(80f, -58f), 12f, new Color32(255, 206, 64, 255), out _, out v.xpText);
            // [UI] Minimum readable sizes (the thin bars would otherwise get 13-15px labels).
            v.mpText.fontSize = UiTheme.FontMin;
            v.xpText.fontSize = 14; // the 12px EXP bar cannot hold more; it overflows the bar slightly
            return v;
        }

        static Image Img(Transform parent, string name, string sprite, Color color)
        {
            var img = UIFactory.Image(parent, name, Game.Art.Get(sprite), color);
            img.preserveAspect = false;
            return img;
        }

        static Image Bar(Transform parent, string name, Vector2 pos, float h, Color color, out Image lag, out Text label)
        {
            var frame = Img(parent, name, "ui_white", new Color32(12, 14, 20, 230));
            UIFactory.Place(frame.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, new Vector2(BarW + 4f, h + 4f));
            lag = Img(frame.transform, "Lag", "ui_white", new Color32(255, 235, 200, 200));
            UIFactory.Place(lag.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(BarW, h));
            var fill = Img(frame.transform, "Fill", "ui_white", color);
            UIFactory.Place(fill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(BarW, h));
            var shine = Img(fill.transform, "Shine", "ui_white", new Color(1f, 1f, 1f, 0.18f));
            UIFactory.Place(shine.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(BarW, h * 0.4f));
            shine.rectTransform.anchorMax = new Vector2(1f, 1f);
            shine.rectTransform.sizeDelta = new Vector2(0f, h * 0.4f);
            label = UIFactory.Text(frame.transform, "Text", "", Mathf.RoundToInt(h * 0.75f), Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(label.rectTransform);
            return fill;
        }

        static void SetFill(Image fill, float ratio) => fill.rectTransform.sizeDelta = new Vector2(BarW * Mathf.Clamp01(ratio), fill.rectTransform.sizeDelta.y);

        void Update()
        {
            var player = Game.Player;
            if (player == null || Game.Session == null) return;
            var hp = player.Health;
            float hpRatio = hp.Max > 0 ? (float)hp.Current / hp.Max : 0f;
            hpShown = hpRatio;
            hpLagShown = hpLagShown > hpShown ? Mathf.MoveTowards(hpLagShown, hpShown, Time.unscaledDeltaTime * 0.6f) : hpShown;
            SetFill(hpFill, hpShown);
            SetFill(hpLag, hpLagShown);
            hpText.text = $"HP {hp.Current} / {hp.Max}";

            int maxMp = player.MaxMana;
            SetFill(mpFill, maxMp > 0 ? Game.Session.PlayerMana / maxMp : 0f);
            mpText.text = maxMp > 0 ? $"MP {player.Mana} / {maxMp}" : "MP 없음 (피의 마법)";

            var prog = Game.Session.Progression;
            levelText.text = prog.Level.ToString();
            int need = prog.XpNeeded;
            SetFill(xpFill, need > 0 ? (float)prog.Xp / need : 1f);
            xpText.text = need > 0 ? $"EXP {prog.Xp} / {need}" : "MAX";
        }
    }
}
