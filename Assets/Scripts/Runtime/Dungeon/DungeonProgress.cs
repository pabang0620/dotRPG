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
        readonly Dictionary<string, int> bestRanks = new Dictionary<string, int>();
        readonly HashSet<string> cleared = new HashSet<string>();

        public static string Key(string dungeonId, DungeonDifficulty d) => $"{dungeonId}:{(int)d}";

        public void Reset()
        {
            DailyStamp = 0;
            entriesUsed = 0;
            RaidClaimedStamp = 0;
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

        public bool RaidRewardAvailable(DateTime now) => ResetClock.WeeklyExpired(RaidClaimedStamp, now);

        public void ClaimRaid(DateTime now) => RaidClaimedStamp = ResetClock.WeeklyResetStart(now).Ticks;

        public bool IsCleared(string dungeonId, DungeonDifficulty d) => cleared.Contains(Key(dungeonId, d));

        /// <summary>일반 is always open; each harder difficulty needs the one before it cleared once.</summary>
        public bool IsUnlocked(DungeonDef dungeon, DungeonDifficulty d)
        {
            if (dungeon == null) return false;
            if (dungeon.isRaid || d == DungeonDifficulty.Normal) return true;
            return IsCleared(dungeon.id, d - 1);
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
            if (data.dungeonBestRanks != null)
                foreach (var s in data.dungeonBestRanks)
                    if (s != null && !string.IsNullOrEmpty(s.id) && s.count > 0) bestRanks[s.id] = Math.Min(s.count - 1, (int)DungeonRank.F);
            if (data.dungeonCleared != null)
                foreach (var k in data.dungeonCleared)
                    if (!string.IsNullOrEmpty(k)) cleared.Add(k);
        }
    }
}
