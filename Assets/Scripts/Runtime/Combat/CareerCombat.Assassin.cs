using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// 어쌔신 (rogue career, Docs/PLAN_CLASS_ROGUE.md 2.3): close single-target burst. Marks pile up on the monster I keep
    /// hitting (급소 간파) and are cashed in by 급소 파열 / 암살; blows from behind hit harder (기습); 은신 hides from monsters.
    /// The rogue-wide states (marks, venom, stealth) and the damage hooks the core file calls live here too.
    /// </summary>
    public sealed partial class CareerCombat
    {
        // ---------- Constants (plan 2.3) ----------

        /// <summary>급소 간파: at most 5 marks per monster, each lasts 4 s from the last hit.</summary>
        public const int MaxMarks = 5;
        const float MarkSeconds = 4f;
        /// <summary>급소 파열: each mark adds 0.24 of the 1.2 raw base (0.35 of 1.73 after S()).</summary>
        const float RupturePerMark = .24f / 1.2f;
        const float AssassinateBehind = 1.5f, AssassinateMarks = 1.8f;
        /// <summary>맹독 도포: the poison of a hit is spread over 3 ticks, 1 s apart (damage over time, through armour).</summary>
        const int VenomTicks = 3;
        /// <summary>은신: +30% move speed while hidden.</summary>
        const float StealthSpeed = 1.3f;
        /// <summary>환영 삼격: the outer phantoms run this many degrees off the aim; speed, push and afterimage spacing.</summary>
        const float PhantomSpread = 25f, PhantomSpeed = 14f, PhantomPush = 8f, PhantomGhostGap = .05f;

        /// <summary>[ROGUE 2026-10-23] Plain colours (user: no premium violet): dagger steel cuts, grey shadows and smoke.</summary>
        public static readonly Color AssassinPink = CareerFx.DaggerSteel;
        static readonly Color AssassinShade = new Color(.42f, .43f, .46f, .55f);
        static readonly Color SmokeGray = new Color(.7f, .7f, .72f, .9f);
        static readonly Color VenomGreen = new Color(.5f, .62f, .38f, 1f);
        /// <summary>Dims the bright green poison picture (a_venom) toward a murky green.</summary>
        static readonly Color VenomTint = new Color(.78f, .82f, .7f, 1f);

        // ---------- State ----------

        readonly Dictionary<EnemyController, int> marks = new Dictionary<EnemyController, int>();
        readonly Dictionary<EnemyController, float> marksUntil = new Dictionary<EnemyController, float>();
        readonly Dictionary<EnemyController, SpriteRenderer> markViews = new Dictionary<EnemyController, SpriteRenderer>();
        float venomEnd, stealthEnd;
        int venomPct, stealthPct;
        int stealthHitsLeft = 1, venomSpread; // [ROGUE SPEC]
        bool venomBossOnly; // [ROGUE SPEC]
        /// <summary>The amount and "from behind" of the direct hit being dealt (ModifyDamage), used when it lands (OnLanded).</summary>
        int pendingAmount;
        bool pendingBehind;
        SpriteRenderer[] stealthRenderers;
        float[] stealthAlpha; // each renderer's own alpha when 은신 began (restored after)

        /// <summary>[ROGUE] Players hidden by 은신: ordinary monsters do not pick them (EnemyController.Target).</summary>
        static readonly HashSet<PlayerController> hidden = new HashSet<PlayerController>();
        public static bool HiddenFromMonsters(PlayerController p) => p != null && hidden.Contains(p);

        public float VenomLeft => Mathf.Max(0f, venomEnd - Time.time);
        public int VenomPct => VenomLeft > 0f ? venomPct : 0;
        public float StealthLeft => Mathf.Max(0f, stealthEnd - Time.time);
        public bool Stealthed => StealthLeft > 0f;

        public int Marks(EnemyController e)
        {
            if (e == null || !marks.TryGetValue(e, out int n)) return 0;
            return marksUntil.TryGetValue(e, out float until) && Time.time < until ? n : 0;
        }

        void ClearMarks(EnemyController e)
        {
            if (e == null) return;
            marks.Remove(e);
            marksUntil.Remove(e);
            if (markViews.TryGetValue(e, out var view)) { if (view != null) Destroy(view.gameObject); markViews.Remove(e); }
        }

        void RogueReset()
        {
            foreach (var v in markViews.Values) if (v != null) Destroy(v.gameObject);
            markViews.Clear();
            marks.Clear();
            marksUntil.Clear();
            venomEnd = 0; venomPct = 0; venomBossOnly = false; venomSpread = 0;
            EndStealth();
            EndForm();
            RogueSpecReset();
            NinjaReset();
        }

        // ---------- Damage hooks (called from the core file) ----------

        /// <summary>Multiplier of a direct hit of mine: marks, 기습 (behind), 은신's first blow, 원거리 집중.</summary>
        float RogueDamageMul(EnemyController enemy, int amount)
        {
            float mult = 1f;
            bool behind = enemy.IsBehind(owner.Position);
            if (Stealthed)
            {
                mult *= 1f + stealthPct / 100f;
                behind = true; // 은신 중 첫 공격은 항상 등 뒤 판정
                if (--stealthHitsLeft <= 0) EndStealth(); // attacking breaks it ([ROGUE SPEC] 연막 은신 B: after 3 blows)
            }
            int vital = Prog.Rank("a_vital");
            if (vital > 0) mult *= 1f + Marks(enemy) * VitalPct(vital) / 100f;
            mult *= AmbushMul(enemy, behind); // [ROGUE SPEC] 기습 A/B
            mult *= FarMul(enemy); // [ROGUE SPEC] 원거리 집중 A/B
            pendingBehind = behind;
            pendingAmount = Mathf.RoundToInt(amount * mult);
            return mult;
        }

        /// <summary>A direct hit of mine landed: add a mark, leave poison, count the trial.</summary>
        void RogueLanded(EnemyController enemy)
        {
            if (Prog.Rank("a_vital") > 0 && !enemy.IsDead) AddMark(enemy, 1);
            if (Prog.Rank("a_vital") > 0 && enemy.IsDead) PassMarks(enemy); // [ROGUE SPEC] 급소 전이 B
            if (VenomPct > 0 && pendingAmount > 0 && !enemy.IsDead && (!venomBossOnly || enemy.IsBoss))
            {
                int poison = Mathf.Max(1, Mathf.RoundToInt(pendingAmount * venomPct / 100f));
                StartCoroutine(Poison(enemy, poison));
                if (venomSpread > 0) SpreadPoison(enemy, poison); // [ROGUE SPEC] 맹독 전염 B
            }
            RogueSpecLanded(enemy);
            if (Mine == Career.Assassin) CareerTrials.Record(owner, pendingBehind ? "behind" : "front", 1);
            pendingAmount = 0;
        }

        IEnumerator Poison(EnemyController e, int total)
        {
            int left = total;
            for (int i = 0; i < VenomTicks; i++)
            {
                yield return new WaitForSeconds(1f);
                if (e == null || e.IsDead || owner == null || owner.IsDead) yield break;
                int tick = i == VenomTicks - 1 ? left : Mathf.Max(1, total / VenomTicks);
                left -= tick;
                if (tick <= 0) yield break;
                SecondaryHit(e, tick, true);
                CareerFx.Gen("a_venom", null, e.Center + Vector2.up * .2f, Vector2.zero, .55f, 0f, 14f, VfxLayer.Top, false, VenomTint);
            }
        }

        void RogueTick()
        {
            if (stealthEnd > 0f && Time.time >= stealthEnd) EndStealth();
            MarkViewsTick();
            FormTick();
            NinjaTick();
        }

        void LateUpdate()
        {
            // 은신: the body (and what it holds) is drawn faint while hidden.
            if (!Stealthed || stealthRenderers == null) return;
            for (int i = 0; i < stealthRenderers.Length; i++)
            {
                var r = stealthRenderers[i];
                if (r != null) { var col = r.color; col.a = Mathf.Min(col.a, stealthAlpha[i] * .35f); r.color = col; }
            }
        }

        void EndStealth()
        {
            if (stealthRenderers != null)
                for (int i = 0; i < stealthRenderers.Length; i++)
                {
                    var r = stealthRenderers[i];
                    if (r != null) { var col = r.color; col.a = stealthAlpha[i]; r.color = col; }
                }
            stealthRenderers = null;
            stealthAlpha = null;
            stealthEnd = 0;
            if (owner != null) hidden.Remove(owner);
        }

        void OnDisable() { if (owner != null) hidden.Remove(owner); }

        /// <summary>The 1~5 mark picture (a_mark frame by count) over each marked monster.</summary>
        void MarkViewsTick()
        {
            if (markViews.Count == 0 && marks.Count == 0) return;
            var frames = VfxLibrary.Get("a_mark");
            List<EnemyController> gone = null;
            foreach (var pair in marks)
            {
                var e = pair.Key;
                int n = Marks(e);
                if (e == null || e.IsDead || n <= 0) { (gone ??= new List<EnemyController>()).Add(e); continue; }
                if (frames.Length == 0) continue;
                if (!markViews.TryGetValue(e, out var view) || view == null)
                {
                    var go = new GameObject("RogueMark");
                    if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
                    view = go.AddComponent<SpriteRenderer>();
                    view.sortingOrder = SkillFx.TopOrder + 4;
                    markViews[e] = view;
                }
                view.sprite = frames[Mathf.Clamp(n - 1, 0, frames.Length - 1)];
                float width = view.sprite.bounds.size.x;
                view.transform.localScale = Vector3.one * (width > .001f ? .5f / width : 1f);
                view.transform.position = e.Center + Vector2.up * (.75f + .25f * e.Size);
            }
            if (gone != null) foreach (var e in gone) ClearMarks(e);
        }

        // ---------- 쿠나이 순간이동 (어쌔신, moved from the ninja 2026-10-23) ----------

        /// <summary>
        /// 쿠나이 순간이동 (user skill): one kunai flies 10 m in a straight line at 20 m/s through monsters (each cut once),
        /// sticks in a wall or drops at the end, and stays 3 s. A second press (TryFollowUp) moves me to it at any time.
        /// The kunai is stopped by what stops the body (walls, water, cliffs, closed doors), so I never land beyond them.
        /// </summary>
        IEnumerator KunaiPort(Run c)
        {
            if (portKunai != null) portKunai.Vanish();
            int version = ++portVersion;
            Vector2 feet = owner.Position;
            float clear = owner.SkillClearance(c.dir, c.n.range);
            var k = KunaiProjectile.Spawn(owner, owner.Center + c.dir * .3f, c.dir, false); // the assassin holds daggers: always a kunai
            portKunai = k;
            Sound("swing", .8f);
            int portDamage = Mathf.Max(1, Mathf.RoundToInt(c.n.damage * (c.n.spec == 'a' ? PortPierceMul : c.n.spec == 'b' ? PortAnchorMul : 1f))); // [ROGUE SPEC]
            portAnchor = c.n.spec == 'b';
            var hit = new HashSet<EnemyController>();
            Vector2 lift = owner.Center - feet;
            float travelled = 0f;
            while (travelled < clear)
            {
                if (!Live(c) || k == null || version != portVersion) yield break;
                float step = Mathf.Min(PortSpeed * Time.deltaTime, clear - travelled);
                Vector2 a = feet + lift + c.dir * travelled, b = a + c.dir * step;
                foreach (var e in Corridor(a, b, c.n.radius)) if (hit.Add(e)) Strike(c, e, portDamage, a, 2f, 1, "c_slash");
                travelled += step;
                k.Place(feet + lift + c.dir * travelled);
                yield return null;
            }
            if (k == null || version != portVersion) yield break;
            float anchor = c.n.spec == 'b' && !float.IsNaN(c.n.duration) ? c.n.duration : c.s.duration; // [ROGUE SPEC] 그림자 닻 6 s
            k.Stick(anchor, false); // not for 회수: this one is the anchor
            yield return new WaitForSeconds(anchor);
            if (version == portVersion && portKunai == k) { portKunai = null; if (k != null) k.Vanish(); }
        }

        /// <summary>The second press: go to the kunai (where it is right now, even mid-flight).</summary>
        void PortToKunai()
        {
            var k = portKunai;
            portKunai = null;
            portVersion++; // stops the flight
            if (k == null) return;
            Vector2 target = k.Position - new Vector2(0f, .45f); // the kunai flies at body height; the body stands at its feet
            k.Vanish();
            Vector2? spot = FreeSpotNear(target, PortSearch);
            if (spot == null)
            {
                if (owner.IsLocal) { GameEvents.RaiseToast(L.T("이동할 자리가 없습니다.")); Game.Audio.PlaySfx("cancel"); }
                return;
            }
            CareerFx.Ghost(owner, new Color(.6f, .62f, .66f, .7f), .3f);
            NinjaClone.Puff(owner.Center);
            if (!owner.NetPuppet) owner.SkillTeleportTo(spot.Value, spot.Value + (spot.Value - owner.Position));
            NinjaClone.Puff(owner.Center);
            Sound("c_dash", .8f);
            if (portAnchor) AddGuard(PortAnchorGuard, PortAnchorGuardSeconds); // [ROGUE SPEC] 그림자 닻
        }

        // ---------- Casts ----------

        IEnumerator Assassin(Run c)
        {
            switch (c.s.effect)
            {
                case "phantom": yield return Phantoms(c); break;
                case "kunaiport": yield return KunaiPort(c); break;
                case "venom": Venom(c); break;
                case "rend": yield return Rend(c); break;
                case "rupture": Rupture(c); break;
                case "assassinate": Assassinate(c); break;
                case "shadowform": yield return Shapeshift(c); break; // [2026-10-23] 변신술 (CareerCombat.ShadowForm), was 천영참
                case "stealth": Stealth(c); break;
            }
        }

        /// <summary>
        /// 환영 삼격 (user skill 2026-10-23): three phantom copies of me run 5 m out on a fan (-25°, 0°, +25°), pushing back
        /// and cutting the monsters on their way (each phantom cuts a monster once, bosses are not pushed). Up close the
        /// three paths overlap, so a monster right in front takes all three cuts; the cuts ignore hit invulnerability.
        /// </summary>
        IEnumerator Phantoms(Run c)
        {
            Pose(.2f, 2);
            Sound("c_dash", .8f);
            // [ROGUE SPEC] 환영 일점 A: the phantoms run one line. 환영 오격 B: five on a wider fan, one cut per monster in all.
            int count = Mathf.Max(1, c.n.spec == 'b' && c.n.projectiles > 0 ? c.n.projectiles : c.s.hits);
            float spread = c.n.spec == 'a' ? 0f : c.n.spec == 'b' && !float.IsNaN(c.n.spreadDeg) ? c.n.spreadDeg : PhantomSpread;
            int damage = PhantomDamage(c);
            Vector2 from = owner.Center;
            var shared = c.n.spec == 'b' ? new HashSet<EnemyController>() : null;
            var dirs = new Vector2[count];
            var lengths = new float[count];
            var cut = new HashSet<EnemyController>[count];
            for (int i = 0; i < count; i++)
            {
                float a = count == 1 ? 0f : -spread + 2f * spread * i / (count - 1);
                dirs[i] = CareerFx.Tilt(c.dir, a);
                lengths[i] = owner.SkillClearance(dirs[i], c.n.range);
                cut[i] = new HashSet<EnemyController>();
            }
            float travelled = 0f, ghost = 0f;
            while (travelled < c.n.range)
            {
                if (!Live(c)) yield break;
                float step = Mathf.Min(PhantomSpeed * Time.deltaTime, c.n.range - travelled);
                ghost -= Time.deltaTime;
                bool drawn = ghost <= 0f;
                if (drawn) ghost = PhantomGhostGap;
                for (int i = 0; i < count; i++)
                {
                    if (travelled >= lengths[i]) continue;
                    Vector2 a = from + dirs[i] * travelled, b = from + dirs[i] * Mathf.Min(lengths[i], travelled + step);
                    if (drawn) ShadowAt(b, Tint(c, AssassinShade), .25f, dirs[i].x < 0f);
                    foreach (var e in Corridor(a, b, c.n.radius))
                    {
                        if (!cut[i].Add(e) || (shared != null && !shared.Add(e))) continue;
                        // Pushed straight along the phantom's line (the knockback runs away from the source point).
                        Strike(c, e, damage, e.Position - dirs[i], e.IsBoss ? .01f : PhantomPush, 1, "c_slash", true);
                    }
                }
                travelled += step;
                yield return null;
            }
        }

        /// <summary>맹독 도포: for a while every direct hit leaves 20% of its damage as poison.</summary>
        void Venom(Run c)
        {
            Pose(.2f, 2);
            Sound("c_heavy", .7f);
            venomPct = Mathf.RoundToInt(c.s.power * c.Scale * (c.n.spec == 'a' ? VenomBossScale : c.n.spec == 'b' ? VenomSpreadScale : 1f));
            venomEnd = Time.time + (c.n.spec == 'a' && !float.IsNaN(c.n.duration) ? c.n.duration : c.s.duration);
            venomBossOnly = c.n.spec == 'a'; // [ROGUE SPEC] 맹독 집중
            venomSpread = c.n.spec == 'b' ? Mathf.Max(1, c.n.targets) : 0; // [ROGUE SPEC] 맹독 전염
            CareerFx.Gen("a_venom", null, owner.Center, Vector2.zero, 1f, 0f, 14f, VfxLayer.Top, false, VenomTint);
            PowerAura.Play(owner, 1.2f, VenomGreen, new Color(.4f, .48f, .32f));
            SkillVisuals.Sparks(owner.Center, VenomGreen, 10, 3.5f, .3f);
        }

        /// <summary>연환 베기 (main hunter): two cuts on a 120° fan in front, 0.17 s apart.</summary>
        IEnumerator Rend(Run c)
        {
            if (c.n.spec != '\0') { yield return RendSpec(c); yield break; } // [ROGUE SPEC] 연환 일점 / 연환 선풍
            for (int i = 0; i < c.s.hits; i++)
            {
                if (!Live(c)) yield break;
                Vector2 from = owner.Center;
                Pose(.1f, i % 2);
                CareerFx.Clip("f_arc", from, CareerFx.Tilt(c.dir, i == 0 ? 14f : -14f), c.n.range / 1.45f, 40f, VfxLayer.Top, true, AssassinPink).FlipY(i == 1);
                Sound("c_slash", .5f);
                foreach (var e in Fan(from, c.dir, c.n.range, 120f)) Strike(c, e, c.n.damage, from, 2f, 0, "c_slash");
                if (i < c.s.hits - 1) yield return new WaitForSeconds(CutGap);
            }
        }

        /// <summary>급소 파열: cash in every mark on the monster in front (base + 0.35 a mark at rank 1).</summary>
        void Rupture(Run c)
        {
            var target = Target(c.dir, c.n.range, out Vector2 point);
            Pose(.2f, 2);
            Sound("c_heavy", .9f);
            if (target == null) { CareerFx.Clip("f_x", point, c.dir, .8f, 30f, VfxLayer.Top, true, AssassinPink); return; }
            int n = Marks(target);
            int damage = Mathf.RoundToInt(c.n.damage * (1f + RupturePerMark * n));
            if (c.n.spec == 'a') damage *= 2; // [ROGUE SPEC] 파열 극 (cooldown x2 in SkillSpecs.Rogue)
            int bleed = c.n.spec == 'b' ? Mathf.RoundToInt(damage * RuptureBleedShare) : 0; // [ROGUE SPEC] 출혈 파열
            damage -= bleed;
            CareerFx.Gen("a_mark", "f_shatter", target.Center, Vector2.zero, 1.2f, 1.2f, 20f, VfxLayer.Top, false, tint: SpecTint(c), fps: 14f);
            SkillVisuals.Flash(target.Center, Tint(c, AssassinPink), 1.2f + .2f * n, .15f);
            bool hit = Strike(c, target, damage, owner.Center, 5f, n >= 3 ? 2 : 1, "c_heavy");
            if (hit && bleed > 0 && c.authority) StartCoroutine(Bleed(target, bleed, float.IsNaN(c.n.duration) ? 4f : c.n.duration));
            if (c.authority) ClearMarks(target);
        }

        /// <summary>암살: one deep stab; x1.5 from behind, x1.8 with all five marks (they are spent).</summary>
        void Assassinate(Run c)
        {
            var target = Target(c.dir, c.n.range, out Vector2 point);
            Pose(.25f, 2);
            if (target == null) { Sound("c_slash", .6f); CareerFx.Clip("f_x", point, c.dir, .9f, 30f, VfxLayer.Top, true, Tint(c, AssassinPink)); return; }
            if (c.n.spec == 'b') BlinkBehind(c, target); // [ROGUE SPEC] 그림자 암살: always from behind
            bool behind = target.IsBehind(owner.Position) || Stealthed || c.n.spec == 'b';
            bool full = Marks(target) >= MarkCap;
            float mult = (behind ? AssassinateBehind : 1f) * (full ? AssassinateMarks : 1f) * AssassinateSpecMul(c, target);
            CareerFx.Gen("r_backstab", "f_x", target.Center, c.dir, 1.5f, 1.4f, tint: SpecTint(c));
            if (full) SkillVisuals.Flash(target.Center, Tint(c, AssassinPink), 2f, .18f);
            Sound("c_heavy");
            bool landed = Strike(c, target, Mathf.RoundToInt(c.n.damage * mult), owner.Center, 6f, 2, "c_heavy");
            if (full && c.authority) ClearMarks(target);
            if (full && landed) CareerTrials.Record(owner, "assassinate5", 1);
        }

        /// <summary>은신: hidden for a while (ordinary monsters lose me), faster, and the first blow is stronger and from behind.</summary>
        void Stealth(Run c)
        {
            Sound("c_dash", .6f);
            CareerFx.Gen("a_smoke", null, owner.Center, Vector2.zero, 1.6f, 0f, 14f, VfxLayer.Top, false, SmokeGray);
            stealthPct = Mathf.RoundToInt(c.s.power * c.Scale * (c.n.spec == 'a' ? StealthDeepScale : c.n.spec == 'b' ? StealthThreeScale : 1f));
            stealthEnd = Time.time + (c.n.spec == 'a' && !float.IsNaN(c.n.duration) ? c.n.duration : c.s.duration);
            stealthHitsLeft = c.n.spec == 'b' ? StealthThreeHits : 1; // [ROGUE SPEC] 암영 A / 연막 은신 B
            if (stealthRenderers == null)
            {
                stealthRenderers = owner.GetComponentsInChildren<SpriteRenderer>(true);
                stealthAlpha = new float[stealthRenderers.Length];
                for (int i = 0; i < stealthRenderers.Length; i++) stealthAlpha[i] = stealthRenderers[i].color.a;
            }
            hidden.Add(owner);
            if (owner.IsLocal) GameEvents.RaiseToast(L.T("은신 · 첫 공격은 등 뒤 판정"));
        }

        // ---------- Helpers ----------

        /// <summary>A career hit that leaves no hit invulnerability (awakening cuts closer than 0.15 s).</summary>
        bool StrikeRapid(Run c, EnemyController e, int damage, Vector2 from, int weight = 0)
        {
            if (e == null || e.IsDead) return false;
            bool landed = !c.authority;
            if (c.authority)
            {
                string previous = currentSkill;
                currentSkill = c.s.id;
                if (e.IsBoss) damage = Mathf.RoundToInt(damage * owner.Data.Stats.BossMul); // [LEGEND] 토벌형 weapon
                try { landed = e.TakeDamage(new DamageInfo(damage, from, 1f, Team.Player, owner.gameObject) { noHitInvulnerability = true }); }
                finally { currentSkill = previous; }
            }
            if (!landed) return false;
            CareerFx.Hit(e.Center, (e.Center - from).normalized, c.s.career, weight);
            ImpactSound("hit", .6f);
            if (weight >= 1) Feel(weight, (e.Center - from).normalized);
            return true;
        }

        /// <summary>A tinted copy of my body standing at <paramref name="at"/> (shadows, clones' smoke-in).</summary>
        void ShadowAt(Vector2 at, Color tint, float life, bool flip)
        {
            var body = owner.GetComponent<CharacterAnimator>()?.Body;
            if (body == null || body.sprite == null) return;
            var go = new GameObject("RogueShadow");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = at - new Vector2(0f, .45f);
            go.transform.localScale = body.transform.lossyScale;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = body.sprite;
            sr.flipX = flip;
            sr.sortingOrder = SkillFx.At(at.y, 2);
            go.AddComponent<GhostFade>().Begin(sr, tint, life);
        }
    }
}
