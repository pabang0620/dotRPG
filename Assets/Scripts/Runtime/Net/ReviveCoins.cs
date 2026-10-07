using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [REVIVE] Get up where you fell (Docs/server/phase12_revive_coins.md). In hunting grounds (fields) it is always
    /// free; in dungeons, weekday dungeons and raids it is free up to Lv.10, and from Lv.11 each revive spends one revive coin, one coin comes every game day (06:00) and at most 5 are kept. Online the server
    /// owns the count; offline play (demos) keeps the same rule in PlayerPrefs per save slot.
    /// </summary>
    public static class ReviveCoins
    {
        public const int FreeUntilLevel = 10, Max = 5, StartCoins = 1;

        public static int Coins { get; private set; } = StartCoins;
        public static bool Busy { get; private set; }
        public static event Action Changed;

        static ApiClient Api => ApiClient.Instance;
        static string Path => "/characters/" + OnlineSession.Current.ActiveCharacter + "/revive";

        public static bool Free => Game.Session == null || Game.Session.Progression.Level <= FreeUntilLevel;

        /// <summary>Button text in the field (always free).</summary>
        public static string FieldLabel => "그 자리에서 부활 (무료)";

        /// <summary>Button text in a dungeon: free up to Lv.10, else the coin cost with what is left.</summary>
        public static string Label(string verb = "그 자리에서 부활")
            => Free ? $"{verb} (Lv.{FreeUntilLevel}까지 무료)" : $"{verb} (부활 코인 1 · 보유 {Coins}/{Max})";

        public static bool CanUse => Free || Coins > 0;

        public static void Refresh()
        {
            if (OnlineSession.Playing)
            {
                Api.Get(Path, r =>
                {
                    if (r.ok) Read(r.data);
                    Changed?.Invoke();
                });
                return;
            }
            LoadLocal();
            Changed?.Invoke();
        }

        static void Read(Dictionary<string, object> d)
        {
            if (d == null) return;
            Coins = MiniJson.Int(d, "coins", Coins);
        }

        /// <summary>Spends a revive (or confirms a free one). done(ok, message for the player when not ok).</summary>
        public static void Use(string context, Action<bool, string> done)
        {
            if (Busy) { done(false, "이전 요청을 처리하는 중입니다."); return; }
            if (OnlineSession.Playing)
            {
                Busy = true;
                var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["context"] = context };
                if (Game.World != null) body["map_id"] = Game.World.MapId;
                Api.Post(Path, body, r =>
                {
                    Busy = false;
                    if (!r.ok)
                    {
                        done(false, r.code == "NO_REVIVE_COIN" ? "부활 코인이 없습니다. (매일 06:00에 1개 지급)"
                            : r.code == "NETWORK" ? "서버에 연결할 수 없습니다." : string.IsNullOrEmpty(r.message) ? "부활하지 못했습니다." : r.message);
                        Refresh();
                        return;
                    }
                    Read(r.data);
                    Changed?.Invoke();
                    done(true, null);
                });
                return;
            }
            LoadLocal();
            if (!Free && context != "field")
            {
                if (Coins <= 0) { done(false, "부활 코인이 없습니다. (매일 06:00에 1개 지급)"); return; }
                Coins--;
                SaveLocal();
            }
            Changed?.Invoke();
            done(true, null);
        }

        // ---------- Offline: same rule, kept per save slot ----------

        static string Key => "dotRPG.revive." + SaveSystem.ActiveSlot;
        static long dayStamp;

        static void LoadLocal()
        {
            long today = ResetClock.DailyResetStart(ResetClock.Now).Ticks;
            string raw = PlayerPrefs.GetString(Key, "");
            var parts = raw.Split('|');
            if (parts.Length == 2 && int.TryParse(parts[0], out int c) && long.TryParse(parts[1], out long day))
            {
                Coins = c;
                dayStamp = day;
            }
            else
            {
                Coins = StartCoins;
                dayStamp = today;
            }
            if (today > dayStamp)
            {
                int days = (int)Math.Min(Max, (today - dayStamp) / TimeSpan.TicksPerDay);
                Coins = Mathf.Min(Max, Coins + Math.Max(1, days));
                dayStamp = today;
            }
            SaveLocal();
        }

        static void SaveLocal()
        {
            PlayerPrefs.SetString(Key, Coins + "|" + dayStamp);
            PlayerPrefs.Save();
        }
    }
}
