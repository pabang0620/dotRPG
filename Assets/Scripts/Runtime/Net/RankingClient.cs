using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [RANK 2026-10-20] 랭킹 (Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 4, 7): top 100 and my place on a board, cached by the
    /// server once a minute. Boards: world_boss (people), guild_boss (guilds), power (people, no reward).
    /// </summary>
    public static class RankingClient
    {
        public const string WorldBoard = "world_boss", GuildBoard = "guild_boss", PowerBoard = "power";

        public sealed class Entry
        {
            public int rank, level;
            public string name, guildName, career, nameColor, guildColor;
            public long value;
        }

        public sealed class Board
        {
            public string board, week;
            public readonly List<Entry> entries = new List<Entry>();
            public int myRank;
            public long myValue;
            public bool hasMe;
            public DateTime? updatedAt;
        }

        public static bool Busy { get; private set; }
        public static event Action Changed;
        static readonly Dictionary<string, Board> cache = new Dictionary<string, Board>();
        static string owner;
        static int generation;

        static string Key(string board, string week) => board + ":" + week;

        /// <summary>The last answer for this board and week (null = not loaded yet).</summary>
        public static Board Get(string board, string week)
        {
            if (!SameCharacter()) return null;
            return cache.TryGetValue(Key(board, week), out var b) ? b : null;
        }

        static bool SameCharacter()
        {
            string now = OnlineSession.Playing ? OnlineSession.Current.ActiveCharacter : null;
            if (now == owner) return now != null;
            owner = now;
            generation++;
            cache.Clear();
            return now != null;
        }

        public static void Load(string board, string week, Action<string> done = null)
        {
            if (!SameCharacter()) { done?.Invoke(L.T("온라인에서 열려요.")); return; }
            int context = generation;
            Busy = true;
            Changed?.Invoke();
            ApiClient.Instance.Get($"/rankings/{board}?week={week}&character_id={Uri.EscapeDataString(owner)}", r =>
            {
                Busy = false;
                if (context != generation) return;
                if (!r.ok) { done?.Invoke(r.code == "NETWORK" ? L.T("서버에 연결할 수 없습니다.") : string.IsNullOrEmpty(r.message) ? L.T("요청이 실패했습니다.") : r.message); Changed?.Invoke(); return; }
                var b = new Board { board = board, week = week, updatedAt = GuildClient.ParseTime(MiniJson.Str(r.data, "updated_at")) };
                foreach (var o in MiniJson.Arr(r.data, "entries") ?? new List<object>())
                    b.entries.Add(new Entry
                    {
                        rank = MiniJson.Int(o, "rank"), name = MiniJson.Str(o, "name", ""), guildName = MiniJson.Str(o, "guild_name", ""),
                        level = MiniJson.Int(o, "level"), career = MiniJson.Str(o, "career", ""),
                        value = (long)MiniJson.Num(o, "value"), nameColor = MiniJson.Str(o, "name_color"), guildColor = MiniJson.Str(o, "guild_color"),
                    });
                var me = MiniJson.Obj(r.data, "me");
                b.hasMe = me != null;
                b.myRank = MiniJson.Int(me, "rank");
                b.myValue = (long)MiniJson.Num(me, "value");
                cache[Key(board, week)] = b;
                done?.Invoke(null);
                Changed?.Invoke();
            });
        }

        /// <summary>Rich-text colour of a ranking name colour ('gold' | 'orange' | 'silver'), null for none.</summary>
        public static string ColorHex(string nameColor) => nameColor switch
        {
            "gold" => "#ffd34a",
            "orange" => "#ff9f4a",
            "silver" => "#d6dee9",
            _ => null,
        };

        /// <summary>The name wrapped in its ranking colour (plain when none).</summary>
        public static string Colored(string name, string nameColor)
        {
            string hex = ColorHex(nameColor);
            return hex == null ? name : $"<color={hex}>{name}</color>";
        }
    }
}
