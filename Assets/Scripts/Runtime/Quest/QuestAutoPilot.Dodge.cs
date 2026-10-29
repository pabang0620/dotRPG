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
        Vector2 escapeTarget;
        float escapeBestDistance, escapeProgressAt;
        PlayerController dodgeBody;
        string dodgeMap;

        // Measurement-only decisions are accumulated separately from damage and successful casts.
        internal static readonly Dictionary<string, float> MeasureDecisionSeconds = new Dictionary<string, float>();
        internal static readonly Dictionary<string, int> MeasureSkillRequests = new Dictionary<string, int>();
        internal static void ResetMeasureTrace()
        {
            MeasureDecisionSeconds.Clear();
            MeasureSkillRequests.Clear();
            MeasureDodges = MeasureCoverMoves = 0;
            if (Instance == null) return;
            Instance.dangerSince = Instance.coverSince = -1f;
            Instance.escaping = false;
            Instance.farWaitUntil = 0f;
            Instance.nextSkill = Instance.nextAttack = Instance.nextSpam = 0f;
            Instance.skillSlot = 0;
            Instance.unreachable.Clear();
            Instance.chaseTarget = null;
        }

        static void MeasureDecision(string key)
        {
            if (!MeasureAnywhere) return;
            MeasureDecisionSeconds.TryGetValue(key, out float seconds);
            MeasureDecisionSeconds[key] = seconds + Time.deltaTime;
        }

        static void MeasureSkillRequest(PlayerController p, int slot)
        {
            if (!MeasureAnywhere) return;
            string key = p.Data.Progression.Active(slot)?.id ?? "empty";
            MeasureSkillRequests.TryGetValue(key, out int count);
            MeasureSkillRequests[key] = count + 1;
        }

        /// <summary>
        /// One frame of mechanic play. True = this frame is spent on it (walking to a circle / out of a warning, or hitting from the
        /// circle), false = fight as usual. <paramref name="best"/> is the monster the bot would hit.
        /// </summary>
        bool MeasureDodgeTurn(PlayerController p, EnemyController best)
        {
            if (dodgeBody != p || dodgeMap != Game.World?.MapId)
            {
                dodgeBody = p;
                dodgeMap = Game.World?.MapId;
                dangerSince = coverSince = -1f;
                escaping = false;
            }
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
                // Finish the escape to a point with clearance instead of immediately turning back at the boundary.
                if (escaping && Vector2.Distance(p.Position, escapeTarget) > .15f && !InGroundWarning(escapeTarget))
                {
                    float distance = Vector2.Distance(p.Position, escapeTarget);
                    if (distance < escapeBestDistance - .05f)
                    {
                        escapeBestDistance = distance;
                        escapeProgressAt = Time.time;
                    }
                    else if (Time.time - escapeProgressAt > .5f)
                    {
                        // Already outside danger: do not spend the attempt pushing into a blocked destination.
                        escaping = false;
                        MeasureDecision("escape_stalled_safe");
                        return false;
                    }
                    MeasureDecision("escape_clearance");
                    input.next.move = AvoidHuntPortals(p, GridPath.Steer(p.Position, escapeTarget), escapeTarget);
                    if (!on) return true;
                    input.next.aim = input.next.move;
                    lastProgressAt = Time.time;
                    return true;
                }
                escaping = false;
                // A person does not walk back into a warning that is still filling: wait at its edge and hit what is in reach.
                if (best == null) return false;
                if (!InGroundWarning(p.Position + GridPath.Steer(p.Position, best.Position) * .5f))
                    return ApproachRitualTotem(p, best);
                AttackInPlace(p, best);
                lastProgressAt = Time.time;
                return true;
            }
            if (dangerSince < 0f) dangerSince = Time.time;
            if (Time.time - dangerSince < DodgeReaction) return false;
            if (escaping)
            {
                float distance = Vector2.Distance(p.Position, escapeTarget);
                if (distance < escapeBestDistance - .05f)
                {
                    escapeBestDistance = distance;
                    escapeProgressAt = Time.time;
                }
                else if (Time.time - escapeProgressAt > .5f)
                {
                    escaping = false;
                    MeasureDecision("escape_repath_blocked");
                }
            }
            if (!escaping || InGroundWarning(escapeTarget) || Vector2.Distance(p.Position, escapeTarget) < .15f)
            {
                if (!escaping) MeasureDodges++;
                escaping = true;
                escapeTarget = SafeStep(p, best != null ? best.Position : p.Position);
                escapeBestDistance = Vector2.Distance(p.Position, escapeTarget);
                escapeProgressAt = Time.time;
            }
            Vector2 to = escapeTarget;
            Vector2 dir = to - p.Position;
            if (dir.sqrMagnitude < .0001f) { escaping = false; MeasureDecision("escape_no_path"); return true; }
            MeasureDecision("escape_warning");
            dir = GridPath.Steer(p.Position, to);
            dir = AvoidHuntPortals(p, dir, to);
            if (!on) return true;
            input.next.move = dir;
            input.next.aim = dir;
            lastProgressAt = Time.time;
            return true;
        }

        /// <summary>Move until a priority totem also wins the real skill auto-aim, rather than firing at a nearer boss.</summary>
        bool ApproachRitualTotem(PlayerController p, EnemyController target)
        {
            if (!RitualTargeting.IsTotem(target)) return false;
            Vector2 toward = target.Center - p.Center;
            float distance = toward.magnitude;
            Vector2 aim = distance > .001f ? toward / distance : p.AimDirection;
            if (!RitualTargeting.NeedsApproach(p, target)) { MeasureDecision("ritual_aim_ready"); return false; }
            MeasureDecision("ritual_aim_approach");
            input.next.move = AvoidHuntPortals(p, RitualTargeting.Steer(p, target.Position), target.Position);
            if (!on) return true;
            if (InGroundWarning(p.Position + input.next.move * .6f)) input.next.move = Vector2.zero;
            input.next.aim = aim;
            input.next.faceAim = true;
            lastProgressAt = Time.time;
            return true;
        }

        /// <summary>Walks to <paramref name="spot"/> after the reaction delay, then hits whatever is in reach without leaving it.</summary>
        bool HoldSpot(PlayerController p, Vector2 spot, float slack, EnemyController best, ref float since)
        {
            if (since < 0f) { since = Time.time; MeasureCoverMoves++; }
            lastProgressAt = Time.time;
            if (Time.time - since < DodgeReaction) { MeasureDecision("cover_reaction"); return true; }
            if (Vector2.Distance(p.Position, spot) > slack)
            {
                MeasureDecision("cover_move");
                Vector2 dir = GridPath.Steer(p.Position, spot);
                dir = AvoidHuntPortals(p, dir, spot);
                if (!on) return true;
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
            float reach = CombatReach(p, spam);
            if (Vector2.Distance(p.Position, target.Position) > reach) { MeasureDecision("safe_wait_out_of_reach"); return; }
            if (!p.Skills.IsCasting && !CanUseHunter(p, spam) && !BasicContact(p, target))
            { MeasureDecision("safe_wait_out_of_reach"); return; }
            MeasureDecision("safe_attack");
            input.next.aim = (target.Center - p.Center).normalized;
            input.next.faceAim = true;
            // Mobility skills can leave the required circle even though the movement command is zero.
            SkilledTurn(p, spam, stationary: true);
        }

        /// <summary>Inside a live, simulated danger warning (not a safe circle, not the room-wide 섬멸 / 의식 notice)?</summary>
        static bool InGroundWarning(Vector2 feet)
        {
            foreach (var t in Telegraph.Active)
                if (t != null && !t.Safe && !t.NoDanger && !t.Cancelled && !t.Resolved && t.Contains(feet)) return true;
            return false;
        }

        /// <summary>The nearest walkable point outside every warning; among equals the one closest to <paramref name="target"/>.</summary>
        static Vector2 SafeStep(PlayerController actor, Vector2 target)
        {
            Vector2 from = actor.Position;
            foreach (float r in DodgeRings)
            {
                Vector2 best = from;
                float bestScore = float.MaxValue;
                for (int i = 0; i < DodgeAngles; i++)
                {
                    float a = i * Mathf.PI * 2f / DodgeAngles;
                    Vector2 c = from + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!GridPath.Walkable(c) || InGroundWarning(c)) continue;
                    if (!RitualTargeting.ClearOfBodies(actor, from, c, allowMovingOut: false)) continue;
                    bool clear = true;
                    // Leave a body-width margin and reject destinations behind a wall.
                    for (int k = 0; k < 8 && clear; k++)
                    {
                        float edge = k * Mathf.PI / 4f;
                        clear = !InGroundWarning(c + new Vector2(Mathf.Cos(edge), Mathf.Sin(edge)) * .35f);
                    }
                    int steps = Mathf.CeilToInt(r / .25f);
                    for (int k = 1; k <= steps && clear; k++)
                    {
                        Vector2 step = Vector2.Lerp(from, c, k / (float)steps);
                        clear = GridPath.Walkable(step);
                        if (clear) clear = RitualTargeting.ClearOfBodies(actor, from, step);
                    }
                    if (!clear) continue;
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
