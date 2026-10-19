using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [MEASURE] Dev-only (-dotrpgMeasure -measureDodge 1, dungeon and raid runs): the measure bot answers boss mechanics the way a
    /// practised person does, so raid clear times and deaths can be read (before this every raid run since the 2026-10-14 boss pass
    /// failed: the bot stood in 섬멸, 운명의 낙인 and the ground warnings). Never on outside the measure: normal 자동 사냥 is unchanged.
    /// A person reacts a moment late (<see cref="DodgeReaction"/>) and walks; nothing here teleports or knows the future.
    /// </summary>
    public sealed partial class QuestAutoPilot
    {
        internal static bool MeasureDodge;
        /// <summary>[MEASURE] Counters for the run row: ground-warning escapes started and cover / circle moves.</summary>
        internal static int MeasureDodges, MeasureCoverMoves;

        /// <summary>Seconds between a warning appearing under the hero and the first step out (a practised player).</summary>
        const float DodgeReaction = .25f;
        static readonly float[] DodgeRings = { 1f, 1.75f, 2.5f, 3.5f, 5f };
        const int DodgeAngles = 16;

        float dangerSince = -1f, coverSince = -1f;
        bool escaping;

        /// <summary>
        /// One frame of mechanic play. True = this frame is spent on it (walking to a circle / out of a warning, or hitting from the
        /// circle), false = fight as usual. <paramref name="best"/> is the monster the bot would hit.
        /// </summary>
        bool MeasureDodgeTurn(PlayerController p, EnemyController best)
        {
            // 운명의 낙인: stand in the circle named for the hero (anyone outside their own circle dies).
            if (RaidDoom.SpotFor(p, out Vector2 fate))
                return HoldSpot(p, fate, BossBrain.FateRadius * .4f, best, ref coverSince);
            // 섬멸: hide in a teal safe circle (or behind a guardian's shield) until the blast is over.
            if (SafeCover.Annihilating && SafeCover.SuggestSpot(p.Position, SafeCover.AnnihilateSource, out Vector2 cover))
                return HoldSpot(p, cover, SafeCover.InSafeZone(p.Position) ? .9f : .35f, best, ref coverSince);
            coverSince = -1f;

            // Ground warnings: step to the nearest free spot outside every live one.
            if (!InGroundWarning(p.Position))
            {
                dangerSince = -1f;
                escaping = false;
                // A person does not walk back into a warning that is still filling: wait at its edge and hit what is in reach.
                if (best == null || !InGroundWarning(p.Position + GridPath.Steer(p.Position, best.Position) * .5f)) return false;
                AttackInPlace(p, best);
                lastProgressAt = Time.time;
                return true;
            }
            if (dangerSince < 0f) dangerSince = Time.time;
            if (Time.time - dangerSince < DodgeReaction) return false;
            if (!escaping) { escaping = true; MeasureDodges++; }
            Vector2 to = SafeStep(p.Position, best != null ? best.Position : p.Position);
            Vector2 dir = to - p.Position;
            if (dir.sqrMagnitude < .0001f) return false;
            dir = GridPath.Steer(p.Position, to);
            input.next.move = dir;
            input.next.aim = dir;
            lastProgressAt = Time.time;
            return true;
        }

        /// <summary>Walks to <paramref name="spot"/> after the reaction delay, then hits whatever is in reach without leaving it.</summary>
        bool HoldSpot(PlayerController p, Vector2 spot, float slack, EnemyController best, ref float since)
        {
            if (since < 0f) { since = Time.time; MeasureCoverMoves++; }
            lastProgressAt = Time.time;
            if (Time.time - since < DodgeReaction) return true;
            if (Vector2.Distance(p.Position, spot) > slack)
            {
                Vector2 dir = GridPath.Steer(p.Position, spot);
                input.next.move = dir;
                input.next.aim = dir;
                return true;
            }
            AttackInPlace(p, best);
            return true;
        }

        /// <summary>Hits <paramref name="target"/> without moving when it is in reach (same reach rule as <see cref="Fight"/>).</summary>
        void AttackInPlace(PlayerController p, EnemyController target)
        {
            if (target == null) return;
            int spam = SpamSlot(p);
            float reach = p.Class == CharacterClass.Mage ? MageReach : AttackReach;
            if (spam >= 0 && p.Skills.Numbers(spam).range > 1.5f) reach = Mathf.Max(reach, Mathf.Min(p.Skills.Numbers(spam).range * .85f, 6f));
            if (Vector2.Distance(p.Position, target.Position) > reach) return;
            input.next.aim = (target.Center - p.Center).normalized;
            input.next.faceAim = true;
            SkilledTurn(p, spam);
        }

        /// <summary>Inside a live, simulated danger warning (not a safe circle, not the room-wide 섬멸 / 의식 notice)?</summary>
        static bool InGroundWarning(Vector2 feet)
        {
            foreach (var t in Telegraph.Active)
                if (t != null && !t.Safe && !t.NoDanger && !t.Cancelled && !t.Resolved && t.Contains(feet)) return true;
            return false;
        }

        /// <summary>The nearest walkable point outside every warning; among equals the one closest to <paramref name="target"/>.</summary>
        static Vector2 SafeStep(Vector2 from, Vector2 target)
        {
            foreach (float r in DodgeRings)
            {
                Vector2 best = from;
                float bestScore = float.MaxValue;
                for (int i = 0; i < DodgeAngles; i++)
                {
                    float a = i * Mathf.PI * 2f / DodgeAngles;
                    Vector2 c = from + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!GridPath.Walkable(c) || InGroundWarning(c)) continue;
                    float score = Vector2.Distance(c, target);
                    if (score < bestScore) { bestScore = score; best = c; }
                }
                if (bestScore < float.MaxValue) return best;
            }
            return from;
        }

        /// <summary>[MEASURE] The gimmick monster a person hits first: a 파멸의 의식 totem, else a standing 사령 토템 (nearest).</summary>
        static EnemyController MeasurePriority(PlayerController p)
        {
            EnemyController best = null;
            float bestD = float.MaxValue;
            void Consider(IEnumerable<EnemyController> list)
            {
                foreach (var t in list)
                {
                    if (t == null || t.IsDead || !t.isActiveAndEnabled) continue;
                    float d = Vector2.Distance(t.Position, p.Position);
                    if (d < bestD) { bestD = d; best = t; }
                }
            }
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead || !(e.Behaviour is BossBrain bb)) continue;
                if (bb.RuinActive) Consider(bb.RuinTotemsLeft);
                if (best == null && bb.TotemsUp) Consider(bb.Totems);
            }
            return best;
        }
    }
}
