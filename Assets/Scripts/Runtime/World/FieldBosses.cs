using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [FIELD BOSS] One boss per region on a fixed hunting ground. It appears 30 minutes after it was last defeated (server
    /// time) and stays until it is defeated; the server credits one kill per character per 30 minutes. It drops gold, materials and, at a low chance, its growth accessory (MonsterDef.bossGear).
    /// </summary>
    public static class FieldBosses
    {
        public const int IntervalSeconds = 1800; // [BALANCE 2026-10-14] every 30 minutes (was 15)

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

        // [FIELD BOSS 2026-10-14] A boss comes back IntervalSeconds after it was last defeated, by server time (not at
        // clock-aligned windows: killed at :29 it used to return at :30). Online, the server's kill log is the truth
        // (GET /characters/:uuid/field-bosses -> next_at); offline, the local defeat time.
        static readonly Dictionary<string, DateTime> nextAt = new Dictionary<string, DateTime>(); // local clock
        static string loadedFor, loadingFor;
        static float retryAt;

        static bool Online => OnlineSession.Playing;

        /// <summary>True when the boss of this map may be out now (online: once the server schedule is known).</summary>
        public static bool Ready(string mapId)
        {
            if (Online && !EnsureLoaded()) return false;
            return !nextAt.TryGetValue(mapId, out var at) || DateTime.UtcNow >= at;
        }

        /// <summary>Seconds until the boss of this map comes back (0 = out now).</summary>
        public static int SecondsToNext(string mapId) =>
            nextAt.TryGetValue(mapId, out var at) ? Math.Max(0, (int)Math.Ceiling((at - DateTime.UtcNow).TotalSeconds)) : 0;

        public static bool Waiting(string mapId) => SecondsToNext(mapId) > 0;

        /// <summary>Defeated here: it returns one interval later (the server records the same kill).</summary>
        public static void MarkDefeated(string mapId) => nextAt[mapId] = DateTime.UtcNow.AddSeconds(IntervalSeconds);

        static bool EnsureLoaded()
        {
            string id = OnlineSession.Current?.ActiveCharacter;
            if (id == null) return false;
            if (loadedFor == id) return true;
            if (loadingFor == id || UnityEngine.Time.unscaledTime < retryAt) return false;
            if (loadedFor != null) nextAt.Clear(); // another character
            loadingFor = id;
            ApiClient.Instance.Get("/characters/" + id + "/field-bosses", r =>
            {
                if (loadingFor == id) loadingFor = null;
                if (!r.ok || OnlineSession.Current?.ActiveCharacter != id) { retryAt = UnityEngine.Time.unscaledTime + 10f; return; }
                // Server clock vs this PC: next_at is turned into the local clock with the offset of this reply.
                DateTime server = ParseTime(MiniJson.Str(r.data, "server_time"), DateTime.UtcNow);
                TimeSpan offset = DateTime.UtcNow - server;
                nextAt.Clear();
                foreach (var b in MiniJson.Arr(r.data, "bosses") ?? new List<object>())
                {
                    string map = MiniJson.Str(b, "map_id");
                    if (!string.IsNullOrEmpty(map)) nextAt[map] = ParseTime(MiniJson.Str(b, "next_at"), server) + offset;
                }
                loadedFor = id;
            });
            return false;
        }

        static DateTime ParseTime(string iso, DateTime fallback) =>
            DateTime.TryParse(iso, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out var t) ? t : fallback;

        /// <summary>One line for the world map ("필드 보스 ... · 다음 출현 7분 후").</summary>
        public static string InfoLine(string mapId)
        {
            var d = For(mapId);
            if (d == null) return null;
            var m = MonsterDatabase.Get(d.monsterId);
            var gear = m != null ? EquipmentDatabase.Get(m.bossGear) : null;
            string when = Waiting(mapId) ? L.F("다음 출현 {0}분 후", SecondsToNext(mapId) / 60 + 1) : L.T("지금 출현 중 (30분마다)");
            return L.F("<color=#ff9f43>필드 보스</color> {0} Lv.{1} · {2}", L.T(m?.name), LevelOf(d), when) + (gear != null ? L.F(" · 보스 장비 <color=#ffb347>{0}</color>", L.T(gear.name)) : "");
        }
    }
}
