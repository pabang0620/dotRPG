using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Casts the skills in the five slots (K / L / U / O, V = awakening). Numbers come from
    /// the member's <see cref="CharacterStatsCalc.Skill"/> (skill × supports × passive tree); the look of each
    /// skill lives in <see cref="SkillVisuals"/>. Companions cast the same skills; only the local player's
    /// casts shake the screen, show toasts and the awakening banner.
    /// </summary>
    public class SkillCaster : MonoBehaviour
    {
        PlayerController owner;
        readonly float[] readyAt = new float[SkillGems.Slots];
        float castEnd;

        public bool IsCasting => Time.time < castEnd;

        public void Setup(PlayerController player) => owner = player;

        Progression Prog => owner.Data.Progression;

        /// <summary>True when the slot's skill is open, off cooldown and nothing else is being cast (AI companions).</summary>
        public bool IsReady(int slot) => Prog.Active(slot) != null && Time.time >= readyAt[slot] && !IsCasting;

        /// <summary>Screen shake for the local player's skills only.</summary>
        void Shake(float strength, float duration)
        {
            if (owner.IsLocal) Game.Camera?.Shake(strength, duration);
        }

        void Toast(string message)
        {
            if (owner.IsLocal) GameEvents.RaiseToast(message);
        }

        /// <summary>Clears every cooldown (new game, class change).</summary>
        public void ResetCooldowns()
        {
            for (int i = 0; i < readyAt.Length; i++) readyAt[i] = 0f;
            castEnd = 0f;
        }

        /// <summary>0..1 cooldown progress for the HUD (1 = ready).</summary>
        public float CooldownProgress(int slot, out float remaining)
        {
            remaining = Mathf.Max(0f, readyAt[slot] - Time.time);
            var gem = Prog.Active(slot);
            if (gem == null || remaining <= 0f) return 1f;
            var n = Numbers(slot);
            return 1f - remaining / Mathf.Max(0.01f, n.cooldown);
        }

        /// <summary>Final numbers of a slot's skill (also for a locked slot, as a preview).</summary>
        public SkillNumbers Numbers(int slot)
        {
            var prog = Prog;
            var gem = prog.Active(slot) ?? SkillGems.ForSlot(owner.Class, slot);
            return gem != null ? owner.Data.Stats.Skill(owner.Class, slot, gem, prog.Supports(slot)) : default;
        }

        public void TryCast(int slot)
        {
            var gem = Prog.Active(slot);
            if (gem == null)
            {
                if (!owner.IsLocal) return;
                var locked = SkillGems.ForSlot(owner.Class, slot);
                GameEvents.RaiseToast(slot == SkillGems.UltimateSlot
                    ? $"각성 기술은 Lv.{Progression.SlotLevel(slot)}에 열린다."
                    : $"아직 잠긴 스킬이다 — {locked?.name} (Lv.{Progression.SlotLevel(slot)}에 해금)");
                Game.Audio.PlaySfx("cancel");
                return;
            }
            if (Time.time < readyAt[slot] || IsCasting) return;
            var n = Numbers(slot);
            if (!owner.TrySpend(n.manaCost, n.usesLife))
            {
                if (!owner.IsLocal) return;
                GameEvents.RaiseToast(n.usesLife ? "HP가 부족하다." : "MP가 부족하다.");
                Game.Audio.PlaySfx("cancel");
                return;
            }
            readyAt[slot] = Time.time + n.cooldown;
            castEnd = Time.time + (gem.IsUltimate ? 0.6f : 0.25f);
            StartCoroutine(Cast(gem, n));
        }

        IEnumerator Cast(SkillGem gem, SkillNumbers n)
        {
            for (int i = 0; i <= n.repeats; i++)
            {
                if (owner.IsDead) yield break;
                switch (gem.id)
                {
                    case "whirl": Whirl(n); break;
                    case "slam": Slam(n); break;
                    case "wave": StartCoroutine(Wave(n, owner.AimDirection)); break;
                    case "cry": WarCry(n); break;
                    case "blades": yield return StartCoroutine(Blades(n, i == 0)); break;
                    case "arc": StartCoroutine(Arc(n)); break;
                    case "nova": Nova(n); break;
                    case "frostorb": StartCoroutine(FrostOrb(n)); break;
                    case "thunder": StartCoroutine(Thunder(n)); break;
                    case "meteor": yield return StartCoroutine(Meteor(n, i == 0)); break;
                }
                if (i < n.repeats) yield return new WaitForSeconds(0.28f);
            }
        }

        // ---------- Hits ----------

        void Hit(EnemyController enemy, SkillNumbers n, Vector2 from, float knockback)
        {
            if (enemy == null || enemy.IsDead) return;
            if (enemy.TakeDamage(new DamageInfo(n.damage, from, knockback, Team.Player, owner.gameObject)) && n.leechPct > 0)
                owner.Heal(Mathf.Max(1, n.damage * n.leechPct / 100));
        }

        static List<EnemyController> EnemiesInRadius(Vector2 center, float radius)
        {
            var list = new List<EnemyController>();
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead && e.isActiveAndEnabled && Vector2.Distance(e.Center, center) <= radius) list.Add(e);
            return list;
        }

        List<EnemyController> EnemiesByDistance(float range)
        {
            var list = EnemiesInRadius(owner.Center, range);
            Vector2 me = owner.Position;
            list.Sort((a, b) => Vector2.Distance(a.Position, me).CompareTo(Vector2.Distance(b.Position, me)));
            return list;
        }

        static IEnumerator After(float seconds, System.Action action)
        {
            yield return new WaitForSeconds(seconds);
            action();
        }

        readonly List<Collider2D> probe = new List<Collider2D>();

        /// <summary>True when a projectile at <paramref name="p"/> runs into a wall, cliff or prop (not water, characters, trees).</summary>
        bool Blocked(Vector2 p)
        {
            probe.Clear();
            Physics2D.OverlapCircle(p, 0.08f, new ContactFilter2D { useTriggers = false }, probe);
            foreach (var col in probe)
            {
                if (col == null || col.attachedRigidbody != null || col.gameObject.name == "Water") continue;
                if (col.GetComponentInParent<IDamageable>() != null) continue;
                return true;
            }
            return false;
        }

        int strikeCursor;

        /// <summary>Where the next strike of an awakening skill lands: mostly on monsters, the rest around the player.</summary>
        Vector2 NextTarget(float range, int index, int total)
        {
            if (index == 0) strikeCursor = 0;
            var enemies = EnemiesByDistance(range);
            if (enemies.Count > 0 && index < Mathf.CeilToInt(total * 0.75f))
                return enemies[strikeCursor++ % enemies.Count].Position + Random.insideUnitCircle * 0.25f;
            float ang = Random.Range(0f, Mathf.PI * 2f);
            return owner.Position + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * Random.Range(1.2f, Mathf.Max(1.5f, range));
        }

        static EnemyController Nearest(Vector2 from, float range, HashSet<EnemyController> exclude)
        {
            EnemyController best = null;
            float bestDist = range;
            foreach (var e in EnemyController.Active)
            {
                if (e == null || e.IsDead || !e.isActiveAndEnabled || (exclude != null && exclude.Contains(e))) continue;
                float d = Vector2.Distance(from, e.Center);
                if (d <= bestDist) { bestDist = d; best = e; }
            }
            return best;
        }

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

        // ================= Warrior =================

        /// <summary>회전 베기: spin once, cutting everything around the player.</summary>
        void Whirl(SkillNumbers n)
        {
            Vector2 c = owner.Center;
            Game.Audio.PlaySfx("swing");
            SkillVisuals.Whirl(c, owner.Position, n.radius);
            foreach (var e in EnemiesInRadius(c, n.radius))
            {
                Hit(e, n, c, 7f);
                SkillVisuals.SlashHit(e.Center, SkillVisuals.WhirlGold);
            }
            Shake(0.06f, 0.12f);
        }

        /// <summary>대지 강타: a shock wave that runs along the aim direction, cracking the ground as it goes.</summary>
        void Slam(SkillNumbers n)
        {
            Vector2 dir = owner.AimDirection;
            StartCoroutine(SlamWave(n, dir, owner.Position + dir * 0.6f));
        }

        IEnumerator SlamWave(SkillNumbers n, Vector2 dir, Vector2 start)
        {
            Game.Audio.PlaySfx("rock_break");
            Shake(0.16f, 0.22f);
            SkillVisuals.SlamImpact(start, n.radius);
            var hit = new HashSet<EnemyController>();
            int steps = Mathf.Max(4, Mathf.RoundToInt(n.range / 0.75f) + 1);
            Vector2 prev = start;
            for (int i = 0; i < steps; i++)
            {
                Vector2 p = start + dir * (n.range * i / (steps - 1));
                if (!owner.IsLocal) CameraFollow.ShakeMute++;
                try { SkillVisuals.SlamStep(prev, p, n.radius, i == steps - 1); }
                finally { if (!owner.IsLocal) CameraFollow.ShakeMute--; }
                foreach (var e in EnemiesInRadius(p + Vector2.up * 0.3f, n.radius))
                    if (hit.Add(e))
                    {
                        Hit(e, n, owner.Position, 9f);
                        SkillVisuals.EarthHit(e.Center);
                    }
                prev = p;
                if (i < steps - 1) yield return new WaitForSeconds(0.06f);
            }
        }

        /// <summary>검기: a crescent blade flies along the aim direction and cuts every monster it passes.</summary>
        IEnumerator Wave(SkillNumbers n, Vector2 dir)
        {
            Game.Audio.PlaySfx("swing");
            const float speed = 13f;
            Vector2 pos = owner.Center + dir * 0.4f;
            var blade = SkillVisuals.WaveBlade(pos, dir, n.radius);
            var hit = new HashSet<EnemyController>();
            float travelled = 0f, trail = 0f;
            while (travelled < n.range)
            {
                float step = speed * Time.deltaTime;
                pos += dir * step;
                travelled += step;
                blade.MoveTo(pos);
                trail -= Time.deltaTime;
                if (trail <= 0f) { trail = 0.03f; SkillVisuals.WaveTrail(pos, dir, n.radius); }
                foreach (var e in EnemiesInRadius(pos, n.radius))
                    if (hit.Add(e))
                    {
                        Hit(e, n, pos - dir, 6f);
                        SkillVisuals.SlashHit(e.Center, SkillVisuals.WhirlGold);
                    }
                if (Blocked(pos)) break;
                yield return null;
            }
            SkillVisuals.WaveEnd(blade, pos, dir, n.radius);
        }

        /// <summary>전쟁 함성: stun everything nearby and raise all damage for a while.</summary>
        void WarCry(SkillNumbers n)
        {
            Vector2 c = owner.Center;
            Game.Audio.PlaySfx("rock_break");
            SkillVisuals.WarCry(c, owner.Position, n.radius);
            Shake(0.12f, 0.25f);
            foreach (var e in EnemiesInRadius(c, n.radius))
            {
                Hit(e, n, c, 8f);
                e.Stun(n.stun);
                StunStars.Attach(e);
            }
            // The buff is this member's own; the shout also pulls every monster around onto the caster.
            owner.Data.ApplyDamageBuff(n.buffPct, n.buffTime);
            ThreatTable.WarCry(owner, c, n.radius);
            BuffAura.Attach(owner.transform, new Color(1f, 0.45f, 0.2f, 1f), n.buffTime);
            Toast($"전쟁 함성!  {n.buffTime:0}초 동안 피해 {n.buffPct}% 증가");
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

        /// <summary>번개 사슬: strike the nearest enemy, then jump from monster to monster.</summary>
        IEnumerator Arc(SkillNumbers n)
        {
            Game.Audio.PlaySfx("magic");
            Vector2 from = owner.Center;
            SkillVisuals.CastCircle(owner.Position, SkillVisuals.MageViolet);
            SkillVisuals.StaffFlash(from + owner.AimDirection * 0.35f, SkillVisuals.ArcGlow);
            var hit = new HashSet<EnemyController>();
            EnemyController current = Nearest(from, n.range, hit);
            if (current == null)
            {
                // Nothing in range: a short zap in the aim direction so the cast still reads.
                SkillVisuals.ArcBolt(from, from + owner.AimDirection * 2.5f, false);
                yield break;
            }
            for (int jump = 0; current != null && jump <= n.chains; jump++)
            {
                Vector2 to = current.Center;
                SkillVisuals.ArcBolt(from, to, true);
                Hit(current, n, from, 3f);
                hit.Add(current);
                if (jump == 0) Shake(0.04f, 0.08f);
                from = to;
                if (jump == n.chains) break;
                // A short beat between jumps so the chain visibly travels.
                yield return new WaitForSeconds(0.06f);
                current = Nearest(from, n.radius, hit);
            }
        }

        /// <summary>서리 폭발: damage and freeze everything around the player.</summary>
        void Nova(SkillNumbers n)
        {
            Vector2 c = owner.Center;
            Game.Audio.PlaySfx("magic");
            SkillVisuals.Nova(c, owner.Position, n.radius);
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
            Vector2 dir = target != null ? (target.Center - from).normalized : owner.AimDirection;
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
                    for (int k = 0; k < near.Count && k < Mathf.Max(1, n.chains); k++)
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
            Game.Audio.PlaySfx("rock_break");
            Shake(0.09f, 0.14f);
            foreach (var e in EnemiesInRadius(pos, n.radius))
            {
                Hit(e, n, pos, 3f);
                e.Freeze(n.freeze);
                if (!e.IsDead && e.IsFrozen) IceEncase.Attach(e);
            }
        }

        /// <summary>낙뢰: lightning from the sky on several nearby monsters at once, stunning them briefly.</summary>
        IEnumerator Thunder(SkillNumbers n)
        {
            Game.Audio.PlaySfx("magic");
            SkillVisuals.CastCircle(owner.Position, SkillVisuals.ArcGlow);
            int strikes = 1 + n.chains;
            var hit = new HashSet<EnemyController>();
            int done = 0;
            foreach (var e in EnemiesByDistance(n.range))
            {
                if (done >= strikes) break;
                if (e == null || e.IsDead || hit.Contains(e)) continue;
                Vector2 ground = e.Position;
                SkillVisuals.Thunder(ground);
                foreach (var t in EnemiesInRadius(ground + Vector2.up * 0.3f, n.radius))
                    if (hit.Add(t))
                    {
                        Hit(t, n, ground, 3f);
                        t.Stun(n.stun);
                        StunStars.Attach(t);
                    }
                done++;
                Shake(0.05f, 0.08f);
                yield return new WaitForSeconds(0.08f);
            }
            if (done == 0)
                for (int k = 0; k < 2; k++)
                {
                    SkillVisuals.Thunder(owner.Position + owner.AimDirection * (1.6f + k * 1.2f) + Random.insideUnitCircle * 0.4f);
                    yield return new WaitForSeconds(0.08f);
                }
        }

        /// <summary>메테오 (awakening): burning rocks crash down across the area around the player.</summary>
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
