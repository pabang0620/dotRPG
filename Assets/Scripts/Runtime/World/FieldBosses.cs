using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [FIELD BOSS] One boss per region on a fixed hunting ground. It appears at every quarter hour (:00 :15 :30 :45, by the
    /// clock) and stays until it is defeated; each character is credited one kill per quarter hour (the server checks the same
    /// window). It drops gold, materials and, at a low chance, its growth accessory (MonsterDef.bossGear).
    /// </summary>
    public static class FieldBosses
    {
        public const int IntervalSeconds = 900;

        public sealed class Def
        {
            public string mapId, monsterId;
            /// <summary>Levels above the zone's monsters.</summary>
            public int levelOffset = 2;
            /// <summary>XP as this many of the zone's monster kills.</summary>
            public int xpKills = 25;
        }

        public static readonly IReadOnlyList<Def> All = new[]
        {
            new Def { mapId = "forest_depths", monsterId = "fboss_forest" },
            new Def { mapId = "canyon_ridge", monsterId = "fboss_canyon" },
            new Def { mapId = "winter_peak", monsterId = "fboss_winter" },
        };

        public static Def For(string mapId)
        {
            foreach (var d in All) if (d.mapId == mapId) return d;
            return null;
        }

        public static int LevelOf(Def d) => (HuntingGrounds.Get(d.mapId)?.monsterLevel ?? 1) + d.levelOffset;
        public static int XpOf(Def d) => (HuntingGrounds.Get(d.mapId)?.KillXp ?? 1) * d.xpKills;

        /// <summary>The quarter hour we are in (UTC seconds / 900), the same number the server uses.</summary>
        public static long Window => DateTimeOffset.UtcNow.ToUnixTimeSeconds() / IntervalSeconds;

        /// <summary>Seconds until the next quarter hour.</summary>
        public static int SecondsToNext => (int)(IntervalSeconds - DateTimeOffset.UtcNow.ToUnixTimeSeconds() % IntervalSeconds);

        /// <summary>Quarter hour in which this PC last defeated the boss of a map (it comes back in the next one).</summary>
        static readonly Dictionary<string, long> killedIn = new Dictionary<string, long>();

        public static bool DefeatedThisWindow(string mapId) => killedIn.TryGetValue(mapId, out long w) && w == Window;
        public static void MarkDefeated(string mapId) => killedIn[mapId] = Window;

        /// <summary>One line for the world map ("필드 보스 ... · 다음 출현 7분 후").</summary>
        public static string InfoLine(string mapId)
        {
            var d = For(mapId);
            if (d == null) return null;
            var m = MonsterDatabase.Get(d.monsterId);
            var gear = m != null ? EquipmentDatabase.Get(m.bossGear) : null;
            string when = DefeatedThisWindow(mapId) ? $"다음 출현 {SecondsToNext / 60 + 1}분 후" : "지금 출현 중 (15분마다)";
            return $"<color=#ff9f43>필드 보스</color> {m?.name} Lv.{LevelOf(d)} · {when}" + (gear != null ? $" · 보스 장비 <color=#ffb347>{gear.name}</color>" : "");
        }
    }
}
