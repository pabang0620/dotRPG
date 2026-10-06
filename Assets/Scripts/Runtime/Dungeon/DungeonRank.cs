using UnityEngine;

namespace DotRPG
{
    /// <summary>Clear rank, best first.</summary>
    public enum DungeonRank
    {
        SSS = 0,
        SS = 1,
        S = 2,
        A = 3,
        B = 4,
        C = 5,
        D = 6,
        E = 7,
        F = 8,
    }

    /// <summary>Score parts of one clear (plan §6.0: time 40 + hits 30 + kills 10 + combo 20, −10 per revive).</summary>
    public struct RankScore
    {
        public int time, hits, kills, combo, revivePenalty;
        public int Total => Mathf.Clamp(time + hits + kills + combo - revivePenalty, 0, 100);
        public DungeonRank Rank => DungeonRanking.RankOf(Total);
    }

    public static class DungeonRanking
    {
        public const int TimeMax = 40, HitsMax = 30, KillsMax = 10, ComboMax = 20, RevivePenalty = 10;
        /// <summary>Time score reaches 0 at this many times the reference time.</summary>
        public const float TimeZeroAt = 3f;
        /// <summary>Points lost per hit taken.</summary>
        public const int PointsPerHit = 3;
        /// <summary>Max combo that gives the full combo score.</summary>
        public const int ComboTarget = 20;
        /// <summary>Seconds between two hits that still continue a combo.</summary>
        public const float ComboWindow = 1.5f;

        /// <summary>Lowest total for SSS, SS, S, A, B, C, D, E (below the last = F).</summary>
        public static readonly int[] Thresholds = { 95, 88, 80, 70, 60, 50, 40, 25 };
        /// <summary>Clear XP bonus % per rank (SSS +50% … B +10%, lower ranks nothing).</summary>
        public static readonly int[] XpBonus = { 50, 40, 30, 20, 10, 0, 0, 0, 0 };

        public static DungeonRank RankOf(int total)
        {
            for (int i = 0; i < Thresholds.Length; i++)
                if (total >= Thresholds[i]) return (DungeonRank)i;
            return DungeonRank.F;
        }

        public static int XpBonusPercent(DungeonRank rank) => XpBonus[(int)rank];

        public static string Name(DungeonRank rank) => rank.ToString();

        /// <summary>Colour of the rank stamp.</summary>
        public static Color Tint(DungeonRank rank)
        {
            switch (rank)
            {
                case DungeonRank.SSS: return new Color32(255, 214, 64, 255);
                case DungeonRank.SS: return new Color32(255, 170, 60, 255);
                case DungeonRank.S: return new Color32(255, 120, 80, 255);
                case DungeonRank.A: return new Color32(190, 120, 255, 255);
                case DungeonRank.B: return new Color32(90, 170, 255, 255);
                case DungeonRank.C: return new Color32(110, 220, 140, 255);
                default: return new Color32(170, 170, 180, 255);
            }
        }

        public static RankScore Score(float seconds, float referenceSeconds, int hitsTaken, int kills, int monsters, int maxCombo, int revives)
        {
            var score = new RankScore();
            float timeRatio = referenceSeconds > 0f ? seconds / referenceSeconds : 1f;
            float timeFactor = timeRatio <= 1f ? 1f : Mathf.Clamp01((TimeZeroAt - timeRatio) / (TimeZeroAt - 1f));
            score.time = Mathf.RoundToInt(TimeMax * timeFactor);
            score.hits = Mathf.Max(0, HitsMax - hitsTaken * PointsPerHit);
            score.kills = monsters > 0 ? Mathf.RoundToInt(KillsMax * Mathf.Clamp01(kills / (float)monsters)) : KillsMax;
            score.combo = Mathf.RoundToInt(ComboMax * Mathf.Clamp01(maxCombo / (float)ComboTarget));
            score.revivePenalty = revives * RevivePenalty;
            return score;
        }
    }
}
