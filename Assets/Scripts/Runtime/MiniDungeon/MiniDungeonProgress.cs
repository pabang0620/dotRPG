using System;
using UnityEngine;

namespace DotRPG
{
    [Serializable]
    public sealed class MiniDungeonSave
    {
        public long dailyStampUtcTicks;
        public float usedSeconds;
        public bool active;
        public long checkpointUtcTicks, deadlineUtcTicks;
        public string returnMap = MapRegistry.Village;
        public float returnX = -1f, returnY = -1f;
        public int returnFacing;
    }

    /// <summary>Offline allowance, independent from weekday dungeon entries. UTC prevents time-zone changes resetting it.</summary>
    public sealed class MiniDungeonProgress
    {
        MiniDungeonSave state = new MiniDungeonSave();
        public bool Active => state.active;
        public string ReturnMap => state.returnMap;
        public Vector2 ReturnPosition => new Vector2(state.returnX, state.returnY);
        public Facing ReturnFacing => (Facing)Mathf.Clamp(state.returnFacing, 0, 7);
        public DateTime DeadlineUtc => new DateTime(Math.Max(DateTime.MinValue.Ticks, state.deadlineUtcTicks), DateTimeKind.Utc);
        public float UsedSeconds => state.usedSeconds;
        public static DateTime DailyStartUtc(DateTime now) => ResetClock.DailyResetStart(now.ToUniversalTime().AddHours(9)).AddHours(-9);
        public static DateTime NextResetUtc(DateTime now) => DailyStartUtc(now).AddDays(1);

        public void Reset() => state = new MiniDungeonSave();

        public void Refresh(DateTime utcNow)
        {
            utcNow = utcNow.ToUniversalTime();
            long stamp = DailyStartUtc(utcNow).Ticks;
            if (state.dailyStampUtcTicks == 0) state.dailyStampUtcTicks = stamp;
            if (state.active)
            {
                long end = Math.Min(utcNow.Ticks, state.deadlineUtcTicks);
                if (end > state.checkpointUtcTicks)
                {
                    state.usedSeconds = Mathf.Clamp(state.usedSeconds + (float)TimeSpan.FromTicks(end - state.checkpointUtcTicks).TotalSeconds, 0f, MiniDungeonCatalog.DailySeconds);
                    state.checkpointUtcTicks = end;
                }
                if (utcNow.Ticks >= state.deadlineUtcTicks) state.active = false;
            }
            // Never move the stamp backwards on a clock adjustment.
            if (stamp > state.dailyStampUtcTicks)
            {
                state.dailyStampUtcTicks = stamp;
                state.usedSeconds = 0f;
                state.active = false; // the old day's run ends; a new day requires a new entry
            }
        }

        /// <summary>Seconds left today for the hero's tier (tier 40: 10 minutes; the seconds used are shared by every tier).</summary>
        public float RemainingSeconds(DateTime now) { Refresh(now); return Mathf.Max(0f, MiniDungeonCatalog.CurrentDailySeconds - state.usedSeconds); }

        /// <summary>Explicit consumption seam for deterministic budget checks.</summary>
        public void Consume(float seconds, DateTime now)
        {
            Refresh(now);
            if (float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
            state.usedSeconds = Mathf.Clamp(state.usedSeconds + Mathf.Max(0f, seconds), 0f, MiniDungeonCatalog.DailySeconds);
            if (state.usedSeconds >= MiniDungeonCatalog.DailySeconds) state.active = false;
        }

        public bool Begin(DateTime now, float grantedSeconds, string returnMap, Vector2 returnPosition, Facing facing)
        {
            if (float.IsNaN(grantedSeconds) || float.IsInfinity(grantedSeconds)) return false;
            Refresh(now);
            float allowance = Mathf.Min(RemainingSeconds(now), Mathf.Max(0f, grantedSeconds));
            allowance = Mathf.Min(allowance, (float)(NextResetUtc(now) - now.ToUniversalTime()).TotalSeconds);
            if (allowance <= 0f || state.active) return false;
            state.active = true;
            state.checkpointUtcTicks = now.ToUniversalTime().Ticks;
            state.deadlineUtcTicks = now.ToUniversalTime().AddSeconds(allowance).Ticks;
            state.returnMap = returnMap;
            state.returnX = returnPosition.x; state.returnY = returnPosition.y; state.returnFacing = (int)facing;
            return true;
        }

        public void End(DateTime now) { Refresh(now); state.active = false; }

        public MiniDungeonSave Capture(DateTime now)
        {
            Refresh(now);
            return JsonUtility.FromJson<MiniDungeonSave>(JsonUtility.ToJson(state));
        }

        public void Restore(MiniDungeonSave saved, DateTime now)
        {
            state = saved == null ? new MiniDungeonSave() : JsonUtility.FromJson<MiniDungeonSave>(JsonUtility.ToJson(saved));
            state.usedSeconds = float.IsNaN(state.usedSeconds) || float.IsInfinity(state.usedSeconds) ? 0f : Mathf.Clamp(state.usedSeconds, 0f, MiniDungeonCatalog.DailySeconds);
            if (state.checkpointUtcTicks < DateTime.MinValue.Ticks || state.checkpointUtcTicks > DateTime.MaxValue.Ticks || state.deadlineUtcTicks < state.checkpointUtcTicks || state.deadlineUtcTicks > DateTime.MaxValue.Ticks) state.active = false;
            // An interrupted session costs elapsed wall-clock time, like the authoritative online session.
            End(now);
        }
    }

    public sealed class MiniDungeonStatus
    {
        public bool Ready, Busy, Active;
        public int Tier, Kills, EarnedXp;
        public float RemainingSeconds;
    }
}
