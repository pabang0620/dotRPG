using System;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [BOSS 2026-10-20] 월드보스 · 길드보스 window (key V, side menu, 할 일): the shared HP, today's tries, my best and rank,
    /// the rewards, and the button that starts a 3-minute attempt (BossRushDirector). The last attempt's result heads it.
    /// </summary>
    public sealed class BossRushScreen : OnlineWindow
    {
        public static BossRushScreen Instance { get; private set; }
        protected override bool PadNavigation => true;
        static readonly Vector2 TL = new Vector2(0f, 1f);

        string kind = BossRushClient.World;
        Button tabWorld, tabGuild, enter;
        Text result, bossName, hpText, info, rules, blocked;
        Image hpFill;
        float nextRefresh;

        public static BossRushScreen Create(Transform canvas)
        {
            var w = CreateWindow<BossRushScreen>(canvas, "BossRush", L.T("월드보스 · 길드보스"), "menuicon_worldboss");
            Instance = w;
            w.tabWorld = Button(w.content, "TabWorld", L.T("월드보스"), "ui_btn", TL, TL, Vector2.zero, new Vector2(170f, 42f), () => w.Pick(BossRushClient.World), 20);
            w.tabGuild = Button(w.content, "TabGuild", L.T("길드보스"), "ui_btngray", TL, TL, new Vector2(180f, 0f), new Vector2(170f, 42f), () => w.Pick(BossRushClient.Guild), 20);
            Button(w.content, "Ranking", L.T("랭킹 보기"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), Vector2.zero, new Vector2(170f, 42f),
                () => RankingScreen.OpenBoard(w.kind == BossRushClient.Guild ? RankingClient.GuildBoard : RankingClient.WorldBoard), 18);
            Button(w.content, "Refresh", L.T("새로고침"), "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-180f, 0f), new Vector2(140f, 42f), () => { BossRushClient.Refresh(); GuildClient.Refresh(); }, 18);
            w.result = Label(w.content, "Result", "", 19, TL, TL, new Vector2(0f, -52f), new Vector2(1220f, 30f), TextAnchor.MiddleLeft);

            var panel = Panel(w.content, "Boss", TL, TL, new Vector2(0f, -90f), new Vector2(1220f, 200f), UiTheme.HudPlate);
            w.bossName = Label(panel.transform, "Name", "", 28, TL, TL, new Vector2(24f, -14f), new Vector2(900f, 40f), TextAnchor.MiddleLeft);
            var track = UIFactory.Image(panel.transform, "HpTrack", Game.Art.Get("ui_white"), new Color32(15, 24, 38, 255));
            track.preserveAspect = false;
            UIFactory.Place(track.rectTransform, TL, TL, new Vector2(24f, -70f), new Vector2(1172f, 26f));
            w.hpFill = UIFactory.Image(track.transform, "Fill", Game.Art.Get("ui_white"), new Color32(196, 70, 90, 255));
            w.hpFill.preserveAspect = false;
            w.hpFill.raycastTarget = false;
            UIFactory.Stretch(w.hpFill.rectTransform);
            w.hpText = Label(track.transform, "HpText", "", 17, new Vector2(.5f, .5f), new Vector2(.5f, .5f), Vector2.zero, new Vector2(1160f, 26f), TextAnchor.MiddleCenter);
            w.info = Label(panel.transform, "Info", "", 19, TL, TL, new Vector2(24f, -108f), new Vector2(1172f, 84f));

            w.rules = Label(w.content, "Rules", "", 17, TL, TL, new Vector2(0f, -304f), new Vector2(800f, 230f));
            w.rules.color = UiTheme.TextSecondary;
            w.enter = Button(w.content, "Enter", L.T("도전하기 (3분)"), "ui_btn", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 40f), new Vector2(360f, 64f), w.Enter, 24);
            w.blocked = Label(w.content, "Blocked", "", 17, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(400f, 36f), TextAnchor.MiddleRight);
            w.blocked.color = new Color32(255, 159, 122, 255);
            w.status = Label(w.content, "Status", "", 18, new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(780f, 30f), TextAnchor.MiddleLeft);
            BossRushClient.Changed += () => { if (w.gameObject.activeInHierarchy) w.Refresh(); };
            return w;
        }

        /// <summary>Opens the window (V, side menu, 할 일) on the world boss tab.</summary>
        public static void Open(string kindTab = null)
        {
            if (Instance == null || Game.Flow == null) return;
            if (kindTab != null) Instance.kind = kindTab;
            Game.Flow.OpenWindow(Instance);
        }

        /// <summary>Picks the tab the next opening shows (할 일 opens the window itself).</summary>
        public static void SetTab(string kindTab) { if (Instance != null && kindTab != null) Instance.kind = kindTab; }

        /// <summary>After an attempt: the window on that boss with the result at the top.</summary>
        public void OpenResult()
        {
            if (BossRushDirector.LastKind != null) kind = BossRushDirector.LastKind;
            Game.Flow.OpenWindow(this);
        }

        public void Redraw() { if (gameObject.activeInHierarchy) Refresh(); }

        public override void Show()
        {
            if (Game.BossRush != null && Game.BossRush.Active && Game.BossRush.Kind != null) kind = Game.BossRush.Kind;
            base.Show();
            if (OnlineSession.Playing) { BossRushClient.Refresh(); GuildClient.Refresh(); }
        }

        void Pick(string k)
        {
            if (kind == k) return;
            kind = k;
            Game.Audio.PlaySfx("select", .5f);
            Refresh();
        }

        void Enter()
        {
            if (Game.BossRush == null) return;
            if (Game.BossRush.Enter(kind)) SetStatus(L.T("서버에 도전을 알리는 중..."));
        }

        protected override void Update()
        {
            base.Update();
            if (Time.unscaledTime < nextRefresh) return;
            nextRefresh = Time.unscaledTime + .5f;
            Refresh();
        }

        protected override void Refresh()
        {
            bool guild = kind == BossRushClient.Guild;
            tabWorld.GetComponent<Image>().sprite = Game.Art.Get(guild ? "ui_btngray" : "ui_btn");
            tabGuild.GetComponent<Image>().sprite = Game.Art.Get(guild ? "ui_btn" : "ui_btngray");
            result.text = string.IsNullOrEmpty(BossRushDirector.LastResultText) ? "" : L.T("<color=#8fe28f>지난 도전</color> · ") + BossRushDirector.LastResultText;
            rules.text = L.T("보스 방에서는 직업 간 피해가 비슷해지도록 보정됩니다.") + "\n" + (guild
                ? L.T("<b>길드보스</b>\n쓰러뜨리는 보스가 아니라 점수를 쌓습니다. 3분 동안 내가 준 피해가 이번 주 길드 점수에 더해집니다(AI 동료 피해 제외).\n길드원마다 하루 2회, 회당 3분 · 길드 가입 24시간 뒤부터 · Lv.40\n순위: 이번 주 길드 점수 합 → 같으면 먼저 도달한 길드\n보상(목요일 06:00 정산, 우편): 이번 주 도전한 길드원 20,000 골드\n1위 길드원 전원 칭호 '최강 길드' + 길드 이름 금색, 2~10위 길드 이름 은색")
                : L.T("<b>월드보스</b>\n모든 모험가가 같은 체력을 함께 깎습니다. 내 방에서 3분 동안 내가 준 피해만 기록됩니다(AI 동료 피해 제외).\n하루 3회, 회당 3분 · Lv.40 · 쓰러져도 그때까지의 피해가 남습니다\n순위: 이번 주 가장 높은 1회 피해량\n보상(목요일 06:00 정산, 우편): 참가 30,000 골드 + 강화석, 격파한 주 30,000 골드 추가\n1위 칭호 '세계를 꺾은 자' + 이름 금색, 2~10위 '거신 사냥꾼' + 이름 주황색, 11~100위 '거신에 맞선 자'"));

            if (!OnlineSession.Playing)
            {
                bossName.text = L.T("심연의 거신 지파크");
                hpFill.rectTransform.anchorMax = new Vector2(1f, 1f);
                hpText.text = "";
                info.text = L.T("온라인에서 열려요.");
                enter.interactable = false;
                blocked.text = L.T("온라인에서 열려요.");
                return;
            }
            var b = BossRushClient.Of(kind);
            string name = b != null && !string.IsNullOrEmpty(b.name) ? L.T(b.name) : L.T("심연의 거신 지파크");
            bossName.text = name;
            if (guild && b != null)
            {
                // Score, not HP: the bar shows my share of the guild's score this week.
                float share = b.score > 0 ? Mathf.Clamp01((float)b.myContribution / b.score) : 0f;
                hpFill.rectTransform.anchorMax = new Vector2(share, 1f);
                hpText.text = L.F("이번 주 길드 점수 <color=#ffe066>{0:N0}</color>   ·   내 기여 {1:N0} ({2:0.0}%)", b.score, b.myContribution, share * 100f);
            }
            else if (b != null && b.maxHp > 0)
            {
                hpFill.rectTransform.anchorMax = new Vector2(b.HpShare, 1f);
                hpText.text = b.Defeated && !guild ? L.T("격파! 이번 주 도전은 계속 랭킹에 기록됩니다")
                    : L.F("남은 체력 {0:N0} / {1:N0} ({2:0.0}%)", b.hpLeft, b.maxHp, b.HpShare * 100f);
            }
            else
            {
                hpFill.rectTransform.anchorMax = new Vector2(0f, 1f);
                hpText.text = BossRushClient.Loaded ? "" : L.T("불러오는 중...");
            }
            string week = BossRushClient.WeekEndsAt.HasValue ? L.F("이번 주 정산 {0:MM-dd HH:mm}", BossRushClient.WeekEndsAt.Value) : "";
            if (b == null) info.text = guild ? (GuildClient.InGuild ? L.T("길드보스 정보를 불러오는 중...") : L.T("길드에 들어가면 도전할 수 있어요. (길드 창: B)")) : week;
            else if (guild)
                info.text = L.F("오늘 남은 도전 <color=#ffe066>{0}</color> / {1}   ·   길드 순위 {2}   ·   {3}", b.attemptsLeft, b.attemptsPerDay,
                    b.guildRank > 0 ? L.F("{0}위", b.guildRank) : L.T("집계 중"), week);
            else
                info.text = L.F("오늘 남은 도전 <color=#ffe066>{0}</color> / {1}   ·   이번 주 내 최고 {2:N0}   ·   내 순위 {3}   ·   {4}", b.attemptsLeft, b.attemptsPerDay,
                    b.myBest, b.myRank > 0 ? L.F("{0}위", b.myRank) : L.T("기록 없음"), week);
            string reason = null;
            bool can = Game.BossRush != null && Game.BossRush.CanEnter(kind, out reason);
            enter.interactable = can;
            blocked.text = can ? "" : reason ?? "";
        }
    }
}
