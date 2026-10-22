using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] 공허 균열 of 공허 침식체 지바크 (Docs/ch3/RAIDS.md 1): from phase 2, every <see cref="RaidRift.Every"/> s two rifts open
    /// in the room. Each open rift heals the boss <see cref="RaidRift.HealShare"/> of its max HP every <see cref="RaidRift.HealEvery"/> s
    /// (a heal, not 그라흐's invulnerable seal). Standing on a rift closes it: 3 s with one member on it, 2 s with two, 1 s with
    /// three or more, and whoever stands there takes +30% damage meanwhile. The rifts are teal safe circles
    /// (<see cref="Telegraph.SafeCircle"/>, members see them through the telegraph snapshot) and each one is handed out through
    /// <see cref="RaidDoom"/> (AI companions first, the person only when there is no AI), so the companions and the measure bot
    /// walk there with the safe-spot lookup they already have. Runs only where the boss is simulated (solo / host).
    /// </summary>
    public sealed partial class BossBrain
    {
        sealed class Rift
        {
            public Vector2 spot;
            public float progress, nextHealAt;
            public Telegraph circle;
        }

        readonly List<Rift> rifts = new List<Rift>();
        float nextRiftAt = -1f;

        /// <summary>At least one rift is open (운명의 낙인 waits: both hand out spots through RaidDoom).</summary>
        public bool RiftsOpen => rifts.Count > 0;

        void RiftTick()
        {
            if (!RaidRift.UsedBy(Def.id)) return;
            float now = Time.time;
            if (Phase < 2) return;
            if (nextRiftAt < 0f) nextRiftAt = now + RaidRift.FirstDelay;
            float speed = Heroic ? DungeonDatabase.RaidHeroicPatternSpeed : 1f;
            if (rifts.Count == 0 && now >= nextRiftAt && !FateActive && !Sealed)
            {
                nextRiftAt = now + RaidRift.Every * speed;
                OpenRifts();
            }
            if (rifts.Count == 0) return;
            var members = MonsterHits.All();
            for (int i = rifts.Count - 1; i >= 0; i--)
            {
                var r = rifts[i];
                int on = 0;
                foreach (var m in members)
                    if (m != null && !m.IsDead && Vector2.Distance(m.NetTarget, r.spot) <= RaidRift.Radius + (m.NetDriven ? FateNetGrace : 0f)) on++;
                if (on > 0 && Game.IsWorldRunning) r.progress += Time.deltaTime / RaidRift.CloseSeconds(on);
                if (r.progress >= 1f)
                {
                    CloseRift(r);
                    rifts.RemoveAt(i);
                    continue;
                }
                if (now >= r.nextHealAt)
                {
                    r.nextHealAt = now + RaidRift.HealEvery;
                    int heal = Mathf.Max(1, Mathf.RoundToInt(E.Health.Max * RaidRift.HealShare));
                    E.Health.Heal(heal);
                    RaidRift.Heals++;
                    SkillFx.Spawn("fx_glow", E.Center, new Color(.55f, .3f, .9f, .6f), .6f, SkillFx.At(E.Position.y, -1)).Additive().Scale(2.2f, 3.2f);
                    Announce?.Invoke(this, L.T("균열이 지바크를 치유한다!"), RaidRift.Violet);
                }
                // The circle telegraph has a fixed life: put up a fresh one when it ran out (the rift stays until closed).
                if (r.circle == null || r.circle.Resolved || r.circle.Cancelled) r.circle = Telegraph.SafeCircle(E, r.spot, RaidRift.Radius, RaidRift.CircleLife);
            }
            RaidRift.Active = SpotsOf(rifts);
            if (rifts.Count == 0) { RaidDoom.End(); return; }
            AssignRifts(members);
        }

        void OpenRifts()
        {
            Rect room = Game.World != null ? Game.World.Bounds : new Rect(E.Home - new Vector2(9f, 6f), new Vector2(18f, 12f));
            const float inset = 2f;
            for (int tries = 0; rifts.Count < RaidRift.Count && tries < 200; tries++)
            {
                var p = new Vector2(NextFloat(room.xMin + inset, room.xMax - inset), NextFloat(room.yMin + inset, room.yMax - inset));
                if (Game.World != null && !Game.World.IsFree(p)) continue;
                bool clear = (p - E.Position).sqrMagnitude > 3f * 3f;
                foreach (var q in rifts) if ((q.spot - p).sqrMagnitude < RaidRift.Apart * RaidRift.Apart) { clear = false; break; }
                if (clear) rifts.Add(new Rift { spot = p });
            }
            while (rifts.Count < RaidRift.Count) rifts.Add(new Rift { spot = FreeNear(E.Home + new Vector2(NextFloat(-6f, 6f), NextFloat(-4f, 4f))) });
            float now = Time.time;
            foreach (var r in rifts)
            {
                r.nextHealAt = now + RaidRift.HealEvery;
                r.circle = Telegraph.SafeCircle(E, r.spot, RaidRift.Radius, RaidRift.CircleLife);
                MonsterFx.SummonCircle(r.spot, 1.1f);
            }
            RaidRift.Opened += rifts.Count;
            Shout(ShoutVoidRift);
            Game.Audio.PlaySfx("magic");
        }

        void CloseRift(Rift r)
        {
            if (r.circle != null && !r.circle.Cancelled) r.circle.Cancel();
            RaidRift.Closed++;
            SkillVisuals.Flash(r.spot, new Color(.8f, .9f, 1f, .8f), 3f, .3f);
            Game.Audio.PlaySfx("rock_break");
            Announce?.Invoke(this, L.T("균열이 닫혔다!"), new Color(.6f, 1f, .9f));
        }

        /// <summary>
        /// Who goes where (RaidDoom): the AI companions take the rifts in turn; with no AI the people do. Handed out again every
        /// frame with a short clock, so a closed rift or a boss that is gone leaves nobody walking to an old spot.
        /// </summary>
        void AssignRifts(List<PlayerController> members)
        {
            var ai = new List<PlayerController>();
            var people = new List<PlayerController>();
            foreach (var m in members)
            {
                if (m == null || m.IsDead) continue;
                (RaidRift.IsAi(m) ? ai : people).Add(m);
            }
            var go = ai.Count > 0 ? ai : people;
            RaidDoom.Begin(RaidRift.AssignClock);
            var taken = new bool[rifts.Count];
            foreach (var m in go)
            {
                // The nearest rift nobody was sent to yet; once every rift has someone, the nearest one.
                int best = -1;
                float bestD = float.MaxValue;
                bool anyFree = System.Array.IndexOf(taken, false) >= 0;
                for (int k = 0; k < rifts.Count; k++)
                {
                    if (anyFree && taken[k]) continue;
                    float d = Vector2.Distance(m.Position, rifts[k].spot);
                    if (d < bestD) { bestD = d; best = k; }
                }
                if (best < 0) continue;
                taken[best] = true;
                RaidDoom.Assign(m, rifts[best].spot);
            }
        }

        static Vector2[] SpotsOf(List<Rift> list)
        {
            if (list.Count == 0) return null;
            var a = new Vector2[list.Count];
            for (int i = 0; i < list.Count; i++) a[i] = list[i].spot;
            return a;
        }

        void EndRifts()
        {
            if (rifts.Count == 0) return;
            foreach (var r in rifts) if (r.circle != null && !r.circle.Cancelled) r.circle.Cancel();
            rifts.Clear();
            RaidRift.Active = null;
            RaidDoom.End();
        }
    }

    /// <summary>[CH3] 공허 균열 numbers and the shared state the damage path and the measure log read.</summary>
    public static class RaidRift
    {
        // ---------- Tuning (Docs/ch3/RAIDS.md 1) ----------
        public const float Every = 32f, FirstDelay = 4f;
        public const int Count = 2;
        public const float HealEvery = 10f, HealShare = .015f;
        public const float Radius = 1.1f, Apart = 5f;
        /// <summary>Damage taken while standing on an open rift (×).</summary>
        public const float TakenMul = 1.3f;
        /// <summary>Life of one rift circle telegraph (renewed while the rift stays open) and of one RaidDoom hand-out.</summary>
        public const float CircleLife = 12f, AssignClock = .6f;

        public static readonly Color Violet = new Color(.7f, .45f, 1f);

        /// <summary>Seconds to close a rift with <paramref name="standing"/> members on it: 1 → 3 s, 2 → 2 s, 3+ → 1 s.</summary>
        public static float CloseSeconds(int standing) => standing >= 3 ? 1f : standing == 2 ? 2f : 3f;

        public static bool UsedBy(string bossId) => bossId == MonsterDatabase.XibakBoss;

        /// <summary>Centres of the open rifts on this PC (host / solo), null when none.</summary>
        public static Vector2[] Active { get; internal set; }

        /// <summary>[MEASURE] Rifts opened / closed and heals that landed (reset by the measure run).</summary>
        public static int Opened, Closed, Heals;

        public static bool IsAi(PlayerController m) => m != null && m.Data != null && MercenaryDatabase.Get(m.Data.MercenaryId) != null;

        /// <summary>The hit a member standing on an open rift takes (+30%); anything else unchanged.</summary>
        public static int Taken(PlayerController m, int amount)
        {
            var rifts = Active;
            if (rifts == null || m == null) return amount;
            foreach (var c in rifts)
                if ((m.Position - c).sqrMagnitude <= Radius * Radius) return Mathf.Max(1, Mathf.RoundToInt(amount * TakenMul));
            return amount;
        }

        public static void ResetCounters() => Opened = Closed = Heals = 0;
    }
}
