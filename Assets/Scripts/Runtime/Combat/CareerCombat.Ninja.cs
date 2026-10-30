using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// 닌자 (rogue career, Docs/PLAN_CLASS_ROGUE.md 2.4): thrown kunai at range. The ninja's basic attack is always a kunai
    /// throw (투척술 only makes it stronger); 연막 도약 is the escape; 분신술 (user skill) puts two clones beside me that copy
    /// every attack of mine (basic attack and attack skills, same direction) at 12% of my damage each (2026-10-29). Clone damage is my own damage. 쿠나이 순간이동 moved to the assassin (2026-10-23) but its anchor state
    /// and the free-spot search stay here, next to the clone swap that shares them.
    /// </summary>
    public sealed partial class CareerCombat
    {
        // ---------- Constants (plan 2.4) ----------

        /// <summary>원거리 집중: monsters this far or farther take the bonus.</summary>
        const float FarDistance = 6f;
        /// <summary>투척술: a throw every 0.3 s, 6 m, the kunai stays 4 s where it stops. [BALANCE 2026-10-30] Hit corridor
        /// 0.3 -> 0.45 m: from 6 m most kunai flew past field monsters that stepped aside (a boss is hit either way).</summary>
        const float ThrowInterval = .3f, ThrowRange = 6f, ThrowSpeed = 18f, GroundSeconds = 4f, KunaiHalfWidth = .45f;
        /// <summary>쿠나이 순간이동: 20 m/s (10 m in 0.5 s); a kunai stuck in a wall sends me to the nearest free spot within 1.5 m.</summary>
        const float PortSpeed = 20f, PortSearch = 1.5f;
        /// <summary>연발 쿠나이 / 회수 / 풍마 수리검 flight speeds.</summary>
        const float VolleySpeed = 20f, RecallSpeed = 24f, FumaSpeed = 9f, FumaHover = .35f;
        /// <summary>회수: at most 12 kunai, each gives back 1% of max MP.</summary>
        const int RecallMax = 12;
        const float RecallMpShare = .01f;
        /// <summary>기폭 쿠나이: 5 kunai on a 2.5 m circle; one goes off when a monster comes within 1.2 m, all after 2 s, one by one.</summary>
        const float BombRing = 2.5f, BombTrigger = 1.2f, BombFuse = 2f, BombChain = .12f, BombRoot = 1f, BombRootBoss = .3f;
        /// <summary>표창 난사: star speed, the random spread around the way I face (degrees), the stars' size.</summary>
        const float StarSpeed = 22f, StarSpread = 6f;
        /// <summary>투척술 (2026-10-23 a plain upgrade): throw damage +10% at rank 1, +5% a rank more, and 1 m more reach.</summary>
        const float ThrowRankPct = 10f, ThrowRankStep = 5f, ThrowRankReach = 1f;
        /// <summary>연막 도약: the smoke bomb lands on the pack in front, at most this far (the ninja fights at range).</summary>
        public const float VeilThrowRange = 6f;
        const float VeilBombSpeed = 16f;
        /// <summary>분신술: a clone's copy of a hit lands this long after mine, the next clone a little later.</summary>
        const float EchoDelay = .05f, EchoStep = .04f;
        /// <summary>
        /// 분신술 does not copy these: moving and setting skills (user: copy the attacks) and the awakening (its own clones).
        /// 투척 / 연발 쿠나이 / 풍마 수리검 are copied as real throws from each clone (Throw, FumaFlight), not as echoes.
        /// The base kit's moving skills (그림자 질주; 연쇄 도약 and 그림자 습격 only in their dagger form) are muted by SkillCaster
        /// (<see cref="Uncopied"/>). [2026-10-23] The ninja throws the base attacks (SkillCaster.NinjaThrow), and the clones copy those.
        /// </summary>
        static readonly HashSet<string> NotEchoed = new HashSet<string> { "n_veil", "n_recall", "n_bomb", "n_awake", "n_clone", "n_throw", "n_volley", "n_fuma" };

        /// <summary>[NINJA 2026-10-23] Plain colours (user: no flashy tints): the 기폭 blasts keep the fire art's own colour,
        /// trails are pale steel, the clones a see-through grey.</summary>
        public static readonly Color NinjaOrange = Color.white;
        static readonly Color CloneTint = new Color(.72f, .74f, .78f, .55f);

        // ---------- State ----------

        float nextThrowAt;
        KunaiProjectile portKunai;
        int portVersion;
        bool portAnchor; // [ROGUE SPEC] 그림자 닻: the arrival guard
        readonly List<NinjaClone> clones = new List<NinjaClone>();
        float clonesEnd, cloneShare;
        bool cloneSwapUsed;
        /// <summary>The amount of the direct hit being dealt, before my multipliers (ModifyDamage): a clone copies its share (cloneShare) of it.</summary>
        int echoRaw;
        /// <summary>A clone's copy is landing (it is not copied again) / a moving skill's hit is landing (not copied).</summary>
        bool echoing;
        int echoMuted;

        public float ClonesLeft => clones.Count > 0 ? Mathf.Max(0f, clonesEnd - Time.time) : 0f;
        /// <summary>A second press of the assassin's kunai skill would teleport instead of throwing.</summary>
        public bool HasKunaiAnchor => portKunai != null;
        bool ClonesUp => ClonesLeft > 0f;

        void NinjaReset()
        {
            portVersion++;
            if (portKunai != null) portKunai.Vanish();
            portKunai = null;
            DismissClones();
            nextThrowAt = 0f;
        }

        void NinjaTick()
        {
            if (clones.Count > 0 && (Time.time >= clonesEnd || owner.IsDead || Mine != Career.Ninja)) DismissClones();
        }

        void DismissClones()
        {
            foreach (var c in clones) if (c != null) c.Dismiss();
            clones.Clear();
            clonesEnd = 0f;
            cloneSwapUsed = false;
        }

        /// <summary>The top weapon art tier throws stars instead of kunai (same numbers).</summary>
        public bool ThrowsStars => EquipmentDatabase.TierOf(owner.Data.Equipment[EquipSlot.Weapon]) >= 3;

        Run QuickRun(CareerSkill s, SkillNumbers n, Vector2 dir) =>
            new Run { s = s, n = n, dir = dir, map = Game.Session.MapId, version = castVersion, authority = !PartyNet.IsMember };

        // ---------- 투척술: the basic attack ----------

        /// <summary>A promoted ninja always throws; learning the throwing node only upgrades the throw.</summary>
        public bool ThrowsKunai => Mine == Career.Ninja && owner.Data.Progression.IsPromoted;

        /// <summary>The basic throw's reach: 6 m, 7 m with 투척술.</summary>
        public float ThrowReach => ThrowRange + (Prog.Rank("n_throw") > 0 ? ThrowRankReach : 0f);

        /// <summary>
        /// [NINJA] Called by PlayerCombat on an attack press. Returns true when the press was taken by 투척술 (a kunai flew,
        /// or the next throw is not ready yet), so the melee swing does not run.
        /// </summary>
        public bool TryNinjaBasic()
        {
            if (!ThrowsKunai) return false;
            if (Time.time < nextThrowAt) return true;
            nextThrowAt = Time.time + ThrowGap() * owner.Data.Stats.CooldownMultiplier; // [ROGUE SPEC] 무거운 쿠나이 / 연속 투척
            var s = CareerCatalog.Get("n_throw");
            float reach = ThrowReach;
            Vector2 dir = owner.AutoAim(reach + 1f);
            if (!owner.NetPuppet) owner.FaceTowards(owner.Position + dir);
            owner.GetComponent<CharacterAnimator>()?.BeginCareerPose(.16f, 0, 0, WarriorAttackMotion.Contact);
            Sound("swing", .6f);
            int rank = Prog.Rank("n_throw");
            float scale = CharacterClassInfo.Get(owner.Class).basicHitScale * (rank > 0 ? 1f + (ThrowRankPct + ThrowRankStep * (rank - 1)) / 100f : 1f) * ThrowHitScale();
            int damage = Mathf.Max(1, Mathf.RoundToInt(owner.Data.Stats.AttackDamage(owner.Class) * scale));
            var run = QuickRun(s, default, dir);
            StartCoroutine(Throw(run, owner.Center + dir * .3f, dir, reach, ThrowSpeed, damage, true, null, ThrowPierce()));
            ForClones(clone => StartCoroutine(Throw(run, clone.Center + dir * .3f, dir, reach, ThrowSpeed, CloneDamage(damage), false, clone)));
            return true;
        }

        /// <summary>
        /// One straight throw that sticks in the first monster (or a wall, or drops at the end). <paramref name="keep"/>:
        /// the kunai stays 4 s for 회수 (the body's own kunai; clones' kunai vanish).
        /// [BALANCE 2026-10-30] Kunai hits on a field monster leave no hit invulnerability (like 표창 난사): the body's two
        /// volley kunai and the clones' copies land on one monster within 0.15 s, and the monster's hit invulnerability threw
        /// away 39% of the body's kunai while hunting (measure_20261030c diag: 195 of 501 refused). Bosses keep it: there the
        /// ninja already dealt 89-93% of the fighter, and dropping it there too made it 117-120%.
        /// </summary>
        IEnumerator Throw(Run c, Vector2 from, Vector2 dir, float range, float speed, int damage, bool keep, NinjaClone clone, int pierce = 0)
        {
            var k = KunaiProjectile.Spawn(owner, from, dir, ThrowsStars, clone != null ? CloneTint : SpecTint(c));
            KunaiStat(clone, 0);
            Vector2 at = from;
            float travelled = 0f;
            HashSet<EnemyController> passed = pierce > 0 ? new HashSet<EnemyController>() : null; // [ROGUE SPEC] piercing throws
            while (travelled < range)
            {
                if (!Live(c) || k == null) { if (k != null) k.Vanish(); yield break; }
                float step = Mathf.Min(speed * Time.deltaTime, range - travelled);
                Vector2 next = at + dir * step;
                if (ProjectileBlocked(next)) { KunaiStat(clone, 3); break; }
                EnemyController victim = null;
                foreach (var e in Corridor(at, next, KunaiHalfWidth)) { if (passed != null && passed.Contains(e)) continue; victim = e; break; }
                if (victim != null && passed != null && passed.Count < pierce)
                {
                    passed.Add(victim);
                    if (Strike(c, victim, damage, at, 1.5f, 0, "hit", rapid: !victim.IsBoss)) KunaiStat(clone, 1);
                    victim = null;
                }
                if (victim != null)
                {
                    KunaiStat(clone, Strike(c, victim, damage, at, 2f, 0, "hit", rapid: !victim.IsBoss) ? 1 : 2);
                    next = victim.Center;
                    k.Place(next);
                    at = next;
                    break;
                }
                k.Place(next);
                at = next;
                travelled += step;
                yield return null;
            }
            if (travelled >= range) KunaiStat(clone, 4);
            if (k == null) yield break;
            if (keep) k.Stick(GroundSeconds, true);
            else k.Vanish();
        }

        /// <summary>[MEASURE] Kunai outcomes: [0..4] the body's throws (thrown, landed, refused by hit invulnerability or a
        /// guard, wall, flew out of range), [5..9] the clones' the same way. Read by DevCapture.Measure.</summary>
        internal static readonly int[] KunaiStats = new int[10];
        static void KunaiStat(NinjaClone clone, int outcome) => KunaiStats[(clone != null ? 5 : 0) + outcome]++;

        readonly List<Collider2D> probeHits = new List<Collider2D>();
        static readonly List<RaycastHit2D> lineHits = new List<RaycastHit2D>();

        /// <summary>
        /// [AUTO 2026-10-30] A kunai thrown from <paramref name="from"/> to <paramref name="to"/> would stop on a wall, cliff or
        /// prop (the same rule as <see cref="ProjectileBlocked"/>). 자동 사냥 steps closer instead of throwing into it: from
        /// 6 m away 45% of the ninja's kunai hit a wall in 바람칼 능선 (measure_20261030c).
        /// </summary>
        public static bool KunaiPathBlocked(Vector2 from, Vector2 to)
        {
            lineHits.Clear();
            Physics2D.Linecast(from, to, new ContactFilter2D { useTriggers = false }, lineHits);
            foreach (var h in lineHits)
            {
                var col = h.collider;
                if (col == null || col.attachedRigidbody != null || col.gameObject.name == "Water") continue;
                if (col.GetComponentInParent<IDamageable>() != null) continue;
                return true;
            }
            return false;
        }

        /// <summary>A thrown kunai runs into a wall, cliff or prop (not water, characters or what can be hit).</summary>
        bool ProjectileBlocked(Vector2 p)
        {
            probeHits.Clear();
            Physics2D.OverlapCircle(p, .08f, new ContactFilter2D { useTriggers = false }, probeHits);
            foreach (var col in probeHits)
            {
                if (col == null || col.attachedRigidbody != null || col.gameObject.name == "Water") continue;
                if (col.GetComponentInParent<IDamageable>() != null) continue;
                return true;
            }
            return false;
        }

        // ---------- Clones ----------

        int CloneDamage(int damage) => Mathf.Max(1, Mathf.RoundToInt(damage * cloneShare));

        /// <summary>Runs <paramref name="action"/> for every clone while 분신술 lasts.</summary>
        void ForClones(System.Action<NinjaClone> action)
        {
            if (!ClonesUp) return;
            foreach (var clone in clones.ToArray()) if (clone != null) action(clone);
        }

        /// <summary>
        /// 분신술: two clones beside me for 3 min (cooldown 2 min, so they are always up) that copy every attack of mine at
        /// 12% of the damage each. [2026-10-29 user "지속 3분, 쿨 2분, 무한이어야지. 대신 딜을 조정"] The time is fixed;
        /// the ranks raise the share (rank potency) and the specializations change the count, the total share staying the same.
        /// </summary>
        void Clones(Run c)
        {
            DismissClones();
            cloneShare = c.s.power / 100f * c.Scale * (c.n.spec == 'a' ? CloneOneShare : c.n.spec == 'b' ? CloneThreeShare : 1f);
            clonesEnd = Time.time + c.s.duration;
            cloneSwapUsed = false;
            // [ROGUE SPEC] 단일 분신 A: one clone at 24%. 분신 셋 B: three at 8% each.
            if (c.n.spec != 'a') clones.Add(NinjaClone.Spawn(owner, -1));
            clones.Add(NinjaClone.Spawn(owner, 1));
            if (c.n.spec == 'b') clones.Add(NinjaClone.Spawn(owner, -2));
            Sound("c_dash", .6f);
            if (owner.IsLocal) GameEvents.RaiseToast(L.T("분신술 · 한 번 더 누르면 분신과 자리 바꾸기"));
        }

        /// <summary>[NINJA] Runs a hit of a moving skill of the base kit (그림자 질주, the dagger 연쇄 도약 / 그림자 습격): the clones do not copy it.</summary>
        public void Uncopied(System.Action hit)
        {
            echoMuted++;
            try { hit(); }
            finally { echoMuted--; }
        }

        /// <summary>
        /// [NINJA] A direct hit of mine landed while the clones are out: each clone does the same blow on the same monster
        /// a moment later at its share (the base kit's stab, backstab, dance and the like). Career skills copy themselves (the
        /// throws) or are not copied (<see cref="NotEchoed"/>), and a clone's copy is never copied again.
        /// </summary>
        void CloneEcho(EnemyController enemy)
        {
            int raw = echoRaw;
            echoRaw = 0;
            if (echoing || echoMuted > 0 || raw <= 0 || !ClonesUp || Mine != Career.Ninja || enemy == null) return;
            if (!string.IsNullOrEmpty(currentSkill) && (NotEchoed.Contains(currentSkill) || CareerCatalog.Get(currentSkill)?.career == Career.Ninja)) return;
            int damage = CloneDamage(raw);
            int i = 0;
            foreach (var clone in clones.ToArray()) if (clone != null) StartCoroutine(EchoHit(clone, enemy, damage, EchoDelay + EchoStep * i++));
        }

        /// <summary>Replays base-skill clone hits on member PCs, where puppet enemies never run damage hooks.</summary>
        public void ReplayBaseCloneEcho(EnemyController enemy)
        {
            if (!PartyNet.IsMember || enemy == null || !enemy.Puppet || echoMuted > 0 || !ClonesUp || Mine != Career.Ninja) return;
            int i = 0;
            foreach (var clone in clones.ToArray())
                if (clone != null) StartCoroutine(EchoHit(clone, enemy, 0, EchoDelay + EchoStep * i++, visualOnly: true));
        }

        IEnumerator EchoHit(NinjaClone clone, EnemyController e, int damage, float delay, bool visualOnly = false)
        {
            yield return new WaitForSeconds(delay);
            if (clone == null || e == null || e.IsDead || owner == null || owner.IsDead || !ClonesUp) yield break;
            Vector2 dir = e.Center - clone.Center;
            dir = dir.sqrMagnitude > .0001f ? dir.normalized : owner.AimDirection;
            CareerFx.Line(clone.Center, e.Center, CloneTint, .04f, .08f);
            CareerFx.Clip("f_x", e.Center, dir, .8f, 30f, VfxLayer.Top, true, CloneTint);
            if (visualOnly) yield break;
            echoing = true;
            try { e.TakeDamage(new DamageInfo(damage, clone.Center, .01f, Team.Player, owner.gameObject) { noHitInvulnerability = true }); }
            finally { echoing = false; }
        }

        /// <summary>
        /// [ROGUE] A second press of a skill whose first press is still out: 쿠나이 순간이동 (assassin) moves me to the kunai,
        /// 분신술 (ninja) swaps me with the farther clone (once a cast). Returns true when the press was used this way (no MP, no cooldown).
        /// </summary>
        public bool TryFollowUp(string skillId)
        {
            if (owner == null || owner.IsDead) return false;
            if (skillId == "a_kunai" && Mine == Career.Assassin && portKunai != null) { PortToKunai(); return true; }
            if (Mine != Career.Ninja) return false;
            if (skillId == "n_clone" && ClonesUp)
            {
                if (cloneSwapUsed) return true; // once a cast: the press does nothing more
                cloneSwapUsed = true;
                StartCoroutine(SwapWithClone());
                return true;
            }
            return false;
        }

        IEnumerator SwapWithClone()
        {
            NinjaClone far = null;
            float best = -1f;
            foreach (var clone in clones)
            {
                if (clone == null) continue;
                float d = Vector2.Distance(clone.Position, owner.Position);
                // Equal distances (standing still): the clone on the side I aim at.
                if (d > best + .01f || (Mathf.Abs(d - best) <= .01f && far != null && Mathf.Sign(clone.Side) == Mathf.Sign(owner.AimDirection.x))) { best = d; far = clone; }
            }
            if (far == null) yield break;
            Vector2 mine = owner.Position, theirs = far.Position;
            NinjaClone.Puff(owner.Center);
            NinjaClone.Puff(far.Center);
            yield return new WaitForSeconds(.1f);
            if (owner == null || owner.IsDead || far == null) yield break;
            Vector2? spot = FreeSpotNear(theirs, PortSearch);
            if (spot == null) { if (owner.IsLocal) GameEvents.RaiseToast(L.T("이동할 자리가 없습니다.")); yield break; }
            if (!owner.NetPuppet) owner.SkillTeleportTo(spot.Value, spot.Value + owner.AimDirection);
            far.Snap(mine);
            foreach (var clone in clones) if (clone != null && clone != far) clone.SnapHome();
            Sound("c_dash", .7f);
        }

        /// <summary>
        /// The free standing spot nearest <paramref name="feet"/> within <paramref name="radius"/>: a walkable tile where the
        /// body overlaps no wall (GridPath.Walkable + a body-sized probe). Null when there is none.
        /// </summary>
        Vector2? FreeSpotNear(Vector2 feet, float radius)
        {
            if (BodyFits(feet)) return feet;
            for (float r = .25f; r <= radius + .001f; r += .25f)
            {
                int steps = Mathf.Max(8, Mathf.RoundToInt(r * 16f));
                Vector2? best = null;
                float bestScore = float.MaxValue;
                for (int i = 0; i < steps; i++)
                {
                    float a = i * Mathf.PI * 2f / steps;
                    Vector2 p = feet + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                    if (!BodyFits(p)) continue;
                    float score = (p - owner.Position).sqrMagnitude; // ties on the ring: the side toward me
                    if (score < bestScore) { bestScore = score; best = p; }
                }
                if (best != null) return best;
            }
            return null;
        }

        bool BodyFits(Vector2 feet)
        {
            if (!GridPath.Walkable(feet)) return false;
            probeHits.Clear();
            var filter = new ContactFilter2D { useTriggers = false };
            filter.SetLayerMask(Physics2D.GetLayerCollisionMask(owner.gameObject.layer));
            Physics2D.OverlapCircle(feet + new Vector2(0f, .12f), .2f, filter, probeHits);
            foreach (var col in probeHits)
            {
                if (col == null || col.isTrigger) continue;
                if (col.GetComponentInParent<PlayerController>() != null || col.GetComponentInParent<EnemyController>() != null
                    || col.GetComponentInParent<NpcController>() != null) continue;
                return false;
            }
            return true;
        }

        // ---------- 연막 도약 ----------

        /// <summary>
        /// [NINJA] 연막 도약 (moved from the assassin 2026-10-23): jump 4 m back while a smoke bomb flies to the pack in front
        /// (the nearest monster in 6 m, else 4 m ahead); monsters in its smoke are hit and can't attack for 2 s.
        /// [2026-10-23] User: the ninja fights at range, so the smoke goes on the monsters, not where I stood.
        /// </summary>
        IEnumerator SmokeLeap(Run c)
        {
            if (c.n.spec == 'a') { yield return SmokeThrough(c); yield break; } // [ROGUE SPEC] 연막 관통탄
            int smokeDamage = c.n.spec == 'b' ? Mathf.Max(1, Mathf.RoundToInt(c.n.damage * VeilDoubleShare)) : c.n.damage;
            var target = Target(c.dir, VeilThrowRange, out _);
            Vector2 from = owner.Center;
            Vector2 spot = target != null ? target.Center : from + c.dir * owner.SkillClearance(c.dir, Mathf.Min(4f, VeilThrowRange));
            StartCoroutine(SmokeBomb(c, from, spot, smokeDamage));
            Sound("c_dash", .8f);
            CareerFx.Ghost(owner, CloneTint, .35f);
            float distance = Dash(-c.dir, c.n.range);
            float seconds = DashSeconds(distance);
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                if (!Live(c)) yield break;
                yield return null;
            }
            if (c.n.spec == 'b') SmokeArrival(c); // [ROGUE SPEC] 이중 연막
            if (ClonesUp) CareerTrials.Record(owner, "veil", 1); // the ninja trial: smoke leaps with the clones out
        }

        /// <summary>연막 도약's bomb: a kunai with a smoke tag flies to <paramref name="spot"/>, then the smoke bursts there.</summary>
        IEnumerator SmokeBomb(Run c, Vector2 from, Vector2 spot, int damage)
        {
            Vector2 dir = spot - from;
            float length = dir.magnitude;
            dir = length > .0001f ? dir / length : c.dir;
            var k = KunaiProjectile.Spawn(owner, from, dir, ThrowsStars, SpecTint(c));
            for (float t = 0f; t < length; t += VeilBombSpeed * Time.deltaTime)
            {
                if (!Live(c) || k == null) { if (k != null) k.Vanish(); yield break; }
                k.Place(from + dir * t);
                yield return null;
            }
            if (k != null) k.Vanish();
            if (!Live(c)) yield break;
            CareerFx.Gen("a_smoke", null, spot, Vector2.zero, c.n.radius * 2.3f, 0f, 14f, VfxLayer.Top, false, SmokeGray); // [VFX 2026-10-29] was x2
            SkillFx.Spawn("fx_ring", spot - new Vector2(0f, .45f), new Color(.6f, .6f, .62f, .6f), .5f, SkillFx.GroundOrder + 14)
                .Scale(new Vector2(c.n.radius * .3f, c.n.radius * .2f), new Vector2(c.n.radius, c.n.radius * .7f)).Fade(FxFade.Late);
            ImpactSound("c_heavy", .5f);
            foreach (var e in Enemies(spot, c.n.radius))
                if (Strike(c, e, damage, spot, 3f, 1, "hit")) Stun(c, e, c.s.duration);
            StartCoroutine(SmokeLinger(spot, c.n.radius, c.s.duration)); // [2026-10-29] the cloud stays as long as the enemies can't attack (user: smoke too short)
        }

        /// <summary>연막 도약: keeps the smoke cloud on the spot for the whole no-attack time (the skill itself ends at once).</summary>
        IEnumerator SmokeLinger(Vector2 spot, float radius, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end - .3f)
            {
                yield return new WaitForSeconds(.45f);
                if (!this) yield break;
                CareerFx.Gen("a_smoke", null, spot, Vector2.zero, radius * 2.3f, 0f, 14f, VfxLayer.Top, false, SmokeGray); // [VFX 2026-10-29] was x2
            }
        }

        // ---------- Casts ----------

        IEnumerator Ninja(Run c)
        {
            switch (c.s.effect)
            {
                case "veil": yield return SmokeLeap(c); break;
                case "clone": Clones(c); break;
                case "recall": yield return Recall(c); break;
                case "volley": yield return Volley(c); break;
                case "fuma": yield return Fuma(c); break;
                case "bomb": yield return Bomb(c); break;
                case "starstorm": yield return StarStorm(c); break; // [2026-10-23] 표창 난사, was 분신 난무
            }
        }

        /// <summary>연발 쿠나이 (main hunter): two kunai 0.17 s apart along 8 m, each goes through two monsters and sticks
        /// in the third. [BALANCE 2026-10-30] Was single target (ninja hunted at 6 kills/min against 11-23 for the other
        /// careers); the pierce adds pack damage only, a lone boss is hit once per kunai as before. 일직선 연발 (A) still sticks.</summary>
        const int VolleyPierce = 2;
        IEnumerator Volley(Run c)
        {
            if (c.n.spec == 'b') { VolleyFan(c); yield break; } // [ROGUE SPEC] 부채 연발
            int throws = c.n.spec == 'a' && c.n.projectiles > 0 ? c.n.projectiles : c.s.hits; // [ROGUE SPEC] 일직선 연발
            for (int i = 0; i < throws; i++)
            {
                if (!Live(c)) yield break;
                Vector2 dir = CareerFx.Tilt(c.dir, i == 0 ? 0f : 3f);
                Pose(.1f, i % 2);
                Sound("swing", .5f);
                int pierce = c.n.spec == 'a' ? 0 : VolleyPierce;
                StartCoroutine(Throw(c, owner.Center + dir * .3f, dir, c.n.range, VolleySpeed, c.n.damage, true, null, pierce));
                ForClones(clone => StartCoroutine(Throw(c, clone.Center + dir * .3f, dir, c.n.range, VolleySpeed, CloneDamage(c.n.damage), false, clone, pierce)));
                if (i < throws - 1) yield return new WaitForSeconds(CutGap);
            }
        }

        /// <summary>
        /// 표창 난사 (awakening, 2026-10-23 user: "닌자는 표창 와다다다 던지는 걸로 ... 쳐다보는 방향으로"): 24 stars in 3 s
        /// (8 a second) along the way I look, each through every monster on its 9 m line. Like every awakening it runs on
        /// while I move, so turning turns the stream. One cast on a boss = 0.667 x 24 = 16.0, the old 분신 난무's total.
        /// Stars come faster than the hit invulnerability, so they all land.
        /// </summary>
        IEnumerator StarStorm(Run c)
        {
            // [ROGUE SPEC] 표창 폭풍 A: 12 stars in 1 s. 표창 비 B: 36 in 9 s. Same total: each star carries 24 / count of the base one.
            int throws = Mathf.Max(1, c.n.spec != '\0' && c.n.hits > 0 ? c.n.hits : c.s.hits);
            float stormFor = c.n.spec != '\0' && !float.IsNaN(c.n.duration) ? c.n.duration : c.s.duration;
            float gap = stormFor / throws;
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * c.s.hits / (float)throws));
            float range = c.n.range > 0f ? c.n.range : c.s.range;
            // [VFX 2026-10-29] A bright burst on the cast so the storm reads at once.
            SkillVisuals.Flash(owner.Center, new Color(1f, 1f, 1f, .6f), 3f, .22f);
            SkillFx.Spawn("fx_ring", owner.Center, new Color(.9f, .95f, 1f, .9f), .35f, SkillFx.TopOrder + 3).Additive().Scale(.4f, 3.2f).Fade(FxFade.Quick);
            for (int i = 0; i < throws; i++)
            {
                if (!Live(c)) yield break;
                Vector2 dir = CareerFx.Tilt(Aim(), Random.Range(-StarSpread, StarSpread));
                if (i % 3 == 0) Pose(.12f, (i / 3) % 2);
                Sound("swing", .35f);
                StartCoroutine(StarFlight(c, owner.Center + dir * .3f, dir, range, damage));
                yield return new WaitForSeconds(gap);
            }
        }

        /// <summary>One star of 표창 난사: straight on, through every monster on the way (each once), stopped by walls.</summary>
        IEnumerator StarFlight(Run c, Vector2 from, Vector2 dir, float range, int damage)
        {
            var k = KunaiProjectile.Spawn(owner, from, dir, true, SpecTint(c), 1.7f); // [VFX 2026-10-29] big stars (hits unchanged: the line check)
            var hit = new HashSet<EnemyController>();
            Vector2 at = from;
            float travelled = 0f;
            while (travelled < range)
            {
                if (!Live(c) || k == null) break;
                float step = Mathf.Min(StarSpeed * Time.deltaTime, range - travelled);
                Vector2 next = at + dir * step;
                if (ProjectileBlocked(next)) break;
                foreach (var e in Corridor(at, next, c.n.radius)) if (hit.Add(e)) StrikeRapid(c, e, damage, at);
                k.Place(next);
                at = next;
                travelled += step;
                yield return null;
            }
            if (k != null) k.Vanish();
        }
    }
}
