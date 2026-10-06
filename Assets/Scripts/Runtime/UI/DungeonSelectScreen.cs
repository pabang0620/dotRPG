using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 던전 선택 window (side menu 요일던전 / 레이드, or the village dungeon guide), plan §6.0 입장 흐름:
    /// today's weekday, the five weekday dungeons (open / closed today), the chosen dungeon's details,
    /// difficulty buttons (locked ones grey), recommended level and 전투력 against the player's, the current
    /// party (with a button to the 파티 window), "오늘 남은 입장 N/3", a reward preview and 입장.
    /// The 레이드 tab shows 해골왕 and this week's reward status.
    /// Keys: ↑/↓ dungeon, ←/→ difficulty, Enter 입장, E switches the tab, Esc closes.
    /// </summary>
    public class DungeonSelectScreen : WindowScreen
    {
        const float ListW = 380f, RowH = 96f, RowGap = 8f, DetailX = 392f, DetailW = 828f, PanelTop = -52f, PanelH = 540f;
        const float DiffW = 150f, DiffH = 46f, DiffGap = 12f;

        sealed class Row
        {
            public DungeonDef def;
            public Image bg, stripe;
            public Text text, tag;
        }

        readonly List<Row> rows = new List<Row>();
        readonly Button[] diffButtons = new Button[4];
        readonly Text[] diffLabels = new Text[4];
        Button tabWeekday, tabRaid, enterButton;
        Image banner;
        Text diffTitle;
        Text dayText, title, desc, info, recommend, partyText, rewards, raidLine, status, hint;
        const int SlotCount = 8;
        const float SlotW = 92f, SlotIcon = 56f;
        readonly List<(Image frame, Image icon, Text name)> slots = new List<(Image, Image, Text)>();
        RectTransform listRoot;
        bool raidTab;
        public bool IsRaidTab => raidTab;
        int selected;
        DungeonDifficulty difficulty;
        float lastMoveX, lastMoveY;

        static readonly Color RowColor = new Color32(24, 36, 54, 235);
        static readonly Color RowSelected = new Color32(44, 66, 96, 245);
        static readonly Color RowClosed = new Color32(20, 26, 36, 235);

        public static DungeonSelectScreen Create(Transform canvas)
        {
            var w = CreateWindow<DungeonSelectScreen>(canvas, "DungeonSelect", "던전", "menuicon_dungeon");
            w.tabWeekday = Button(w.content, "TabWeekday", "요일던전", "ui_btn", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(170f, 44f), () => w.SetTab(false), 22);
            w.tabRaid = Button(w.content, "TabRaid", "레이드", "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(180f, 0f), new Vector2(170f, 44f), () => w.SetTab(true), 22);
            w.dayText = Label(w.content, "Day", "", 18, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(0f, 0f), new Vector2(DetailW, 44f), TextAnchor.MiddleRight);

            w.listRoot = UIFactory.Place(UIFactory.Rect(w.content, "List"), new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, PanelTop), new Vector2(ListW, PanelH));

            var detail = Panel(w.content, "Detail", new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(DetailX, PanelTop), new Vector2(DetailW, PanelH), new Color32(24, 36, 54, 235));
            var d = detail.transform;
            var tl = new Vector2(0f, 1f);
            w.title = Label(d, "Title", "", 28, tl, tl, new Vector2(24f, -14f), new Vector2(530f, 40f));
            w.desc = Label(d, "Desc", "", 17, tl, tl, new Vector2(24f, -56f), new Vector2(530f, 46f));
            // [E4] Dungeon banner art (Art/banner_<id>) in the top-right corner of the details.
            w.banner = UIFactory.Image(d, "Banner", null, Color.white);
            w.banner.preserveAspect = true;
            UIFactory.Place(w.banner.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-18f, -14f), new Vector2(240f, 80f));
            w.info = Label(d, "Info", "", 17, tl, tl, new Vector2(24f, -106f), new Vector2(780f, 46f));
            w.diffTitle = Label(d, "DiffTitle", "<b>난이도</b>", 19, tl, tl, new Vector2(24f, -160f), new Vector2(780f, 26f));
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                diffButtonsInit(w, d, index);
            }
            w.raidLine = Label(d, "RaidLine", "", 17, tl, tl, new Vector2(24f, -188f), new Vector2(780f, 46f));
            w.recommend = Label(d, "Recommend", "", 17, tl, tl, new Vector2(24f, -246f), new Vector2(380f, 46f));
            w.partyText = Label(d, "Party", "", 17, tl, tl, new Vector2(420f, -246f), new Vector2(220f, 46f));
            Button(d, "PartyButton", "파티 편성", "ui_btngray", new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-24f, -246f), new Vector2(150f, 44f),
                () => Game.Flow.OpenWindow(Game.UI.Party), 18);
            w.rewards = Label(d, "Rewards", "", 17, tl, tl, new Vector2(24f, -300f), new Vector2(780f, 26f));
            for (int i = 0; i < SlotCount; i++)
            {
                var frame = Panel(d, "Slot" + i, tl, tl, new Vector2(24f + i * SlotW, -330f), new Vector2(SlotIcon + 8f, SlotIcon + 8f), new Color32(14, 20, 32, 255));
                var icon = UIFactory.Image(frame.transform, "Icon", null, Color.white);
                icon.preserveAspect = true;
                UIFactory.Place(icon.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(SlotIcon - 6f, SlotIcon - 6f));
                var name = Label(d, "SlotName" + i, "", 16, tl, tl, new Vector2(24f + i * SlotW - 14f, -398f), new Vector2(SlotW, 34f), TextAnchor.UpperCenter);
                w.slots.Add((frame, icon, name));
            }
            w.status = Label(d, "Status", "", 17, new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(24f, 22f), new Vector2(520f, 50f), TextAnchor.LowerLeft);
            w.enterButton = Button(d, "Enter", "입장", "ui_btn", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-24f, 20f), new Vector2(240f, 62f), () => w.TryEnter(), 28);
            w.hint = Label(w.content, "Hint", "", 16, new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, -4f), new Vector2(820f, 24f), TextAnchor.UpperRight);
            return w;
        }

        static void diffButtonsInit(DungeonSelectScreen w, Transform d, int index)
        {
            var b = Button(d, "Diff" + index, DungeonDatabase.Difficulty((DungeonDifficulty)index).name, "ui_btngray", new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(24f + index * (DiffW + DiffGap), -188f), new Vector2(DiffW, DiffH), () => w.PickDifficulty((DungeonDifficulty)index), 20);
            w.diffButtons[index] = b;
            w.diffLabels[index] = b.GetComponentInChildren<Text>();
        }

        /// <summary>Opens the window on the weekday tab or the raid tab.</summary>
        public void Open(bool raid, string keeperName = null, string keeperLine = null)
        {
            SetKeeper(keeperName, keeperLine);
            raidTab = raid;
            selected = -1;
            Game.Flow.OpenWindow(this);
        }

        void SetTab(bool raid)
        {
            if (raidTab == raid) return;
            raidTab = raid;
            selected = -1;
            Game.Audio.PlaySfx("select");
            Refresh();
        }

        /// <summary>Raids by entry level (low to high); weekday dungeons open today first, then the rest.</summary>
        IReadOnlyList<DungeonDef> Listed
        {
            get
            {
                var list = new List<DungeonDef>(raidTab ? DungeonDatabase.Raids : DungeonDatabase.Weekday);
                var now = ResetClock.Now;
                if (raidTab) return list.OrderBy(d => DungeonDatabase.DifficultyFor(d, DungeonDifficulty.Normal).recommendedLevel).ThenBy(d => list.IndexOf(d)).ToList();
                return list.OrderBy(d => ResetClock.IsOpen(d, now) ? 0 : 1).ThenBy(d => list.IndexOf(d)).ToList();
            }
        }

        DungeonDef Selected
        {
            get
            {
                var list = Listed;
                return selected >= 0 && selected < list.Count ? list[selected] : null;
            }
        }

        void BuildRows()
        {
            foreach (var r in rows) Destroy(r.bg.gameObject);
            rows.Clear();
            var list = Listed;
            for (int i = 0; i < list.Count; i++)
            {
                int index = i;
                var r = new Row { def = list[i] };
                r.bg = Panel(listRoot, "Row_" + list[i].id, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, -i * (RowH + RowGap)), new Vector2(ListW, RowH), RowColor);
                r.bg.raycastTarget = true;
                var b = r.bg.gameObject.AddComponent<Button>();
                b.targetGraphic = r.bg;
                b.transition = Selectable.Transition.None;
                b.onClick.AddListener(() => { selected = index; Game.Audio.PlaySfx("select"); Refresh(); });
                r.stripe = Panel(r.bg.transform, "Stripe", new Vector2(0f, 0f), new Vector2(0f, 0f), Vector2.zero, new Vector2(6f, RowH), Color.white);
                r.text = Label(r.bg.transform, "Text", "", 18, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(18f, -8f), new Vector2(ListW - 30f, 84f));
                r.text.horizontalOverflow = HorizontalWrapMode.Overflow; // one line each: never wrap into the next row
                r.tag = Label(r.bg.transform, "Tag", "", 17, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-12f, -10f), new Vector2(110f, 28f), TextAnchor.UpperRight);
                rows.Add(r);
            }
        }

        protected override void Refresh()
        {
            if (Game.Session == null) return;
            var now = ResetClock.Now;
            var progress = Game.Session.Dungeons;
            if (rows.Count == 0 || rows[0].def.isRaid != raidTab) BuildRows();
            var list = Listed;
            if (selected < 0 || selected >= list.Count)
            {
                selected = 0;
                for (int i = 0; i < list.Count; i++)
                    if (ResetClock.IsOpen(list[i], now)) { selected = i; break; }
            }

            tabWeekday.image.sprite = Game.Art.Get(raidTab ? "ui_btngray" : "ui_btn");
            tabRaid.image.sprite = Game.Art.Get(raidTab ? "ui_btn" : "ui_btngray");
            var day = ResetClock.GameDay(now);
            string weekend = day == DayOfWeek.Saturday || day == DayOfWeek.Sunday ? "  <color=#8fe28f>주말: 모든 요일던전 개방</color>" : "";
            dayText.text = raidTab
                ? $"오늘 <color=#ffe066>{DungeonDatabase.DayName(day)}</color>   봉인 열쇠 조각 <color=#ffe066>{Game.Session.Inventory.Count(DungeonDatabase.SealKey)}</color> <color=#b8c4d8>(중간 레이드 보상)</color>   <color=#b8c4d8>초기화 매일 06:00 · 주간 목 06:00</color>"
                : $"오늘 <color=#ffe066>{DungeonDatabase.DayName(day)}</color>{weekend}   남은 입장 <color=#ffe066>{progress.EntriesLeft(now)}/{DungeonDatabase.DailyEntries}</color>   <color=#b8c4d8>초기화 06:00</color>";

            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                bool open = ResetClock.IsOpen(r.def, now);
                bool sel = i == selected;
                r.bg.color = sel ? RowSelected : open ? RowColor : RowClosed;
                r.stripe.color = sel ? (Color)UIColors.Highlight : open ? new Color32(110, 200, 130, 255) : new Color32(70, 76, 90, 255);
                string nameColor = open ? "#ffffff" : "#8c96a8";
                string lv = r.def.isRaid ? $"Lv.{DungeonDatabase.DifficultyFor(r.def, DungeonDifficulty.Normal).recommendedLevel} · " : "";
                r.text.text = $"<size=24><b><color={nameColor}>{r.def.name}</color></b></size>\n<color=#b8c4d8>{lv}{r.def.themeName} · {DungeonDatabase.OpenDaysLabel(r.def)}</color>\n<size=15><color=#ffe066>{r.def.specialty}</color></size>";
                r.tag.text = open ? "<color=#8fe28f><b>오늘 개방</b></color>" : "<color=#8c96a8>닫힘</color>";
                // Open today: a warm glow on the row so it reads as the one to play.
                if (open && !sel) r.bg.color = new Color32(34, 58, 52, 245);
            }

            var def = Selected;
            if (def == null) return;
            if (def.isRaid) difficulty = DungeonDifficulty.Normal;
            var numbers = DungeonDatabase.DifficultyFor(def, difficulty);
            banner.sprite = Game.Art.Get("banner_" + def.id);
            banner.enabled = banner.sprite != null;
            title.text = $"<b>{def.name}</b>  <size=20><color=#b8c4d8>{def.themeName}</color></size>";
            desc.text = def.description;
            info.text = $"<color=#ffe066>보스</color> {def.bossName}    <color=#ffe066>특징 몬스터</color> {def.featureMonster}\n" +
                        $"<color=#b8c4d8>방 {def.RoomCount}개 (보스 포함) · 최대 {def.maxParty}인 · 부활 {numbers.revives}회</color>";

            for (int i = 0; i < diffButtons.Length; i++)
            {
                var d = (DungeonDifficulty)i;
                bool show = !def.isRaid;
                diffButtons[i].gameObject.SetActive(show);
                if (!show) continue;
                bool unlocked = progress.IsUnlocked(def, d);
                bool sel = d == difficulty;
                var best = progress.BestRank(def.id, d);
                string label = def.isRaid ? "레이드" : DungeonDatabase.Difficulty(d).name;
                diffLabels[i].text = unlocked ? (best.HasValue ? $"{label}  <size=16><color=#ffe066>{best.Value}</color></size>" : label) : $"<color=#9aa0aa>{label} (잠김)</color>";
                diffButtons[i].image.sprite = Game.Art.Get(sel ? "ui_btn" : "ui_btngray");
                diffButtons[i].image.color = unlocked ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            }

            var local = Game.Player;
            var cls = local != null ? local.Class : Game.Session.PlayerClass;
            int level = Game.Session.Progression.Level;
            int power = local != null && local.Data != null ? local.Data.Stats.Power(cls) : 0;
            string lvColor = level >= numbers.recommendedLevel ? "#8fe28f" : "#ff9f7a";
            string pwColor = power >= numbers.recommendedPower ? "#8fe28f" : "#ff9f7a";
            recommend.text = $"<color=#b8c4d8>{(def.isRaid ? "입장" : "권장")}</color>  Lv.{numbers.recommendedLevel} · 전투력 {numbers.recommendedPower:N0}\n" +
                             $"<color=#b8c4d8>내 캐릭터</color>  <color={lvColor}>Lv.{level}</color> · <color={pwColor}>전투력 {power:N0}</color>";

            var party = Game.Party;
            var names = new List<string> { "나" };
            if (party != null)
                for (int i = 1; i < party.Members.Count; i++) if (party.Members[i] != null) names.Add(party.Members[i].DisplayName);
            int size = names.Count;
            partyText.text = $"<b>파티</b> <color=#ffe066>{size}/{def.maxParty}</color> · 체력 ×{DungeonDatabase.PartyScale(size):0.0}\n<size=15>{string.Join(" · ", names)}</size>";

            // Raid: its own schedule and reward status on one line where the difficulty buttons would be.
            raidLine.gameObject.SetActive(def.isRaid);
            string schedule = def.isRaid ? (def.raidTier == RaidTier.Mid ? $"{DungeonDatabase.OpenDaysLabel(def)} 개방 · 하루 1회 보상" : $"{DungeonDatabase.OpenDaysLabel(def)} 개방 · 주 1회 보상") : "";
            diffTitle.text = def.isRaid ? $"<b>레이드 일정</b>   <color=#b8c4d8>{schedule}</color>" : "<b>난이도</b>";
            if (def.isRaid)
            {
                // Two lines: what this entry gives now, then where its keys come from.
                string state, how;
                if (!progress.RaidRewardAvailable(def, now))
                {
                    state = def.raidTier == RaidTier.Mid ? "<color=#ff9f7a>오늘 보상을 받았습니다 · 지금은 연습 입장</color>" : "<color=#ff9f7a>이번 주 보상을 받았습니다 · 지금은 연습 입장</color>";
                    how = def.raidTier == RaidTier.Mid ? "<color=#b8c4d8>보상은 매일 06:00에 다시 열립니다.</color>" : "<color=#b8c4d8>보상은 매주 목요일 06:00에 다시 열립니다.</color>";
                }
                else if (def.raidTier == RaidTier.Mid)
                {
                    state = $"보상  봉인 열쇠 조각 <color=#ffe066>{def.keyMin}~{def.keyMax}개</color> · 이번 주 클리어 {progress.RaidClearsThisWeek(def, now)}/3";
                    how = "<color=#b8c4d8>모은 조각은 최종 레이드 보상 조건에 쓰입니다.</color>";
                }
                else
                {
                    int have = Game.Session.Inventory.Count(DungeonDatabase.SealKey);
                    state = $"보상 조건  봉인 열쇠 조각 <color=#ffe066>{def.keyCost}개</color> <color={(have >= def.keyCost ? "#8fe28f" : "#ff9f7a")}>(보유 {have})</color>";
                    how = $"<color=#b8c4d8>조각은 <color=#8fe28f>{KeySources()}</color> 클리어 보상으로 얻습니다 · 모자라면 연습 입장</color>";
                }
                raidLine.text = $"{state}\n{how}";
            }
            rewards.text = $"<color=#ffe066>보상</color>  카드 4장 중 1장   <color=#b8c4d8>클리어 경험치 {Progression.XpPercent(DungeonRewards.ClearXp(def, numbers, DungeonRank.C), level)} (내 레벨 기준) + 랭크 보너스(SSS +50%)</color>";
            var list2 = DungeonRewards.Slots(def, numbers, cls);
            for (int i = 0; i < slots.Count; i++)
            {
                var sl = slots[i];
                bool on = i < list2.Count;
                sl.frame.gameObject.SetActive(on);
                sl.name.gameObject.SetActive(on);
                if (!on) continue;
                var it = list2[i];
                sl.icon.sprite = Game.Art.Get(it.icon);
                sl.frame.color = it.special ? new Color32(96, 70, 22, 255) : new Color32(14, 20, 32, 255);
                sl.name.text = it.special ? $"<color=#ffd34a>{it.name}</color>" : it.name;
            }

            string reason = Game.Dungeon != null ? Game.Dungeon.CannotEnterReason(def, difficulty, now) : "준비 중";
            string practice = reason == null ? PracticeReason(def, now) : null;
            status.text = reason != null ? $"<color=#ff9f7a>{reason}</color>" : practice != null ? $"<color=#ffe066>보상 없는 연습 입장입니다.</color> <color=#b8c4d8>{practice}</color>" : "<color=#8fe28f>입장할 수 있습니다.</color>";
            enterButton.image.color = reason == null ? Color.white : new Color(1f, 1f, 1f, 0.45f);
            enterButton.image.sprite = Game.Art.Get(practice != null ? "ui_btngray" : "ui_btn");
            TextOf(enterButton).text = practice != null ? "연습 입장" : "입장";
            hint.text = "<color=#b8c4d8>↑/↓ 던전   ←/→ 난이도   Enter 입장   E 탭 전환   ESC 닫기</color>";
        }

        /// <summary>The mid raids that drop seal key fragments, by name ("해골왕").</summary>
        static string KeySources()
        {
            var names = new List<string>();
            foreach (var r in DungeonDatabase.Raids) if (r.keyMax > 0) names.Add(r.name);
            return string.Join("·", names);
        }

        void PickDifficulty(DungeonDifficulty d)
        {
            var def = Selected;
            if (def == null || def.isRaid) return;
            if (!Game.Session.Dungeons.IsUnlocked(def, d))
            {
                GameEvents.RaiseToast($"{DungeonDatabase.Difficulty(d - 1).name} 난이도를 먼저 클리어해야 합니다.");
                Game.Audio.PlaySfx("cancel");
                return;
            }
            difficulty = d;
            Game.Audio.PlaySfx("select");
            Refresh();
        }

        static Text TextOf(Button b) => b.GetComponentInChildren<Text>();

        /// <summary>Why a raid entry would give no reward (null = it gives the reward). Daily dungeons always reward.</summary>
        string PracticeReason(DungeonDef def, DateTime now)
        {
            if (def == null || !def.isRaid) return null;
            if (!Game.Session.Dungeons.RaidRewardAvailable(def, now)) return def.raidTier == RaidTier.Mid ? "오늘 보상을 이미 받았습니다." : "이번 주 보상을 이미 받았습니다.";
            if (def.keyCost > 0 && Game.Session.Inventory.Count(DungeonDatabase.SealKey) < def.keyCost) return "봉인 열쇠 조각이 모자랍니다.";
            return null;
        }

        bool TryEnter()
        {
            var def = Selected;
            if (def == null || Game.Dungeon == null) return false;
            string practice = Game.Dungeon.CannotEnterReason(def, difficulty, ResetClock.Now) == null ? PracticeReason(def, ResetClock.Now) : null;
            if (practice != null)
            {
                Game.UI.Confirm($"{practice}\n<size=18>클리어해도 보상은 없고, 이야기 진행만 이어집니다.</size>\n연습으로 입장할까요?", () => { if (!Game.Dungeon.Enter(def, difficulty)) Refresh(); }, true);
                return false;
            }
            bool ok = Game.Dungeon.Enter(def, difficulty);
            if (!ok) Refresh();
            return ok;
        }

        protected override void Update()
        {
            base.Update();
            if (!TakesInput || !gameObject.activeInHierarchy) return;
            var input = Game.Input;
            Vector2 move = input.Move;
            int dx = move.x > 0.5f && lastMoveX <= 0.5f ? 1 : move.x < -0.5f && lastMoveX >= -0.5f ? -1 : 0;
            int dy = move.y > 0.5f && lastMoveY <= 0.5f ? -1 : move.y < -0.5f && lastMoveY >= -0.5f ? 1 : 0;
            lastMoveX = move.x;
            lastMoveY = move.y;
            if (dy != 0 && rows.Count > 0)
            {
                selected = (selected + dy + rows.Count) % rows.Count;
                Game.Audio.PlaySfx("select");
                Refresh();
            }
            if (dx != 0 && Selected != null && !Selected.isRaid)
            {
                var next = (DungeonDifficulty)Mathf.Clamp((int)difficulty + dx, 0, DungeonDatabase.DifficultyCount - 1);
                if (next != difficulty) PickDifficulty(next);
            }
            if (input.InteractPressed) SetTab(!raidTab);
            else if (input.SubmitPressed) TryEnter();
        }

        // ---------- Automated checks ----------

        public bool DevSelect(string dungeonId, DungeonDifficulty d)
        {
            var def = DungeonDatabase.Get(dungeonId);
            if (def == null) return false;
            raidTab = def.isRaid;
            var list = Listed;
            for (int i = 0; i < list.Count; i++) if (list[i].id == dungeonId) selected = i;
            difficulty = d;
            Refresh();
            return Selected == def;
        }

        public bool DevEnter() => TryEnter();
        public string DevStatus => status != null ? status.text : "";
        public string DevDayText => dayText != null ? dayText.text : "";
        public bool DevRaidTab => raidTab;

        /// <summary>"개방" / "닫힘" tag of a listed dungeon row (null if not listed on this tab).</summary>
        public string DevRowTag(string dungeonId)
        {
            foreach (var r in rows) if (r.def.id == dungeonId) return r.tag.text;
            return null;
        }
    }
}
