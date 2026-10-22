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
        /// <summary>천영참: the closing burst on each monster cut is 480% (6x the 80% cuts).</summary>
        const float ShadowBurst = 4.8f / .8f;

        public static readonly Color AssassinPink = new Color(.88f, .35f, .78f, .95f); // #e05ac8
        static readonly Color AssassinShade = new Color(.75f, .3f, .85f, .55f);

        // ---------- State ----------

        readonly Dictionary<EnemyController, int> marks = new Dictionary<EnemyController, int>();
        readonly Dictionary<EnemyController, float> marksUntil = new Dictionary<EnemyController, float>();
        readonly Dictionary<EnemyController, SpriteRenderer> markViews = new Dictionary<EnemyController, SpriteRenderer>();
        float venomEnd, stealthEnd;
        int venomPct, stealthPct;
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
            venomEnd = 0; venomPct = 0;
            EndStealth();
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
                EndStealth();  // attacking breaks it
            }
            int vital = Prog.Rank("a_vital");
            if (vital > 0) mult *= 1f + Marks(enemy) * (4 + (vital - 1)) / 100f;
            int ambush = Prog.Rank("a_ambush");
            if (ambush > 0 && behind) mult *= 1f + (15 + 5 * (ambush - 1)) / 100f;
            int far = Prog.Rank("n_far");
            if (far > 0 && Vector2.Distance(owner.Center, enemy.Center) >= FarDistance) mult *= 1f + (12 + 4 * (far - 1)) / 100f;
            pendingBehind = behind;
            pendingAmount = Mathf.RoundToInt(amount * mult);
            return mult;
        }

        /// <summary>A direct hit of mine landed: add a mark, leave poison, count the trial.</summary>
        void RogueLanded(EnemyController enemy)
        {
            if (Prog.Rank("a_vital") > 0 && !enemy.IsDead)
            {
                marks[enemy] = Mathf.Min(MaxMarks, Marks(enemy) + 1);
                marksUntil[enemy] = Time.time + MarkSeconds;
            }
            if (VenomPct > 0 && pendingAmount > 0 && !enemy.IsDead)
                StartCoroutine(Poison(enemy, Mathf.Max(1, Mathf.RoundToInt(pendingAmount * venomPct / 100f))));
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
                CareerFx.Gen("a_venom", null, e.Center + Vector2.up * .2f, Vector2.zero, .55f, 0f, 14f, VfxLayer.Top, false);
            }
        }

        void RogueTick()
        {
            if (stealthEnd > 0f && Time.time >= stealthEnd) EndStealth();
            MarkViewsTick();
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

        // ---------- Casts ----------

        IEnumerator Assassin(Run c)
        {
            switch (c.s.effect)
            {
                case "twin": yield return TwinStab(c); break;
                case "veil": yield return SmokeLeap(c); break;
                case "venom": Venom(c); break;
                case "rend": yield return Rend(c); break;
                case "rupture": Rupture(c); break;
                case "assassinate": Assassinate(c); break;
                case "shadows": yield return Shadows(c); break;
                case "stealth": Stealth(c); break;
            }
        }

        /// <summary>쌍인참: four stabs, 0.17 s apart, on the one monster in front.</summary>
        IEnumerator TwinStab(Run c)
        {
            var target = Target(c.dir, c.n.range, out Vector2 point);
            for (int i = 0; i < c.s.hits; i++)
            {
                if (!Live(c)) yield break;
                Pose(.12f, i % 2);
                Vector2 dir = target != null && !target.IsDead ? (target.Center - owner.Center).normalized : c.dir;
                Vector2 at = target != null && !target.IsDead ? target.Center : owner.Center + c.dir * Mathf.Min(1f, c.n.range);
                CareerFx.Clip("f_x", at, CareerFx.Tilt(dir, i % 2 == 0 ? 20f : -20f), .8f, 30f, VfxLayer.Top, true, AssassinPink);
                Sound("c_slash", .6f);
                if (target != null) Strike(c, target, c.n.damage, owner.Center, 2f, i == c.s.hits - 1 ? 1 : 0, "c_slash");
                if (i < c.s.hits - 1) yield return new WaitForSeconds(CutGap);
            }
        }

        /// <summary>연막 도약: jump 4 m back and burst smoke where I stood; monsters in it are hit and can't attack for 2 s.</summary>
        IEnumerator SmokeLeap(Run c)
        {
            Vector2 spot = owner.Center, feet = owner.Position;
            CareerFx.Gen("a_smoke", null, spot, Vector2.zero, c.n.radius * 2f, 0f, 14f, VfxLayer.Top, false);
            SkillFx.Spawn("fx_ring", feet, new Color(.6f, .45f, .7f, .7f), .5f, SkillFx.GroundOrder + 14)
                .Scale(new Vector2(c.n.radius * .3f, c.n.radius * .2f), new Vector2(c.n.radius, c.n.radius * .7f)).Fade(FxFade.Late);
            Sound("c_dash", .8f);
            foreach (var e in Enemies(spot, c.n.radius))
                if (Strike(c, e, c.n.damage, spot, 3f, 1, "hit")) Stun(c, e, c.s.duration);
            CareerFx.Ghost(owner, AssassinShade, .35f);
            float distance = Dash(-c.dir, c.n.range);
            float seconds = DashSeconds(distance);
            for (float t = 0; t < seconds; t += Time.deltaTime)
            {
                if (!Live(c)) yield break;
                yield return null;
            }
        }

        /// <summary>맹독 도포: for a while every direct hit leaves 20% of its damage as poison.</summary>
        void Venom(Run c)
        {
            Pose(.2f, 2);
            Sound("c_heavy", .7f);
            venomPct = Mathf.RoundToInt(c.s.power * c.Scale);
            venomEnd = Time.time + c.s.duration;
            CareerFx.Gen("a_venom", null, owner.Center, Vector2.zero, 1f, 0f, 14f, VfxLayer.Top, false);
            PowerAura.Play(owner, 1.2f, new Color(.55f, .9f, .3f), new Color(.85f, .4f, .9f));
            SkillVisuals.Sparks(owner.Center, new Color(.6f, 1f, .4f, 1f), 10, 3.5f, .3f);
        }

        /// <summary>연환 베기 (main hunter): two cuts on a 120° fan in front, 0.17 s apart.</summary>
        IEnumerator Rend(Run c)
        {
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
            CareerFx.Gen("a_mark", "f_shatter", target.Center, Vector2.zero, 1.2f, 1.2f, 20f, VfxLayer.Top, false, fps: 14f);
            SkillVisuals.Flash(target.Center, AssassinPink, 1.2f + .2f * n, .15f);
            Strike(c, target, damage, owner.Center, 5f, n >= 3 ? 2 : 1, "c_heavy");
            if (c.authority) ClearMarks(target);
        }

        /// <summary>암살: one deep stab; x1.5 from behind, x1.8 with all five marks (they are spent).</summary>
        void Assassinate(Run c)
        {
            var target = Target(c.dir, c.n.range, out Vector2 point);
            Pose(.25f, 2);
            if (target == null) { Sound("c_slash", .6f); CareerFx.Clip("f_x", point, c.dir, .9f, 30f, VfxLayer.Top, true, AssassinPink); return; }
            bool behind = target.IsBehind(owner.Position) || Stealthed;
            bool full = Marks(target) >= MaxMarks;
            float mult = (behind ? AssassinateBehind : 1f) * (full ? AssassinateMarks : 1f);
            CareerFx.Gen("r_backstab", "f_x", target.Center, c.dir, 1.5f, 1.4f);
            if (full) SkillVisuals.Flash(target.Center, AssassinPink, 2f, .18f);
            Sound("c_heavy");
            bool landed = Strike(c, target, Mathf.RoundToInt(c.n.damage * mult), owner.Center, 6f, 2, "c_heavy");
            if (full && c.authority) ClearMarks(target);
            if (full && landed) CareerTrials.Record(owner, "assassinate5", 1);
        }

        /// <summary>
        /// 천영참 (awakening): six shadows dart between the monsters around me for 3 s (18 cuts), then every monster cut
        /// takes the 480% burst. Cuts come faster than the hit invulnerability, so they all land.
        /// </summary>
        IEnumerator Shadows(Run c)
        {
            Vector2 center = owner.Center;
            int cuts = Mathf.Max(1, c.s.hits);
            float gap = c.s.duration / cuts;
            var cut = new HashSet<EnemyController>();
            for (int i = 0; i < cuts; i++)
            {
                if (!Live(c)) yield break;
                var foes = Enemies(center, c.n.radius);
                Vector2 at;
                EnemyController foe = foes.Count > 0 ? foes[i % foes.Count] : null;
                if (foe != null) at = foe.Center;
                else { float a = Random.Range(0f, Mathf.PI * 2f); at = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * Random.Range(1f, c.n.radius * .7f); }
                float turn = (i * 60f + Random.Range(-20f, 20f)) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(turn), Mathf.Sin(turn));
                ShadowAt(at - dir * .5f, AssassinShade, .3f, dir.x < 0f);
                CareerFx.Gen("r_dance_slash", "f_x", at, dir, 1.4f, .9f, 30f, VfxLayer.Top, true, AssassinPink);
                Sound("c_slash", .45f);
                if (foe != null && StrikeRapid(c, foe, c.n.damage, at - dir)) cut.Add(foe);
                yield return new WaitForSeconds(gap);
            }
            if (!Live(c)) yield break;
            Sound("c_heavy");
            Feel(2, Vector2.up);
            foreach (var e in cut)
            {
                if (e == null || e.IsDead) continue;
                CareerFx.Gen("a_mark", "f_shatter", e.Center, Vector2.zero, 1.6f, 1.6f, 20f, VfxLayer.Top, false, fps: 14f);
                StrikeRapid(c, e, Mathf.RoundToInt(c.n.damage * ShadowBurst), center, 2);
                if (c.authority) ClearMarks(e);
            }
            SkillVisuals.UltFinish(owner.Center, AssassinPink, c.n.radius);
        }

        /// <summary>은신: hidden for a while (ordinary monsters lose me), faster, and the first blow is stronger and from behind.</summary>
        void Stealth(Run c)
        {
            Sound("c_dash", .6f);
            CareerFx.Gen("a_smoke", null, owner.Center, Vector2.zero, 1.6f, 0f, 14f, VfxLayer.Top, false);
            stealthPct = Mathf.RoundToInt(c.s.power * c.Scale);
            stealthEnd = Time.time + c.s.duration;
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
