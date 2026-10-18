using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public sealed class MiniDungeonHudView : MonoBehaviour
    {
        RectTransform card;
        Text time, counters, mode;
        Image bar;
        Button toggle;
        Text toggleText;
        int lastSecond = -1, lastKills = -1, lastXp = -1;

        public static void Create(Transform parent)
        {
            var root = UIFactory.Stretch(UIFactory.Rect(parent, "MiniDungeonHud"));
            var view = root.gameObject.AddComponent<MiniDungeonHudView>();
            view.Build(root);
        }

        void Build(RectTransform root)
        {
            card = UIFactory.Place(UIFactory.Rect(root, "TrainingStatus"), new Vector2(1, 1), new Vector2(1, 1),
                new Vector2(-DungeonHudView.ClockRight, -DungeonHudView.ClockTop), new Vector2(DungeonHudView.ClockWidth, 126));
            UIFactory.Stretch(UIFactory.Panel(card, "Frame", true).rectTransform);
            mode = UIFactory.Text(card, "Mode", L.T("성장의 틈 · 자동 수련"), 18, UiTheme.AccentBlue, TextAnchor.MiddleLeft, true);
            UIFactory.Place(mode.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -10), new Vector2(286, 26));
            time = UIFactory.Text(card, "Time", "30:00", 29, UiTheme.AccentLight, TextAnchor.MiddleLeft, true);
            UIFactory.Place(time.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -38), new Vector2(120, 36));
            counters = UIFactory.Text(card, "Counters", "", 16, UiTheme.TextPrimary, TextAnchor.MiddleRight, true);
            UIFactory.Place(counters.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-16, -38), new Vector2(176, 36));
            var track = UIFactory.Image(card, "TimeTrack", Game.Art.Get("ui_white"), new Color32(13, 22, 36, 255));
            track.preserveAspect = false;
            UIFactory.Place(track.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -77), new Vector2(288, 5));
            bar = UIFactory.Image(track.transform, "Fill", Game.Art.Get("ui_white"), UiTheme.AccentBlue);
            bar.preserveAspect = false;
            UIFactory.Stretch(bar.rectTransform); bar.raycastTarget = false;
            toggle = MakeButton(card, "ToggleHunt", L.T("사냥 중지"), new Vector2(16, -89), 135, () => QuestAutoPilot.ToggleHunt());
            toggleText = toggle.GetComponentInChildren<Text>();
            MakeButton(card, "Leave", L.T("퇴장"), new Vector2(161, -89), 143, () => Game.MiniDungeon?.Leave());
            card.gameObject.SetActive(false);
        }

        static Button MakeButton(Transform parent, string name, string text, Vector2 at, float width, UnityEngine.Events.UnityAction action)
        {
            var img = UIFactory.Image(parent, name, Game.Art.Get("ui_btngray"), Color.white);
            UIFactory.Place(img.rectTransform, new Vector2(0, 1), new Vector2(0, 1), at, new Vector2(width, 28));
            img.preserveAspect = false;
            var b = img.gameObject.AddComponent<Button>(); b.targetGraphic = img; b.onClick.AddListener(action); UiButton.Attach(b);
            var label = UIFactory.Text(img.transform, "Text", text, 15, UiTheme.TextPrimary, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(label.rectTransform, 8, 1, 8, 1);
            return b;
        }

        void Update()
        {
            var run = Game.MiniDungeon;
            bool active = run?.Active == true;
            if (card.gameObject.activeSelf != active) card.gameObject.SetActive(active);
            if (!active) { lastSecond = -1; return; }
            toggle.interactable = !run.Busy;
            bool hunting = QuestAutoPilot.Hunting;
            // Catch-up (x10 while the next main quest waits on level) is the mini dungeon's own rule, so it heads the card.
            bool catchUp = run.CatchingUp;
            mode.text = (catchUp ? L.F("따라잡기 x{0}", MiniDungeonCatalog.CatchUpMultiplier) : L.T("성장의 틈")) + " · " + (hunting ? L.T("자동 수련") : L.T("직접 조작 중"));
            mode.color = catchUp ? UiTheme.Accent : UiTheme.AccentBlue;
            toggleText.text = hunting ? L.T("사냥 중지") : L.T("자동사냥 시작");
            int second = Mathf.CeilToInt(run.RemainingSeconds);
            if (lastSecond == second && lastKills == run.Kills && lastXp == run.EarnedXp) return;
            lastSecond = second; lastKills = run.Kills; lastXp = run.EarnedXp;
            time.text = MiniDungeonScreen.TimeText(run.RemainingSeconds);
            time.color = second <= 60 ? new Color32(255, 165, 128, 255) : UiTheme.AccentLight;
            counters.text = $"<color=#ffe066>{run.EarnedXp:N0} EXP</color>\n" + L.F("{0}마리 처치", run.Kills);
            bar.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(run.RemainingSeconds / MiniDungeonCatalog.DailySeconds), 1);
        }
    }
}
