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
        public const string PreviewTag = "오프라인 모드 (다른 모험가와 연결되지 않음)";
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
        protected override bool PadNavigation => focusField == null || !focusField.isFocused;
        const int PageSize = 9;
        const float RowH = 44f, TableW = 1220f, ListTop = -104f;

        IPartyFinderService Service => OnlineServices.PartyFinder;
        bool createTab;
        int page;
        int filter = -1; // -1 = every dungeon
        RectTransform listRoot, createRoot;
        Button tabList, tabCreate, filterBtn, queueBtn, aiBtn, cancelQueueBtn;
        Text queueText, pageText, emptyText;
        readonly List<(RectTransform row, Text dungeon, Text diff, Text members, Text power, Text leader, Text msg, Button apply)> rows = new List<(RectTransform, Text, Text, Text, Text, Text, Text, Button)>();
        List<PartyPost> shown = new List<PartyPost>();
        // create tab
        int cDungeon, cDiff, cMembers = 4, cPowerPct = 80, cMsg;
        Text levelGuide;
        readonly Text[] createValues = new Text[5];

        static List<DungeonDef> Dungeons => new List<DungeonDef>(DungeonDatabase.Weekday) { DungeonDatabase.SkeletonKing, DungeonDatabase.Get(DungeonDatabase.RaidGrah) };

        public static PartyFinderScreen Create(Transform canvas)
        {
            var w = CreateWindow<PartyFinderScreen>(canvas, "PartyFinder", "파티 찾기", "menuicon_finder");
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
            w.emptyText = Label(w.listRoot, "Empty", "", 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, ListTop - 40f), new Vector2(TableW, 40f), TextAnchor.MiddleCenter);
            w.emptyText.color = new Color32(184, 196, 216, 255);
            Button(w.listRoot, "Prev", "◀", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-180f, 0f), UiSizes.PageButton, () => { w.page = Math.Max(0, w.page - 1); w.Refresh(); }, UiSizes.PageFont);
            w.pageText = Label(w.listRoot, "Page", "", 18, new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-60f, 0f), new Vector2(110f, 34f), TextAnchor.MiddleCenter);
            Button(w.listRoot, "Next", "▶", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(0f, 0f), UiSizes.PageButton, () => { w.page++; w.Refresh(); }, UiSizes.PageFont);

            // ----- create tab -----
            w.createRoot = UIFactory.Stretch(UIFactory.Rect(w.content, "Create"));
            var panel = Panel(w.createRoot, "Panel", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -56f), new Vector2(760f, 460f), new Color32(24, 36, 54, 235));
            string[] labels = { "던전", "난이도", "모집 인원", "최소 전투력", "메시지" };
            Action[] cycles = { () => w.cDungeon++, () => w.cDiff++, () => w.cMembers = w.cMembers >= 4 ? 2 : w.cMembers + 1, () => w.cPowerPct = w.cPowerPct >= 100 ? 50 : w.cPowerPct + 10, () => w.cMsg++ };
            for (int i = 0; i < labels.Length; i++)
            {
                // Each label, value and button shares a fixed row; wrapping cannot shift later fields.
                var stripe = Panel(panel.transform, "CreateRow" + i, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(20f, -20f - i * 62f), new Vector2(720f, 52f), i % 2 == 0 ? RowA : RowB);
                stripe.raycastTarget = false;
                w.createValues[i] = Label(panel.transform, "Value" + i, "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(190f, -24f - i * 62f), new Vector2(354f, 44f), TextAnchor.MiddleLeft);
                w.createValues[i].lineSpacing = 1f;
                w.createValues[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                w.createValues[i].verticalOverflow = VerticalWrapMode.Truncate;
                Label(panel.transform, "L" + i, labels[i], 20, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(30f, -24f - i * 62f), new Vector2(160f, 44f), TextAnchor.MiddleLeft);
                int k = i;
                Button(panel.transform, "C" + i, "바꾸기 ▶", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(560f, -24f - i * 62f), new Vector2(160f, 44f), () => { cycles[k](); w.Refresh(); }, 18);
            }
            Button(panel.transform, "Post", "모집 글 등록", "ui_btn", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-110f, 24f), new Vector2(200f, 50f), w.Post, 21);
            Button(panel.transform, "CancelPost", "모집 취소", "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(110f, 24f), new Vector2(200f, 50f), w.CancelPost, 21);
            var help = Panel(w.createRoot, "Help", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, -56f), new Vector2(440f, 460f), new Color32(24, 36, 54, 235));
            // Recommended level / power of the picked dungeon, per difficulty, against my character (refreshed with the picks).
            w.levelGuide = Label(help.transform, "Text", "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(24f, -20f), new Vector2(392f, 420f));
            w.levelGuide.lineSpacing = 1.15f;
            return w;
        }

        int MyPower => Game.Player != null ? CharacterStats.Power(Game.Player.Class) : 0;

        static readonly string[] DayNames = { "일", "월", "화", "수", "목", "금", "토" };

        /// <summary>
        /// The picked dungeon's recommended level and power for every difficulty (weekday dungeons share them; raids have
        /// their own), green when my character meets them, plus the days it opens and whether it is open today.
        /// </summary>
        string LevelGuide(DungeonDef d, DungeonDifficulty picked)
        {
            int myLevel = Game.Session != null ? Game.Session.Progression.Level : 1, myPower = MyPower;
            string Row(string name, DifficultyDef n, bool sel)
            {
                string lv = myLevel >= n.recommendedLevel ? "#8fe28f" : "#ff9f7a";
                string pw = myPower >= n.recommendedPower ? "#8fe28f" : "#ff9f7a";
                string head = sel ? $"<b>▶ {name}</b>" : $"   {name}";
                return $"{head}   <color={lv}>Lv.{n.recommendedLevel}</color> · <color={pw}>전투력 {n.recommendedPower:N0}</color>\n";
            }
            var sb = new System.Text.StringBuilder($"<b>{d.name}</b> 권장 레벨\n<color=#b8c4d8>내 캐릭터 Lv.{myLevel} · 전투력 {myPower:N0}</color>\n\n");
            if (d.isRaid) sb.Append(Row("레이드", DungeonDatabase.DifficultyFor(d, DungeonDifficulty.Normal), true));
            else
                for (int i = 0; i < DungeonDatabase.DifficultyCount; i++)
                {
                    var diff = (DungeonDifficulty)i;
                    sb.Append(Row(PartyFinderRules.DifficultyName(diff), DungeonDatabase.DifficultyFor(d, diff), diff == picked));
                }
            var days = d.openDays != null && d.openDays.Length > 0 ? string.Join("·", System.Array.ConvertAll(d.openDays, x => DayNames[(int)x])) + "요일" : "매일";
            bool open = ResetClock.IsOpen(d, ResetClock.Now);
            sb.Append($"\n열리는 날: {days}{(d.isRaid ? "" : " (주말엔 모든 요일던전)")}\n오늘: {(open ? "<color=#8fe28f>열림</color>" : "<color=#ff9f7a>닫힘</color>")}");
            sb.Append("\n\n<color=#8c96a8>초록 = 내 캐릭터가 권장을 넘음. 빈자리는 방장의 AI 동료가 채웁니다. 모집 글은 10분 뒤 내려갑니다.</color>");
            return sb.ToString();
        }

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
            SetStatus($"사람 {humans}명 + AI 용병 {ai}명으로 출발합니다.");
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
            var banner = content.Find("Preview");
            if (banner != null) banner.gameObject.SetActive(!Service.IsOnline); // [PARTY] real board online
            listRoot.gameObject.SetActive(!createTab);
            createRoot.gameObject.SetActive(createTab);
            tabList.image.sprite = UiTheme.Tab(!createTab);
            tabCreate.image.sprite = UiTheme.Tab(createTab);
            RefreshQueue();
            var ds = Dungeons;
            TextOf(filterBtn).text = filter < 0 ? "던전: 전체" : "던전: " + ds[filter].name;
            shown = Service.List(filter < 0 ? null : ds[filter].id).ToList();
            int pages = Math.Max(1, (shown.Count + PageSize - 1) / PageSize);
            page = Mathf.Clamp(page, 0, pages - 1);
            pageText.text = $"{page + 1} / {pages}";
            emptyText.gameObject.SetActive(shown.Count == 0);
            emptyText.text = filter < 0 ? "등록된 모집 글이 없습니다. 모집 글을 등록하거나 자동 매칭을 써 보세요." : "이 던전의 모집 글이 없습니다.";
            int power = MyPower;
            for (int r = 0; r < rows.Count; r++)
            {
                int i = page * PageSize + r;
                var row = rows[r];
                row.row.gameObject.SetActive(i < shown.Count);
                if (i >= shown.Count) continue;
                var p = shown[i];
                row.dungeon.text = (p.mine ? "<color=#ffe066>★</color> " : "") + p.dungeonName;
                row.diff.text = $"<color={PartyFinderRules.DifficultyColor(p.difficulty)}>{PartyFinderRules.ModeName(p.dungeonId, p.difficulty)}</color>\n<size=14><color=#b8c4d8>{PartyFinderRules.Recommended(p.dungeonId, p.difficulty, true)}</color></size>";
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
            levelGuide.text = LevelGuide(d, diff);
            createValues[0].text = d.name;
            createValues[1].text = $"<color={PartyFinderRules.DifficultyColor(diff)}>{(d.isRaid ? "레이드" : PartyFinderRules.DifficultyName(diff))}</color>   <size=16><color=#b8c4d8>{PartyFinderRules.Recommended(d.id, diff, true)}</color></size>";
            createValues[2].text = $"{cMembers}명";
            createValues[3].text = $"{power * cPowerPct / 100:N0} <color=#b8c4d8>(내 전투력의 {cPowerPct}%)</color>";
            createValues[4].text = $"“{PartyFinderRules.PresetMessages[Mod(cMsg, PartyFinderRules.PresetMessages.Length)]}”";
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
            OnlineServices.PartyFinder.Departed += Depart;
            return v;
        }

        void OnDestroy() => OnlineServices.PartyFinder.Departed -= Depart;

        /// <summary>[F4] The matched party (humans + AI mercenaries) enters the queued dungeon right away.</summary>
        static void Depart(MatchQueueState q, int ai)
        {
            var def = DungeonDatabase.Get(q.dungeonId);
            if (def == null || Game.Dungeon == null) return;
            if (Game.Dungeon.InRun) { GameEvents.RaiseToast("이미 던전 안에 있어 매칭 출발을 취소했습니다."); return; }
            GameEvents.RaiseToast($"매칭 완료: 사람 {q.humans}명 + AI 용병 {ai}명. {def.name}에 입장합니다.");
            Game.Dungeon.Enter(def, q.difficulty);
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
