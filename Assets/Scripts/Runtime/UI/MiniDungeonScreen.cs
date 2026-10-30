using System;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Daily XP-only, solo training instances, accessed from the dungeon guide or side menu.</summary>
    public sealed class MiniDungeonScreen : WindowScreen
    {
        static readonly Vector2 TL = new Vector2(0, 1);
        static readonly Color[] Accents = { new Color32(126, 214, 159, 255), new Color32(240, 188, 113, 255), new Color32(137, 197, 255, 255), new Color32(214, 140, 196, 255) };
        static readonly string[] Descriptions = { "이끼 낀 유적에서 검술과 마법을 연마합니다.", "고대 광맥의 수련장에서 실력을 다집니다.", "서리 감옥의 강적을 상대하며 성장합니다.", "메마른 협곡의 공허 짐승을 상대하며 단련합니다." }; // i18n-ignore
        const float RowWidth = 1220f, CardGap = 22f;
        static readonly int CardCount = MiniDungeonCatalog.All.Length;
        readonly Button[] enters = new Button[CardCount];
        readonly Text[] gates = new Text[CardCount];
        readonly RectTransform[] cards = new RectTransform[CardCount];
        readonly Image[] locks = new Image[CardCount]; // [UI 2026-10-30] P7 lock on a card above my level
        int laidOut = -1;
        Text remaining, status, recap, rule;
        Image budget;
        float nextRefresh;

        public static MiniDungeonScreen Create(Transform parent)
        {
            var w = CreateWindow<MiniDungeonScreen>(parent, "MiniDungeon", L.T("성장의 틈 · 미니던전"), "menuicon_dungeon");
            var top = Panel(w.content, "DailyBudget", TL, TL, Vector2.zero, new Vector2(1220, 88), UiTheme.HudPlate);
            Label(top.transform, "Title", L.T("경험치 집중 수련"), 26, TL, TL, new Vector2(22, -11), new Vector2(490, 36));
            w.rule = Label(top.transform, "Rule", L.T("매일 06:00 갱신 · 전체 구간 하루 총 30분 · 1인 자동사냥"), 17, TL, TL, new Vector2(22, -51), new Vector2(836, 26));
            UIFactory.OneLine(w.rule, true); // [UI 2026-10-30] up to the time bar, shrinks to 14 px at most
            Button(top.transform, "RefreshBudget", L.T("새로고침"), "ui_btngray", TL, TL, new Vector2(650, -15), new Vector2(140, 34), () => Game.MiniDungeon?.Refresh(), 16);
            w.remaining = Label(top.transform, "Remaining", "", 25, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -12), new Vector2(380, 36), TextAnchor.MiddleRight);
            var track = UIFactory.Image(top.transform, "Track", Game.Art.Get("ui_white"), new Color32(15, 24, 38, 255));
            track.preserveAspect = false;
            UIFactory.Place(track.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -60), new Vector2(326, 8));
            w.budget = UIFactory.Image(track.transform, "RemainingFill", Game.Art.Get("ui_white"), UiTheme.AccentBlue);
            w.budget.preserveAspect = false;
            UIFactory.Stretch(w.budget.rectTransform);
            w.budget.raycastTarget = false;
            for (int n = 0; n < CardCount; n++) w.BuildCard(n);
            w.status = Label(w.content, "Status", "", 19, TL, TL, new Vector2(8, -462), new Vector2(1204, 54));
            w.recap = Label(w.content, "SessionSummary", "", 19, TL, TL, new Vector2(8, -518), new Vector2(1204, 35));
            // [UI 2026-10-30] P5: the bottom small-print line is a "?" tip next to the title.
            UiHelpTip.Create(top.transform, "TipHelp", TL, new Vector2(600, -14), L.T("중도 퇴장해도 남은 시간으로 재입장할 수 있습니다. 이동·공격을 직접 입력하면 자동사냥이 멈춥니다."));
            return w;
        }

        void BuildCard(int index)
        {
            var def = MiniDungeonCatalog.All[index];
            var accent = Accents[index];
            var p = Panel(content, "Tier_" + def.Tier, TL, TL, new Vector2(index * 414, -108), new Vector2(392, 330), UiTheme.HudPlate);
            cards[index] = p.rectTransform;
            var stripe = UIFactory.Image(p.transform, "Accent", Game.Art.Get("ui_white"), accent);
            stripe.preserveAspect = false;
            UIFactory.Place(stripe.rectTransform, TL, TL, new Vector2(16, -16), new Vector2(360, 3));
            var crest = UIFactory.Image(p.transform, "Crest", Game.Art.Get("ui_circle"), new Color(accent.r, accent.g, accent.b, .14f));
            UIFactory.Place(crest.rectTransform, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -28), new Vector2(76, 76));
            var icon = UIFactory.Image(crest.transform, "TrainingIcon", Game.Art.Get("menuicon_dungeon"), accent);
            UIFactory.Place(icon.rectTransform, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(52, 52));
            icon.raycastTarget = false;
            var name = Label(p.transform, "TierName", L.T(def.Name), 24, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -108), new Vector2(354, 58), TextAnchor.MiddleCenter);
            name.color = accent;
            FitCardText(name, 16, 24);
            Label(p.transform, "Band", $"Lv.{def.MinLevel} ~ {def.MaxLevel}", 22, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -168), new Vector2(354, 30), TextAnchor.MiddleCenter);
            var description = Label(p.transform, "Description", L.T(Descriptions[index]), 17, new Vector2(.5f, 1), new Vector2(.5f, 1), new Vector2(0, -204), new Vector2(354, 58), TextAnchor.UpperCenter);
            FitCardText(description, UIFactory.ReadableFloor, 17);
            gates[index] = Label(p.transform, "Gate", "", 16, TL, TL, new Vector2(16, -264), new Vector2(354, 24), TextAnchor.MiddleCenter);
            enters[index] = Button(p.transform, "Enter", L.T("자동사냥 입장"), "ui_btn", new Vector2(1, 0), new Vector2(1, 0), new Vector2(-18, 8), new Vector2(250, 34), () => Game.MiniDungeon?.Enter(def.Tier), 18);
            FitCardText(gates[index], UIFactory.ReadableFloor, 16);
            FitCardText(enters[index].GetComponentInChildren<Text>(), UIFactory.ReadableFloor, 18);
            locks[index] = UIFactory.Image(crest.transform, "Lock", Game.Art.Get("ui_lock"), Color.white);
            locks[index].preserveAspect = true;
            locks[index].raycastTarget = false;
            UIFactory.Place(locks[index].rectTransform, new Vector2(1, 0), new Vector2(0.5f, 0.5f), new Vector2(-6, 6), new Vector2(34, 34));
            locks[index].gameObject.SetActive(false);
        }

        static void FitCardText(Text text, int min, int max)
        {
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = min;
            text.resizeTextMaxSize = max;
        }

        public void Open()
        {
            Game.MiniDungeon?.Refresh();
            Game.Flow.OpenWindow(this);
        }

        public static string TimeText(float seconds)
        {
            int n = Math.Max(0, (int)Math.Ceiling(seconds));
            return $"{n / 60:00}:{n % 60:00}";
        }

        protected override void Refresh()
        {
            var director = Game.MiniDungeon;
            if (director == null) return;
            var s = director.GetStatus();
            remaining.text = s.Ready ? L.F("오늘 남은 시간  {0}", TimeText(s.RemainingSeconds)) : L.T("이용 정보 확인 중...");
            budget.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(s.RemainingSeconds / MiniDungeonCatalog.CurrentDailySeconds), 1);
            LayoutCards();
            for (int i = 0; i < enters.Length; i++)
            {
                if (!MiniDungeonCatalog.Visible(MiniDungeonCatalog.All[i])) continue;
                bool allowed = director.CanEnter(MiniDungeonCatalog.All[i].Tier, out string why);
                enters[i].interactable = allowed && !s.Busy && !s.Active;
                var def = MiniDungeonCatalog.All[i];
                int currentLevel = Game.Session?.Progression.Level ?? 1;
                gates[i].text = !s.Ready ? L.T("시간 확인 중...") : s.Busy ? L.T("입장 처리 중...") : s.Active ? L.T("수련 진행 중")
                    : allowed ? L.T("입장 가능 · EXP 전용") : currentLevel < def.MinLevel || currentLevel > def.MaxLevel ? $"Lv.{def.MinLevel}~{def.MaxLevel} " + L.T("전용")
                    : s.RemainingSeconds <= 0 ? L.T("오늘 이용 완료") : L.T("입장 불가");
                gates[i].color = allowed ? Accents[i] : UiTheme.TextSecondary;
                locks[i].gameObject.SetActive(currentLevel < def.MinLevel);
            }
            int level = Game.Session?.Progression.Level ?? 1;
            status.text = s.Active ? L.T("수련 중에는 메뉴를 열어도 자동사냥과 이용 시간이 계속 진행됩니다.")
                : level >= Progression.LevelCap ? (Chapter3.Enabled ? L.T("최고 레벨을 달성했습니다. 성장용 미니던전은 Lv.10~49에서 이용할 수 있습니다.") : L.T("최고 레벨을 달성했습니다. 성장용 미니던전은 Lv.10~39에서 이용할 수 있습니다."))
                : director.CatchingUp ? L.F("내 레벨 Lv.{0} · <color=#ffe066>따라잡기 x{1}</color> · 처치당 {2:N0} EXP\n다음 메인 퀘스트가 Lv.{3}을 요구하는 동안만 적용되고, 그 레벨이 되면 평소({4:N0} EXP)로 돌아갑니다.",
                    level, MiniDungeonCatalog.CatchUpMultiplier, MiniDungeonCatalog.XpPerKill(level, true), director.CatchUpGate, MiniDungeonCatalog.XpPerKill(level))
                : L.F("내 레벨 Lv.{0} · 처치당 {1:N0} EXP\n다음 메인 퀘스트가 레벨 때문에 막혔을 때 오면 경험치가 {2}배(따라잡기)입니다.", level, MiniDungeonCatalog.XpPerKill(level), MiniDungeonCatalog.CatchUpMultiplier);
            int tier = level / 10 * 10;
            if (!s.Active && level >= 10 && level < Progression.LevelCap && !director.CanEnter(tier, out string blocked))
                status.text = L.T(blocked);
            recap.text = s.Kills > 0 ? L.F("이번 수련  <color=#ffe066>{0:N0} EXP</color> · {1}마리 처치", s.EarnedXp, s.Kills)
                : L.T("입장하면 자동사냥이 시작됩니다. 장비와 스킬에 따라 실제 획득 경험치가 달라집니다.");
        }

        /// <summary>[CH3] Three cards while chapter 3 is closed, four (tier 40 too) once it is open; the row is shared evenly.</summary>
        void LayoutCards()
        {
            int visible = 0;
            foreach (var d in MiniDungeonCatalog.All) if (MiniDungeonCatalog.Visible(d)) visible++;
            if (visible == laidOut) return;
            laidOut = visible;
            float width = (RowWidth - CardGap * (visible - 1)) / visible;
            int slot = 0;
            for (int i = 0; i < cards.Length; i++)
            {
                bool on = MiniDungeonCatalog.Visible(MiniDungeonCatalog.All[i]);
                cards[i].gameObject.SetActive(on);
                if (!on) continue;
                // Width only: the card's children are centred or edge-anchored, so they follow.
                cards[i].anchoredPosition = new Vector2(slot * (width + CardGap), -108);
                cards[i].sizeDelta = new Vector2(width, 330);
                // Labels and the accent stripe were built for the three-card row; keep them inside a narrower card.
                foreach (Transform child in cards[i])
                    if (child is RectTransform rt && rt.sizeDelta.x > width - 32f) rt.sizeDelta = new Vector2(width - 32f, rt.sizeDelta.y);
                slot++;
            }
            rule.text = Chapter3.Enabled ? L.T("매일 06:00 갱신 · 하루 총 30분(Lv.40~49 구간은 10분) · 1인 자동사냥") : L.T("매일 06:00 갱신 · 전체 구간 하루 총 30분 · 1인 자동사냥");
        }

        protected override bool PadNavigation => true;

        protected override void Update()
        {
            base.Update();
            if (TakesInput && !Game.Input.UsingGamepad && Game.Input.SubmitPressed)
            {
                int tier = (Game.Session?.Progression.Level ?? 1) / 10 * 10;
                if (Game.MiniDungeon != null && Game.MiniDungeon.CanEnter(tier, out _)) Game.MiniDungeon.Enter(tier);
            }
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .25f;
            Refresh();
        }
    }
}
