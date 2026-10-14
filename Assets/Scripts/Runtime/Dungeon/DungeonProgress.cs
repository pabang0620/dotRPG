using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// Saved dungeon progress (plan §6.0 저장): today's entries, the weekly raid lock, best rank and cleared
    /// difficulties per dungeon. Lives on <see cref="GameSession.Dungeons"/>; counters roll over through
    /// <see cref="ResetClock"/> whenever they are read.
    /// </summary>
    public sealed class DungeonProgress
    {
        /// <summary>Ticks of the daily reset the entry count belongs to.</summary>
        public long DailyStamp { get; private set; }
        int entriesUsed;
        /// <summary>Ticks of the weekly reset whose raid reward was taken (0 = never).</summary>
        public long RaidClaimedStamp { get; private set; }
        /// <summary>[RAID] Raid id -> ticks of the reset (daily for mid raids, weekly for final raids) whose reward was taken.</summary>
        readonly Dictionary<string, long> raidClaims = new Dictionary<string, long>();
        readonly Dictionary<string, int> bestRanks = new Dictionary<string, int>();
        readonly HashSet<string> cleared = new HashSet<string>();

        public static string Key(string dungeonId, DungeonDifficulty d) => $"{dungeonId}:{(int)d}";

        public void Reset()
        {
            DailyStamp = 0;
            entriesUsed = 0;
            RaidClaimedStamp = 0;
            raidClaims.Clear();
            bestRanks.Clear();
            cleared.Clear();
        }

        void Roll(DateTime now)
        {
            if (!ResetClock.DailyExpired(DailyStamp, now)) return;
            DailyStamp = ResetClock.DailyResetStart(now).Ticks;
            entriesUsed = 0;
        }

        public int EntriesUsed(DateTime now)
        {
            Roll(now);
            return entriesUsed;
        }

        /// <summary>[DUNGEON 2026-10-14] 요일 던전 초기화권: today's weekday dungeon entries back to the full count.</summary>
        public void ResetEntries(DateTime now)
        {
            Roll(now);
            entriesUsed = 0;
        }

        /// <summary>[AUDIT 2026-10-14] Online: today's used entries follow the server (resets, sweeps, other PCs).</summary>
        public void SyncEntriesUsed(DateTime now, int used)
        {
            Roll(now);
            entriesUsed = Math.Max(0, used);
        }

        /// <summary>[CONTENT] Automated checks only: today's entry count back to 0 (ranks and raid claim kept).</summary>
        public void DevClearEntries()
        {
            DailyStamp = 0;
            entriesUsed = 0;
        }

        public int EntriesLeft(DateTime now) => Math.Max(0, DungeonDatabase.DailyEntries - EntriesUsed(now));

        /// <summary>Spends one of today's weekday-dungeon entries. False when none are left.</summary>
        public bool UseEntry(DateTime now)
        {
            if (EntriesLeft(now) <= 0) return false;
            entriesUsed++;
            return true;
        }

        /// <summary>
        /// [RAID] Mid raids pay out once per open day (수·토·일 = 주 3회), final raids once per week (목 06:00).
        /// </summary>
        public bool RaidRewardAvailable(DungeonDef raid, DateTime now, DungeonDifficulty difficulty = DungeonDifficulty.Normal)
        {
            if (raid == null) return false;
            bool heroic = difficulty == DungeonDatabase.RaidHeroic;
            raidClaims.TryGetValue(ClaimKey(raid, difficulty), out long stamp);
            return raid.raidTier == RaidTier.Mid && !heroic ? ResetClock.DailyExpired(stamp, now) : ResetClock.WeeklyExpired(stamp, now);
        }

        public void ClaimRaid(DungeonDef raid, DateTime now, DungeonDifficulty difficulty = DungeonDifficulty.Normal)
        {
            if (raid == null) return;
            bool heroic = difficulty == DungeonDatabase.RaidHeroic;
            raidClaims[ClaimKey(raid, difficulty)] = raid.raidTier == RaidTier.Mid && !heroic ? ResetClock.DailyResetStart(now).Ticks : ResetClock.WeeklyResetStart(now).Ticks;
        }

        /// <summary>[RAID] 영웅 레이드 is paid once a week, kept apart from the raid's own claim.</summary>
        static string ClaimKey(DungeonDef raid, DungeonDifficulty difficulty) => difficulty == DungeonDatabase.RaidHeroic ? raid.id + ":heroic" : raid.id;

        /// <summary>Mid raid: rewarded clears this week (shown as "이번 주 n/3").</summary>
        public int RaidClearsThisWeek(DungeonDef raid, DateTime now)
        {
            if (raid == null || !raidClaims.TryGetValue(raid.id, out long stamp)) return 0;
            if (raid.raidTier != RaidTier.Mid) return ResetClock.WeeklyExpired(stamp, now) ? 0 : 1;
            // Only the last claimed day is kept; count the open days of this week up to it.
            if (ResetClock.WeeklyExpired(stamp, now)) return 0;
            var week = ResetClock.WeeklyResetStart(now);
            int n = 0;
            for (var d = week; d.Ticks <= stamp; d = d.AddDays(1))
                if (Array.IndexOf(raid.openDays, ResetClock.GameDay(d)) >= 0) n++;
            return n;
        }

        public bool IsCleared(string dungeonId, DungeonDifficulty d) => cleared.Contains(Key(dungeonId, d));

        /// <summary>일반 is always open; each harder difficulty needs the one before it cleared once.</summary>
        public bool IsUnlocked(DungeonDef dungeon, DungeonDifficulty d)
        {
            if (dungeon == null) return false;
            if (d == DungeonDifficulty.Normal) return true;
            // [RAID] 영웅 레이드 opens after the raid's first clear; raids have no other difficulty.
            if (dungeon.isRaid) return d == DungeonDatabase.RaidHeroic && IsCleared(dungeon.id, DungeonDifficulty.Normal);
            return true; // [2026-10-14] weekday and normal dungeons: every difficulty open (the level requirement still applies)
        }

        /// <summary>Best rank so far (null = never cleared).</summary>
        public DungeonRank? BestRank(string dungeonId, DungeonDifficulty d) =>
            bestRanks.TryGetValue(Key(dungeonId, d), out int r) ? (DungeonRank)r : (DungeonRank?)null;

        /// <summary>Marks a clear and keeps the better rank.</summary>
        public void RecordClear(string dungeonId, DungeonDifficulty d, DungeonRank rank)
        {
            string key = Key(dungeonId, d);
            cleared.Add(key);
            if (!bestRanks.TryGetValue(key, out int old) || (int)rank < old) bestRanks[key] = (int)rank;
        }

        public void Capture(SaveData data)
        {
            data.dungeonDailyStamp = DailyStamp;
            data.dungeonEntriesUsed = entriesUsed;
            data.raidClaimedStamp = RaidClaimedStamp;
            data.raidClaims = new List<RaidClaimSave>();
            foreach (var pair in raidClaims) data.raidClaims.Add(new RaidClaimSave { id = pair.Key, stamp = pair.Value });
            data.dungeonBestRanks = new List<ItemStack>();
            // Stored as rank + 1 so a zero count never means "SSS".
            foreach (var pair in bestRanks) data.dungeonBestRanks.Add(new ItemStack(pair.Key, pair.Value + 1));
            data.dungeonCleared = new List<string>(cleared);
        }

        /// <summary>Missing fields (older saves) leave everything at "nothing done yet".</summary>
        public void Restore(SaveData data)
        {
            Reset();
            if (data == null) return;
            DailyStamp = data.dungeonDailyStamp;
            entriesUsed = Math.Max(0, data.dungeonEntriesUsed);
            RaidClaimedStamp = data.raidClaimedStamp;
            if (data.raidClaims != null)
                foreach (var c in data.raidClaims)
                    if (c != null && !string.IsNullOrEmpty(c.id)) raidClaims[c.id] = c.stamp;
            if (data.dungeonBestRanks != null)
                foreach (var s in data.dungeonBestRanks)
                    if (s != null && !string.IsNullOrEmpty(s.id) && s.count > 0) bestRanks[s.id] = Math.Min(s.count - 1, (int)DungeonRank.F);
            if (data.dungeonCleared != null)
                foreach (var k in data.dungeonCleared)
                    if (!string.IsNullOrEmpty(k)) cleared.Add(k);
        }
    }
}
