using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>[BOSS 2026-10-20] Top-right card during a 월드보스 / 길드보스 attempt: the 3-minute clock, my damage, 나가기.</summary>
    public sealed class BossRushHudView : MonoBehaviour
    {
        RectTransform card;
        Text title, time, damage;
        Image bar;
        int lastSecond = -1;
        long lastDamage = -1;

        public static void Create(Transform parent)
        {
            var root = UIFactory.Stretch(UIFactory.Rect(parent, "BossRushHud"));
            root.gameObject.AddComponent<BossRushHudView>().Build(root);
        }

        void Build(RectTransform root)
        {
            card = UIFactory.Place(UIFactory.Rect(root, "AttemptStatus"), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-DungeonHudView.ClockRight, -DungeonHudView.ClockTop), new Vector2(DungeonHudView.ClockWidth, 126));
            UIFactory.Stretch(UIFactory.Panel(card, "Frame", true).rectTransform);
            title = UIFactory.Text(card, "Kind", "", 18, UiTheme.Accent, TextAnchor.MiddleLeft, true);
            UIFactory.Place(title.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -10), new Vector2(286, 26));
            time = UIFactory.Text(card, "Time", "03:00", 29, UiTheme.AccentLight, TextAnchor.MiddleLeft, true);
            UIFactory.Place(time.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -38), new Vector2(120, 36));
            damage = UIFactory.Text(card, "Damage", "", 16, UiTheme.TextPrimary, TextAnchor.MiddleRight, true);
            UIFactory.Place(damage.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -38), new Vector2(176, 36));
            var track = UIFactory.Image(card, "TimeTrack", Game.Art.Get("ui_white"), new Color32(13, 22, 36, 255));
            track.preserveAspect = false;
            UIFactory.Place(track.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -77), new Vector2(288, 5));
            bar = UIFactory.Image(track.transform, "Fill", Game.Art.Get("ui_white"), UiTheme.Accent);
            bar.preserveAspect = false;
            bar.raycastTarget = false;
            UIFactory.Stretch(bar.rectTransform);
            var img = UIFactory.Image(card, "Leave", Game.Art.Get("ui_btngray"), Color.white);
            UIFactory.Place(img.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -89), new Vector2(288, 28));
            img.preserveAspect = false;
            var b = img.gameObject.AddComponent<Button>();
            b.targetGraphic = img;
            b.onClick.AddListener(() => Game.BossRush?.Leave());
            UiButton.Attach(b);
            var label = UIFactory.Text(img.transform, "Text", L.T("도전 마치기 (지금까지 피해 기록)"), 15, UiTheme.TextPrimary, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(label.rectTransform, 8, 1, 8, 1);
            card.gameObject.SetActive(false);
        }

        void Update()
        {
            var run = Game.BossRush;
            bool active = run?.Active == true;
            if (card.gameObject.activeSelf != active) card.gameObject.SetActive(active);
            if (!active) { lastSecond = -1; lastDamage = -1; return; }
            int second = Mathf.CeilToInt(run.RemainingSeconds);
            if (second == lastSecond && run.MyDamage == lastDamage) return;
            lastSecond = second;
            lastDamage = run.MyDamage;
            title.text = BossRushClient.KindName(run.Kind) + " · " + L.T("심연의 거신 지파크");
            time.text = MiniDungeonScreen.TimeText(run.RemainingSeconds);
            time.color = second <= 30 ? new Color32(255, 165, 128, 255) : UiTheme.AccentLight;
            damage.text = L.F("내 피해\n<color=#ffe066>{0:N0}</color>", run.MyDamage);
            bar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(run.RemainingSeconds / Mathf.Max(1f, run.AttemptSeconds)), 1);
        }
    }
}
