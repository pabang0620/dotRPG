using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// AI of a mercenary companion (an <see cref="IActorInput"/>): each frame it decides one
    /// <see cref="ActorCommand"/>. Priorities: catch up with the leader (teleport when far or stuck) →
    /// step out of <see cref="DangerZones"/> → fight (<see cref="PriorityTarget"/>, monsters already
    /// fighting the party, or the nearest monster close by) using its role's positioning and skills →
    /// otherwise walk in formation behind the leader. There is no path finding: straight lines plus the
    /// stuck teleport, which suits the open field and dungeon rooms.
    /// </summary>
    public sealed class CompanionBrain : IActorInput
    {
        // ---------- Tuning ----------
        public const float TeleportDistance = 12f, StuckSeconds = 2f;
        /// <summary>Monsters this close to the companion are engaged even if nobody fights them yet.</summary>
        public const float EngageRadius = 7f;
        /// <summary>Monsters further than this from the leader are left alone (don't run off).</summary>
        public const float LeashFromLeader = 10f;
        /// <summary>Mages keep between these distances from their target.</summary>
        public const float MageMinRange = 3f, MageMaxRange = 5f;
        /// <summary>Warriors close in to this distance (sword reach).</summary>
        public const float MeleeRange = 1.05f;
        /// <summary>Formation: start walking when further than this from the spot, run when far.</summary>
        public const float FormationSlack = 0.7f, FormationRun = 3f;
        /// <summary>Keep this share of MP for the most important skill (war cry / frost nova).</summary>
        public const float ManaReserve = 0.2f;
        /// <summary>Pause between two skills so basic attacks happen too.</summary>
        public const float SkillGap = 0.7f;
        /// <summary>Awakening skills wait for at least this many monsters in range.</summary>
        public const int UltimateMinTargets = 3;

        /// <summary>A place companions should not stand in (telegraphed boss attack), until <see cref="expiresAt"/> (Time.time).</summary>
        public struct DangerZone
        {
            public Vector2 center;
            public float radius;
            public float expiresAt;

            public bool Contains(Vector2 p, float margin = 0.3f) => Vector2.Distance(p, center) < radius + margin;
        }

        /// <summary>Areas to avoid (bosses add their telegraphs here). Expired zones are ignored and pruned.</summary>
        public static readonly List<DangerZone> DangerZones = new List<DangerZone>();

        /// <summary>Marks a circle as dangerous for <paramref name="seconds"/>.</summary>
        public static void AddDangerZone(Vector2 center, float radius, float seconds) =>
            DangerZones.Add(new DangerZone { center = center, radius = radius, expiresAt = Time.time + seconds });

        /// <summary>When set (boss gimmick: a totem), companions attack this monster first.</summary>
        public static EnemyController PriorityTarget;

        public readonly MercenaryDef Def;
        public EnemyController Target { get; private set; }

        Vector2 lastPos;
        float stuckTime, nextSkillAt, strafeSign = 1f, strafeFlipAt;

        public CompanionBrain(MercenaryDef def) => Def = def;

        public ActorCommand Read(PlayerController self)
        {
            var cmd = ActorCommand.None;
            var party = Game.Party;
            var leader = party != null ? party.Leader : Game.Player;
            if (leader == null || leader == self) return cmd;
            Vector2 me = self.Position;

            // 1. Catch up: too far from the leader, or stuck against something for a while.
            if (!leader.IsDead && Vector2.Distance(me, leader.Position) > TeleportDistance)
            {
                TeleportToLeader(self, party);
                return cmd;
            }

            // 2. Get out of telegraphed areas before anything else.
            if (EscapeDanger(me, out Vector2 away))
            {
                cmd.move = away;
                TrackStuck(self, cmd.move, party);
                return cmd;
            }

            // [DUNGEON] Just (re)spawned with the leader: stay in formation for a moment.
            if (party != null && Time.time < party.RegroupUntil && !leader.IsDead)
            {
                Target = null;
                Follow(self, party, ref cmd);
                TrackStuck(self, cmd.move, party);
                return cmd;
            }

            // 3. Fight.
            Target = PickTarget(self, leader);
            if (Target != null) Fight(self, leader, ref cmd);
            else if (!leader.IsDead) Follow(self, party, ref cmd);

            TrackStuck(self, cmd.move, party);
            return cmd;
        }

        // ---------- Movement ----------

        void TeleportToLeader(PlayerController self, PartyManager party)
        {
            Vector2 spot = party != null ? party.SpotFor(Mathf.Max(0, IndexOf(self, party) - 1)) : Game.Player.Position;
            self.Place(spot, self.Facing);
            Fx.Sparkle(self.Center, 4, 0.5f);
            stuckTime = 0f;
            lastPos = spot;
        }

        static int IndexOf(PlayerController self, PartyManager party)
        {
            for (int i = 0; i < party.Members.Count; i++) if (party.Members[i] == self) return i;
            return 1;
        }

        void TrackStuck(PlayerController self, Vector2 move, PartyManager party)
        {
            Vector2 p = self.Position;
            bool trying = move.sqrMagnitude > 0.2f;
            if (trying && Vector2.Distance(p, lastPos) < 0.6f * Time.deltaTime) stuckTime += Time.deltaTime;
            else stuckTime = Mathf.Max(0f, stuckTime - Time.deltaTime);
            lastPos = p;
            if (stuckTime > StuckSeconds && party != null && party.Leader != null && !party.Leader.IsDead) TeleportToLeader(self, party);
        }

        static bool EscapeDanger(Vector2 me, out Vector2 away)
        {
            away = Vector2.zero;
            float now = Time.time;
            DangerZones.RemoveAll(z => z.expiresAt <= now);
            foreach (var z in DangerZones)
            {
                if (!z.Contains(me)) continue;
                Vector2 d = me - z.center;
                away += d.sqrMagnitude > 0.0001f ? d.normalized : Random.insideUnitCircle.normalized;
            }
            if (away.sqrMagnitude < 0.0001f) return false;
            away = away.normalized;
            return true;
        }

        static bool InDanger(Vector2 p)
        {
            foreach (var z in DangerZones) if (z.expiresAt > Time.time && z.Contains(p)) return true;
            return false;
        }

        void Follow(PlayerController self, PartyManager party, ref ActorCommand cmd)
        {
            Vector2 spot = party != null ? party.FormationSpot(self) : Game.Player.Position;
            Vector2 to = spot - self.Position;
            float d = to.magnitude;
            if (d < FormationSlack) return;
            cmd.move = to / d * Mathf.Clamp(d / FormationRun, 0.45f, 1f);
        }

        // ---------- Targeting ----------

        static bool Valid(EnemyController e) => e != null && !e.IsDead && e.isActiveAndEnabled;

        EnemyController PickTarget(PlayerController self, PlayerController leader)
        {
            Vector2 me = self.Position;
            Vector2 anchor = leader.IsDead ? me : leader.Position;
            if (Valid(PriorityTarget) && Vector2.Distance(PriorityTarget.Position, anchor) < LeashFromLeader * 1.5f) return PriorityTarget;

            EnemyController best = null;
            float bestScore = float.MaxValue;
            foreach (var e in EnemyController.Active)
            {
                if (!Valid(e)) continue;
                if (Vector2.Distance(e.Position, anchor) > LeashFromLeader) continue;
                float d = Vector2.Distance(e.Position, me);
                // Monsters already on a party member come first; the tank goes for whoever is hitting its friends.
                var table = e.GetComponent<ThreatTable>();
                bool fighting = table != null && table.Current != null && table.Current != self && d < EngageRadius * 1.6f;
                if (!fighting && d > EngageRadius && (table == null || table.Current != self)) continue;
                float score = d - (fighting ? (Def.role == MercRole.Tank ? 4f : 1.5f) : 0f);
                // Keep hitting the current target unless something is much closer.
                if (e == Target) score -= 1f;
                if (score < bestScore) { bestScore = score; best = e; }
            }
            return best;
        }

        // ---------- Fighting ----------

        void Fight(PlayerController self, PlayerController leader, ref ActorCommand cmd)
        {
            Vector2 me = self.Position;
            Vector2 tp = Target.Position;
            Vector2 to = tp - me;
            float dist = to.magnitude;
            cmd.aim = (Target.Center - self.Center).normalized;
            cmd.faceAim = true;

            if (Def.IsMage)
            {
                // Keep 3–5 units away; strafe a little while shooting.
                if (dist < MageMinRange) cmd.move = Away(me, tp);
                else if (dist > MageMaxRange) cmd.move = to / dist;
                else
                {
                    if (Time.time > strafeFlipAt) { strafeSign = -strafeSign; strafeFlipAt = Time.time + Random.Range(1.2f, 2.2f); }
                    cmd.move = new Vector2(-to.y, to.x).normalized * 0.25f * strafeSign;
                }
                var info = CharacterClassInfo.Get(CharacterClass.Mage);
                if (dist <= info.boltRange * 0.95f) cmd.attack = true;
            }
            else
            {
                // Warriors walk up to sword reach; the tank stands between the monster and the leader.
                Vector2 goal = tp;
                if (Def.role == MercRole.Tank && !leader.IsDead)
                {
                    Vector2 side = (leader.Position - tp);
                    goal = tp + (side.sqrMagnitude > 0.01f ? side.normalized : Vector2.down) * (MeleeRange * 0.8f);
                }
                Vector2 toGoal = goal - me;
                if (dist > MeleeRange || toGoal.magnitude > 0.5f) cmd.move = toGoal.sqrMagnitude > 0.0001f ? toGoal.normalized : Vector2.zero;
                if (dist <= MeleeRange + 0.25f) cmd.attack = true;
            }
            // Never walk into a telegraph.
            if (cmd.move.sqrMagnitude > 0.01f && InDanger(me + cmd.move * 0.6f)) cmd.move = Vector2.zero;

            int slot = ChooseSkill(self, dist);
            if (slot >= 0)
            {
                cmd.skillSlot = slot;
                cmd.attack = false;
                nextSkillAt = Time.time + SkillGap;
            }
        }

        static Vector2 Away(Vector2 me, Vector2 from)
        {
            Vector2 d = me - from;
            return d.sqrMagnitude > 0.0001f ? d.normalized : Vector2.up;
        }

        int ChooseSkill(PlayerController self, float dist)
        {
            if (Time.time < nextSkillAt || self.Skills == null || self.Skills.IsCasting || Def.skillPriority == null) return -1;
            foreach (int slot in Def.skillPriority)
            {
                if (!self.Skills.IsReady(slot)) continue;
                var n = self.Skills.Numbers(slot);
                if (n.usesLife ? self.Health.Current <= n.manaCost * 2 : self.Mana < n.manaCost) continue;
                var gem = self.Data.Progression.Active(slot);
                if (gem == null || !WorthCasting(self, gem, n, dist)) continue;
                // Leave some MP for the defining skill (war cry, frost nova) unless this is it.
                bool defining = slot == Def.skillPriority[0];
                if (!defining && !n.usesLife && self.Mana - n.manaCost < self.MaxMana * ManaReserve) continue;
                return slot;
            }
            return -1;
        }

        bool WorthCasting(PlayerController self, SkillGem gem, SkillNumbers n, float dist)
        {
            Vector2 c = self.Center;
            switch (gem.id)
            {
                case "cry":
                    // Shout when monsters are close, or when one is on somebody else (take its aggro).
                    return CountNear(c, n.radius * 0.9f) >= 1 && (CountNear(c, n.radius) >= 2 || !TargetsMe(self));
                case "whirl":
                    return CountNear(c, n.radius * 0.9f) >= (Def.role == MercRole.Tank ? 1 : 2);
                case "nova":
                    // Freeze whatever gets close (the control mage's answer to melee monsters).
                    return CountNear(c, n.radius * 0.85f) >= 1;
                case "slam":
                case "wave":
                    return dist <= n.range * 0.9f;
                case "arc":
                case "thunder":
                case "frostorb":
                    return dist <= n.range * 0.9f;
                default:
                    // Awakening skills: only into a crowd.
                    return gem.IsUltimate && CountNear(self.Position, n.range * 0.8f) >= UltimateMinTargets;
            }
        }

        bool TargetsMe(PlayerController self)
        {
            var table = Target != null ? Target.GetComponent<ThreatTable>() : null;
            return table != null && table.Current == self;
        }

        static int CountNear(Vector2 p, float radius)
        {
            int n = 0;
            foreach (var e in EnemyController.Active)
                if (Valid(e) && Vector2.Distance(e.Center, p) <= radius) n++;
            return n;
        }
    }
}
