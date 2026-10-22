using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public partial class SkillCaster
    {
        /// <summary>Awakening cut-in: banner, dimming and shake for the local player; a plain flash for companions.</summary>
        void Awaken(string skillName, Color color)
        {
            if (owner.IsLocal)
            {
                SkillVisuals.Awakening(owner, skillName, color);
                Game.Audio.PlaySfx("quest");
                return;
            }
            SkillVisuals.Flash(owner.Center, new Color(color.r, color.g, color.b, 0.6f), 2.4f, 0.35f);
            Game.Audio.PlaySfx("quest", 0.4f);
        }

        // ================= v2 single-target and defence skills =================

        /// <summary>[SKILL v2] 파쇄 일격: one heavy blow on the monster in front (the nearest, leaning towards the aim).
        /// [CH3 SPEC] A 일점 파쇄: the single blow at 4.0. B 파쇄 연파: 0.67 on the target, then two shock waves (1.6x radius)
        /// on up to n.targets monsters, so each one of a pack takes two hits.</summary>
        void Crush(SkillNumbers n)
        {
            Vector2 c = owner.Center, aim = castAim;
            EnemyController target = null;
            float best = float.MaxValue;
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead || !e.isActiveAndEnabled) continue;
                Vector2 to = e.Center - c;
                float d = to.magnitude;
                if (d > n.range + 0.35f * e.Size) continue;
                float score = d - Vector2.Dot(to.normalized, aim) * 0.6f; // in front first
                if (score < best) { best = score; target = e; }
            }
            Vector2 at = target != null ? target.Center : c + aim * Mathf.Min(1.1f, n.range);
            Game.Audio.PlaySfx("rock_break");
            // [VFX] A downward cut on the target, the shock spreading on the ground under it.
            Vector2 ground = target != null ? target.Position : at + Vector2.down * 0.3f;
            CareerFx.Clip("f_vslash", ground, aim, 0.8f, 30f, VfxLayer.Top, false);
            CareerFx.Clip("impact", ground, Vector2.zero, n.radius / 1.4f, 26f, VfxLayer.Ground, false, SpecTint(n, CareerFx.Steel));
            if (target != null)
            {
                Hit(target, n, c, 9f);
                CareerFx.Hit(target.Center, aim, Career.Fighter, 2);
            }
            if (n.spec == 'b') StartCoroutine(CrushWaves(n, target, ground));
            Shake(0.1f, 0.16f);
        }

        /// <summary>[SKILL v2] 번개 창: one thick bolt from the sky onto the nearest monster in range.</summary>
        void Lance(SkillNumbers n)
        {
            Vector2 from = owner.Center;
            SkillVisuals.CastCircle(owner.Position, SkillVisuals.MageViolet);
            SkillVisuals.StaffFlash(from + castAim * 0.35f, SkillVisuals.ArcGlow);
            var target = Nearest(from, n.range, null);
            Game.Audio.PlaySfx("magic");
            if (target == null)
            {
                SkillVisuals.ArcBolt(from, from + castAim * 2.5f, false);
                return;
            }
            SkillVisuals.Thunder(target.Position);
            CareerFx.Clip("m_spark", target.Center, Vector2.zero, 1.3f, 30f, VfxLayer.Top, false);
            // [CH3 SPEC] Worked example for the other cast functions: the spec colour via SkillSpecs.Tint, the behaviour
            // via n.spec / n.specExtra / n.targets (numbers such as damage already came through SkillSpecs.Apply).
            CareerFx.Clip("impact", target.Position, Vector2.zero, 0.55f, 26f, VfxLayer.Ground, false, SkillSpecs.Tint(n, CareerFx.Violet));
            Hit(target, n, from, 4f);
            Vector2 at = target.Center;
            if (n.HasExtra("focus_no_splash")) // A 뇌창 집속: all the power in the one bolt (x1.5), no splash
            {
                Shake(0.08f, 0.14f);
                return;
            }
            if (n.HasExtra("branch_25")) // B 분기 뇌창: the bolt forks to the n.targets nearest within n.radius, 25% each
            {
                var branch = n;
                branch.damage = Mathf.Max(1, Mathf.RoundToInt(n.damage * 0.25f));
                int forks = 0;
                foreach (var e in EnemiesInRadius(at, n.radius))
                {
                    if (e == target) continue;
                    SkillVisuals.ArcBolt(at, e.Center, false);
                    CareerFx.Clip("impact", e.Position, Vector2.zero, 0.35f, 26f, VfxLayer.Ground, false, SkillSpecs.Tint(n, CareerFx.Violet));
                    Hit(e, branch, at, 2f);
                    if (++forks >= Mathf.Max(1, n.targets)) break;
                }
                Shake(0.06f, 0.12f);
                return;
            }
            // Splash: the bolt's electricity hits every monster around the impact point too, at full damage.
            foreach (var e in EnemiesInRadius(at, n.radius))
            {
                if (e == target) continue;
                SkillVisuals.ArcBolt(at, e.Center, false);
                Hit(e, n, at, 3f);
            }
            Shake(0.06f, 0.12f);
        }

        // ================= Warrior =================

        /// <summary>돌진 베기: dash along the aim, cutting and shoving every monster met on the way.
        /// [CH3 SPEC] A 관통 돌진: twice as far, the first monster takes 4.0, the rest 1.0. B 무적 돌진: invulnerable while
        /// dashing (2.4) and the monsters at the end are shoved away.</summary>
        IEnumerator Charge(SkillNumbers n)
        {
            Vector2 dir = castAim;
            // A puppet (another PC's body) only shows the cut; its position comes from that PC.
            float dist = owner.NetPuppet ? 0f : owner.SkillDash(dir, n.range);
            Game.Audio.PlaySfx("swing");
            Shake(0.08f, 0.12f);
            float speed = PlayerController.DashDistance / PlayerController.DashDuration;
            float end = Time.time + dist / speed + 0.05f;
            if (n.spec == 'b') CareerCombat.For(owner).AddInvulnerable(dist / speed + 0.15f);
            var hit = new HashSet<EnemyController>();
            // [VFX] A cut line along the dash and afterimages of the body.
            CareerFx.Clip("f_line", owner.Center, dir, 1f, 26f, VfxLayer.Top, true, n.spec != '\0' ? (Color?)SpecTint(n, Color.white) : null).Squash(Mathf.Max(.25f, Mathf.Max(dist, 1.5f) / 6f), 1f);
            float ghost = 0f;
            var ghostColor = n.spec != '\0' ? SpecTint(n, Color.white) : new Color(1f, 0.85f, 0.45f, 0.6f);
            ghostColor.a = 0.6f;
            do
            {
                ghost -= Time.deltaTime;
                if (ghost <= 0f) { ghost = 0.04f; CareerFx.Ghost(owner, ghostColor); }
                foreach (var e in EnemiesInRadius(owner.Center + dir * 0.3f, n.radius))
                {
                    if (!hit.Add(e)) continue;
                    if (n.spec == 'a' && hit.Count == 1)
                    {
                        var first = n;
                        first.damage = n.damage * 4; // 4.0 on the first monster met (n.damage is the 1.0 of the rest)
                        Hit(e, first, owner.Center - dir, 12f);
                    }
                    else Hit(e, n, owner.Center - dir, 10f);
                    CareerFx.Hit(e.Center, dir, Career.Fighter, 1);
                }
                yield return null;
            } while (Time.time < end);
            CareerFx.Clip("impact", owner.Position + dir * 0.5f, Vector2.zero, n.radius / 1.5f, 26f, VfxLayer.Ground, false, SpecTint(n, CareerFx.Steel));
            if (n.spec == 'b')
                foreach (var e in EnemiesInRadius(owner.Center, n.radius * 1.5f))
                {
                    Vector2 away = (e.Center - owner.Center).sqrMagnitude > .0001f ? (e.Center - owner.Center).normalized : dir;
                    e.PullTowards(e.Position + away * 2.5f, 9f, 0.25f);
                }
            Shake(hit.Count > 0 ? 0.15f : 0.06f, 0.18f);
        }

        /// <summary>회전 베기: spin once, cutting everything around the player.
        /// [CH3 SPEC] A 회전 흡인: a 0.9 s channel of three spins that pull the pack in (rooted). B 이동 회전: two spins that
        /// follow the walking player, 25% less damage taken for 1 s.</summary>
        void Whirl(SkillNumbers n)
        {
            if (n.spec == 'a' || n.spec == 'b') { StartCoroutine(WhirlSpins(n)); return; }
            Spin(n, owner.Center, null);
        }

        /// <summary>전쟁 함성: stun everything nearby and raise all damage for a while.
        /// [CH3 SPEC] A 위압의 함성: 2.5 m, 2.4 s stun and 10% more damage taken for 4 s. B 전장의 함성: 6 m, 0.8 s stun
        /// and allies in reach walk 15% faster for 4 s.</summary>
        void WarCry(SkillNumbers n)
        {
            Vector2 c = owner.Center;
            Game.Audio.PlaySfx("rock_break");
            SkillVisuals.WarCry(c, owner.Position, n.radius);
            var aim = castAim;
            Color? tint = n.spec != '\0' ? (Color?)SpecTint(n, Color.white) : null;
            CareerFx.Clip("g_roar", c, aim, n.radius / 3f, 22f, VfxLayer.Top, true, tint);
            CareerFx.Clip("g_roar", c, -aim, n.radius / 3.4f, 22f, VfxLayer.Top, true, tint);
            Shake(0.12f, 0.25f);
            float stun = float.IsNaN(n.duration) ? n.stun : n.duration;
            foreach (var e in EnemiesInRadius(c, n.radius))
            {
                Hit(e, n, c, 8f);
                e.Stun(stun);
                StunStars.Attach(e);
                if (n.spec == 'a') CareerCombat.For(owner).MarkFromCaster(e, 10, 4f);
            }
            if (n.spec == 'b')
            {
                CareerCombat.For(owner).AddHaste(15, 4f);
                if (Game.Party != null)
                    foreach (var p in Game.Party.Members)
                        if (p != null && !p.IsDead && Vector2.Distance(p.Center, c) <= n.radius) CareerCombat.For(p).AddHaste(15, 4f);
            }
            // The shout pulls every monster around onto the caster (its old damage buff is the warrior's passive now).
            ThreatTable.WarCry(owner, c, n.radius);
            // [SKILL 2026-10-14] Follow-up: an aftershock hits the same area once more (Lv12 skills of every class).
            StartCoroutine(After(FollowUpDelay, () => FollowUp(owner.Center, n, "rock_break", SkillVisuals.UltGold)));
        }

        const float FollowUpDelay = 0.4f;

        /// <summary>[SKILL] The Lv12 skills' second hit: the same damage again to every monster still in the area, no crowd control.</summary>
        void FollowUp(Vector2 c, SkillNumbers n, string sfx, Color color)
        {
            if (owner == null || owner.IsDead) return;
            Game.Audio.PlaySfx(sfx, 0.7f);
            SkillVisuals.CastCircle(owner.Position, color);
            CareerFx.Clip("impact", owner.Position, Vector2.zero, n.radius / 2.4f, 26f, VfxLayer.Ground, false);
            Shake(0.06f, 0.12f);
            foreach (var e in EnemiesInRadius(c, n.radius)) Hit(e, n, c, 3f);
        }

        /// <summary>천검 강림 (awakening): giant swords rain down on the monsters around the player.
        /// [CH3 SPEC] A 단검 강림: one sword (28.8, 0.8 m) that staggers for 2 s. B 검의 비: 12 swords of 1.2 on 3.2 m, every 12 s.</summary>
        IEnumerator Blades(SkillNumbers n, bool cutIn)
        {
            if (cutIn)
            {
                Awaken(L.T("천검 강림"), SkillVisuals.UltGold);
                yield return new WaitForSeconds(0.35f);
            }
            const float fall = 0.14f;
            for (int i = 0; i < n.hits; i++)
            {
                Vector2 p = NextTarget(n.range, i, n.hits);
                SkillVisuals.SwordDrop(p, fall);
                StartCoroutine(After(fall, () =>
                {
                    SkillVisuals.SwordImpact(p, n.radius);
                    CareerFx.Clip("f_shatter", p + Vector2.up * 0.3f, Vector2.zero, 1.1f, 22f);
                    if (n.spec != '\0') CareerFx.Clip("impact", p, Vector2.zero, n.radius / 1.4f, 26f, VfxLayer.Ground, false, SpecTint(n, CareerFx.Steel));
                    Game.Audio.PlaySfx("rock_break", 0.6f);
                    foreach (var e in EnemiesInRadius(p + Vector2.up * 0.3f, n.radius))
                    {
                        Hit(e, n, p, 5f);
                        if (n.spec == 'a' && !e.IsDead) e.Stagger(2f);
                    }
                    Shake(n.spec == 'a' ? 0.18f : 0.07f, n.spec == 'a' ? 0.22f : 0.1f);
                }));
                yield return new WaitForSeconds(0.1f);
            }
            yield return new WaitForSeconds(fall + 0.1f);
            SkillVisuals.UltFinish(owner.Center, SkillVisuals.UltGold, n.range);
        }

        // ================= Mage =================

        /// <summary>서리 폭발: damage and freeze everything around the player.</summary>
        void Nova(SkillNumbers n)
        {
            Vector2 c = owner.Center;
            Game.Audio.PlaySfx("magic");
            SkillVisuals.Nova(c, owner.Position, n.radius);
            // [CH3 SPEC] 절대 영도 / 서리 파도: the freeze time comes from the spec (5.0 s / 1.6 s), the blooms take its colour.
            float freeze = float.IsNaN(n.duration) ? n.freeze : n.duration;
            // [VFX] Ice blooms open in a ring around the caster.
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad;
                CareerFx.Clip("m_icebloom", owner.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f) * n.radius * 0.6f, Vector2.zero, 0.9f, 22f, VfxLayer.AtFeet, false, MageTint(n));
            }
            Shake(0.08f, 0.15f);
            foreach (var e in EnemiesInRadius(c, n.radius))
            {
                Hit(e, n, c, 2f);
                e.Freeze(freeze);
                if (!e.IsDead && e.IsFrozen) IceEncase.Attach(e);
                if (e.IsDead) continue;
                if (n.HasExtra("nova_wave")) e.Slow(0.4f, freeze + 3f); // B: 40% slower for 3 s after the thaw
                else if (n.HasExtra("nova_shatter")) StartCoroutine(NovaShatter(e, n, freeze)); // A: the ice bursts when it breaks
            }
            // [SKILL 2026-10-14] Follow-up: the frost bursts once more (Lv12 skills of every class).
            StartCoroutine(After(FollowUpDelay, () => FollowUp(owner.Center, n, "magic", SkillVisuals.FrostBlue)));
        }

        /// <summary>
        /// 빙뢰구: an ice orb wrapped in lightning flies at the nearest monster (or along the aim). While it
        /// flies it zaps up to <see cref="SkillNumbers.chains"/> monsters near it with small bolts; when it
        /// hits a monster, a wall or the end of its range it shatters into ice, hurting and freezing
        /// everything around.
        /// </summary>
        IEnumerator FrostOrb(SkillNumbers n)
        {
            Game.Audio.PlaySfx("magic");
            Vector2 from = owner.Center;
            var target = Nearest(from, n.range, null);
            Vector2 dir = target != null ? (target.Center - from).normalized : castAim;
            if (target != null) owner.FaceTowards(target.Position);
            SkillVisuals.CastCircle(owner.Position, SkillVisuals.FrostBlue);
            SkillVisuals.StaffFlash(from + dir * 0.35f, SkillVisuals.ArcGlow);
            const float speed = 7.5f, zapEvery = 0.2f;
            Vector2 pos = from + dir * 0.5f;
            var orb = SkillVisuals.FrostOrbHead(pos, dir);
            float travelled = 0f, trail = 0f, zap = 0.08f;
            int zapDamage = Mathf.Max(1, Mathf.RoundToInt(n.damage * 0.3f));
            float zapRange = 1.6f + n.radius * 0.6f;
            while (travelled < n.range)
            {
                // Gentle homing on the first target, like the basic magic bolt.
                if (target != null && !target.IsDead && target.isActiveAndEnabled)
                {
                    Vector2 want = (target.Center - pos).normalized;
                    float turn = 3f * Time.deltaTime;
                    float angle = Vector2.SignedAngle(dir, want) * Mathf.Deg2Rad;
                    angle = Mathf.Clamp(angle, -turn, turn);
                    dir = new Vector2(dir.x * Mathf.Cos(angle) - dir.y * Mathf.Sin(angle), dir.x * Mathf.Sin(angle) + dir.y * Mathf.Cos(angle)).normalized;
                }
                float step = speed * Time.deltaTime;
                pos += dir * step;
                travelled += step;
                orb.MoveTo(pos);
                trail -= Time.deltaTime;
                if (trail <= 0f) { trail = 0.025f; SkillVisuals.FrostOrbTrail(pos, dir); }
                zap -= Time.deltaTime;
                if (zap <= 0f)
                {
                    zap = zapEvery;
                    // Crackle: small bolts to the closest monsters around the orb.
                    var near = EnemiesInRadius(pos, zapRange);
                    near.Sort((a, b) => Vector2.Distance(a.Center, pos).CompareTo(Vector2.Distance(b.Center, pos)));
                    for (int k = 0; k < near.Count && k < n.chains; k++) // v2: no zaps in flight (chains 0)
                    {
                        SkillVisuals.FrostOrbZap(pos, near[k].Center);
                        int zapHit = BossScaled(near[k], n, zapDamage); // same boss bonuses as every other skill hit
                        if (near[k].TakeDamage(new DamageInfo(zapHit, pos, 1.5f, Team.Player, owner.gameObject)) && n.leechPct > 0)
                            owner.Heal(Mathf.Max(1, zapHit * n.leechPct / 100));
                    }
                    if (near.Count == 0) SkillVisuals.FrostOrbSpark(pos);
                }
                if (Nearest(pos, 0.45f, null) != null || Blocked(pos)) break;
                yield return null;
            }
            orb.Kill();
            SkillVisuals.FrostOrbBurst(pos, pos + Vector2.down * 0.4f, n.radius);
            for (int k = -1; k <= 1; k++) CareerFx.Clip("m_icebloom", pos + new Vector2(k * 0.55f, -0.4f - 0.1f * Mathf.Abs(k)), Vector2.zero, 1.15f, 22f, VfxLayer.AtFeet, false, MageTint(n));
            Game.Audio.PlaySfx("rock_break");
            Shake(0.09f, 0.14f);
            // [CH3 SPEC] 빙뢰 응집 (A) adds a short freeze to its small, heavy burst; 빙뢰 파편 (B) throws six shards that slow.
            float orbFreeze = n.HasExtra("frost_dense") ? Mathf.Max(n.freeze, 0.5f) : n.freeze;
            foreach (var e in EnemiesInRadius(pos, n.radius))
            {
                Hit(e, n, pos, 3f);
                e.Freeze(orbFreeze);
                if (!e.IsDead && e.IsFrozen) IceEncase.Attach(e);
            }
            if (n.HasExtra("frost_shards")) FrostShards(pos, n);
        }

        /// <summary>메테오 (awakening): burning rocks crash down across the area around the player.</summary>
        /// <summary>화염 장판: a burning patch on the crowd around the nearest monster, ticking <c>hits</c> times over 3 s.</summary>
        IEnumerator FireField(SkillNumbers n)
        {
            var target = Nearest(owner.Center, n.range, null);
            Vector2 at = target != null ? target.Position : owner.Position + castAim * Mathf.Min(4f, n.range);
            SkillVisuals.CastCircle(owner.Position, SkillVisuals.FireOrange);
            SkillVisuals.Explosion(at + Vector2.up * 0.2f, at, n.radius * 0.8f, false);
            CareerFx.Clip("m_explode", at + Vector2.up * 0.2f, Vector2.zero, n.radius / 1.6f, 22f, VfxLayer.Top, false);
            Game.Audio.PlaySfx("magic");
            Shake(0.06f, 0.12f);
            int ticks = Mathf.Max(1, n.hits);
            // [CH3 SPEC] 화염 기둥 (A): a narrow pillar that slows whoever stands in it by half. 들불 (B): a wide field that
            // spreads after the monsters walking through it, up to three more patches; a monster burns once per tick however
            // many patches it stands in.
            bool pillar = n.HasExtra("fire_pillar"), wildfire = n.HasExtra("wildfire");
            Color flame = n.spec != '\0' ? SkillSpecs.Tint(n, SkillVisuals.FireOrange) : SkillVisuals.FireOrange;
            var patches = new List<(Vector2 at, float r)> { (at, n.radius) };
            for (int i = 0; i < ticks; i++)
            {
                foreach (var patch in patches)
                {
                    for (int k = 0; k < 4; k++)
                    {
                        Vector2 p = patch.at + Random.insideUnitCircle * patch.r * 0.85f;
                        SkillVisuals.Flash(p + Vector2.up * 0.15f, new Color(flame.r, flame.g * 0.5f + 0.25f, flame.b * 0.5f, 0.55f), 0.9f, 0.4f);
                        SkillVisuals.Sparks(p, flame, 3, 1.4f, 0.4f);
                    }
                    // [VFX] Small bursts of flame rise from the patch every tick.
                    CareerFx.Clip("m_explode", patch.at + Random.insideUnitCircle * patch.r * 0.6f + Vector2.up * 0.15f, Vector2.zero, 0.55f, 26f, VfxLayer.Top, false, MageTint(n));
                }
                var burned = new HashSet<EnemyController>();
                foreach (var patch in patches)
                    foreach (var e in EnemiesInRadius(patch.at, patch.r))
                    {
                        if (!burned.Add(e)) continue;
                        Hit(e, n, patch.at, 1.5f);
                        if (pillar && !e.IsDead) e.Slow(0.5f, 3f / ticks + 0.1f);
                    }
                if (wildfire && patches.Count < 4 && i < ticks - 1)
                {
                    // The fire follows a burning monster that walked furthest from the patches already lit.
                    EnemyController walker = null;
                    float far = 0f;
                    foreach (var e in burned)
                    {
                        if (e == null || e.IsDead) continue;
                        float d = float.MaxValue;
                        foreach (var patch in patches) d = Mathf.Min(d, Vector2.Distance(e.Position, patch.at));
                        if (d > far) { far = d; walker = e; }
                    }
                    if (walker != null && far > n.radius * 0.35f) patches.Add((walker.Position, n.radius * 0.5f));
                }
                yield return new WaitForSeconds(3f / ticks);
            }
        }

        IEnumerator Meteor(SkillNumbers n, bool cutIn)
        {
            if (cutIn)
            {
                Awaken(L.T("메테오"), SkillVisuals.FireOrange);
                yield return new WaitForSeconds(0.35f);
            }
            // [CH3 SPEC] 대운석 (A): one rock after a 1.5 s warning, then a burning crack. 유성 시간차 (B): the rocks land 0.4 s
            // apart and slow what they hit by 30% for 3 s.
            if (n.HasExtra("meteor_one"))
            {
                yield return BigMeteor(n);
                SkillVisuals.UltFinish(owner.Center, SkillSpecs.Tint(n, SkillVisuals.FireOrange), n.range);
                yield break;
            }
            bool timed = n.HasExtra("meteor_timed");
            const float fall = 0.38f;
            for (int i = 0; i < n.hits; i++)
            {
                Vector2 p = NextTarget(n.range, i, n.hits);
                SkillVisuals.MeteorFall(p, fall, n.radius);
                StartCoroutine(After(fall, () =>
                {
                    SkillVisuals.MeteorImpact(p, n.radius);
                    CareerFx.Clip("m_explode", p + Vector2.up * 0.3f, Vector2.zero, n.radius / 1.2f, 22f, VfxLayer.Top, false, MageTint(n));
                    Game.Audio.PlaySfx("rock_break");
                    foreach (var e in EnemiesInRadius(p + Vector2.up * 0.3f, n.radius))
                    {
                        Hit(e, n, p, 9f);
                        if (timed && !e.IsDead) e.Slow(0.3f, 3f);
                    }
                    Shake(0.14f, 0.18f);
                }));
                yield return new WaitForSeconds(timed ? 0.4f : 0.2f);
            }
            yield return new WaitForSeconds(fall + 0.1f);
            SkillVisuals.UltFinish(owner.Center, SkillVisuals.FireOrange, n.range);
        }
    }
}
