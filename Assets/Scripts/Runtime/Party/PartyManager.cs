using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The party (Game.Party): the local player plus up to three AI companions (mercenaries).
    /// <see cref="Local"/> is <see cref="Game.Player"/>; companions are built from
    /// <see cref="GameSession.PartyRoster"/> whenever the local player (re)spawns, placed next to it on
    /// every map change, and removed when it leaves the world (title screen). Also owns the shared
    /// combat bookkeeping: XP once per kill into the local progression, kill passives for the killer,
    /// per-member damage meters, companion field revives, and the geometry helpers monsters use.
    /// </summary>
    public sealed class PartyManager : MonoBehaviour
    {
        // ---------- Tuning ----------
        public const int MaxMembers = 4, MaxCompanions = MaxMembers - 1;
        /// <summary>Field rule: a downed companion gets up after this many seconds with <see cref="FieldReviveHp"/> of its HP.</summary>
        public const float FieldReviveSeconds = 8f, FieldReviveHp = 0.5f;
        /// <summary>Formation spots around the leader (companion 1, 2, 3), in world units.</summary>
        static readonly Vector2[] Formation = { new Vector2(-1.1f, 0.5f), new Vector2(1.1f, 0.5f), new Vector2(0f, 1.2f) };

        readonly List<PlayerController> members = new List<PlayerController>();
        readonly Dictionary<string, int> damageDealt = new Dictionary<string, int>();
        readonly Dictionary<PlayerController, Coroutine> reviving = new Dictionary<PlayerController, Coroutine>();
        readonly Dictionary<PlayerController, float> reviveAt = new Dictionary<PlayerController, float>();
        Transform root;
        bool autoRevive = true;
        static bool quitting;

        /// <summary>Everyone in the party, local player first.</summary>
        public IReadOnlyList<PlayerController> Members => members;
        /// <summary>The member this PC controls (camera, HUD, pickups, portals, interaction).</summary>
        public PlayerController Local { get; private set; }
        /// <summary>Who may use portals and enter dungeons (for now always the local player).</summary>
        public PlayerController Leader => Local;
        public int Count => members.Count;
        public bool HasCompanions => members.Count > 1;

        /// <summary>A member was downed (HP 0). For the local player the game's own death flow runs as well.</summary>
        public event Action<PlayerController> MemberDowned;
        public event Action<PlayerController> MemberRevived;
        /// <summary>A member joined or left.</summary>
        public event Action Changed;

        public static PartyManager Create(Transform parent, PlayerController local)
        {
            var go = new GameObject("Party");
            go.transform.SetParent(parent, false);
            var party = go.AddComponent<PartyManager>();
            party.root = parent;
            party.Local = local;
            party.members.Add(local);
            CharacterData.Session.DisplayName = "나";
            EnemyController.Killed += party.OnEnemyKilled;
            Game.Session.Progression.Changed += party.SyncCompanionLevels;
            Application.quitting += () => quitting = true;
            return party;
        }

        void OnDestroy()
        {
            EnemyController.Killed -= OnEnemyKilled;
            if (Game.Session != null) Game.Session.Progression.Changed -= SyncCompanionLevels;
        }

        // =============================== Roster ===============================

        public bool Contains(PlayerController member) => member != null && members.Contains(member);

        public bool Has(string mercId) => Find(mercId) != null;

        /// <summary>The companion hired as <paramref name="mercId"/>, or null.</summary>
        public PlayerController Find(string mercId)
        {
            foreach (var m in members)
                if (m != null && m.Data != null && m.Data.MercenaryId == mercId) return m;
            return null;
        }

        /// <summary>
        /// Hires a mercenary: it joins the saved roster and, while the local player is in the world, appears
        /// next to it. Null if the id is unknown, already in the party, or the party is full.
        /// </summary>
        public PlayerController AddCompanion(string mercId)
        {
            var def = MercenaryDatabase.Get(mercId);
            var roster = Game.Session.PartyRoster;
            if (def == null || roster.Contains(mercId) || roster.Count >= MaxCompanions) return null;
            roster.Add(mercId);
            var member = LocalInWorld && companionsOut ? Spawn(def, roster.Count - 1) : null; // hired in town: joins at the next dungeon
            Changed?.Invoke();
            return member;
        }

        /// <summary>Dismisses a mercenary (roster and world). False if it was not in the party.</summary>
        public bool RemoveCompanion(string mercId)
        {
            bool inRoster = Game.Session.PartyRoster.Remove(mercId);
            var member = Find(mercId);
            if (member != null) Despawn(member);
            if (!inRoster && member == null) return false;
            Changed?.Invoke();
            return true;
        }

        /// <summary>Removes every companion (roster included).</summary>
        public void ClearCompanions()
        {
            foreach (var id in new List<string>(Game.Session.PartyRoster)) RemoveCompanion(id);
        }

        bool LocalInWorld => Local != null && Local.gameObject.activeInHierarchy;

        // =============================== [PARTY NET] Network members ===============================

        readonly HashSet<PlayerController> netMembers = new HashSet<PlayerController>();
        bool rosterHidden;
        /// <summary>Most of this PC's own mercenaries in the world (-1 = the whole roster). An online party sets it to
        /// the seats people leave free: AI fill the party, they never push it past four.</summary>
        int companionCap = -1;

        /// <summary>How many of the roster may be out (shared play: the free seats). -1 lifts the cap.</summary>
        public void SetCompanionCap(int cap)
        {
            if (companionCap == cap) return;
            companionCap = cap;
            if (rosterHidden || Local == null || !companionsOut) { Changed?.Invoke(); return; }
            // Trim from the end of the roster, then bring roster members back up to the cap.
            int ai = 0;
            foreach (var id in Game.Session.PartyRoster)
            {
                var m = Find(id);
                if (m == null) continue;
                if (cap >= 0 && ai >= cap) Despawn(m);
                else ai++;
            }
            if (LocalInWorld) OnLocalSpawned(false);
            else Changed?.Invoke();
        }

        bool companionsOut;

        /// <summary>
        /// The hired mercenaries join only inside weekday dungeons and raids: DungeonDirector turns this on as a run
        /// starts (before the party size is read) and off when the party is back on the map.
        /// </summary>
        public void SetDungeonCompanions(bool on)
        {
            if (companionsOut == on) return;
            companionsOut = on;
            if (LocalInWorld) OnLocalSpawned(false);
            else Changed?.Invoke();
        }

        /// <summary>The roster members allowed out now, in roster order.</summary>
        public int CompanionCap => companionCap;

        /// <summary>A member that came from the network (remote human on the host, puppet on a member PC).</summary>
        public bool IsNetMember(PlayerController m) => m != null && netMembers.Contains(m);

        /// <summary>
        /// Adds a member built from network data. The host drives a remote human with a
        /// <see cref="NetworkInput"/>; a member PC draws everyone else as puppets (<paramref name="puppet"/>).
        /// </summary>
        public PlayerController AddNetMember(CharacterData data, IActorInput input, bool puppet, Vector2 at, Facing facing)
        {
            var member = PlayerController.CreateCompanion(Game.Config, root, data, input);
            foreach (var other in members)
                if (other != null && other.BodyCollider != null) Physics2D.IgnoreCollision(member.BodyCollider, other.BodyCollider, true);
            members.Add(member);
            netMembers.Add(member);
            member.Spawn(at, facing, int.MaxValue, 0);
            member.SetNetDriven(true, puppet);
            Changed?.Invoke();
            return member;
        }

        public void RemoveNetMember(PlayerController member)
        {
            if (member == null || !netMembers.Remove(member)) return;
            Despawn(member);
            Changed?.Invoke();
        }

        /// <summary>
        /// While a party run is on, this PC's own hired mercenaries stay out (the host's AI fills the empty
        /// seats). Off again brings them back from the roster.
        /// </summary>
        public void SetRosterHidden(bool hidden)
        {
            if (rosterHidden == hidden) return;
            rosterHidden = hidden;
            if (hidden)
            {
                for (int i = members.Count - 1; i >= 1; i--)
                    if (!netMembers.Contains(members[i])) Despawn(members[i]);
            }
            else
            {
                foreach (var m in new List<PlayerController>(netMembers)) Despawn(m);
                netMembers.Clear();
                if (LocalInWorld) OnLocalSpawned(false);
            }
            Changed?.Invoke();
        }

        PlayerController Spawn(MercenaryDef def, int slot)
        {
            var data = MercenaryDatabase.CreateData(def, Game.Session.Progression.Level);
            var brain = new CompanionBrain(def);
            var member = PlayerController.CreateCompanion(Game.Config, root, data, brain);
            // Party members walk through each other (no blocking in corridors).
            foreach (var other in members)
                if (other != null && other.BodyCollider != null) Physics2D.IgnoreCollision(member.BodyCollider, other.BodyCollider, true);
            members.Add(member);
            member.Spawn(SpotFor(slot), Local.Facing, int.MaxValue, 0);
            return member;
        }

        void Despawn(PlayerController member)
        {
            if (member == null || member == Local) return;
            members.Remove(member);
            StopRevive(member);
            foreach (var e in EnemyController.Active)
                if (e != null) e.GetComponent<ThreatTable>()?.Forget(member);
            Destroy(member.gameObject);
        }

        /// <summary>Called by the local player's Spawn (new game, continue, travel, village respawn).</summary>
        internal void OnLocalSpawned(bool fromDeath)
        {
            if (quitting || this == null) return;
            // Make the companions in the world match the saved roster.
            var roster = Game.Session.PartyRoster;
            for (int i = members.Count - 1; i >= 1; i--)
            {
                var m = members[i];
                if (m == null) { members.RemoveAt(i); continue; }
                if (netMembers.Contains(m)) continue; // [PARTY NET] placed by the run, not by the roster
                if (!roster.Contains(m.Data.MercenaryId)) Despawn(m);
            }
            if (!rosterHidden)
            {
                int out_ = 0;
                // AI companions fight in weekday dungeons and raids only: hunting grounds and towns are walked alone
                // (or with friends), so the roster stays home until DungeonDirector calls them out.
                bool inRun = companionsOut;
                foreach (var id in roster)
                {
                    if (!inRun || (companionCap >= 0 && out_ >= companionCap)) { if (Find(id) is PlayerController extra) Despawn(extra); continue; }
                    if (Find(id) == null && MercenaryDatabase.Get(id) != null) Spawn(MercenaryDatabase.Get(id), members.Count - 1);
                    out_++;
                }
            }
            SyncCompanionLevels();
            // Everyone follows the local player to where it appeared.
            for (int i = 1; i < members.Count; i++)
            {
                var m = members[i];
                StopRevive(m);
                if (fromDeath || m.IsDead)
                {
                    // Back at full HP after the village respawn; downed ones get up (half HP) on a map change.
                    m.Spawn(SpotFor(i - 1), Local.Facing, fromDeath ? int.MaxValue : Mathf.CeilToInt(m.Data.Stats.MaxHp * FieldReviveHp), 0);
                    if (fromDeath) m.Data.Mana = m.Data.Stats.MaxMp;
                }
                else m.Place(SpotFor(i - 1), Local.Facing);
            }
            ResetMeters();
            Regroup(); // [DUNGEON] arrive together (continue / travel): no running off to nearby monsters at once
            Changed?.Invoke();
        }

        // [DUNGEON] Regroup window: companions only follow the leader until this time (Time.time).
        public const float RegroupSeconds = 2.5f;
        public float RegroupUntil { get; private set; }
        public void Regroup(float seconds = RegroupSeconds) => RegroupUntil = Time.time + seconds;

        /// <summary>Called when the local player leaves the world (title screen): the companions go with it.</summary>
        internal void OnLocalHidden()
        {
            if (quitting || this == null) return;
            for (int i = members.Count - 1; i >= 1; i--) Despawn(members[i]);
            Changed?.Invoke();
        }

        /// <summary>A free spot next to the local player for companion <paramref name="slot"/> (0-2).</summary>
        public Vector2 SpotFor(int slot)
        {
            Vector2 leader = Local != null ? Local.Position : Vector2.zero;
            Vector2 offset = Formation[Mathf.Clamp(slot, 0, Formation.Length - 1)];
            Vector2 p = leader + offset;
            if (Game.World == null || Game.World.IsFree(p)) return p;
            // Blocked (wall, water): try a few rings around the leader, else stand on the leader.
            for (int ring = 1; ring <= 3; ring++)
                for (int k = 0; k < 8; k++)
                {
                    float a = (k / 8f + slot * 0.1f) * Mathf.PI * 2f;
                    Vector2 q = leader + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (0.6f * ring);
                    if (Game.World.IsFree(q)) return q;
                }
            return leader;
        }

        /// <summary>Formation spot of a companion (for its brain).</summary>
        public Vector2 FormationSpot(PlayerController member)
        {
            int index = members.IndexOf(member);
            Vector2 leader = Local != null ? Local.Position : Vector2.zero;
            return leader + Formation[Mathf.Clamp(index - 1, 0, Formation.Length - 1)];
        }

        void SyncCompanionLevels()
        {
            int level = Game.Session.Progression.Level;
            for (int i = 1; i < members.Count; i++)
            {
                var m = members[i];
                if (netMembers.Contains(m)) continue; // [PARTY NET] their level is their own (or the host's)
                var def = m != null ? MercenaryDatabase.Get(m.Data.MercenaryId) : null;
                if (def != null && m.Data.Level != level) MercenaryDatabase.ApplyLevel(m.Data, def, level);
            }
        }

        // =============================== Queries ===============================

        public IEnumerable<PlayerController> AliveMembers
        {
            get
            {
                foreach (var m in members)
                    if (m != null && !m.IsDead && m.isActiveAndEnabled) yield return m;
            }
        }

        public int AliveCount
        {
            get
            {
                int n = 0;
                foreach (var _ in AliveMembers) n++;
                return n;
            }
        }

        /// <summary>Every member is down (dungeon failure rule).</summary>
        public bool IsWiped => AliveCount == 0;

        /// <summary>Alive members whose body centre is within <paramref name="radius"/> of <paramref name="center"/>.</summary>
        public List<PlayerController> MembersInCircle(Vector2 center, float radius)
        {
            var list = new List<PlayerController>();
            foreach (var m in AliveMembers)
                if (Vector2.Distance(m.Center, center) < radius) list.Add(m);
            return list;
        }

        /// <summary>
        /// Alive members inside a cone: within <paramref name="radius"/> of <paramref name="origin"/> and at most
        /// <paramref name="halfAngleDeg"/> degrees off <paramref name="direction"/>.
        /// </summary>
        public List<PlayerController> MembersInArc(Vector2 origin, Vector2 direction, float radius, float halfAngleDeg)
        {
            var list = new List<PlayerController>();
            Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
            foreach (var m in AliveMembers)
            {
                Vector2 to = m.Center - origin;
                if (to.magnitude > radius) continue;
                if (to.sqrMagnitude < 0.0001f || Vector2.Angle(dir, to) <= halfAngleDeg) list.Add(m);
            }
            return list;
        }

        // [MONSTER] Line / rectangle query (charges, arrow lines, line telegraphs).
        /// <summary>
        /// Alive members whose body centre lies in the rectangle starting at <paramref name="origin"/>, running
        /// <paramref name="length"/> along <paramref name="direction"/>, <paramref name="halfWidth"/> to each side.
        /// </summary>
        public List<PlayerController> MembersInRect(Vector2 origin, Vector2 direction, float length, float halfWidth)
        {
            var list = new List<PlayerController>();
            Vector2 dir = direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector2.down;
            foreach (var m in AliveMembers)
            {
                Vector2 to = m.Center - origin;
                float along = Vector2.Dot(to, dir);
                float across = Mathf.Abs(to.x * dir.y - to.y * dir.x);
                if (along >= 0f && along <= length && across <= halfWidth) list.Add(m);
            }
            return list;
        }

        /// <summary>Nearest alive member to <paramref name="point"/> (null when everyone is down).</summary>
        public PlayerController NearestAlive(Vector2 point)
        {
            PlayerController best = null;
            float bestDist = float.MaxValue;
            foreach (var m in AliveMembers)
            {
                float d = Vector2.Distance(m.Position, point);
                if (d < bestDist) { bestDist = d; best = m; }
            }
            return best;
        }

        /// <summary>The member a monster should go after (threat table; solo = the local player).</summary>
        public PlayerController SelectTarget(EnemyController enemy)
        {
            if (enemy == null) return null;
            return ThreatTable.For(enemy).Select(this);
        }

        /// <summary>True for a hit dealt by a companion (drawn with white damage numbers).</summary>
        public static bool IsCompanionHit(DamageInfo info)
        {
            var m = info.AttackerMember;
            return m != null && !m.IsLocal;
        }

        // =============================== Combat bookkeeping ===============================

        /// <summary>Damage dealt this map / run, keyed by member display name ("나", "브론", ...).</summary>
        public IReadOnlyDictionary<string, int> DamageDealt => damageDealt;

        public int DamageOf(PlayerController member) =>
            member != null && damageDealt.TryGetValue(member.DisplayName, out int v) ? v : 0;

        /// <summary>Clears the damage meters (dungeon start). Also done on every map change.</summary>
        public void ResetMeters() => damageDealt.Clear();

        internal void RecordDamageDealt(PlayerController member, int amount)
        {
            if (member == null || amount <= 0) return;
            string key = member.DisplayName;
            damageDealt[key] = (damageDealt.TryGetValue(key, out int v) ? v : 0) + amount;
        }

        /// <summary>Hits taken by members (hook for later: dungeon rank "피격" score).</summary>
        public int HitsTaken { get; private set; }

        internal void RecordDamageTaken(PlayerController member, DamageInfo info) => HitsTaken++;

        void OnEnemyKilled(EnemyController enemy, int xp)
        {
            // XP goes into the local progression exactly once per kill (companions follow its level).
            var prog = Game.Session.Progression;
            if (!OnlineEconomy.On) // [SERVER] online XP comes with the kill report's answer
            {
                prog.AddXp(xp);
                if (xp > 0) GameEvents.RaiseToast($"+{xp} EXP");
            }
            // Kill passives (HP / MP on kill) belong to whoever landed the blow; unknown killer = local player.
            var table = enemy != null ? enemy.GetComponent<ThreatTable>() : null;
            var killer = table != null && Contains(table.LastAttacker) ? table.LastAttacker : Local;
            killer?.OnKillingBlow();
        }

        // =============================== Down / revive ===============================

        /// <summary>Field rule on/off: downed companions get up by themselves after <see cref="FieldReviveSeconds"/>.</summary>
        public void SetCompanionAutoRevive(bool enabled)
        {
            autoRevive = enabled;
            if (!enabled) foreach (var m in new List<PlayerController>(reviving.Keys)) StopRevive(m);
        }

        public bool CompanionAutoRevive => autoRevive;

        /// <summary>Seconds until a downed companion gets up (0 = not counting down).</summary>
        public float ReviveRemaining(PlayerController member) =>
            member != null && reviveAt.TryGetValue(member, out float at) ? Mathf.Max(0f, at - Time.time) : 0f;

        /// <summary>Called by a member when its HP reaches 0.</summary>
        internal void NotifyDowned(PlayerController member)
        {
            MemberDowned?.Invoke(member);
            if (member != Local && autoRevive && Contains(member))
            {
                StopRevive(member);
                reviveAt[member] = Time.time + FieldReviveSeconds;
                reviving[member] = StartCoroutine(ReviveLater(member));
            }
        }

        IEnumerator ReviveLater(PlayerController member)
        {
            yield return new WaitForSeconds(FieldReviveSeconds);
            reviving.Remove(member);
            reviveAt.Remove(member);
            if (member != null && member.IsDead && Contains(member)) ReviveMember(member, FieldReviveHp);
        }

        void StopRevive(PlayerController member)
        {
            if (member != null && reviving.TryGetValue(member, out var co) && co != null) StopCoroutine(co);
            reviving.Remove(member);
            reviveAt.Remove(member);
        }

        /// <summary>Gets a downed member up with <paramref name="hpFraction"/> of its max HP (dungeon coins, skills, field timer).</summary>
        public void ReviveMember(PlayerController member, float hpFraction, float invulnerableSeconds = 0f)
        {
            if (member == null || !member.IsDead) return;
            StopRevive(member);
            member.Revive(hpFraction, invulnerableSeconds);
            MemberRevived?.Invoke(member);
        }
    }
}
