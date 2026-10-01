using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>[ONLINE] Shared helpers of the online preview windows (party finder, auction house).</summary>
    public abstract class OnlineWindow : WindowScreen
    {
        public const string PreviewTag = "오프라인 미리보기 (서버 연결 전)";
        protected Text status;
        protected InputField focusField;

        protected static readonly Color RowA = new Color32(30, 45, 66, 235), RowB = new Color32(26, 39, 58, 235), HeadRow = new Color32(18, 26, 40, 255);

        protected static Text TextOf(Button b) => b.GetComponentInChildren<Text>();

        protected void PreviewBanner()
        {
            var banner = Panel(content, "Preview", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(330f, 40f), new Color32(120, 70, 20, 230));
            Label(banner.transform, "Text", "⚠ " + PreviewTag, 17, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(320f, 36f), TextAnchor.MiddleCenter);
            status = Label(content, "Status", "", 18, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(900f, 30f), TextAnchor.MiddleLeft);
        }

        protected void SetStatus(string msg, bool ok = true) { if (status != null) status.text = ok ? $"<color=#8fe28f>{msg}</color>" : $"<color=#ff9f7a>{msg}</color>"; }

        protected InputField Field(Transform parent, string name, string placeholder, Vector2 pos, Vector2 size)
        {
            var bg = Panel(parent, name, new Vector2(0f, 1f), new Vector2(0f, 1f), pos, size, new Color32(12, 18, 28, 255));
            bg.raycastTarget = true;
            var text = UIFactory.Text(bg.transform, "Text", "", 19, Color.white, TextAnchor.MiddleLeft);
            UIFactory.Stretch(text.rectTransform, 10f, 2f, 10f, 2f);
            text.supportRichText = false;
            var ph = UIFactory.Text(bg.transform, "Placeholder", placeholder, 18, new Color32(140, 150, 170, 255), TextAnchor.MiddleLeft);
            UIFactory.Stretch(ph.rectTransform, 10f, 2f, 10f, 2f);
            var f = bg.gameObject.AddComponent<InputField>();
            f.textComponent = text;
            f.placeholder = ph;
            f.characterLimit = 20;
            f.targetGraphic = bg;
            return f;
        }

        /// <summary>A list row with a background stripe.</summary>
        protected static RectTransform Row(Transform parent, int i, float top, float height, float width)
        {
            var bg = Panel(parent, "Row" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, top - i * height), new Vector2(width, height - 2f), i % 2 == 0 ? RowA : RowB);
            return bg.rectTransform;
        }

        protected static Text Cell(Transform row, string name, float x, float w, int size = 18, TextAnchor align = TextAnchor.MiddleLeft) =>
            Label(row, name, "", size, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(x, 0f), new Vector2(w, 34f), align);

        protected override void Update()
        {
            if (focusField != null && focusField.isFocused) return; // typing must not close the window
            base.Update();
        }
    }

    // =====================================================================================

    /// <summary>[ONLINE] 파티 찾기: 모집 게시판, 모집 글 등록, 자동 매칭 (PLAN_ONLINE §2.1). Mock data offline.</summary>
    public class PartyFinderScreen : OnlineWindow
    {
        public static PartyFinderScreen Instance { get; private set; }
        const int PageSize = 9;
        const float RowH = 44f, TableW = 1220f, ListTop = -104f;

        IPartyFinderService Service => OnlineServices.PartyFinder;
        bool createTab;
        int page;
        int filter = -1; // -1 = every dungeon
        RectTransform listRoot, createRoot;
        Button tabList, tabCreate, filterBtn, queueBtn, aiBtn, cancelQueueBtn;
        Text queueText, pageText;
        readonly List<(RectTransform row, Text dungeon, Text diff, Text members, Text power, Text leader, Text msg, Button apply)> rows = new List<(RectTransform, Text, Text, Text, Text, Text, Text, Button)>();
        List<PartyPost> shown = new List<PartyPost>();
        // create tab
        int cDungeon, cDiff, cMembers = 4, cPowerPct = 80, cMsg;
        Text createSummary;

        static List<DungeonDef> Dungeons => new List<DungeonDef>(DungeonDatabase.Weekday) { DungeonDatabase.SkeletonKing };

        public static PartyFinderScreen Create(Transform canvas)
        {
            var w = CreateWindow<PartyFinderScreen>(canvas, "PartyFinder", "파티 찾기", "menuicon_party");
            Instance = w;
            w.PreviewBanner();
            w.tabList = Button(w.content, "TabList", "모집 목록", "ui_btn", new Vector2(0f, 1f), new Vector2(0f, 1f), Vector2.zero, new Vector2(150f, 42f), () => { w.createTab = false; w.Refresh(); }, 20);
            w.tabCreate = Button(w.content, "TabCreate", "모집 글 등록", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(160f, 0f), new Vector2(150f, 42f), () => { w.createTab = true; w.Refresh(); }, 20);
            // Auto-match panel (top right, left of the preview banner).
            w.queueBtn = Button(w.content, "Queue", "자동 매칭", "ui_btn", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(330f, 0f), new Vector2(130f, 42f), w.ToggleQueue, 20);
            w.queueText = Label(w.content, "QueueText", "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(470f, 0f), new Vector2(240f, 42f), TextAnchor.MiddleLeft);
            w.aiBtn = Button(w.content, "FillAi", "AI로 채워 출발", "ui_btn", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(715f, 0f), new Vector2(160f, 42f), w.DepartWithAi, 18);

            // ----- list tab -----
            w.listRoot = UIFactory.Stretch(UIFactory.Rect(w.content, "List"));
            w.filterBtn = Button(w.listRoot, "Filter", "던전: 전체", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -52f), new Vector2(260f, 38f), w.CycleFilter, 18);
            Button(w.listRoot, "Refresh", "새로고침", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(270f, -52f), new Vector2(120f, 38f), () => { w.SetStatus("목록을 새로 불러왔습니다."); w.Refresh(); }, 18);
            var head = Panel(w.listRoot, "Head", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, ListTop + 8f), new Vector2(TableW, 30f), HeadRow);
            string[] heads = { "던전", "난이도", "인원", "최소 전투력", "방장", "메시지" };
            float[] hx = { 12f, 232f, 342f, 422f, 572f, 792f };
            for (int i = 0; i < heads.Length; i++) Label(head.transform, "H" + i, heads[i], 16, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(hx[i], 0f), new Vector2(200f, 28f), TextAnchor.MiddleLeft).color = new Color32(184, 196, 216, 255);
            for (int i = 0; i < PageSize; i++)
            {
                var r = Row(w.listRoot, i, ListTop - 24f, RowH, TableW);
                int idx = i;
                var apply = Button(r, "Apply", "참가 신청", "ui_btn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-8f, 0f), new Vector2(120f, 36f), () => w.ApplyRow(idx), 17);
                w.rows.Add((r, Cell(r, "Dungeon", 12f, 215f), Cell(r, "Diff", 232f, 105f), Cell(r, "Members", 342f, 75f), Cell(r, "Power", 422f, 145f),
                    Cell(r, "Leader", 572f, 215f, 17), Cell(r, "Msg", 792f, 290f, 17), apply));
            }
            Button(w.listRoot, "Prev", "◀", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-180f, 0f), new Vector2(50f, 34f), () => { w.page = Math.Max(0, w.page - 1); w.Refresh(); }, 18);
            w.pageText = Label(w.listRoot, "Page", "", 18, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 0f), new Vector2(110f, 34f), TextAnchor.MiddleCenter);
            Button(w.listRoot, "Next", "▶", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(50f, 34f), () => { w.page++; w.Refresh(); }, 18);

            // ----- create tab -----
            w.createRoot = UIFactory.Stretch(UIFactory.Rect(w.content, "Create"));
            var panel = Panel(w.createRoot, "Panel", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -56f), new Vector2(760f, 460f), new Color32(24, 36, 54, 235));
            string[] labels = { "던전", "난이도", "모집 인원", "최소 전투력", "메시지" };
            Action[] cycles = { () => w.cDungeon++, () => w.cDiff++, () => w.cMembers = w.cMembers >= 4 ? 2 : w.cMembers + 1, () => w.cPowerPct = w.cPowerPct >= 100 ? 50 : w.cPowerPct + 10, () => w.cMsg++ };
            for (int i = 0; i < labels.Length; i++)
            {
                Label(panel.transform, "L" + i, labels[i], 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -24f - i * 62f), new Vector2(160f, 44f), TextAnchor.MiddleLeft);
                int k = i;
                Button(panel.transform, "C" + i, "바꾸기 ▶", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(560f, -24f - i * 62f), new Vector2(160f, 44f), () => { cycles[k](); w.Refresh(); }, 18);
            }
            w.createSummary = Label(panel.transform, "Summary", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(190f, -24f), new Vector2(360f, 310f));
            w.createSummary.lineSpacing = 2.25f;
            Button(panel.transform, "Post", "모집 글 등록", "ui_btn", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-110f, 24f), new Vector2(200f, 50f), w.Post, 21);
            Button(panel.transform, "CancelPost", "모집 취소", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(110f, 24f), new Vector2(200f, 50f), w.CancelPost, 21);
            var help = Panel(w.createRoot, "Help", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -56f), new Vector2(440f, 460f), new Color32(24, 36, 54, 235));
            Label(help.transform, "Text", "<b>파티 모집 안내</b>\n\n· 모집 글은 10분 뒤 자동으로 내려갑니다.\n· 최소 전투력은 내 전투력의 50~100%로 정합니다.\n· 인원이 모자라면 입장할 때 AI 용병이 빈자리를 채웁니다.\n· 자동 매칭은 60초 동안 같은 던전·난이도의 모험가를 찾고, 시간이 지나면 AI로 채워 출발합니다.\n\n<color=#ffb066>서버 연결 전에는 다른 모험가가 실제로 참가하지 않습니다.</color>",
                19, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(392f, 420f));
            return w;
        }

        int MyPower => Game.Player != null ? CharacterStats.Power(Game.Player.Class) : 0;

        void CycleFilter()
        {
            filter++;
            if (filter >= Dungeons.Count) filter = -1;
            page = 0;
            Refresh();
        }

        void ToggleQueue()
        {
            var q = Service.Queue;
            if (q.active) { Service.CancelQueue(); SetStatus("자동 매칭을 취소했습니다."); }
            else
            {
                var d = Dungeons[Mod(cDungeon, Dungeons.Count)];
                Service.StartQueue(d.id, d.isRaid ? DungeonDifficulty.Normal : (DungeonDifficulty)Mod(cDiff, DungeonDatabase.DifficultyCount));
                SetStatus($"{d.name} 자동 매칭을 시작했습니다.");
            }
            Refresh();
        }

        void DepartWithAi()
        {
            if (!Service.Queue.active) { SetStatus("매칭 대기 중이 아닙니다.", false); return; }
            int humans = Service.Queue.humans;
            int ai = Service.DepartWithAi();
            SetStatus($"사람 {humans}명 + AI 용병 {ai}명으로 출발합니다. (오프라인: 던전 선택 창에서 입장하세요)");
            Refresh();
        }

        void ApplyRow(int idx)
        {
            int i = page * PageSize + idx;
            if (i >= shown.Count) return;
            var msg = Service.Apply(shown[i].id, MyPower);
            SetStatus(msg, shown[i].applied);
            Refresh();
        }

        void Post()
        {
            var d = Dungeons[Mod(cDungeon, Dungeons.Count)];
            var diff = d.isRaid ? DungeonDifficulty.Normal : (DungeonDifficulty)Mod(cDiff, DungeonDatabase.DifficultyCount);
            Service.Create(d.id, diff, cMembers, MyPower * cPowerPct / 100, PartyFinderRules.PresetMessages[Mod(cMsg, PartyFinderRules.PresetMessages.Length)]);
            SetStatus("모집 글을 등록했습니다. 목록 맨 위에 표시됩니다.");
            createTab = false;
            page = 0;
            Refresh();
        }

        void CancelPost()
        {
            var mine = Service.List(null).FirstOrDefault(p => p.mine);
            SetStatus(mine != null && Service.Cancel(mine.id) ? "모집을 취소했습니다." : "등록한 모집 글이 없습니다.", mine != null);
            Refresh();
        }

        static int Mod(int a, int n) => ((a % n) + n) % n;

        float tickAcc;
        protected override void Update()
        {
            base.Update();
            tickAcc += Time.unscaledDeltaTime;
            if (tickAcc >= 0.25f) { tickAcc = 0f; RefreshQueue(); }
        }

        void RefreshQueue()
        {
            var q = Service.Queue;
            if (q.active)
            {
                int s = Mathf.FloorToInt(q.elapsed);
                queueText.text = $"<color=#ffe066>매칭 중 {s / 60:00}:{s % 60:00} / 01:00</color>  {q.humans}/4명";
            }
            else queueText.text = "<color=#8c96a8>매칭 대기 없음</color>";
            TextOf(queueBtn).text = q.active ? "매칭 취소" : "자동 매칭";
            aiBtn.interactable = q.active;
        }

        protected override void Refresh()
        {
            listRoot.gameObject.SetActive(!createTab);
            createRoot.gameObject.SetActive(createTab);
            tabList.image.sprite = Game.Art.Get(createTab ? "ui_btngray" : "ui_btn");
            tabCreate.image.sprite = Game.Art.Get(createTab ? "ui_btn" : "ui_btngray");
            RefreshQueue();
            var ds = Dungeons;
            TextOf(filterBtn).text = filter < 0 ? "던전: 전체" : "던전: " + ds[filter].name;
            shown = Service.List(filter < 0 ? null : ds[filter].id).ToList();
            int pages = Math.Max(1, (shown.Count + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            pageText.text = $"{page + 1} / {pages}";
            int power = MyPower;
            for (int r = 0; r < rows.Count; r++)
            {
                int i = page * PageSize + r;
                var row = rows[r];
                row.row.gameObject.SetActive(i < shown.Count);
                if (i >= shown.Count) continue;
                var p = shown[i];
                row.dungeon.text = (p.mine ? "<color=#ffe066>★</color> " : "") + p.dungeonName;
                row.diff.text = $"<color={PartyFinderRules.DifficultyColor(p.difficulty)}>{PartyFinderRules.DifficultyName(p.difficulty)}</color>";
                row.members.text = p.Full ? $"<color=#8c96a8>{p.members}/{p.maxMembers}</color>" : $"{p.members}/{p.maxMembers}";
                row.power.text = power >= p.minPower ? $"{p.minPower:N0}" : $"<color=#ff9f7a>{p.minPower:N0}</color>";
                row.leader.text = $"{p.leaderName} Lv{p.leaderLevel} {CharacterClassInfo.Get(p.leaderClass).displayName}";
                row.msg.text = p.message;
                string label = p.mine ? "내 모집 글" : p.applied ? "신청 중…" : p.Full ? "모집 완료" : power < p.minPower ? "전투력 부족" : "참가 신청";
                TextOf(row.apply).text = label;
                bool can = label == "참가 신청";
                row.apply.interactable = can;
                row.apply.image.sprite = Game.Art.Get(can ? "ui_btn" : "ui_btngray");
            }
            var d = ds[Mod(cDungeon, ds.Count)];
            var diff = d.isRaid ? DungeonDifficulty.Normal : (DungeonDifficulty)Mod(cDiff, DungeonDatabase.DifficultyCount);
            createSummary.text = $"{d.name}\n<color={PartyFinderRules.DifficultyColor(diff)}>{(d.isRaid ? "레이드" : PartyFinderRules.DifficultyName(diff))}</color>\n{cMembers}명\n{power * cPowerPct / 100:N0} (내 전투력의 {cPowerPct}%)\n“{PartyFinderRules.PresetMessages[Mod(cMsg, PartyFinderRules.PresetMessages.Length)]}”";
        }

        // ---------- dev hooks ----------
        public int DevVisibleRows => rows.Count(r => r.row.gameObject.activeSelf);
        public string DevRowText(int r) => r < rows.Count ? rows[r].dungeon.text + " | " + rows[r].members.text + " | " + TextOf(rows[r].apply).text : "";
        public void DevPost() => Post();
        public void DevToggleQueue() => ToggleQueue();
        public void DevDepart() => DepartWithAi();
        public string DevQueueText => queueText.text;
        public string DevStatus => status.text;
        public void DevApplyFirstOpen()
        {
            for (int r = 0; r < rows.Count; r++)
                if (rows[r].row.gameObject.activeSelf && rows[r].apply.interactable) { ApplyRow(r); return; }
        }
    }

    // =====================================================================================

    /// <summary>[ONLINE] 경매장: 검색 / 내 등록 / 등록하기 / 우편함 (PLAN_AUCTION §10). Mock data offline.</summary>
    public class AuctionScreen : OnlineWindow
    {
        public static AuctionScreen Instance { get; private set; }
        enum Tab { Search, Mine, Register, Mail }
        const int PageSize = 9;
        const float RowH = 42f, TableW = 1220f, TableTop = -150f;

        IAuctionService Service => OnlineServices.Auction;
        Tab tab;
        int page;
        readonly AuctionQuery query = new AuctionQuery();
        int enhBand, priceBand;
        static readonly (int min, int max, string name)[] EnhBands = { (0, 20, "전체"), (0, 0, "+0"), (1, 6, "+1~6"), (7, 9, "+7~9"), (10, 12, "+10~12"), (13, 20, "+13 이상") };
        static readonly (long min, long max, string name)[] PriceBands = { (0, long.MaxValue, "전체"), (0, 999, "1천 미만"), (1000, 9999, "1천~1만"), (10000, long.MaxValue, "1만 이상") };

        readonly Dictionary<Tab, Button> tabs = new Dictionary<Tab, Button>();
        RectTransform tableRoot, filterRoot, registerRoot;
        Button catBtn, rarBtn, enhBtn, priceBtn, sortBtn, claimAllBtn;
        InputField search;
        Text pageText, goldText, headText;
        readonly List<(RectTransform row, Image icon, Text name, Text grade, Text price, Text bid, Text seller, Text time, Button a, Button b)> rows =
            new List<(RectTransform, Image, Text, Text, Text, Text, Text, Text, Button, Button)>();
        List<AuctionListing> listings = new List<AuctionListing>();
        List<AuctionMail> mails = new List<AuctionMail>();
        // register tab
        List<string> bagKeys = new List<string>();
        int regPage, regSel = -1, regHoursIdx = 1;
        long regPrice;
        readonly List<(RectTransform row, Image icon, Text name, Text bind, Button pick)> regRows = new List<(RectTransform, Image, Text, Text, Button)>();
        Text regInfo, regPageText;

        public static AuctionScreen Create(Transform canvas)
        {
            var w = CreateWindow<AuctionScreen>(canvas, "Auction", "경매장", "icon_gold");
            Instance = w;
            w.PreviewBanner();
            string[] names = { "검색", "내 등록", "등록하기", "우편함" };
            for (int i = 0; i < 4; i++)
            {
                var t = (Tab)i;
                w.tabs[t] = Button(w.content, "Tab" + i, names[i], "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(i * 140f, 0f), new Vector2(132f, 42f), () => { w.tab = t; w.page = 0; w.Refresh(); }, 20);
            }
            w.goldText = Label(w.content, "Gold", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(580f, 0f), new Vector2(290f, 42f), TextAnchor.MiddleRight);

            // ----- search filters -----
            w.filterRoot = UIFactory.Stretch(UIFactory.Rect(w.content, "Filters"));
            w.search = w.Field(w.filterRoot, "Search", "아이템 이름 검색", new Vector2(0f, -54f), new Vector2(250f, 40f));
            w.focusField = w.search;
            w.search.onEndEdit.AddListener(_ => { w.page = 0; w.Refresh(); });
            float x = 260f;
            Button Filter(string n, Action a, float width) { var b = Button(w.filterRoot, n, "", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -54f), new Vector2(width, 40f), () => { a(); w.page = 0; w.Refresh(); }, 17); x += width + 8f; return b; }
            w.catBtn = Filter("Cat", () => w.query.category = (AuctionCategory)(((int)w.query.category + 1) % 6), 150f);
            w.rarBtn = Filter("Rar", () => w.query.rarity = w.query.rarity >= (int)ItemRarity.Legendary ? -1 : w.query.rarity + 1, 150f);
            w.enhBtn = Filter("Enh", () => w.enhBand = (w.enhBand + 1) % EnhBands.Length, 150f);
            w.priceBtn = Filter("Price", () => w.priceBand = (w.priceBand + 1) % PriceBands.Length, 160f);
            w.sortBtn = Filter("Sort", () => w.query.sort = (AuctionSort)(((int)w.query.sort + 1) % 5), 190f);
            Button(w.filterRoot, "Go", "검색", "ui_btn", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(x, -54f), new Vector2(TableW - x, 40f), () => { w.page = 0; w.Refresh(); }, 19);

            // ----- table (search / mine / mail) -----
            w.tableRoot = UIFactory.Stretch(UIFactory.Rect(w.content, "Table"));
            var head = Panel(w.tableRoot, "Head", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, TableTop + 46f), new Vector2(TableW, 30f), HeadRow);
            w.headText = Label(head.transform, "Text", "", 16, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(12f, 0f), new Vector2(TableW - 20f, 28f), TextAnchor.MiddleLeft);
            for (int i = 0; i < PageSize; i++)
            {
                var r = Row(w.tableRoot, i, TableTop + 14f, RowH, TableW);
                var icon = UIFactory.Image(r, "Icon", null, Color.white);
                UIFactory.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(34f, 34f));
                int idx = i;
                var a = Button(r, "A", "즉시 구매", "ui_btn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-82f, 0f), new Vector2(104f, 34f), () => w.RowAction(idx, true), 16);
                var b = Button(r, "B", "입찰", "ui_btngray", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(72f, 34f), () => w.RowAction(idx, false), 16);
                w.rows.Add((r, icon, Cell(r, "Name", 50f, 330f), Cell(r, "Grade", 385f, 95f, 17), Cell(r, "Price", 485f, 140f, 18, TextAnchor.MiddleRight),
                    Cell(r, "Bid", 635f, 125f, 17, TextAnchor.MiddleRight), Cell(r, "Seller", 780f, 120f, 17), Cell(r, "Time", 905f, 120f, 16), a, b));
            }
            w.claimAllBtn = Button(w.tableRoot, "ClaimAll", "모두 받기", "ui_btn", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(920f, 0f), new Vector2(120f, 34f), () => w.Report(w.Service.ClaimAll()), 17);
            Button(w.tableRoot, "Prev", "◀", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-130f, 0f), new Vector2(44f, 34f), () => { w.page = Math.Max(0, w.page - 1); w.Refresh(); }, 18);
            w.pageText = Label(w.tableRoot, "Page", "", 18, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-48f, 0f), new Vector2(80f, 34f), TextAnchor.MiddleCenter);
            Button(w.tableRoot, "Next", "▶", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), new Vector2(44f, 34f), () => { w.page++; w.Refresh(); }, 18);

            // ----- register -----
            w.registerRoot = UIFactory.Stretch(UIFactory.Rect(w.content, "Register"));
            var left = Panel(w.registerRoot, "Bag", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -54f), new Vector2(620f, 470f), new Color32(24, 36, 54, 235));
            Label(left.transform, "Title", "<b>가방</b>  <color=#8c96a8>(거래 가능한 아이템만 등록할 수 있습니다)</color>", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(14f, -8f), new Vector2(600f, 30f));
            for (int i = 0; i < 8; i++)
            {
                var r = Row(left.transform, i, -44f, 46f, 600f);
                r.anchoredPosition += new Vector2(10f, 0f);
                var icon = UIFactory.Image(r, "Icon", null, Color.white);
                UIFactory.Place(icon.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(8f, 0f), new Vector2(34f, 34f));
                int idx = i;
                var pick = Button(r, "Pick", "선택", "ui_btn", new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-6f, 0f), new Vector2(80f, 36f), () => { w.regSel = w.regPage * 8 + idx; w.regPrice = 0; w.Refresh(); }, 16);
                w.regRows.Add((r, icon, Cell(r, "Name", 50f, 340f), Cell(r, "Bind", 395f, 110f, 16), pick));
            }
            Button(left.transform, "RPrev", "◀", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-130f, 8f), new Vector2(44f, 32f), () => { w.regPage = Math.Max(0, w.regPage - 1); w.Refresh(); }, 18);
            w.regPageText = Label(left.transform, "RPage", "", 18, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-58f, 8f), new Vector2(70f, 32f), TextAnchor.MiddleCenter);
            Button(left.transform, "RNext", "▶", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-10f, 8f), new Vector2(44f, 32f), () => { w.regPage++; w.Refresh(); }, 18);
            var right = Panel(w.registerRoot, "Form", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -54f), new Vector2(590f, 470f), new Color32(24, 36, 54, 235));
            w.regInfo = Label(right.transform, "Info", "", 19, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -12f), new Vector2(550f, 270f));
            long[] steps = { -1000, -100, 100, 1000 };
            for (int i = 0; i < steps.Length; i++)
            {
                long s = steps[i];
                Button(right.transform, "P" + i, (s > 0 ? "+" : "") + s.ToString("N0"), "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f + i * 112f, 140f), new Vector2(104f, 40f), () => { w.regPrice = Math.Max(10, w.regPrice + s); w.Refresh(); }, 17);
            }
            for (int i = 0; i < AuctionRules.Durations.Length; i++)
            {
                int k = i;
                Button(right.transform, "H" + i, AuctionRules.Durations[i] + "시간", "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(20f + i * 112f, 90f), new Vector2(104f, 40f), () => { w.regHoursIdx = k; w.Refresh(); }, 17);
            }
            Button(right.transform, "Submit", "등록", "ui_btn", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 20f), new Vector2(240f, 52f), w.SubmitRegister, 22);
            return w;
        }

        void Report(AuctionResult r) { SetStatus(r.message, r.ok); Refresh(); }

        AuctionListing RowListing(int idx)
        {
            int i = page * PageSize + idx;
            return i < listings.Count ? listings[i] : null;
        }

        void RowAction(int idx, bool primary)
        {
            if (tab == Tab.Mail)
            {
                int i = page * PageSize + idx;
                if (i < mails.Count) Report(Service.Claim(mails[i].id));
                return;
            }
            var l = RowListing(idx);
            if (l == null) return;
            if (tab == Tab.Mine) { Report(Service.Cancel(l.id)); return; }
            var price = Service.Price(l.itemKey);
            if (primary)
            {
                int pct = price.avg7d > 0 ? (int)Math.Round((l.buyout / (double)(price.avg7d * l.count) - 1) * 100) : 0;
                string cmp = pct >= 100 ? $"<color=#ff7a7a>평균보다 +{pct}%</color>" : pct >= 0 ? $"평균 대비 +{pct}%" : $"평균 대비 {pct}%";
                Game.UI.Confirm($"{RichKey(l.itemKey)} x{l.count}\n{l.buyout:N0}G에 즉시 구매할까요?\n<size=18>최근 7일 평균 {price.avg7d * l.count:N0}G ({cmp})</size>", () => Report(Service.Buyout(l.id)), overlay: true);
            }
            else
            {
                if (!l.Biddable) { SetStatus("즉시 구매만 가능한 물건입니다.", false); return; }
                long bid = AuctionRules.MinBid(l);
                Game.UI.Confirm($"{RichKey(l.itemKey)} x{l.count}\n최소 입찰가 {bid:N0}G로 입찰할까요?\n<size=18>입찰 골드는 예치되고, 더 높은 입찰이 오면 우편으로 돌아옵니다.</size>", () => Report(Service.Bid(l.id, bid)), overlay: true);
            }
        }

        static string RichKey(string key) => EquipmentDatabase.IsEquipment(key) ? EquipmentDatabase.RichName(key) : DungeonDatabase.ItemName(key);

        void SubmitRegister()
        {
            if (regSel < 0 || regSel >= bagKeys.Count) { SetStatus("등록할 아이템을 고르세요.", false); return; }
            string key = bagKeys[regSel];
            int count = EquipmentDatabase.IsEquipment(key) ? 1 : Game.Session.Inventory.Count(key);
            var r = Service.Register(key, count, regPrice, 0, AuctionRules.Durations[regHoursIdx]);
            if (r.ok) { regSel = -1; regPrice = 0; }
            Report(r);
        }

        static List<string> TradableBag()
        {
            var bag = Game.Session?.Inventory;
            if (bag == null) return new List<string>();
            return bag.Ids.Where(id => id != ConsumableDatabase.Gold && bag.Count(id) > 0)
                .OrderBy(id => AuctionRules.BindOf(id) == ItemBind.Tradable ? 0 : 1).ThenBy(id => id).ToList();
        }

        protected override void Refresh()
        {
            foreach (var kv in tabs) kv.Value.image.sprite = Game.Art.Get(kv.Key == tab ? "ui_btn" : "ui_btngray");
            int mailN = Service.UnclaimedMail;
            TextOf(tabs[Tab.Mail]).text = mailN > 0 ? $"우편함 <color=#ff6b6b>●{mailN}</color>" : "우편함";
            goldText.text = $"<color=#ffd34a>{Game.Session?.Gold ?? 0:N0} G</color>";
            filterRoot.gameObject.SetActive(tab == Tab.Search);
            tableRoot.gameObject.SetActive(tab != Tab.Register);
            registerRoot.gameObject.SetActive(tab == Tab.Register);
            claimAllBtn.gameObject.SetActive(tab == Tab.Mail);
            if (tab == Tab.Register) { RefreshRegister(); return; }

            // Search / mine tables start right under the tabs when there is no filter row.
            tableRoot.anchoredPosition = tab == Tab.Search ? Vector2.zero : new Vector2(0f, 50f);
            TextOf(catBtn).text = "분류: " + AuctionRules.CategoryName(query.category);
            TextOf(rarBtn).text = "등급: " + (query.rarity < 0 ? "전체" : EquipmentDatabase.RarityName((ItemRarity)query.rarity));
            TextOf(enhBtn).text = "강화: " + EnhBands[enhBand].name;
            TextOf(priceBtn).text = "가격: " + PriceBands[priceBand].name;
            TextOf(sortBtn).text = "정렬: " + AuctionRules.SortName(query.sort);
            query.text = search.text;
            query.enhMin = EnhBands[enhBand].min;
            query.enhMax = EnhBands[enhBand].max;
            query.priceMin = PriceBands[priceBand].min;
            query.priceMax = PriceBands[priceBand].max;

            int total;
            if (tab == Tab.Mail)
            {
                mails = Service.Mailbox().ToList();
                total = mails.Count;
                headText.text = "<color=#b8c4d8>내용                                                                                                                          보관 30일</color>";
            }
            else
            {
                listings = (tab == Tab.Search ? Service.Search(query) : Service.MyListings()).ToList();
                total = listings.Count;
                headText.text = "<color=#b8c4d8>        아이템                                                  등급              즉시 구매가               입찰가           판매자              남은 시간</color>";
            }
            int pages = Math.Max(1, (total + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            pageText.text = $"{page + 1} / {pages}";
            for (int r = 0; r < rows.Count; r++)
            {
                int i = page * PageSize + r;
                var row = rows[r];
                bool on = i < total;
                row.row.gameObject.SetActive(on);
                if (!on) continue;
                if (tab == Tab.Mail)
                {
                    var m = mails[i];
                    string key = m.itemKey ?? ConsumableDatabase.Gold;
                    row.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(key));
                    row.name.text = m.text;
                    row.grade.text = "";
                    row.price.text = m.gold > 0 ? $"<color=#ffd34a>{m.gold:N0}G</color>" : "";
                    row.bid.text = row.seller.text = row.time.text = "";
                    TextOf(row.a).text = "받기";
                    row.a.interactable = true;
                    row.b.gameObject.SetActive(false);
                    continue;
                }
                var l = listings[i];
                row.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(l.itemKey));
                row.name.text = RichKey(l.itemKey) + (l.count > 1 ? $" x{l.count}" : "");
                bool gear = EquipmentDatabase.IsEquipment(l.itemKey);
                row.grade.text = gear ? $"<color={EquipmentDatabase.RarityColor(l.rarity)}>{EquipmentDatabase.RarityName(l.rarity)}</color>" : $"<color=#8c96a8>{AuctionRules.CategoryName(l.category)}</color>";
                row.price.text = $"<color=#ffd34a>{l.buyout:N0}G</color>";
                row.bid.text = !l.Biddable ? "<color=#8c96a8>-</color>" : l.hasBid ? (l.myBid ? $"<color=#8fe28f>{l.currentBid:N0}G</color>" : $"{l.currentBid:N0}G") : $"<color=#b8c4d8>{l.startBid:N0}G~</color>";
                row.seller.text = l.seller;
                row.time.text = AuctionRules.TimeBand(l.hoursLeft);
                if (tab == Tab.Mine)
                {
                    TextOf(row.a).text = "등록 취소";
                    row.a.interactable = !l.hasBid;
                    row.b.gameObject.SetActive(false);
                }
                else
                {
                    TextOf(row.a).text = "즉시 구매";
                    row.a.interactable = true;
                    row.b.gameObject.SetActive(true);
                    row.b.interactable = l.Biddable;
                }
            }
        }

        void RefreshRegister()
        {
            bagKeys = TradableBag();
            int pages = Math.Max(1, (bagKeys.Count + 7) / 8);
            regPage = Mathf.Clamp(regPage, 0, pages - 1);
            regPageText.text = $"{regPage + 1} / {pages}";
            var bag = Game.Session.Inventory;
            for (int r = 0; r < regRows.Count; r++)
            {
                int i = regPage * 8 + r;
                var row = regRows[r];
                bool on = i < bagKeys.Count;
                row.row.gameObject.SetActive(on);
                if (!on) continue;
                string key = bagKeys[i];
                var bind = AuctionRules.BindOf(key);
                row.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(key));
                int n = bag.Count(key);
                row.name.text = (i == regSel ? "<color=#ffe066>▶</color> " : "") + RichKey(key) + (n > 1 ? $" x{n}" : "");
                row.bind.text = AuctionRules.BindLabel(bind);
                row.pick.interactable = bind == ItemBind.Tradable;
            }
            if (regSel < 0 || regSel >= bagKeys.Count)
            {
                regInfo.text = "<b>등록하기</b>\n\n왼쪽 가방에서 팔 아이템을 고르세요.\n\n· 보증금: 즉시 구매가의 1% (최소 10G, 판매 시 반환)\n· 수수료: 장비 5%, 재료·소모품 3% (판매 시)\n· 기간: 12 / 24 / 48시간\n· 장비 보호권·퀘스트 보상은 귀속이라 팔 수 없습니다.";
                return;
            }
            string k = bagKeys[regSel];
            int count = EquipmentDatabase.IsEquipment(k) ? 1 : bag.Count(k);
            var price = Service.Price(k);
            if (regPrice <= 0) regPrice = Math.Max(10, price.avg7d * count / 10 * 10);
            long deposit = AuctionRules.Deposit(regPrice), fee = AuctionRules.Fee(k, regPrice);
            regInfo.text = $"<b>{RichKey(k)}</b> x{count}\n" +
                           $"<color=#b8c4d8>최근 7일 평균 {price.avg7d * count:N0}G  (최저 {price.min * count:N0} · 최고 {price.max * count:N0})</color>\n\n" +
                           $"즉시 구매가  <color=#ffd34a>{regPrice:N0}G</color>\n" +
                           $"기간  {AuctionRules.Durations[regHoursIdx]}시간\n" +
                           $"보증금  {deposit:N0}G <color=#8c96a8>(판매되면 반환)</color>\n" +
                           $"수수료  {fee:N0}G ({AuctionRules.FeePct(k)}%)\n" +
                           $"예상 수령액  <color=#8fe28f>{AuctionRules.Payout(k, regPrice) + deposit:N0}G</color>";
        }

        // ---------- dev hooks ----------
        public void DevTab(int t) { tab = (Tab)t; page = 0; Refresh(); }
        public int DevVisibleRows => rows.Count(r => r.row.gameObject.activeSelf);
        public string DevRowName(int r) => r < rows.Count ? rows[r].name.text : "";
        public AuctionListing DevListing(int r) => RowListing(r);
        public void DevBuyRow(int r) => RowAction(r, true);
        public void DevSelectRegister(string key, long price, int hoursIdx)
        {
            tab = Tab.Register;
            bagKeys = TradableBag();
            regSel = bagKeys.IndexOf(key);
            regPage = Math.Max(0, regSel) / 8;
            regPrice = price;
            regHoursIdx = hoursIdx;
            Refresh();
        }
        public void DevSubmitRegister() => SubmitRegister();
        public string DevStatus => status.text;
        public void DevSearch(string text) { search.text = text; page = 0; Refresh(); }
    }

    // =====================================================================================

    /// <summary>[ONLINE] Small "매칭 대기 중 00:42 · 2/4명" bar at the top of the HUD while the auto-match queue runs; ticks the service.</summary>
    public class MatchQueueIndicator : MonoBehaviour
    {
        Text text;
        GameObject bar;

        public static MatchQueueIndicator Create(Transform canvas)
        {
            var root = UIFactory.Place(UIFactory.Rect(canvas, "MatchQueue"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -8f), new Vector2(360f, 34f));
            var v = root.gameObject.AddComponent<MatchQueueIndicator>();
            var bg = UIFactory.Image(root, "Bg", Game.Art.Get("ui_white"), new Color32(18, 26, 40, 220));
            bg.preserveAspect = false;
            UIFactory.Stretch(bg.rectTransform);
            v.bar = bg.gameObject;
            v.text = UIFactory.Text(bg.transform, "Text", "", 18, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(v.text.rectTransform);
            v.bar.SetActive(false);
            return v;
        }

        void Update()
        {
            var svc = OnlineServices.PartyFinder;
            svc.Tick(Time.unscaledDeltaTime);
            var q = svc.Queue;
            bool show = q.active && Game.State != null && Game.State.Current == GameState.Playing;
            if (bar.activeSelf != show) bar.SetActive(show);
            if (!show) return;
            int s = Mathf.FloorToInt(q.elapsed);
            text.text = $"<color=#ffe066>매칭 대기 중</color> {s / 60:00}:{s % 60:00} / 01:00 · {q.humans}/4명";
        }
    }
}
