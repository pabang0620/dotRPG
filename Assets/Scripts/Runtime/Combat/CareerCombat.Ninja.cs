using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// 닌자 (rogue career, Docs/PLAN_CLASS_ROGUE.md 2.4): thrown kunai at range. The ninja's basic attack is always a kunai
    /// throw (투척술 only makes it stronger); 연막 도약 is the escape; 분신술 (user skill) puts two clones beside me that copy
    /// every attack of mine (basic attack and attack skills, same direction) at 60% of my damage. Clone damage is my own damage. 쿠나이 순간이동 moved to the assassin (2026-10-23) but its anchor state
    /// and the free-spot search stay here, next to the clone swap that shares them.
    /// </summary>
    public sealed partial class CareerCombat
    {
        // ---------- Constants (plan 2.4) ----------

        /// <summary>원거리 집중: monsters this far or farther take the bonus.</summary>
        const float FarDistance = 6f;
        /// <summary>투척술: a throw every 0.3 s, 6 m, the kunai stays 4 s where it stops.</summary>
        const float ThrowInterval = .3f, ThrowRange = 6f, ThrowSpeed = 18f, GroundSeconds = 4f, KunaiHalfWidth = .3f;
        /// <summary>쿠나이 순간이동: 20 m/s (10 m in 0.5 s); a kunai stuck in a wall sends me to the nearest free spot within 1.5 m.</summary>
        const float PortSpeed = 20f, PortSearch = 1.5f;
        /// <summary>연발 쿠나이 / 회수 / 풍마 수리검 flight speeds.</summary>
        const float VolleySpeed = 20f, RecallSpeed = 24f, FumaSpeed = 9f, FumaHover = .35f;
        /// <summary>회수: at most 12 kunai, each gives back 1% of max MP.</summary>
        const int RecallMax = 12;
        const float RecallMpShare = .01f;
        /// <summary>기폭 쿠나이: 5 kunai on a 2.5 m circle; one goes off when a monster comes within 1.2 m, all after 2 s, one by one.</summary>
        const float BombRing = 2.5f, BombTrigger = 1.2f, BombFuse = 2f, BombChain = .12f, BombRoot = 1f, BombRootBoss = .3f;
        /// <summary>분신 난무: the closing burst on each monster hit is 280% (the 55% throws x 5.09).</summary>
        const float CloneBurst = 2.8f / .55f;
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
        /// The base kit's moving skills (연쇄 도약, 그림자 습격, 그림자 질주) are muted by SkillCaster (<see cref="Uncopied"/>).
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
        /// <summary>The amount of the direct hit being dealt, before my multipliers (ModifyDamage): a clone copies 60% of it.</summary>
        int echoRaw;
        /// <summary>A clone's copy is landing (it is not copied again) / a moving skill's hit is landing (not copied).</summary>
        bool echoing;
        int echoMuted;

        public float ClonesLeft => clones.Count > 0 ? Mathf.Max(0f, clonesEnd - Time.time) : 0f;
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
        bool ThrowsStars => EquipmentDatabase.TierOf(owner.Data.Equipment[EquipSlot.Weapon]) >= 3;

        Run QuickRun(CareerSkill s, SkillNumbers n, Vector2 dir) =>
            new Run { s = s, n = n, dir = dir, map = Game.Session.MapId, version = castVersion, authority = !PartyNet.IsMember };

        // ---------- 투척술: the basic attack ----------

        /// <summary>True while 투척술 replaces the basic attack (promoted ninja with the node learned).</summary>
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
        /// </summary>
        IEnumerator Throw(Run c, Vector2 from, Vector2 dir, float range, float speed, int damage, bool keep, NinjaClone clone, int pierce = 0)
        {
            var k = KunaiProjectile.Spawn(owner, from, dir, ThrowsStars, clone != null ? CloneTint : SpecTint(c));
            Vector2 at = from;
            float travelled = 0f;
            HashSet<EnemyController> passed = pierce > 0 ? new HashSet<EnemyController>() : null; // [ROGUE SPEC] piercing throws
            while (travelled < range)
            {
                if (!Live(c) || k == null) { if (k != null) k.Vanish(); yield break; }
                float step = Mathf.Min(speed * Time.deltaTime, range - travelled);
                Vector2 next = at + dir * step;
                if (ProjectileBlocked(next)) break;
                EnemyController victim = null;
                foreach (var e in Corridor(at, next, KunaiHalfWidth)) { if (passed != null && passed.Contains(e)) continue; victim = e; break; }
                if (victim != null && passed != null && passed.Count < pierce)
                {
                    passed.Add(victim);
                    Strike(c, victim, damage, at, 1.5f, 0, "hit");
                    victim = null;
                }
                if (victim != null)
                {
                    Strike(c, victim, damage, at, 2f, 0, "hit");
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
            if (k == null) yield break;
            if (keep) k.Stick(GroundSeconds, true);
            else k.Vanish();
        }

        readonly List<Collider2D> probeHits = new List<Collider2D>();

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
        /// 분신술: two clones beside me for 8 s (longer with ranks) that copy every attack of mine at 60% of the damage
        /// (user, fixed: the ranks and the specializations change the time and the count, never the share).
        /// </summary>
        void Clones(Run c)
        {
            DismissClones();
            cloneShare = c.s.power / 100f;
            clonesEnd = Time.time + c.s.duration * c.Scale * (c.n.spec == 'a' ? CloneOneTime : c.n.spec == 'b' ? CloneThreeTime : 1f);
            cloneSwapUsed = false;
            // [ROGUE SPEC] 단일 분신 A: one clone for twice as long. 분신 셋 B: three for two thirds of the time.
            if (c.n.spec != 'a') clones.Add(NinjaClone.Spawn(owner, -1));
            clones.Add(NinjaClone.Spawn(owner, 1));
            if (c.n.spec == 'b') clones.Add(NinjaClone.Spawn(owner, -2));
            Sound("c_dash", .6f);
            if (owner.IsLocal) GameEvents.RaiseToast(L.T("분신술 · 한 번 더 누르면 분신과 자리 바꾸기"));
        }

        /// <summary>[NINJA] Runs a hit of a moving skill of the base kit (연쇄 도약, 그림자 습격, 그림자 질주): the clones do not copy it.</summary>
        public void Uncopied(System.Action hit)
        {
            echoMuted++;
            try { hit(); }
            finally { echoMuted--; }
        }

        /// <summary>
        /// [NINJA] A direct hit of mine landed while the clones are out: each clone does the same blow on the same monster
        /// a moment later at 60% (the base kit's stab, backstab, dance and the like). Career skills copy themselves (the
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

        IEnumerator EchoHit(NinjaClone clone, EnemyController e, int damage, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (clone == null || e == null || e.IsDead || owner == null || owner.IsDead || !ClonesUp) yield break;
            Vector2 dir = e.Center - clone.Center;
            dir = dir.sqrMagnitude > .0001f ? dir.normalized : owner.AimDirection;
            CareerFx.Line(clone.Center, e.Center, CloneTint, .04f, .08f);
            CareerFx.Clip("f_x", e.Center, dir, .8f, 30f, VfxLayer.Top, true, CloneTint);
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
            CareerFx.Gen("a_smoke", null, spot, Vector2.zero, c.n.radius * 2f, 0f, 14f, VfxLayer.Top, false, SmokeGray);
            SkillFx.Spawn("fx_ring", spot - new Vector2(0f, .45f), new Color(.6f, .6f, .62f, .6f), .5f, SkillFx.GroundOrder + 14)
                .Scale(new Vector2(c.n.radius * .3f, c.n.radius * .2f), new Vector2(c.n.radius, c.n.radius * .7f)).Fade(FxFade.Late);
            ImpactSound("c_heavy", .5f);
            foreach (var e in Enemies(spot, c.n.radius))
                if (Strike(c, e, damage, spot, 3f, 1, "hit")) Stun(c, e, c.s.duration);
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
                case "cloneburst": yield return CloneStorm(c); break;
            }
        }

        /// <summary>연발 쿠나이 (main hunter): two kunai 0.17 s apart along 8 m, each sticks in the first monster.</summary>
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
                StartCoroutine(Throw(c, owner.Center + dir * .3f, dir, c.n.range, VolleySpeed, c.n.damage, true, null));
                ForClones(clone => StartCoroutine(Throw(c, clone.Center + dir * .3f, dir, c.n.range, VolleySpeed, CloneDamage(c.n.damage), false, clone)));
                if (i < throws - 1) yield return new WaitForSeconds(CutGap);
            }
        }

        /// <summary>회수: every kunai of mine left within 8 m (up to 12) flies back to my hand, cutting what it passes; 1% MP each.</summary>
        IEnumerator Recall(Run c)
        {
            var mine = new List<KunaiProjectile>();
            foreach (var k in KunaiProjectile.Grounded)
                if (k != null && k.Owner == owner && Vector2.Distance(k.Position, owner.Center) <= c.n.range) mine.Add(k);
            mine.Sort((a, b) => (a.Position - owner.Center).sqrMagnitude.CompareTo((b.Position - owner.Center).sqrMagnitude));
            if (mine.Count > RecallMax) mine.RemoveRange(RecallMax, mine.Count - RecallMax);
            Pose(.2f, 1);
            Sound("c_dash", .6f);
            if (mine.Count == 0) { if (owner.IsLocal) GameEvents.RaiseToast(L.T("회수할 쿠나이가 없습니다.")); yield break; }
            EnemyController mark = c.n.spec == 'a' ? Target(c.dir, c.n.range, out _) : null; // [ROGUE SPEC] 표적 회수
            foreach (var k in mine) StartCoroutine(RecallOne(c, k, mark));
            yield return null;
        }

        IEnumerator RecallOne(Run c, KunaiProjectile k, EnemyController mark = null)
        {
            var hit = new HashSet<EnemyController>();
            Vector2 at = k.Position;
            float width = c.n.spec == 'b' && c.n.radius > 0f ? RecallWideHalf : .5f; // [ROGUE SPEC] 회오리 회수
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * (c.n.spec == 'a' ? RecallTargetMul : c.n.spec == 'b' ? RecallWideMul : 1f)));
            if (mark != null) { yield return RecallVia(c, k, mark, damage); at = k != null ? k.Position : at; hit.Add(mark); }
            k.Lift(owner.Center - at);
            while (k != null)
            {
                if (!Live(c)) { k.Vanish(); yield break; }
                Vector2 hand = owner.Center;
                Vector2 to = hand - at;
                float step = RecallSpeed * Time.deltaTime;
                Vector2 next = to.magnitude <= step ? hand : at + to.normalized * step;
                if (mark == null) foreach (var e in Corridor(at, next, width)) if (hit.Add(e)) Strike(c, e, damage, at, 1.5f, 0, "c_slash");
                k.Aim(to);
                k.Place(next);
                at = next;
                if (next == hand) break;
                yield return null;
            }
            if (k != null) k.Vanish();
            if (c.authority && owner != null && !owner.IsDead)
                owner.Data.Mana = Mathf.Min(owner.MaxMana, owner.Data.Mana + owner.MaxMana * RecallMpShare);
        }

        /// <summary>풍마 수리검: a big star out 7 m and back to me, piercing; the same monster is cut every 0.17 s, 6 times at most.</summary>
        IEnumerator Fuma(Run c)
        {
            Pose(.2f, 2);
            Sound("c_slash", .8f);
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * (c.n.spec == 'a' ? FumaBigMul : c.n.spec == 'b' ? FumaHoverMul : 1f))); // [ROGUE SPEC]
            StartCoroutine(FumaFlight(c, null, damage));
            ForClones(clone => StartCoroutine(FumaFlight(c, clone, CloneDamage(damage))));
            yield return null;
        }

        IEnumerator FumaFlight(Run c, NinjaClone clone, int damage)
        {
            Vector2 Origin() => clone != null ? clone.Center : owner.Center;
            Vector2 at = Origin() + c.dir * .4f;
            float size = c.n.radius * 2.2f;
            var star = CareerFx.Gen("n_fuma", "f_wave", at, Vector2.zero, size, size, 20f, VfxLayer.Top, false,
                clone != null ? CloneTint : SpecTint(c), 30f, true);
            float hoverFor = c.n.spec == 'b' && !float.IsNaN(c.n.duration) ? c.n.duration : FumaHover; // [ROGUE SPEC] 회전 풍마
            var count = new Dictionary<EnemyController, int>();
            var next = new Dictionary<EnemyController, float>();
            float travelled = 0f, hover = 0f;
            bool back = false;
            int hits = Mathf.Max(1, c.n.spec != '\0' && c.n.hits > 0 ? c.n.hits : c.s.hits);
            for (float guard = 0f; guard < 4f + hoverFor; guard += Time.deltaTime)
            {
                if (!Live(c)) break;
                Vector2 pos;
                if (!back)
                {
                    travelled += FumaSpeed * Time.deltaTime;
                    if (travelled >= c.n.range) { travelled = c.n.range; hover += Time.deltaTime; if (hover >= hoverFor) back = true; }
                    pos = Origin() + c.dir * (.4f + travelled);
                    if (ProjectileBlocked(pos)) back = true;
                }
                else
                {
                    Vector2 to = Origin() - at;
                    float step = FumaSpeed * 1.2f * Time.deltaTime;
                    if (to.magnitude <= step + .2f) break;
                    pos = at + to.normalized * step;
                }
                at = pos;
                star?.Place(at);
                foreach (var e in Enemies(at, c.n.radius))
                {
                    count.TryGetValue(e, out int n);
                    if (n >= hits || (next.TryGetValue(e, out float t) && Time.time < t)) continue;
                    if (Strike(c, e, damage, at, 1.5f, 0, "c_slash") || !c.authority) { count[e] = n + 1; next[e] = Time.time + CutGap; }
                }
                yield return null;
            }
            star?.Stop();
        }

        /// <summary>기폭 쿠나이: five tagged kunai around the target spot; each blows when a monster comes close, all after 2 s.</summary>
        IEnumerator Bomb(Run c)
        {
            Target(c.dir, c.n.range, out Vector2 point);
            Pose(.2f, 2);
            Sound("swing", .8f);
            int count = Mathf.Max(1, c.n.spec != '\0' && c.n.projectiles > 0 ? c.n.projectiles : c.s.hits); // [ROGUE SPEC] 대형 기폭 1 / 기폭 지뢰밭 8
            float ring = count == 1 ? 0f : c.n.spec == 'b' ? BombFieldRing : BombRing;
            var tags = new List<(Vector2 at, VfxPlayer view)>();
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count + Mathf.PI * .5f;
                Vector2 spot = point + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * ring;
                var view = CareerFx.Gen("n_tag", "f_spark", spot, Vector2.zero, .7f, .6f, 10f, VfxLayer.AtFeet, false, null, BombFuse + 1.5f, true);
                tags.Add((spot, view));
            }
            float fuse = Time.time + BombFuse;
            var left = new List<int>();
            for (int i = 0; i < tags.Count; i++) left.Add(i);
            while (left.Count > 0)
            {
                if (!Live(c)) { foreach (int i in left) tags[i].view?.Stop(); yield break; }
                for (int j = left.Count - 1; j >= 0; j--)
                {
                    int i = left[j];
                    if (Enemies(tags[i].at, BombTrigger).Count == 0) continue;
                    BombBlast(c, tags[i].at);
                    tags[i].view?.Stop();
                    left.RemoveAt(j);
                }
                if (Time.time >= fuse && left.Count > 0)
                {
                    int i = left[0];
                    BombBlast(c, tags[i].at);
                    tags[i].view?.Stop();
                    left.RemoveAt(0);
                    yield return new WaitForSeconds(BombChain);
                    continue;
                }
                yield return null;
            }
        }

        void BombBlast(Run c, Vector2 at)
        {
            CareerFx.Gen("m2_fire_explosion", "impact", at, Vector2.zero, c.n.radius * 2f, c.n.radius / 1.7f, 26f, VfxLayer.Top, false, Tint(c, NinjaOrange));
            ImpactSound("c_heavy", .7f);
            int damage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * (c.n.spec == 'a' ? BombOneMul : c.n.spec == 'b' ? BombFieldMul : 1f))); // [ROGUE SPEC]
            float root = c.n.spec == 'a' && !float.IsNaN(c.n.duration) ? c.n.duration : BombRoot;
            foreach (var e in Enemies(at, c.n.radius))
            {
                if (!Strike(c, e, damage, at, 3f, 1, "c_heavy") || !c.authority || e.IsDead) continue;
                e.Root(e.IsBoss ? root * BombRootBoss / BombRoot : root);
                CareerTrials.Record(owner, "rooted", 1);
            }
        }

        /// <summary>
        /// 분신 난무 (awakening): six clones stand around me (8 m) and throw kunai for 3 s; at the end every kunai that
        /// stuck blows at once (280% on each monster hit). Throws come faster than the hit invulnerability.
        /// </summary>
        IEnumerator CloneStorm(Run c)
        {
            Vector2 center = owner.Center;
            float ring = c.n.radius * .75f;
            var spots = new Vector2[6];
            for (int i = 0; i < spots.Length; i++)
            {
                float a = i * Mathf.PI / 3f;
                spots[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .7f) * ring;
                NinjaClone.Puff(spots[i]);
                ShadowAt(spots[i], CloneTint, (c.n.spec != '\0' && !float.IsNaN(c.n.duration) ? c.n.duration : c.s.duration) + .2f, spots[i].x > center.x);
            }
            // [ROGUE SPEC] 분신 일제 투척 A: 6 throws in 1 s. 분신 진 B: 36 throws over 9 s.
            int throws = Mathf.Max(1, c.n.spec != '\0' && c.n.hits > 0 ? c.n.hits : c.s.hits);
            float stormFor = c.n.spec != '\0' && !float.IsNaN(c.n.duration) ? c.n.duration : c.s.duration;
            float gap = stormFor / throws;
            int throwDamage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * (c.n.spec == 'a' ? StormVolleyMul : c.n.spec == 'b' ? StormLongMul : 1f)));
            var stuck = new List<KunaiProjectile>();
            var struck = new HashSet<EnemyController>();
            for (int i = 0; i < throws; i++)
            {
                if (!Live(c)) break;
                var foes = Enemies(center, c.n.radius);
                Vector2 from = spots[i % spots.Length];
                EnemyController foe = foes.Count > 0 ? foes[i % foes.Count] : null;
                Vector2 to = foe != null ? foe.Center : center + Random.insideUnitCircle * ring * .6f;
                Vector2 dir = (to - from).sqrMagnitude > .0001f ? (to - from).normalized : Vector2.right;
                var k = KunaiProjectile.Spawn(owner, to, dir, ThrowsStars);
                k.Stick(stormFor + 1f, false);
                stuck.Add(k);
                CareerFx.Line(from, to, new Color(.8f, .82f, .86f, .6f), .06f, .1f);
                Sound("swing", .35f);
                if (foe != null && StrikeRapid(c, foe, throwDamage, from)) struck.Add(foe);
                yield return new WaitForSeconds(gap);
            }
            foreach (var k in stuck)
            {
                if (k == null) continue;
                CareerFx.Gen("m2_fire_explosion", "impact", k.Position, Vector2.zero, 1.4f, .8f, 26f, VfxLayer.Top, false, Tint(c, NinjaOrange));
                k.Vanish();
            }
            if (!Live(c)) yield break;
            Sound("c_heavy");
            Feel(2, Vector2.up);
            float burst = CloneBurst * (c.n.spec == 'a' ? StormVolleyBurst : c.n.spec == 'b' ? StormLongBurst : 1f);
            foreach (var e in struck) if (e != null && !e.IsDead) StrikeRapid(c, e, Mathf.RoundToInt(c.n.damage * burst), center, 2);
            SkillVisuals.UltFinish(owner.Center, Tint(c, CareerFx.KunaiSteel), c.n.radius);
        }
    }
}
