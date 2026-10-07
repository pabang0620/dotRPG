namespace DotRPG
{
    /// <summary>
    /// [SWEEP] Dungeon clear ticket rules (Docs/PLAN_SWEEP_AND_MAIL.md 2-3, Docs/server/phase10_sweep_mail.md 10).
    /// The server computes every sweep; these values are exported to server/data/sweep.json and shown in the UI.
    /// </summary>
    public static class DungeonSweep
    {
        public const string TicketItem = "ticket_sweep";
        public const string EventTicketItem = "ticket_sweep_event";
        /// <summary>Event tickets (mail) expire this many days after they are claimed.</summary>
        public const int EventTicketDays = 14;
        /// <summary>The best rank needed on that dungeon and difficulty (cleared by hand at least once).</summary>
        public const DungeonRank MinRank = DungeonRank.B;
        /// <summary>Sweep clear XP is the B-rank base with no rank bonus.</summary>
        public const int XpBonusPercent = 0;
        public const int CardCount = 1;
        /// <summary>Chance the one card stays a gear card, in % of the hand-played chance.</summary>
        public const int GearKeepPercent = 70;
        /// <summary>Store price = BasePrice x (account's best level tier + 1), at most WeeklyLimit a week per account.</summary>
        public const int ShopBasePrice = 1000, ShopWeeklyLimit = 7;
        /// <summary>Weekly activity: this many daily dungeons cleared by hand give RewardTickets tickets.</summary>
        public const int WeeklyDirectClears = 10, WeeklyRewardTickets = 3;
    }
}
