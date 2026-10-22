using System;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    public sealed class MiniDungeonDef
    {
        public int Tier, MinLevel, MaxLevel;
        public string Name, MapId, MonsterId;
        public MapTheme Theme;
        public string Music;
    }

    /// <summary>Solo experience training rooms. Their allowance is shared, not three separate entry tickets.</summary>
    public static class MiniDungeonCatalog
    {
        /// <summary>Longest daily allowance (tiers 10~30). The seconds used today are shared by every tier.</summary>
        public const int DailySeconds = 30 * 60;
        /// <summary>[CH3] Tier 40 gets 10 minutes a day: catch-up is on almost every day there (Docs/ch3/LIVE_CONTENT.md 1).</summary>
        public const int DailySecondsTier40 = 10 * 60;
        public const int Chapter3Tier = 40;
        public const int SpawnCount = 6;
        public const float RespawnSeconds = 5f;
        public static readonly MiniDungeonDef[] All =
        {
            new MiniDungeonDef { Tier = 10, MinLevel = 10, MaxLevel = 19, Name = "이끼빛 수련의 숲", MapId = "mini_dungeon_10", MonsterId = "skel_warrior", Theme = MapTheme.Forest, Music = MapRegistry.MusicDgnForest }, // i18n-ignore
            new MiniDungeonDef { Tier = 20, MinLevel = 20, MaxLevel = 29, Name = "수정빛 수련의 석실", MapId = "mini_dungeon_20", MonsterId = "skel_warrior", Theme = MapTheme.Canyon, Music = MapRegistry.MusicDgnCanyon }, // i18n-ignore
            new MiniDungeonDef { Tier = 30, MinLevel = 30, MaxLevel = 39, Name = "서리빛 수련의 동굴", MapId = "mini_dungeon_30", MonsterId = "skel_warrior", Theme = MapTheme.Winter, Music = MapRegistry.MusicDgnWinter }, // i18n-ignore
            // [CH3] Closed with the chapter (MapRegistry.Chapter3Maps); the level cap is 40 while closed, so Lv.40 has no tier.
            new MiniDungeonDef { Tier = 40, MinLevel = 40, MaxLevel = 49, Name = "잿빛 수련의 협곡", MapId = "mini_dungeon_40", MonsterId = "void_hound", Theme = MapTheme.DryCanyon, Music = "music_sayab" }, // i18n-ignore
        };

        /// <summary>Daily allowance of a tier in seconds (server miniDungeonRules.dailyLimitMs).</summary>
        public static int DailySecondsFor(int tier) => tier == Chapter3Tier ? DailySecondsTier40 : DailySeconds;

        /// <summary>The tier the hero's level enters (0 = none: below 10 or at the level cap).</summary>
        public static int TierAt(int level)
        {
            if (level >= Progression.LevelCap) return 0;
            foreach (var d in All) if (level >= d.MinLevel && level <= d.MaxLevel && Visible(d)) return d.Tier;
            return 0;
        }

        /// <summary>Today's allowance for the current hero (the tier their level enters; 30 minutes when none).</summary>
        public static int CurrentDailySeconds => DailySecondsFor(TierAt(Game.Session != null ? Game.Session.Progression.Level : 1));

        /// <summary>[CH3] Tier 40 exists only while chapter 3 is open.</summary>
        public static bool Visible(MiniDungeonDef d) => d != null && (d.Tier != Chapter3Tier || Chapter3.Enabled);

        public static MiniDungeonDef Get(int tier) => Array.Find(All, d => d.Tier == tier);
        public static MiniDungeonDef ForMap(string map) => Array.Find(All, d => d.MapId == map);
        public static bool IsMap(string map) => ForMap(map) != null;
        /// <summary>
        /// [BALANCE 2026-10-18 catch-up] The mini dungeon is where a hero fills the level the next main quest waits on, not a
        /// second hunting ground. Normally one entry-level bar per <see cref="KillsPerBar"/> kills (about 0.5x hunting at Lv20);
        /// while every open main quest needs a higher level, one bar per <see cref="KillsPerBarStuck"/> (x10, about 5x at Lv20).
        /// The check runs again on every kill, so the bonus stops the moment the level is reached. With the Lv16+ curve x2.2,
        /// Lv40 stays at 42 hours or more even with a mini dungeon every day at the kill-rate cap
        /// (Tools/balance/theory_progress.py --measured --mini, Docs/MiniDungeon/design.json). Server: miniDungeonRules.ts.
        /// </summary>
        public const int KillsPerBar = 8000, KillsPerBarStuck = 800, CatchUpMultiplier = KillsPerBar / KillsPerBarStuck;
        /// <summary>
        /// [BALANCE 2026-10-21] Gold per kill (no drops, no materials): three times a hunting ground's gold pile (8~17), so a
        /// player who spends the daily 30 minutes here instead of hunting is not left without gold for enhancement. With the
        /// day model fixed in Tools/balance/theory_gold.py (--mini), both paths reach Lv40 with 81~88% of the clear kit.
        /// Server: miniDungeonRules.MINI_GOLD_PER_KILL.
        /// </summary>
        public const int GoldPerKill = 36;
        public static int XpPerKill(int entryLevel, bool stuck = false) =>
            Math.Max(1, (int)Math.Ceiling(Progression.XpToNext(entryLevel) / (double)(stuck ? KillsPerBarStuck : KillsPerBar)));

        /// <summary>
        /// The level the next main quest waits on, or 0 when none does: at least one open main quest (requirements and flags
        /// met, not completed) and every one of them needs a level above the hero's (its minLevel or a level objective).
        /// Main quests blocked by anything but level are ignored. Same rule as the server's mainQuestLevelGate.
        /// </summary>
        public static int CatchUpGate(int level)
        {
            var q = Game.Quest;
            if (q == null || Game.Session == null) return 0;
            var j = Game.Session.Journal;
            int gate = 0;
            foreach (var d in q.Database.All)
            {
                if (d.Kind != QuestKind.Main || j.IsCompleted(d.id)) continue;
                if (!d.requires.TrueForAll(j.IsCompleted) || !d.requiresFlags.TrueForAll(j.HasFlag)) continue;
                int need = d.minLevel;
                foreach (var st in d.steps)
                    foreach (var o in st.objectives)
                        if (o.type == ObjectiveTypes.Level) need = Math.Max(need, o.count);
                if (need <= level) return 0;
                gate = gate == 0 ? need : Math.Min(gate, need);
            }
            return gate;
        }

        /// <summary>Small open arena: six connected spawn courts, decorative water stays beyond the combat floor.</summary>
        public static string Layout(string map)
        {
            var def = ForMap(map);
            if (def == null) return null;
            const int w = 28, h = 22;
            var cells = new char[w, h];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                bool border = x < 2 || y < 2 || x >= w - 2 || y >= h - 2;
                bool bevel = (x < 4 || x >= w - 4) && (y < 4 || y >= h - 4);
                cells[x, y] = border || bevel ? 'W' : def.Theme == MapTheme.Winter ? '.' : ',';
                if (!border && !bevel && (x == 5 || x == 22 || y == 5 || y == 16)) cells[x, y] = '=';
            }
            // Peripheral pools and paired props leave the central routes unobstructed.
            for (int y = 17; y <= 18; y++) for (int x = 4; x <= 7; x++) cells[x, y] = '~';
            for (int y = 3; y <= 4; y++) for (int x = 20; x <= 23; x++) cells[x, y] = '~';
            char prop = def.Theme == MapTheme.Forest ? 'T' : def.Theme == MapTheme.Winter ? 'q' : 'R';
            cells[4, 10] = cells[23, 10] = prop;
            cells[4, 14] = cells[23, 7] = prop;
            cells[10, 17] = cells[17, 4] = def.Theme == MapTheme.Canyon ? 'p' : 't';
            cells[14, 10] = 'P';
            cells[9, 7] = '1'; cells[18, 7] = '2';
            cells[8, 11] = '3'; cells[19, 11] = '4';
            cells[10, 15] = '5'; cells[17, 15] = '6';
            var text = new StringBuilder();
            for (int y = h - 1; y >= 0; y--) { for (int x = 0; x < w; x++) text.Append(cells[x, y]); text.Append('\n'); }
            return text.ToString();
        }
    }

    public static partial class MapRegistry
    {
        static MapInfo GetMiniDungeon(string id)
        {
            var d = MiniDungeonCatalog.ForMap(id);
            return d == null ? null : new MapInfo { id = id, displayName = L.T(d.Name), resource = "", theme = d.Theme,
                music = d.Music, instanced = true, safe = false, chapter = d.Tier == MiniDungeonCatalog.Chapter3Tier ? 3 : 0,
                hint = d.Tier == MiniDungeonCatalog.Chapter3Tier ? L.F("Lv.{0}~{1} 경험치 미니던전 · 하루 10분 · 자동 사냥", d.MinLevel, d.MaxLevel)
                    : L.F("Lv.{0}~{1} 경험치 미니던전 · 하루 총 30분 · 자동 사냥", d.MinLevel, d.MaxLevel) };
        }
    }
}
