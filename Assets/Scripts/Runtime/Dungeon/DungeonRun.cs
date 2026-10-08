using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public enum DungeonRunState
    {
        /// <summary>Fighting through the rooms.</summary>
        Playing,
        /// <summary>Boss down: slow motion + CLEAR banner, then the result.</summary>
        Clearing,
        Cleared,
        Failed,
    }

    /// <summary>
    /// Statistics of one dungeon run (plan §6.0): where the party is, how long it took (playing time only),
    /// kills, hits taken, the best combo, revives, per-member damage and - once over - the score and rewards.
    /// Plain data; <see cref="DungeonDirector"/> drives it.
    /// </summary>
    public sealed class DungeonRun
    {
        public readonly DungeonDef Dungeon;
        public readonly DungeonDifficulty Difficulty;
        /// <summary>The numbers of the difficulty (the raid has its own).</summary>
        public readonly DifficultyDef Numbers;
        public readonly int PartySize;

        public DungeonRunState State = DungeonRunState.Playing;
        public int RoomIndex;
        public readonly HashSet<int> ClearedRooms = new HashSet<int>();
        /// <summary>Seconds of play (the world frozen by a window or pause does not count).</summary>
        public float Elapsed;
        public int Kills;
        /// <summary>Monsters spawned so far (the kill score is kills / monsters).</summary>
        public int Monsters;
        /// <summary>Hits the local player took.</summary>
        public int HitsTaken;
        public int Combo, MaxCombo;
        public int RevivesUsed;
        public string FailReason;
        /// <summary>Raid only: this week's reward was already taken, so the run gives none.</summary>
        public bool RewardsLocked;
        /// <summary>[SERVER] Why a cleared run shows no cards: "held" (server review) or "noanswer". Null otherwise.</summary>
        public string NoRewardNote;

        // ---------- Result (filled when the run ends) ----------
        public RankScore Score;
        public DungeonRank Rank = DungeonRank.F;
        public int XpGained;
        public List<RewardCard> Cards;
        /// <summary>[RAID] Sure gold of a rewarded raid clear (already in the bag / wallet), and the cores / seal keys it paid, for the result's top row.</summary>
        public int RaidGold, RaidCoreGain, RaidKeyGain;
        /// <summary>[RAID] All four cards are taken one by one (false = pick one of four, the weekday dungeons).</summary>
        public bool CardsTakeAll;
        /// <summary>[RAID] Bit i = card i is already in the bag.</summary>
        public int TakenMask;
        /// <summary>Damage per member display name (from <see cref="PartyManager.DamageDealt"/>), local first.</summary>
        public readonly List<(string name, int damage, bool local)> MemberDamage = new List<(string, int, bool)>();

        float lastHitAt = -100f;

        public DungeonRun(DungeonDef dungeon, DungeonDifficulty difficulty, int partySize)
        {
            Dungeon = dungeon;
            Difficulty = dungeon != null && dungeon.isRaid ? DungeonDifficulty.Normal : difficulty;
            Numbers = DungeonDatabase.DifficultyFor(dungeon, difficulty);
            PartySize = Mathf.Clamp(partySize, 1, PartyManager.MaxMembers);
        }

        public int RoomCount => Dungeon.RoomCount;
        public RoomDef Room => Dungeon.rooms[Mathf.Clamp(RoomIndex, 0, Dungeon.rooms.Length - 1)];
        public bool InBossRoom => RoomIndex == Dungeon.bossRoom;
        public int RevivesLeft => Mathf.Max(0, Numbers.revives - RevivesUsed);
        public bool IsOver => State == DungeonRunState.Cleared || State == DungeonRunState.Failed;

        /// <summary>Monster HP multiplier: difficulty × party size.</summary>
        public float HpMul => Numbers.hpMul * DungeonDatabase.PartyScale(PartySize);
        public float DamageMul => Numbers.damageMul;

        /// <summary>A hit by the local player at <paramref name="time"/>: continues the combo if the last one was at most 1.5 s ago.</summary>
        public void RegisterHit(float time)
        {
            if (time - lastHitAt > DungeonRanking.ComboWindow) Combo = 0;
            Combo++;
            lastHitAt = time;
            if (Combo > MaxCombo) MaxCombo = Combo;
        }

        /// <summary>[FEEL] The combo still running at <paramref name="time"/> (0 once the window has passed), for the HUD.</summary>
        public int LiveCombo(float time) => time - lastHitAt > DungeonRanking.ComboWindow ? 0 : Combo;

        /// <summary>0..1 of the combo window left (1 = just hit).</summary>
        public float ComboTimeLeft(float time) => Mathf.Clamp01(1f - (time - lastHitAt) / DungeonRanking.ComboWindow);

        /// <summary>"02:31" style clock.</summary>
        public static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.FloorToInt(seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }
    }
}
