using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [RAID] Raid-wide mechanics on the boss side (Docs/PLAN_RAID_MECHANICS.md, numbers in RaidMechanics): enrage at
    /// 40% HP (or 해골왕's phase 3, whichever comes first, never twice) and the "이대론 죽을 수 없다!" roar. Runs only
    /// where the boss is simulated (solo / party host); members draw it from PartyMsg.RaidRoar / RaidStun.
    /// </summary>
    public sealed partial class BossBrain
    {
        bool lowEnraged, enrageAnnounced, roaring;
        float nextRoarAt = float.MaxValue;

        /// <summary>True while the roar is being cast (the boss stands still; nothing interrupts it but death).</summary>
        public bool Roaring => roaring;

        /// <summary>Lets the member side (PartyNet) show a boss line on the HP bar.</summary>
        public static void RaiseAnnounce(BossBrain b, string text, Color color) => Announce?.Invoke(b, text, color);

        /// <summary>Called every boss frame: the 40% enrage and the roar timer. True = the roar owns this frame.</summary>
        bool RaidTick()
        {
            if (!Def.raid || E.IsDead) return false;
            if (!lowEnraged && E.Health.Max > 0 && (float)E.Health.Current / E.Health.Max <= RaidMechanics.EnrageAt) lowEnraged = true;
            if (Enraged && !enrageAnnounced)
            {
                enrageAnnounced = true;
                // 해골왕 already said "해골왕이 광폭해집니다!" when its phase 3 started.
                if (!(IsKing && Phase >= 3)) Announce?.Invoke(this, L.T("광폭화!"), new Color(1f, .3f, .25f));
                Game.Camera?.Shake(.3f, .35f);
                nextRoarAt = Time.time + RaidMechanics.RoarFirstDelay;
            }
            HeroicClock();
            if (SealTick()) return true; // [RAID] 결계 의식 (BossBrain.Seal.cs)
            if (roaring)
            {
                E.MonsterHalt();
                E.MonsterAnim(CharacterAnim.Attack, true);
                return true;
            }
            // Start between actions (never cut 망자의 심판, a running pattern or an instant-death gimmick: the roar would
            // send everyone to safe circles while the totems / marks demand the opposite, and it stalls DoomTick).
            if (Enraged && Time.time >= nextRoarAt && !Busy && !CastingJudgment && !Sealed && !RuinActive && !FateActive)
            {
                nextRoarAt = Time.time + RaidMechanics.RoarEvery;
                StartCoroutine(Roar());
                return true;
            }
            return false;
        }

        float fightStart = -1f, nextWipeAt;
        int clockStep;
        static readonly float[] ClockWarn = { 120f, 60f, 30f, 10f };

        /// <summary>
        /// [G11] 심화 레이드 time limit: warnings at 2 min / 1 min / 30 s / 10 s left, then a wipe blow every 3 s
        /// (unblockable, 999% of max HP) until the fight ends. Counts from the boss's first frame.
        /// </summary>
        void HeroicClock()
        {
            if (!Heroic) return;
            if (fightStart < 0f) fightStart = Time.time;
            float left = DungeonDatabase.RaidHeroicTimeLimit - (Time.time - fightStart);
            if (clockStep < ClockWarn.Length && left <= ClockWarn[clockStep])
            {
                Announce?.Invoke(this, ClockWarn[clockStep] >= 60f ? L.F("전멸까지 {0:0}분", ClockWarn[clockStep] / 60f) : L.F("전멸까지 {0:0}초", ClockWarn[clockStep]), new Color(1f, .3f, .25f));
                clockStep++;
            }
            if (left > 0f || Time.time < nextWipeAt) return;
            nextWipeAt = Time.time + 3f;
            Announce?.Invoke(this, L.T("시간 초과: 전멸"), new Color(1f, .2f, .2f));
            SkillVisuals.Flash(E.Center, new Color(1f, .2f, .2f, .9f), 16f, .4f);
            foreach (var m in MonsterHits.All())
                if (m != null && !m.IsDead) MonsterHits.Apply(new[] { m }, Mathf.Max(1, m.Health.Max * 10), E.Position, 4f, true, E.gameObject);
        }

        /// <summary>Enraged bosses hit harder (all their patterns go through MonsterDamage).</summary>
        public float DamageMul => Enraged ? RaidMechanics.EnrageDamage : 1f;

        IEnumerator Roar()
        {
            roaring = true;
            var circles = SafeSpots();
            RaidMechanics.ShowRoar(E, RaidMechanics.RoarCast, circles);
            RaidMechanics.RaiseRoar(E, RaidMechanics.RoarCast, circles);
            float t = 0f;
            while (t < RaidMechanics.RoarCast)
            {
                if (E.IsDead) { roaring = false; yield break; }
                if (Game.IsWorldRunning) t += Time.deltaTime;
                yield return null;
            }
            roaring = false;
            Game.Camera?.Shake(.45f, .5f);
            SkillVisuals.Flash(E.Center, new Color(1f, .3f, .25f, .8f), 14f, .35f);
            Game.Audio.PlaySfx("rock_break");
            foreach (var m in MonsterHits.All())
            {
                if (m == null || m.IsDead) continue;
                if (RaidMechanics.InSafeCircle(m.NetTarget, circles)) continue; // remote members: where their PC last reported them
                if (m.Health.IsInvulnerable) continue;
                var career = CareerCombat.For(m);
                if (career.InvulnerableLeft > 0f) continue;
                int damage = Mathf.Max(1, Mathf.RoundToInt(m.Health.Max * RaidMechanics.RoarDamageShare));
                bool countered = career.CounterLeft > 0f; // 응보의 방진: Absorb blocks this blow and answers it
                MonsterHits.Apply(new[] { m }, damage, E.Position, 4f, true, E.gameObject);
                if (countered || m.IsDead) continue;
                m.Stun(RaidMechanics.RoarStun);
                RaidMechanics.RaiseStun(m, RaidMechanics.RoarStun);
            }
        }

        /// <summary>Safe circle centres: free walkable spots spread around the boss.</summary>
        Vector2[] SafeSpots()
        {
            var spots = new Vector2[RaidMechanics.SafeCircles];
            float start = NextFloat(0f, Mathf.PI * 2f);
            for (int i = 0; i < spots.Length; i++)
            {
                float a = start + i * Mathf.PI * 2f / spots.Length;
                float r = NextFloat(RaidMechanics.SafeSpread * .45f, RaidMechanics.SafeSpread);
                spots[i] = FreeNear(E.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .7f) * r);
            }
            return spots;
        }
    }
}
