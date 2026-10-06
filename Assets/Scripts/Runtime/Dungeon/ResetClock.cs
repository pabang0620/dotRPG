using System;

namespace DotRPG
{
    /// <summary>
    /// Daily / weekly reset times on the local PC clock (plan D3): the day turns over at 06:00, the week at
    /// Thursday 06:00. Pure functions of a <see cref="DateTime"/> so they are testable;
    /// no protection against changing the clock (offline single player).
    /// </summary>
    public static class ResetClock
    {
        public const int ResetHour = 6;
        public const DayOfWeek WeeklyResetDay = DayOfWeek.Thursday;

        /// <summary>Overridable "now" (tests); null = <see cref="DateTime.Now"/>.</summary>
        public static Func<DateTime> NowOverride;

        public static DateTime Now => NowOverride != null ? NowOverride() : DateTime.Now;

        /// <summary>The most recent daily reset at or before <paramref name="now"/> (today 06:00, or yesterday's before 06:00).</summary>
        public static DateTime DailyResetStart(DateTime now)
        {
            var today = now.Date.AddHours(ResetHour);
            return now >= today ? today : today.AddDays(-1);
        }

        /// <summary>The next daily reset after <paramref name="now"/>.</summary>
        public static DateTime NextDailyReset(DateTime now) => DailyResetStart(now).AddDays(1);

        /// <summary>The most recent Thursday 06:00 at or before <paramref name="now"/>.</summary>
        public static DateTime WeeklyResetStart(DateTime now)
        {
            var daily = DailyResetStart(now);
            int back = ((int)daily.DayOfWeek - (int)WeeklyResetDay + 7) % 7;
            return daily.AddDays(-back);
        }

        public static DateTime NextWeeklyReset(DateTime now) => WeeklyResetStart(now).AddDays(7);

        /// <summary>The weekday the game counts as "today": the day before until 06:00.</summary>
        public static DayOfWeek GameDay(DateTime now) => DailyResetStart(now).DayOfWeek;

        /// <summary>True when a stamp (ticks of the reset it belongs to, 0 = never) is older than the current daily reset.</summary>
        public static bool DailyExpired(long stampTicks, DateTime now) => stampTicks < DailyResetStart(now).Ticks;

        /// <summary>True when a stamp is older than the current weekly reset.</summary>
        public static bool WeeklyExpired(long stampTicks, DateTime now) => stampTicks < WeeklyResetStart(now).Ticks;

        /// <summary>Weekday dungeons open on their own days; Saturday and Sunday open all of them. The raid is always open.</summary>
        public static bool IsOpen(DungeonDef dungeon, DateTime now)
        {
            if (dungeon == null) return false;
            if (dungeon.isRaid) return dungeon.openDays == null || dungeon.openDays.Length == 0 || Array.IndexOf(dungeon.openDays, GameDay(now)) >= 0;
            var day = GameDay(now);
            if (day == DayOfWeek.Saturday || day == DayOfWeek.Sunday) return true;
            return Array.IndexOf(dungeon.openDays, day) >= 0;
        }
    }
}
