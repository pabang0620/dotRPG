using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [GUILD 2026-10-20] 길드 window (key B, side menu, phone menu): my guild (members, roles, applications, notice, join
    /// mode, leave / disband) or, without one, the guild search with apply and the make-a-guild form
    /// (Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 1). Esc goes back one step (form -> search -> game).
    /// Lists and row actions: GuildScreen.Lists.cs.
    /// </summary>
    public sealed partial class GuildScreen : OnlineWindow
    {
        public static GuildScreen Instance { get; private set; }
        protected override bool PadNavigation => focusField == null || !focusField.isFocused;
        static readonly Vector2 TL = new Vector2(0f, 1f), BL = new Vector2(0f, 0f), BR = new Vector2(1f, 0f), TR = new Vector2(1f, 1f);
        const int PageSize = 9;
        const float RowH = 44f, TableW = 1220f;

        enum Page { Mine, Find, Create }
        Page page = Page.Find;
        bool showApplications;
        int listPage;

        RectTransform mineRoot, findRoot, createRoot;
        // mine
        Text guildTitle, guildNotice, guildMeta;
        Button membersTab, appsTab, noticeBtn, modeBtn, leaveBtn, bossBtn;
        InputField noticeField;
        // find
        InputField searchField;
        Text findInfo;
        // create
        InputField nameField, createNoticeField;
        Button createModeBtn;
        Text createCost;
        bool createOpen = true;
        // shared list + paging
        Text emptyText, pageText;
        Button prevBtn, nextBtn;

        public static GuildScreen Create(Transform canvas)
        {
            var w = CreateWindow<GuildScreen>(canvas, "Guild", L.T("길드"), "menuicon_guild");
            Instance = w;
            w.BuildMine();
            w.BuildFind();
            w.BuildCreate();
            w.BuildList();
            w.status = Label(w.content, "Status", "", 18, BL, BL, Vector2.zero, new Vector2(820f, 30f), TextAnchor.MiddleLeft);
            GuildClient.Changed += () => { if (w.gameObject.activeInHierarchy) w.Refresh(); };
            return w;
        }

        void BuildMine()
        {
            mineRoot = UIFactory.Stretch(UIFactory.Rect(content, "Mine"));
            var top = Panel(mineRoot, "Top", TL, TL, Vector2.zero, new Vector2(TableW, 96f), UiTheme.HudPlate);
            guildTitle = Label(top.transform, "Title", "", 26, TL, TL, new Vector2(18f, -8f), new Vector2(700f, 38f), TextAnchor.MiddleLeft);
            guildMeta = Label(top.transform, "Meta", "", 17, TR, TR, new Vector2(-18f, -8f), new Vector2(480f, 38f), TextAnchor.MiddleRight);
            guildMeta.color = UiTheme.TextSecondary;
            guildNotice = Label(top.transform, "Notice", "", 18, TL, TL, new Vector2(18f, -52f), new Vector2(1180f, 34f), TextAnchor.MiddleLeft);
            membersTab = Button(mineRoot, "Members", L.T("길드원"), "ui_btn", TL, TL, new Vector2(0f, -106f), new Vector2(150f, 40f), () => { showApplications = false; listPage = 0; Refresh(); }, 19);
            appsTab = Button(mineRoot, "Apps", L.T("가입 신청"), "ui_btngray", TL, TL, new Vector2(160f, -106f), new Vector2(170f, 40f), () => { showApplications = true; listPage = 0; Refresh(); }, 19);
            noticeField = Field(mineRoot, "NoticeField", L.T("한 줄 공지 (60자)"), new Vector2(340f, -106f), new Vector2(330f, 40f));
            noticeField.characterLimit = GuildClient.NoticeMax;
            noticeBtn = Button(mineRoot, "NoticeSave", L.T("공지 저장"), "ui_btngray", TL, TL, new Vector2(678f, -106f), new Vector2(120f, 40f), SaveNotice, 17);
            modeBtn = Button(mineRoot, "Mode", "", "ui_btngray", TL, TL, new Vector2(806f, -106f), new Vector2(190f, 40f), ToggleMode, 17);
            bossBtn = Button(mineRoot, "Boss", L.T("길드보스"), "ui_btn", TR, TR, new Vector2(-110f, -106f), new Vector2(110f, 40f), () => BossRushScreen.Open(BossRushClient.Guild), 17);
            leaveBtn = Button(mineRoot, "Leave", L.T("나가기"), "ui_btngray", TR, TR, new Vector2(0f, -106f), new Vector2(100f, 40f), LeaveOrDisband, 17);
        }

        void BuildFind()
        {
            findRoot = UIFactory.Stretch(UIFactory.Rect(content, "Find"));
            searchField = Field(findRoot, "Search", L.T("길드 이름 검색"), Vector2.zero, new Vector2(330f, 42f));
            searchField.characterLimit = GuildClient.NameMax;
            searchField.onEndEdit.AddListener(_ => Search(1));
            Button(findRoot, "Go", L.T("검색"), "ui_btn", TL, TL, new Vector2(340f, 0f), new Vector2(110f, 42f), () => Search(1), 19);
            Button(findRoot, "Make", L.T("길드 만들기"), "ui_btn", TR, TR, Vector2.zero, new Vector2(170f, 42f), () => Show(Page.Create), 19);
            findInfo = Label(findRoot, "Info", "", 17, TL, TL, new Vector2(470f, 0f), new Vector2(560f, 42f), TextAnchor.MiddleLeft);
            findInfo.color = UiTheme.TextSecondary;
        }

        void BuildCreate()
        {
            createRoot = UIFactory.Stretch(UIFactory.Rect(content, "Create"));
            var panel = Panel(createRoot, "Panel", TL, TL, Vector2.zero, new Vector2(760f, 380f), new Color32(24, 36, 54, 235));
            Label(panel.transform, "Head", L.T("새 길드 만들기"), 24, TL, TL, new Vector2(24f, -16f), new Vector2(700f, 36f), TextAnchor.MiddleLeft);
            Label(panel.transform, "NameL", L.T("길드 이름"), 19, TL, TL, new Vector2(24f, -72f), new Vector2(160f, 42f), TextAnchor.MiddleLeft);
            nameField = Field(panel.transform, "Name", L.F("{0}~{1}자", GuildClient.NameMin, GuildClient.NameMax), new Vector2(190f, -72f), new Vector2(380f, 42f));
            nameField.characterLimit = GuildClient.NameMax;
            Label(panel.transform, "NoticeL", L.T("한 줄 공지"), 19, TL, TL, new Vector2(24f, -130f), new Vector2(160f, 42f), TextAnchor.MiddleLeft);
            createNoticeField = Field(panel.transform, "Notice", L.T("선택 (60자)"), new Vector2(190f, -130f), new Vector2(540f, 42f));
            createNoticeField.characterLimit = GuildClient.NoticeMax;
            Label(panel.transform, "ModeL", L.T("가입 방식"), 19, TL, TL, new Vector2(24f, -188f), new Vector2(160f, 42f), TextAnchor.MiddleLeft);
            createModeBtn = Button(panel.transform, "Mode", "", "ui_btngray", TL, TL, new Vector2(190f, -188f), new Vector2(260f, 42f), () => { createOpen = !createOpen; Refresh(); }, 18);
            createCost = Label(panel.transform, "Cost", "", 18, TL, TL, new Vector2(24f, -246f), new Vector2(712f, 54f));
            Button(panel.transform, "Make", L.T("만들기"), "ui_btn", new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(-110f, 20f), new Vector2(200f, 50f), Make, 21);
            Button(panel.transform, "Back", L.T("돌아가기"), "ui_btngray", new Vector2(.5f, 0f), new Vector2(.5f, 0f), new Vector2(110f, 20f), new Vector2(200f, 50f), () => Show(Page.Find), 21);
            var help = Panel(createRoot, "Help", TR, TR, Vector2.zero, new Vector2(440f, 380f), new Color32(24, 36, 54, 235));
            var t = Label(help.transform, "Text", L.F("<b>길드</b>\n· 최대 {0}명 · 길드장 1, 부길드장 최대 3\n· 바로 가입: 신청하면 바로 들어옵니다\n· 승인 후 가입: 길드장·부길드장이 받아 줍니다\n· 길드를 나오거나 내보내지면 24시간 동안 다른 길드에 못 들어갑니다\n· 해체는 길드장 혼자 남았을 때만\n· Lv.40 길드원은 길드보스에 도전해 길드 순위를 다툽니다", GuildClient.MaxMembers),
                17, TL, TL, new Vector2(20f, -18f), new Vector2(400f, 340f));
            t.color = UiTheme.TextSecondary;
        }

        /// <summary>Opens the window (B, side menu, phone menu, 할 일).</summary>
        public static void Open()
        {
            if (Instance == null || Game.Flow == null) return;
            Game.Flow.OpenWindow(Instance);
        }

        public override void Show()
        {
            page = GuildClient.InGuild ? Page.Mine : page == Page.Create ? Page.Create : Page.Find;
            listPage = 0;
            base.Show();
            if (OnlineSession.Playing)
            {
                GuildClient.Refresh();
                if (!GuildClient.InGuild) Search(1);
            }
        }

        /// <summary>Esc: the make-a-guild form goes back to the search, everything else closes.</summary>
        public override void Close()
        {
            if (page == Page.Create) { Show(Page.Find); return; }
            base.Close();
        }

        void Show(Page p)
        {
            page = p;
            listPage = 0;
            Game.Audio.PlaySfx("select", .5f);
            Refresh();
        }

        protected override void Update()
        {
            focusField = searchField != null && searchField.isFocused ? searchField : nameField != null && nameField.isFocused ? nameField
                : createNoticeField != null && createNoticeField.isFocused ? createNoticeField : noticeField != null && noticeField.isFocused ? noticeField : null;
            base.Update();
        }

        static void Report(GuildScreen w, bool ok, string msg)
        {
            if (w == null) return;
            w.SetStatus(msg, ok);
            Game.Audio.PlaySfx(ok ? "select" : "cancel");
        }

        void Search(int toPage)
        {
            if (!OnlineSession.Playing) return;
            GuildClient.Search(searchField != null ? searchField.text : "", toPage, (ok, msg) => { if (!ok) Report(this, false, msg); });
        }

        void Make()
        {
            GuildClient.Create(nameField.text, createNoticeField.text, createOpen, (ok, msg) =>
            {
                Report(this, ok, msg);
                if (ok) { nameField.text = createNoticeField.text = ""; page = Page.Mine; Refresh(); }
            });
        }

        void SaveNotice()
        {
            if (!GuildClient.IsStaff) return;
            GuildClient.UpdateSettings(noticeField.text, null, (ok, msg) => Report(this, ok, msg));
        }

        void ToggleMode()
        {
            if (!GuildClient.IsLeader || GuildClient.Guild == null) return;
            GuildClient.UpdateSettings(null, !GuildClient.Guild.Open, (ok, msg) => Report(this, ok, msg));
        }

        void LeaveOrDisband()
        {
            var g = GuildClient.Guild;
            if (g == null) return;
            if (GuildClient.IsLeader)
            {
                if (g.memberCount > 1) { Report(this, false, L.T("길드장은 다른 길드원에게 길드장을 넘긴 뒤 나갈 수 있습니다. 혼자 남으면 해체할 수 있습니다.")); return; }
                Game.UI.Confirm(L.F("길드 '{0}'을(를) 해체할까요?\n<size=18>되돌릴 수 없습니다.</size>", g.name), () => GuildClient.Disband((ok, msg) => Report(this, ok, msg)), overlay: true);
                return;
            }
            Game.UI.Confirm(L.F("길드 '{0}'에서 나갈까요?\n<size=18>24시간 동안 다른 길드에 들어갈 수 없습니다.</size>", g.name), () => GuildClient.Leave((ok, msg) => Report(this, ok, msg)), overlay: true);
        }

        protected override void Refresh()
        {
            if (!OnlineSession.Playing)
            {
                mineRoot.gameObject.SetActive(false);
                findRoot.gameObject.SetActive(false);
                createRoot.gameObject.SetActive(false);
                HideList(L.T("온라인에서 열려요."));
                return;
            }
            // Joining or leaving elsewhere switches the page by itself.
            if (GuildClient.InGuild && page != Page.Mine) page = Page.Mine;
            else if (GuildClient.Loaded && !GuildClient.InGuild && page == Page.Mine) page = Page.Find;
            mineRoot.gameObject.SetActive(page == Page.Mine);
            findRoot.gameObject.SetActive(page == Page.Find);
            createRoot.gameObject.SetActive(page == Page.Create);
            if (page == Page.Create) { RefreshCreate(); HideList(""); return; }
            if (page == Page.Find) { RefreshFind(); DrawListings(); return; }
            RefreshMine();
            if (showApplications && GuildClient.IsStaff) DrawApplications(); else { showApplications = false; DrawMembers(); }
        }

        void RefreshCreate()
        {
            TextOf(createModeBtn).text = createOpen ? L.T("바로 가입 ▶ 바꾸기") : L.T("승인 후 가입 ▶ 바꾸기");
            int level = Game.Session != null ? Game.Session.Progression.Level : 1;
            int gold = Game.Session != null ? Game.Session.Gold : 0;
            string lv = level >= GuildClient.CreateLevel ? "#8fe28f" : "#ff9f7a", gd = gold >= GuildClient.CreateGold ? "#8fe28f" : "#ff9f7a";
            createCost.text = L.F("필요: <color={0}>Lv.{1} 이상</color> · <color={2}>골드 {3:N0}</color> (가진 골드 {4:N0})", lv, GuildClient.CreateLevel, gd, GuildClient.CreateGold, gold);
        }

        void RefreshFind()
        {
            var parts = new List<string>();
            if (GuildClient.CoolingDown) parts.Add(L.F("<color=#ff9f7a>{0:MM-dd HH:mm}까지 가입 불가</color>", GuildClient.CooldownUntil.Value));
            if (GuildClient.MyApplications.Count > 0)
            {
                var names = new List<string>();
                foreach (var a in GuildClient.MyApplications) names.Add(a.guildName);
                parts.Add(L.F("신청 중: {0} ({1}/3)", string.Join(", ", names), GuildClient.MyApplications.Count));
            }
            findInfo.text = string.Join("   ·   ", parts);
        }

        void RefreshMine()
        {
            var g = GuildClient.Guild;
            if (g == null) return;
            guildTitle.text = "<b>" + Safe(g.name) + "</b>";
            guildMeta.text = L.F("{0} / {1}명 · {2} · 내 직위 <color=#ffe066>{3}</color>", g.memberCount, g.maxMembers, g.Open ? L.T("바로 가입") : L.T("승인 후 가입"), GuildClient.RoleName(g.myRole));
            guildNotice.text = string.IsNullOrEmpty(g.notice) ? L.T("<color=#8c96a8>공지가 없습니다.</color>") : L.F("<color=#ffd34a>공지</color>  {0}", g.notice.Replace("<", "＜").Replace(">", "＞"));
            bool staff = GuildClient.IsStaff, leader = GuildClient.IsLeader;
            appsTab.gameObject.SetActive(staff);
            TextOf(appsTab).text = g.applications.Count > 0 ? L.F("가입 신청 ({0})", g.applications.Count) : L.T("가입 신청");
            membersTab.GetComponent<Image>().sprite = Game.Art.Get(showApplications ? "ui_btngray" : "ui_btn");
            appsTab.GetComponent<Image>().sprite = Game.Art.Get(showApplications ? "ui_btn" : "ui_btngray");
            noticeField.gameObject.SetActive(staff);
            noticeBtn.gameObject.SetActive(staff);
            if (staff && !noticeField.isFocused && string.IsNullOrEmpty(noticeField.text)) noticeField.text = g.notice ?? "";
            modeBtn.gameObject.SetActive(leader);
            TextOf(modeBtn).text = g.Open ? L.T("바로 가입 ▶ 승인제로") : L.T("승인 후 가입 ▶ 바로로");
            TextOf(leaveBtn).text = leader ? L.T("해체") : L.T("나가기");
            leaveBtn.interactable = !GuildClient.Busy;
            bossBtn.interactable = true;
        }
    }
}
