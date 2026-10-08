using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// Who decides the result-affecting numbers of a dungeon run (plan PLAN_ONLINE §3.2 권한 분리): the rank
    /// score, the clear XP, the reward cards and the companions' card picks. Offline this is
    /// <see cref="LocalDungeonAuthority"/>; an online build swaps in a server-backed implementation that
    /// validates the run statistics and returns the same answers.
    /// </summary>
    public interface IDungeonAuthority
    {
        /// <summary>Rank score of a cleared run (time, hits, kills, combo, revives).</summary>
        RankScore ScoreRun(DungeonRun run);

        /// <summary>Clear XP including the rank bonus.</summary>
        int ClearXp(DungeonRun run, DungeonRank rank);

        /// <summary>[RAID] The sure gold of a rewarded raid clear (rolled before the cards).</summary>
        int RaidGold(DungeonRun run);

        /// <summary>The four face-down reward cards (item ids and counts) for the local player's class.</summary>
        List<RewardCard> DealCards(DungeonRun run, CharacterClass cls);

        /// <summary>Which face-down card (index into <paramref name="faceDown"/>) companion <paramref name="memberIndex"/> flips.</summary>
        int CompanionPick(DungeonRun run, int memberIndex, IReadOnlyList<int> faceDown);
    }

    /// <summary>Offline authority: everything is rolled on this PC.</summary>
    public sealed class LocalDungeonAuthority : IDungeonAuthority
    {
        readonly System.Random rng;

        /// <param name="seed">Fixed seed for reproducible tests; null = time based.</param>
        public LocalDungeonAuthority(int? seed = null)
        {
            rng = seed.HasValue ? new System.Random(seed.Value) : new System.Random();
        }

        public RankScore ScoreRun(DungeonRun run)
        {
            float reference = run.Dungeon.referenceSeconds[UnityEngine.Mathf.Clamp((int)run.Difficulty, 0, run.Dungeon.referenceSeconds.Length - 1)];
            return DungeonRanking.Score(run.Elapsed, reference, run.HitsTaken, run.Kills, run.Monsters, run.MaxCombo, run.RevivesUsed);
        }

        public int ClearXp(DungeonRun run, DungeonRank rank) => DungeonRewards.ClearXp(run.Dungeon, run.Numbers, rank);

        public int RaidGold(DungeonRun run) => run.Dungeon.raidReward == null ? 0 : DungeonRewards.RollRaidGold(run.Dungeon.raidReward, rng);

        public List<RewardCard> DealCards(DungeonRun run, CharacterClass cls) => DungeonRewards.RollCards(run.Dungeon, run.Numbers, cls, rng);

        public int CompanionPick(DungeonRun run, int memberIndex, IReadOnlyList<int> faceDown) =>
            faceDown == null || faceDown.Count == 0 ? -1 : rng.Next(0, faceDown.Count);
    }

    /// <summary>The authority in use (tests may replace it with a seeded one).</summary>
    public static class DungeonAuthority
    {
        public static IDungeonAuthority Current = new LocalDungeonAuthority();
    }
}
