using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [RAID] 수호자 그라흐's 결계 의식 on the boss side (numbers and drawing in <see cref="RaidSeal"/>): at each HP mark the boss
    /// speaks its line, then turns invulnerable and opens one circle per living member. Every circle held at once =
    /// broken (groggy, gauge kept); time out = 90% of max HP and MP off everyone.
    /// </summary>
    public sealed partial class BossBrain
    {
        int sealIndex;
        bool sealRunning;

        /// <summary>The ritual is running (line + circles): the boss takes no damage.</summary>
        public bool Sealed => sealRunning;
        /// <summary>Rituals broken / not broken this fight (for the measure log).</summary>
        public int SealsBroken { get; private set; }
        public int SealsFailed { get; private set; }

        /// <summary>Starts the ritual at its HP mark (between actions); true while it owns the frame.</summary>
        bool SealTick()
        {
            if (sealRunning)
            {
                E.MonsterHalt();
                E.MonsterAnim(CharacterAnim.Attack, true);
                return true;
            }
            if (!RaidSeal.UsedBy(Def.id) || sealIndex >= RaidSeal.At.Length || E.Health.Max <= 0) return false;
            if ((float)E.Health.Current / E.Health.Max > RaidSeal.At[sealIndex]) return false;
            if (Busy || CastingJudgment || Roaring || E.IsGroggy) return false;
            sealIndex++;
            StartCoroutine(Seal());
            return true;
        }

        IEnumerator Seal()
        {
            sealRunning = true;
            string line = RaidSeal.Line;
            RaidSeal.ShowLine(E, line);
            RaidSeal.RaiseLine(E, line);
            var aura = SkillFx.Spawn("fx_glow", E.Center, new Color(.7f, .4f, 1f, .5f), RaidSeal.LineLead + RaidSeal.WindowMax, SkillFx.At(E.Position.y, -1))
                .Additive().Scale(2.4f, 3.2f);
            float t = 0f;
            while (t < RaidSeal.LineLead)
            {
                if (E.IsDead) { sealRunning = false; yield break; }
                if (Game.IsWorldRunning) t += Time.deltaTime;
                yield return null;
            }
            var members = MonsterHits.All();
            var circles = SealSpots(Mathf.Max(1, members.Count));
            float window = SealWindow(circles, members) * (Heroic ? DungeonDatabase.RaidHeroicSealScale : 1f);
            RaidSeal.ShowOpened(E, window, circles);
            RaidSeal.RaiseOpened(E, window, circles);
            bool broken = false;
            t = 0f;
            while (t < window)
            {
                if (E.IsDead) { RaidSeal.ShowEnded(E, true); sealRunning = false; yield break; }
                if (Game.IsWorldRunning) t += Time.deltaTime;
                var alive = MonsterHits.All();
                // Fewer people alive than circles: the ones still standing must fill as many circles as they are.
                if (alive.Count > 0 && RaidSeal.Filled(circles, alive) >= Mathf.Min(circles.Length, alive.Count)) { broken = true; break; }
                yield return null;
            }
            sealRunning = false;
            if (aura != null) Destroy(aura.gameObject);
            RaidSeal.ShowEnded(E, broken);
            RaidSeal.RaiseEnded(E, broken);
            if (broken)
            {
                SealsBroken++;
                E.ForceGroggy(RaidSeal.BrokenGroggy);
                yield break;
            }
            SealsFailed++;
            Game.Camera?.Shake(.45f, .5f);
            Game.Audio.PlaySfx("rock_break");
            foreach (var m in MonsterHits.All())
            {
                if (m == null || m.IsDead) continue;
                var career = CareerCombat.For(m);
                if (m.Health.IsInvulnerable || career.InvulnerableLeft > 0f) continue;
                m.Data.Mana = Mathf.Max(0f, m.Data.Mana - m.Data.Stats.MaxMp * RaidSeal.FailMpShare);
                RaidSeal.RaiseDrain(m, RaidSeal.FailMpShare);
                int damage = Mathf.Max(1, Mathf.RoundToInt(m.Health.Max * RaidSeal.FailHpShare));
                MonsterHits.Apply(new[] { m }, damage, E.Position, 4f, true, E.gameObject);
            }
        }

        /// <summary>One circle per member at random free spots anywhere in the boss room (far ones too), apart from each other.</summary>
        Vector2[] SealSpots(int n)
        {
            var spots = new List<Vector2>();
            Rect room = Game.World != null ? Game.World.Bounds : new Rect(E.Home - new Vector2(9f, 6f), new Vector2(18f, 12f));
            float inset = 1.5f;
            for (int tries = 0; spots.Count < n && tries < 300; tries++)
            {
                var p = new Vector2(NextFloat(room.xMin + inset, room.xMax - inset), NextFloat(room.yMin + inset, room.yMax - inset));
                if (Game.World != null && !Game.World.IsFree(p)) continue;
                float apart = tries < 200 ? RaidSeal.Apart : RaidSeal.Radius * 2.2f;
                bool clear = (p - E.Position).sqrMagnitude > 2.5f * 2.5f;
                foreach (var q in spots) if ((q - p).sqrMagnitude < apart * apart) { clear = false; break; }
                if (clear) spots.Add(p);
            }
            while (spots.Count < n) spots.Add(FreeNear(E.Home + new Vector2(NextFloat(-6f, 6f), NextFloat(-4f, 4f))));
            return spots.ToArray();
        }

        /// <summary>
        /// Seconds the party gets: each member is matched to the nearest free circle (as they would run), the slowest run
        /// decides (path ≈ straight line × PathSlack at the slowest member's speed) plus a reaction allowance.
        /// </summary>
        static float SealWindow(Vector2[] circles, List<PlayerController> members)
        {
            float speed = float.MaxValue, worst = 0f;
            var taken = new bool[circles.Length];
            foreach (var m in members)
            {
                if (m == null || m.IsDead) continue;
                speed = Mathf.Min(speed, Mathf.Max(1f, m.TopSpeed));
                int best = -1;
                float bestD = float.MaxValue;
                for (int i = 0; i < circles.Length; i++)
                {
                    if (taken[i]) continue;
                    float d = Vector2.Distance(m.Position, circles[i]);
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best < 0) continue;
                taken[best] = true;
                worst = Mathf.Max(worst, bestD);
            }
            if (speed == float.MaxValue) speed = 4.2f;
            return Mathf.Clamp(RaidSeal.React + worst * RaidSeal.PathSlack / speed, RaidSeal.WindowMin, RaidSeal.WindowMax);
        }
    }
}
