using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [GROWTH] 성장 추천 (Docs/PLAN_GROWTH_GUIDE.md): what to do while no main or side quest can be worked on (the next one
    /// needs a higher level). Builds a ranked list from the weekday dungeons left today, the matching normal dungeon, the
    /// best hunting ground, daily / weekly requests and the party bonus. Other content adds itself with
    /// <see cref="RegisterSource"/> (mini dungeons). Client data only; nothing here talks to the server.
    /// </summary>
    public static class GrowthGuide
    {
        /// <summary>One recommendation line. <see cref="action"/> null = a hint without a button.</summary>
        public sealed class Entry
        {
            /// <summary>Sort key: lower shows first (see the Order* constants).</summary>
            public int order;
            /// <summary>Short name for the HUD line ("얼어붙은 회랑(모험)").</summary>
            public string shortText;
            /// <summary>Full line for the quest window.</summary>
            public string text;
            /// <summary>Button label, already translated.</summary>
            public string button;
            public Action action;
        }

        /// <summary>Mini dungeon catch-up (the next main quest waits on level): right after the main quest line.</summary>
        public const int OrderCatchUp = 50;
        public const int OrderWeekday = 100, OrderNormal = 200, OrderHunt = 300, OrderDaily = 400, OrderParty = 500, OrderExtra = 600;

        /// <summary>Registered sources: each returns an entry for the given level, or null when it has nothing to offer.</summary>
        static readonly List<Func<int, Entry>> sources = new List<Func<int, Entry>>();

        /// <summary>
        /// Adds a recommendation source (e.g. mini dungeons). Called once at startup; the function runs every time the
        /// guide is built, gets the hero's level and returns null when it has nothing for that level. Set
        /// <see cref="Entry.order"/> to rank it (default slot <see cref="OrderExtra"/>).
        /// </summary>
        public static void RegisterSource(Func<int, Entry> source)
        {
            if (source != null && !sources.Contains(source)) sources.Add(source);
        }

        static float activeCheckedAt = -10f;
        static int activeLevel = -1;
        static bool activeCached;

        static int Level => Game.Session != null ? Game.Session.Progression.Level : 1;

        /// <summary>
        /// True when no main or side quest is active, ready to turn in or waiting for its giver (the next one needs a
        /// higher level). Re-checked at most twice a second, and at once after a level-up.
        /// </summary>
        public static bool Active
        {
            get
            {
                if (Game.Quest == null || Game.Session == null) return false;
                if (Time.unscaledTime - activeCheckedAt < 0.5f && activeLevel == Level) return activeCached;
                activeCheckedAt = Time.unscaledTime;
                activeLevel = Level;
                activeCached = Game.Quest.WithStatus(QuestStatus.Active, QuestStatus.ReadyToTurnIn, QuestStatus.Available).Count == 0;
                return activeCached;
            }
        }

        /// <summary>Forces the next <see cref="Active"/> read to look at the quests again (a quest just changed).</summary>
        public static void Invalidate() => activeCheckedAt = -10f;

        /// <summary>Lowest level at which a locked quest whose other requirements are met opens (0 = none waits on level).</summary>
        public static int NextQuestLevel()
        {
            var q = Game.Quest;
            if (q == null || Game.Session == null) return 0;
            var j = Game.Session.Journal;
            int level = Level, best = 0;
            foreach (var d in q.Database.All)
            {
                if (d.minLevel <= level || q.StatusOf(d.id) != QuestStatus.Locked) continue;
                if (!d.requires.TrueForAll(j.IsCompleted) || !d.requiresFlags.TrueForAll(j.HasFlag)) continue;
                if (best == 0 || d.minLevel < best) best = d.minLevel;
            }
            return best;
        }

        /// <summary>The highest hunting ground the hero's level has reached (auto-progress hunts here too).</summary>
        public static HuntingZone BestZone(int level)
        {
            HuntingZone best = null;
            foreach (var z in HuntingGrounds.All)
                if (z.minLevel <= level && (best == null || z.minLevel > best.minLevel)) best = z;
            return best;
        }

        /// <summary>Expected XP per minute in a zone at the plan's pace (<see cref="HuntingGrounds.KillsPerMinute"/>).</summary>
        public static int XpPerMinute(HuntingZone z) => z == null ? 0 : z.KillXp * HuntingGrounds.KillsPerMinute;

        /// <summary>The hardest dungeon difficulty whose recommended level the hero has reached (null below 일반's).
        /// [TIERS 2026-10-29] 일반 던전 stop at 영웅; weekday dungeons go on to 심연.</summary>
        static DifficultyDef DifficultyFor(int level, bool weekday)
        {
            DifficultyDef best = null;
            int top = weekday ? (int)DungeonDifficulty.Lord : (int)DungeonDatabase.FirstWeekdayOnly;
            for (int i = 0; i < top; i++) // [CH3] 군주 is weekday-only (recommended separately)
            {
                var d = DungeonDatabase.Difficulty((DungeonDifficulty)i);
                if (d.recommendedLevel <= level) best = d;
            }
            return best;
        }

        static string DungeonLabel(DungeonDef d, DifficultyDef diff) => L.F("{0}({1})", L.T(d.name), L.T(diff.name));

        /// <summary>The region theme of the hero's level: the normal dungeon and weekday dungeon of that region come first.</summary>
        static MapTheme RegionTheme(int level)
        {
            var z = BestZone(level);
            if (z == null || z.theme == MapTheme.Forest) return MapTheme.Forest;
            return z.theme == MapTheme.Canyon ? MapTheme.Canyon : MapTheme.Winter;
        }

        /// <summary>Every recommendation for now, best first.</summary>
        public static List<Entry> Build()
        {
            var list = new List<Entry>();
            if (Game.Session == null) return list;
            int level = Level;
            var now = ResetClock.Now;
            var theme = RegionTheme(level);
            var diff = DifficultyFor(level, true);
            var normalDiff = DifficultyFor(level, false);

            // 1. Weekday dungeons open today with entries left (the region's own first).
            int left = Game.Session.Dungeons.EntriesLeft(now);
            // [UNLOCK] Content the main quest has not opened yet is never recommended.
            if (diff != null && left > 0 && ContentUnlocks.IsOpen(ContentUnlocks.Feature.WeekdayDungeon))
            {
                DungeonDef pick = null;
                foreach (var d in DungeonDatabase.Weekday)
                {
                    if (!ResetClock.IsOpen(d, now)) continue;
                    if (pick == null || (d.theme == theme && pick.theme != theme)) pick = d;
                }
                if (pick != null)
                {
                    var def = pick; var id = diff.id;
                    list.Add(new Entry
                    {
                        order = OrderWeekday,
                        shortText = DungeonLabel(def, diff),
                        text = L.F("요일 던전 <color=#ffe066>{0}</color> · 오늘 남은 입장 {1}/{2}", DungeonLabel(def, diff), left, DungeonDatabase.DailyEntries),
                        button = L.T("던전 창 열기"),
                        action = () => OpenDungeon(def.id, id),
                    });
                }
            }

            // 1b. [CH3 step 5] 군주 from Lv.45 (its recommended level) while chapter 3 is open: today's weekday dungeon on 군주.
            if (Chapter3.Enabled && level >= LordGuideLevel && left > 0 && ContentUnlocks.IsOpen(ContentUnlocks.Feature.WeekdayDungeon))
            {
                DungeonDef lord = null;
                foreach (var d in DungeonDatabase.Weekday)
                    if (ResetClock.IsOpen(d, now) && DungeonDatabase.LordOpen(d)) { lord = d; break; }
                if (lord != null)
                {
                    var def = lord; var lordDiff = DungeonDatabase.Difficulty(DungeonDifficulty.Lord);
                    list.Add(new Entry
                    {
                        order = OrderWeekday,
                        shortText = DungeonLabel(def, lordDiff),
                        text = L.F("군주 던전 <color=#ff9f6b>{0}</color> · 오늘 남은 입장 {1}/{2}", DungeonLabel(def, lordDiff), left, DungeonDatabase.DailyEntries),
                        button = L.T("던전 창 열기"),
                        action = () => OpenDungeon(def.id, DungeonDifficulty.Lord),
                    });
                }
            }

            // 2. The region's normal dungeon (every day, no entry count).
            if (normalDiff != null && ContentUnlocks.IsOpen(ContentUnlocks.Feature.NormalDungeon))
            {
                DungeonDef normal = null;
                foreach (var d in DungeonDatabase.Normal)
                    if (normal == null || (d.theme == theme && normal.theme != theme)) normal = d;
                if (normal != null)
                {
                    var def = normal; var id = normalDiff.id;
                    list.Add(new Entry
                    {
                        order = OrderNormal,
                        shortText = DungeonLabel(def, normalDiff),
                        text = L.F("일반 던전 <color=#8fe28f>{0}</color> · 매일, 횟수 제한 없음", DungeonLabel(def, normalDiff)),
                        button = L.T("던전 창 열기"),
                        action = () => OpenDungeon(def.id, id),
                    });
                }
            }

            // 3. The best hunting ground for the level, with the expected XP per minute.
            var zone = BestZone(level);
            if (zone != null)
            {
                list.Add(new Entry
                {
                    order = OrderHunt,
                    shortText = L.F("{0} 사냥터", L.T(zone.name)),
                    text = L.F("사냥터 <color=#78d6ff>{0}</color> (Lv{1}~{2}) · 분당 경험치 약 {3:N0}", L.T(zone.name), zone.minLevel, zone.maxLevel, XpPerMinute(zone)) + Progression.XpShareSuffix(XpPerMinute(zone), level), // [UI 2026-10-29] the share of the bar too
                    // Before 자동 사냥 opens the line is a hint only (the player walks there).
                    button = ContentUnlocks.IsOpen(ContentUnlocks.Feature.AutoHunt) ? L.T("추천 사냥터로 자동 사냥") : null,
                    action = ContentUnlocks.IsOpen(ContentUnlocks.Feature.AutoHunt) ? () => { QuestAutoPilot.StartGrowth(); if (Game.Flow != null) Game.Flow.CloseInventory(); } : (Action)null,
                });
            }

            // 3b. [CH3 step 5] Lv.41+: the other hunting ground of the level (메마른 협곡 dry_* or 지하 B1~B4), whichever the
            // line above did not pick, so both chapter 3 routes show up by level (Docs/ch3/LIVE_CONTENT.md 5).
            var other = Chapter3.Enabled && level >= Chapter3.ClosedCap + 1 && zone != null ? OtherChapter3Zone(level, zone) : null;
            if (other != null)
            {
                var target = other;
                list.Add(new Entry
                {
                    order = OrderHunt,
                    shortText = L.F("{0} 사냥터", L.T(target.name)),
                    text = L.F("사냥터 <color=#78d6ff>{0}</color> (Lv{1}~{2}) · 분당 경험치 약 {3:N0}", L.T(target.name), target.minLevel, target.maxLevel, XpPerMinute(target)) + Progression.XpShareSuffix(XpPerMinute(target), level),
                    button = L.T("이동"),
                    action = () => { if (Game.Flow != null && (Game.Dungeon == null || !Game.Dungeon.InRun)) { Game.Flow.CloseInventory(); Game.Flow.TravelTo(target.id); } },
                });
            }

            // 4. Daily / weekly requests still to take or hand in (online, once unlocked).
            if (DailyQuestClient.Loaded && DailyQuestClient.Unlocked && ContentUnlocks.IsOpen(ContentUnlocks.Feature.Daily))
            {
                int day = 0, week = 0;
                foreach (var s in DailyQuestClient.Slots) if (!s.Claimed) day++;
                foreach (var s in DailyQuestClient.WeeklySlots) if (!s.Claimed) week++;
                if (day + week > 0)
                    list.Add(new Entry
                    {
                        order = OrderDaily,
                        shortText = L.T("일일 의뢰"),
                        text = L.F("일일 의뢰 {0}개 · 주간 의뢰 {1}개 남음", day, week),
                        button = L.T("의뢰 창 열기"),
                        action = () => { if (DailyQuestScreen.Instance != null) Game.Flow.OpenWindow(DailyQuestScreen.Instance); },
                    });
            }

            // 5. Party hunting bonus (shared kills online).
            if (OnlineSession.Playing && ContentUnlocks.IsOpen(ContentUnlocks.Feature.PartyFinder))
                list.Add(new Entry
                {
                    order = OrderParty,
                    shortText = L.T("파티 사냥"),
                    text = L.T("파티 사냥 처치 경험치: 2인 +15% · 3인 +30% · 4인 +50%"),
                });

            // 6. Registered sources (mini dungeons and later content).
            foreach (var source in sources)
            {
                Entry e = null;
                try { e = source(level); }
                catch (Exception ex) { Debug.LogException(ex); }
                if (e == null) continue;
                if (e.order == 0) e.order = OrderExtra;
                list.Add(e);
            }

            // Stable sort by order (List.Sort is not stable).
            var sorted = new List<Entry>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                int at = sorted.Count;
                while (at > 0 && sorted[at - 1].order > list[i].order) at--;
                sorted.Insert(at, list[i]);
            }
            return sorted;
        }

        /// <summary>
        /// HUD card title: "성장 추천 · Lv23 → 다음 퀘스트 Lv25". <paramref name="compact"/>: the folded phone tracker's one
        /// short line ("성장 추천 Lv23 → 25").
        /// </summary>
        public static string HudTitle(bool compact = false)
        {
            int next = NextQuestLevel();
            if (compact) return next > 0 ? L.F("성장 추천 Lv{0} → {1}", Level, next) : L.F("성장 추천 Lv{0}", Level);
            return next > 0 ? L.F("성장 추천 · Lv{0} → 다음 퀘스트 Lv{1}", Level, next) : L.F("성장 추천 · Lv{0}", Level);
        }

        /// <summary>HUD card body: the top dungeon and the hunting ground, then how to start.</summary>
        public static string HudBody(List<Entry> entries)
        {
            string dungeon = null, hunt = null;
            foreach (var e in entries)
            {
                if (dungeon == null && (e.order == OrderWeekday || e.order == OrderNormal)) dungeon = e.shortText;
                if (hunt == null && e.order == OrderHunt) hunt = e.shortText;
            }
            var sb = new StringBuilder();
            if (dungeon != null && hunt != null) sb.Append(L.F("추천: {0} / {1}", dungeon, hunt));
            else if (dungeon != null || hunt != null) sb.Append(L.F("추천: {0}", dungeon ?? hunt));
            if (sb.Length > 0) sb.Append('\n');
            sb.Append(L.T("<color=#b8c4d8>자동 진행: 추천 사냥터에서 사냥 · 퀘스트 창에서 전체 보기</color>"));
            return sb.ToString();
        }

        /// <summary>[CH3] 군주 is recommended from its recommended level (45), though it opens at Lv.41.</summary>
        public const int LordGuideLevel = 45;

        /// <summary>
        /// [CH3] The best zone of the other chapter 3 route than <paramref name="picked"/>: 메마른 협곡 (dry_*) when the
        /// underground was picked and the other way round (null when that route has nothing at this level).
        /// </summary>
        static HuntingZone OtherChapter3Zone(int level, HuntingZone picked)
        {
            bool pickedDry = picked.theme == MapTheme.DryCanyon;
            HuntingZone best = null;
            foreach (var z in HuntingGrounds.All)
            {
                bool dry = z.theme == MapTheme.DryCanyon, under = z.theme == MapTheme.Underground;
                if (!(pickedDry ? under : dry) || z.minLevel > level || z.maxLevel < level) continue;
                if (best == null || z.minLevel > best.minLevel) best = z;
            }
            return best;
        }

        /// <summary>Opens the dungeon window on the dungeon's tab with this difficulty chosen.</summary>
        public static void OpenDungeon(string dungeonId, DungeonDifficulty difficulty)
        {
            if (Game.UI == null || Game.UI.Dungeon == null) return;
            if (Game.Dungeon != null && Game.Dungeon.InRun) { GameEvents.RaiseToast(L.T("이미 던전 안에 있습니다.")); return; }
            Game.UI.Dungeon.OpenOn(dungeonId, difficulty);
        }
    }
}
