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

        /// <summary>[SKILL v2] 파쇄 일격: one heavy blow on the monster in front (the nearest, leaning towards the aim).</summary>
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
            CareerFx.Clip("impact", ground, Vector2.zero, n.radius / 1.4f, 26f, VfxLayer.Ground, false, CareerFx.Steel);
            if (target != null)
            {
                Hit(target, n, c, 9f);
                CareerFx.Hit(target.Center, aim, Career.Fighter, 2);
            }
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
            CareerFx.Clip("impact", target.Position, Vector2.zero, 0.55f, 26f, VfxLayer.Ground, false, CareerFx.Violet);
            Hit(target, n, from, 4f);
            Shake(0.06f, 0.12f);
        }

        // ================= Warrior =================

        /// <summary>돌진 베기: dash along the aim, cutting and shoving every monster met on the way.</summary>
        IEnumerator Charge(SkillNumbers n)
        {
            Vector2 dir = castAim;
            // A puppet (another PC's body) only shows the cut; its position comes from that PC.
            float dist = owner.NetPuppet ? 0f : owner.SkillDash(dir, n.range);
            Game.Audio.PlaySfx("swing");
            Shake(0.08f, 0.12f);
            float speed = PlayerController.DashDistance / PlayerController.DashDuration;
            float end = Time.time + dist / speed + 0.05f;
            var hit = new HashSet<EnemyController>();
            // [VFX] A cut line along the dash and afterimages of the body.
            CareerFx.Clip("f_line", owner.Center, dir, 1f, 26f).Squash(Mathf.Max(.25f, Mathf.Max(dist, 1.5f) / 6f), 1f);
            float ghost = 0f;
            do
            {
                ghost -= Time.deltaTime;
                if (ghost <= 0f) { ghost = 0.04f; CareerFx.Ghost(owner, new Color(1f, 0.85f, 0.45f, 0.6f)); }
                foreach (var e in EnemiesInRadius(owner.Center + dir * 0.3f, n.radius))
                {
                    if (!hit.Add(e)) continue;
                    Hit(e, n, owner.Center - dir, 10f);
                    CareerFx.Hit(e.Center, dir, Career.Fighter, 1);
                }
                yield return null;
            } while (Time.time < end);
            CareerFx.Clip("impact", owner.Position + dir * 0.5f, Vector2.zero, n.radius / 1.5f, 26f, VfxLayer.Ground, false, CareerFx.Steel);
            Shake(hit.Count > 0 ? 0.15f : 0.06f, 0.18f);
        }

        /// <summary>회전 베기: spin once, cutting everything around the player.</summary>
        void Whirl(SkillNumbers n)
        {
            Vector2 c = owner.Center;
            Game.Audio.PlaySfx("swing");
            // [VFX] Four crescents around the body make one full turn of the blade.
            for (int k = 0; k < 4; k++)
            {
                float a = k * 90f * Mathf.Deg2Rad;
                CareerFx.Clip("f_arc", c, new Vector2(Mathf.Cos(a), Mathf.Sin(a)), n.radius / 1.35f, 40f).FlipY(k % 2 == 1);
            }
            foreach (var e in EnemiesInRadius(c, n.radius))
            {
                Hit(e, n, c, 7f);
                CareerFx.Hit(e.Center, (e.Center - c).normalized, Career.Fighter, 0);
            }
            Shake(0.06f, 0.12f);
        }

        /// <summary>전쟁 함성: stun everything nearby and raise all damage for a while.</summary>
        void WarCry(SkillNumbers n)
        {
            Vector2 c = owner.Center;
            Game.Audio.PlaySfx("rock_break");
            SkillVisuals.WarCry(c, owner.Position, n.radius);
            var aim = castAim;
            CareerFx.Clip("g_roar", c, aim, n.radius / 3f, 22f, VfxLayer.Top, true);
            CareerFx.Clip("g_roar", c, -aim, n.radius / 3.4f, 22f, VfxLayer.Top, true);
            Shake(0.12f, 0.25f);
            foreach (var e in EnemiesInRadius(c, n.radius))
            {
                Hit(e, n, c, 8f);
                e.Stun(n.stun);
                StunStars.Attach(e);
            }
            // The shout pulls every monster around onto the caster (its old damage buff is the warrior's passive now).
            ThreatTable.WarCry(owner, c, n.radius);
        }

        /// <summary>천검 강림 (awakening): giant swords rain down on the monsters around the player.</summary>
        IEnumerator Blades(SkillNumbers n, bool cutIn)
        {
            if (cutIn)
            {
                Awaken("천검 강림", SkillVisuals.UltGold);
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
                    Game.Audio.PlaySfx("rock_break", 0.6f);
                    foreach (var e in EnemiesInRadius(p + Vector2.up * 0.3f, n.radius)) Hit(e, n, p, 5f);
                    Shake(0.07f, 0.1f);
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
            // [VFX] Ice blooms open in a ring around the caster.
            for (int k = 0; k < 6; k++)
            {
                float a = k * 60f * Mathf.Deg2Rad;
                CareerFx.Clip("m_icebloom", owner.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f) * n.radius * 0.6f, Vector2.zero, 0.9f, 22f, VfxLayer.AtFeet, false);
            }
            Shake(0.08f, 0.15f);
            foreach (var e in EnemiesInRadius(c, n.radius))
            {
                Hit(e, n, c, 2f);
                e.Freeze(n.freeze);
                if (!e.IsDead && e.IsFrozen) IceEncase.Attach(e);
            }
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
                        if (near[k].TakeDamage(new DamageInfo(zapDamage, pos, 1.5f, Team.Player, owner.gameObject)) && n.leechPct > 0)
                            owner.Heal(Mathf.Max(1, zapDamage * n.leechPct / 100));
                    }
                    if (near.Count == 0) SkillVisuals.FrostOrbSpark(pos);
                }
                if (Nearest(pos, 0.45f, null) != null || Blocked(pos)) break;
                yield return null;
            }
            orb.Kill();
            SkillVisuals.FrostOrbBurst(pos, pos + Vector2.down * 0.4f, n.radius);
            for (int k = -1; k <= 1; k++) CareerFx.Clip("m_icebloom", pos + new Vector2(k * 0.55f, -0.4f - 0.1f * Mathf.Abs(k)), Vector2.zero, 1.15f, 22f, VfxLayer.AtFeet, false);
            Game.Audio.PlaySfx("rock_break");
            Shake(0.09f, 0.14f);
            foreach (var e in EnemiesInRadius(pos, n.radius))
            {
                Hit(e, n, pos, 3f);
                e.Freeze(n.freeze);
                if (!e.IsDead && e.IsFrozen) IceEncase.Attach(e);
            }
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
            for (int i = 0; i < ticks; i++)
            {
                for (int k = 0; k < 4; k++)
                {
                    Vector2 p = at + Random.insideUnitCircle * n.radius * 0.85f;
                    SkillVisuals.Flash(p + Vector2.up * 0.15f, new Color(1f, 0.5f, 0.12f, 0.55f), 0.9f, 0.4f);
                    SkillVisuals.Sparks(p, SkillVisuals.FireOrange, 3, 1.4f, 0.4f);
                }
                // [VFX] Small bursts of flame rise from the patch every tick.
                CareerFx.Clip("m_explode", at + Random.insideUnitCircle * n.radius * 0.6f + Vector2.up * 0.15f, Vector2.zero, 0.55f, 26f, VfxLayer.Top, false);
                foreach (var e in EnemiesInRadius(at, n.radius)) Hit(e, n, at, 1.5f);
                yield return new WaitForSeconds(3f / ticks);
            }
        }

        IEnumerator Meteor(SkillNumbers n, bool cutIn)
        {
            if (cutIn)
            {
                Awaken("메테오", SkillVisuals.FireOrange);
                yield return new WaitForSeconds(0.35f);
            }
            const float fall = 0.38f;
            for (int i = 0; i < n.hits; i++)
            {
                Vector2 p = NextTarget(n.range, i, n.hits);
                SkillVisuals.MeteorFall(p, fall, n.radius);
                StartCoroutine(After(fall, () =>
                {
                    SkillVisuals.MeteorImpact(p, n.radius);
                    CareerFx.Clip("m_explode", p + Vector2.up * 0.3f, Vector2.zero, n.radius / 1.2f, 22f, VfxLayer.Top, false);
                    Game.Audio.PlaySfx("rock_break");
                    foreach (var e in EnemiesInRadius(p + Vector2.up * 0.3f, n.radius)) Hit(e, n, p, 9f);
                    Shake(0.14f, 0.18f);
                }));
                yield return new WaitForSeconds(0.2f);
            }
            yield return new WaitForSeconds(fall + 0.1f);
            SkillVisuals.UltFinish(owner.Center, SkillVisuals.FireOrange, n.range);
        }
    }
}
