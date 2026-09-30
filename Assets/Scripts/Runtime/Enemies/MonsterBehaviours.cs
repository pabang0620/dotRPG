using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>황금 해골: never attacks, runs from the party and spills gold on every hit (big pile on death).</summary>
    public sealed class GoldRunnerBehaviour : MonsterBehaviour
    {
        const float TurnEvery = 1.1f;
        float turnAt, side = 1f;

        /// <summary>Gold piles spilled by hits so far (tests).</summary>
        public int GoldSpilled { get; private set; }

        protected override void Engaged(PlayerController target, float dist)
        {
            if (dist > Def.keepDistance * 2.4f)
            {
                E.GiveUp();
                return;
            }
            if (dist < Def.keepDistance)
            {
                if (Time.time >= turnAt)
                {
                    turnAt = Time.time + TurnEvery;
                    side = E.Rng.Next(2) == 0 ? -1f : 1f;
                }
                // Away from the chaser, veering to one side so it zig-zags.
                Vector2 away = Dir(target.Position, E.Position);
                MoveAvoiding(Rotate(away, 35f * side), E.Speed);
            }
            else
            {
                E.MonsterHalt();
                E.MonsterFace(target.Position);
            }
            E.MonsterAutoAnim();
        }

        public override void OnHit(DamageInfo info)
        {
            if (Def.goldPerHitMax <= 0 || E.Health.IsDead) return;
            var parent = Game.World != null && Game.World.ObjectsRoot != null ? Game.World.ObjectsRoot : transform.parent;
            int amount = E.Rng.Next(Def.goldPerHitMin, Def.goldPerHitMax + 1);
            Pickup.Create(ConsumableDatabase.Gold, amount, E.Position + new Vector2(0f, 0.2f), parent);
            GoldSpilled++;
            Fx.Sparkle(E.Center, 2, 0.4f);
            turnAt = 0f; // panic: pick a new direction
        }
    }

    /// <summary>해골 광부: walks up, swings the pickaxe up close, and from mid range telegraphs a straight charge.</summary>
    public sealed class ChargerBehaviour : MonsterBehaviour
    {
        const float ChargeLength = 6f, ChargeWidth = 1.0f, ChargeWindup = 0.85f, ContactRadius = 0.75f, ChargeMul = 1.3f, ChargeKnock = 9f;
        const float MinChargeDist = 1.8f, MaxChargeDist = 6.5f;

        protected override void OnInit() => nextSkillAt = Time.time + 1f;

        protected override void Engaged(PlayerController target, float dist)
        {
            if (dist > Def.loseInterest) { E.GiveUp(); return; }
            if (Time.time >= nextSkillAt && dist >= MinChargeDist && dist <= MaxChargeDist)
            {
                nextSkillAt = Time.time + Def.skillInterval;
                DevCharge(Dir(E.Position, target.Position));
                return;
            }
            if (dist <= Def.attackRange)
            {
                Run(Swing(target, Def.windup, Def.attackRange * 0.9f + 0.35f, 1f, Def.recover));
                return;
            }
            E.MonsterMoveTo(target.Position, E.Speed);
            E.MonsterAutoAnim();
        }

        /// <summary>Starts a charge in <paramref name="dir"/> now (AI and tests).</summary>
        public void DevCharge(Vector2 dir) =>
            Run(Charge(dir.normalized, ChargeLength, ChargeWidth, ChargeWindup, Def.projectileSpeed, ContactRadius, ChargeMul, ChargeKnock, 0.9f));
    }

    /// <summary>해골 사령술사: keeps its distance, lobs slow soul bolts and raises up to two skeletons about every 10 s.</summary>
    public sealed class NecroBehaviour : MonsterBehaviour
    {
        const float FirstSummon = 1.5f, SummonCast = 1.0f, BoltCast = 0.55f, BoltRange = 10f, BoltRadius = 0.3f;
        float nextSummonAt, strafeAt, strafe = 1f;

        /// <summary>Skeletons raised in total (tests).</summary>
        public int Summoned { get; private set; }

        protected override void OnInit()
        {
            nextSummonAt = Time.time + FirstSummon;
            nextSkillAt = Time.time + 1f;
        }

        public int AliveMinions => CountMinions(E);

        public static int CountMinions(EnemyController summoner)
        {
            int n = 0;
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead && e.Summoner == summoner && e.Def != null && e.Def.kind != MonsterKind.Totem) n++;
            return n;
        }

        protected override void Engaged(PlayerController target, float dist)
        {
            if (dist > Def.loseInterest) { E.GiveUp(); return; }
            if (Time.time >= nextSummonAt)
            {
                nextSummonAt = Time.time + Def.summonInterval;
                int want = Def.maxMinions - AliveMinions;
                if (want > 0)
                {
                    Run(Summon(want));
                    return;
                }
            }
            if (Time.time >= nextSkillAt && dist < BoltRange * 0.9f)
            {
                nextSkillAt = Time.time + Def.skillInterval;
                Run(Bolt(target));
                return;
            }
            KeepDistance(target, dist);
        }

        void KeepDistance(PlayerController target, float dist)
        {
            float keep = Def.keepDistance;
            if (dist < keep - 1f) MoveAvoiding(Dir(target.Position, E.Position), E.Speed);
            else if (dist > keep + 2f) E.MonsterMoveTo(target.Position, E.Speed);
            else
            {
                if (Time.time >= strafeAt) { strafeAt = Time.time + NextFloat(1.2f, 2.2f); strafe = E.Rng.Next(2) == 0 ? -1f : 1f; }
                Vector2 to = Dir(E.Position, target.Position);
                MoveAvoiding(new Vector2(-to.y, to.x) * strafe, E.Speed * 0.4f);
                E.MonsterFace(target.Position);
            }
            E.MonsterAutoAnim();
        }

        IEnumerator Summon(int count)
        {
            var spots = new List<Vector2>();
            for (int i = 0; i < count; i++)
            {
                float a = (i * 150f + 60f) * Mathf.Deg2Rad;
                spots.Add(FreeNear(E.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f) * 1.4f));
            }
            foreach (var s in spots) MonsterFx.SummonCircle(s, SummonCast + 0.3f);
            Game.Audio.PlaySfx("magic");
            yield return Hold(SummonCast, CharacterAnim.Attack, true);
            foreach (var s in spots)
            {
                if (AliveMinions >= Def.maxMinions) break;
                var m = MonsterDatabase.SpawnMinion(Def.minionId, s, E.transform.parent, E);
                MonsterFx.Rise(m);
                Summoned++;
            }
            yield return Hold(0.4f);
        }

        IEnumerator Bolt(PlayerController target)
        {
            E.MonsterFace(target.Position);
            yield return Hold(BoltCast, CharacterAnim.Attack, true);
            if (target == null) yield break;
            Game.Audio.PlaySfx("magic");
            Vector2 from = E.Center + E.MonsterFacing.ToVector() * 0.3f;
            MonsterProjectile.Fire(E, "mon_bolt", from, Dir(from, target.Center), Def.projectileSpeed, BoltRange, BoltRadius,
                E.MonsterDamage(1f), 4f, new Color(0.45f, 1f, 0.55f, 0.7f));
            yield return Hold(0.35f, CharacterAnim.Attack);
        }
    }

    /// <summary>해골 궁수: keeps about 5 units away and shoots arrows along a short line telegraph.</summary>
    public sealed class ArcherBehaviour : MonsterBehaviour
    {
        const float AimTime = 0.7f, ArrowRange = 9f, LineWidth = 0.5f, ArrowRadius = 0.22f;
        float strafeAt, strafe = 1f;

        /// <summary>Arrows fired so far (tests).</summary>
        public int Shots { get; private set; }

        protected override void OnInit() => nextSkillAt = Time.time + 1.2f;

        protected override void Engaged(PlayerController target, float dist)
        {
            if (dist > Def.loseInterest) { E.GiveUp(); return; }
            float keep = Def.keepDistance;
            if (Time.time >= nextSkillAt && dist < ArrowRange - 0.5f && dist > 2f)
            {
                nextSkillAt = Time.time + Def.skillInterval;
                Run(Shoot(target));
                return;
            }
            if (dist < keep - 1f) MoveAvoiding(Dir(target.Position, E.Position), E.Speed);
            else if (dist > keep + 1.5f) E.MonsterMoveTo(target.Position, E.Speed);
            else
            {
                if (Time.time >= strafeAt) { strafeAt = Time.time + NextFloat(1f, 2f); strafe = E.Rng.Next(2) == 0 ? -1f : 1f; }
                Vector2 to = Dir(E.Position, target.Position);
                MoveAvoiding(new Vector2(-to.y, to.x) * strafe, E.Speed * 0.35f);
                E.MonsterFace(target.Position);
            }
            E.MonsterAutoAnim();
        }

        IEnumerator Shoot(PlayerController target)
        {
            Vector2 dir = Dir(E.Position, target.Position);
            E.MonsterFace(target.Position);
            Track(Telegraph.Rect(E, E.Position, dir, ArrowRange, LineWidth, AimTime, 0));
            yield return Hold(AimTime, CharacterAnim.Attack, true);
            Game.Audio.PlaySfx("swing");
            MonsterProjectile.Fire(E, "mon_arrow", E.Center + dir * 0.4f, dir, Def.projectileSpeed, ArrowRange, ArrowRadius,
                E.MonsterDamage(1f), 5f, new Color(0f, 0f, 0f, 0f));
            Shots++;
            yield return Hold(0.4f, CharacterAnim.Attack);
        }
    }

    /// <summary>
    /// 해골 방패병: holds a tower shield towards its target. Hits from the front lose 80% of their damage and
    /// knockback and do not stagger; hits from behind or the sides land fully. Drops the guard to bash.
    /// </summary>
    public sealed class ShieldGuardBehaviour : MonsterBehaviour
    {
        /// <summary>Share of damage / knockback a frontal hit keeps, and the half-angle of "frontal".</summary>
        public const float FrontalKeep = 0.2f, FrontalHalfAngle = 60f;
        const float TurnRate = 200f;
        Vector2 guardDir = Vector2.down;
        bool bashing;

        public Vector2 GuardDirection => guardDir;
        public bool Guarding => !bashing;
        public override bool SuperArmorNow => false;

        public bool IsFrontal(DamageInfo info)
        {
            Vector2 to = info.sourcePosition - E.Position;
            return to.sqrMagnitude > 0.0001f && Vector2.Angle(guardDir, to) <= FrontalHalfAngle;
        }

        public override bool ArmorAgainst(DamageInfo info) => Guarding && IsFrontal(info);

        public override bool BeforeDamage(ref DamageInfo info)
        {
            if (!Guarding || !IsFrontal(info)) return true;
            info.amount = Mathf.Max(1, Mathf.RoundToInt(info.amount * FrontalKeep));
            info.knockback *= FrontalKeep;
            Game.Audio.PlaySfx("mine");
            SkillVisuals.Sparks(E.Center + guardDir * 0.35f, new Color(1f, 0.9f, 0.6f, 1f), 4, 3f);
            return true;
        }

        public override bool Tick(bool engaged)
        {
            var target = E.MonsterTarget;
            if (target != null && !bashing)
            {
                Vector2 want = Dir(E.Position, target.Position);
                guardDir = Vector3.RotateTowards(guardDir, want, TurnRate * Mathf.Deg2Rad * Time.deltaTime, 0f);
            }
            return base.Tick(engaged);
        }

        protected override void Engaged(PlayerController target, float dist)
        {
            if (dist > Def.loseInterest) { E.GiveUp(); return; }
            if (dist <= Def.attackRange)
            {
                Run(Bash(target));
                return;
            }
            E.MonsterMoveTo(target.Position, E.Speed);
            E.MonsterFace(E.Position + guardDir);
            E.MonsterAutoAnim();
        }

        IEnumerator Bash(PlayerController target)
        {
            bashing = true;
            yield return Swing(target, Def.windup, Def.attackRange * 0.9f + 0.35f, 1f, 0.2f);
            bashing = false;
            yield return Hold(Def.recover - 0.2f);
        }

        protected override void OnInterrupted() => bashing = false;
    }

    /// <summary>해골 기사 (raid elite): two-hit cone combo with super armor from the wind-up to the end.</summary>
    public sealed class KnightBehaviour : MonsterBehaviour
    {
        const float Hit1Radius = 1.8f, Hit2Radius = 2.2f, Hit1Angle = 100f, Hit2Angle = 140f, Hit2Mul = 1.3f, Hit2Windup = 0.3f;
        bool attacking;

        public bool Attacking => attacking;
        public override bool SuperArmorNow => attacking;

        protected override void Engaged(PlayerController target, float dist)
        {
            if (dist > Def.loseInterest) { E.GiveUp(); return; }
            if (dist <= Def.attackRange + 0.2f && Time.time >= nextSkillAt)
            {
                DevCombo(target);
                return;
            }
            E.MonsterMoveTo(target.Position, E.Speed);
            E.MonsterAutoAnim();
        }

        /// <summary>Starts the combo at <paramref name="target"/> now (AI and tests).</summary>
        public void DevCombo(PlayerController target) => Run(Combo(target));

        IEnumerator Combo(PlayerController target)
        {
            attacking = true;
            Vector2 dir = Dir(E.Position, target.Position);
            E.MonsterFace(target.Position);
            Game.Audio.PlaySfx("enemy_windup");
            var t1 = Track(Telegraph.Cone(E, E.Position, dir, Hit1Radius, Hit1Angle, Def.windup, E.MonsterDamage(1f), 7f));
            t1.unblockable = false;
            yield return Hold(Def.windup, CharacterAnim.Attack, true);
            Game.Audio.PlaySfx("enemy_attack");
            Fx.Spawn("fx_slash", E.Center + dir * 0.7f, Vector2.zero, 0f, 0.15f, 0f, 10);
            // Second, wider swing: re-aim a little.
            if (target != null && !target.IsDead) dir = Vector3.RotateTowards(dir, Dir(E.Position, target.Position), 40f * Mathf.Deg2Rad, 0f);
            var t2 = Track(Telegraph.Cone(E, E.Position, dir, Hit2Radius, Hit2Angle, Hit2Windup, E.MonsterDamage(Hit2Mul), 9f));
            t2.unblockable = false;
            yield return Hold(Hit2Windup, CharacterAnim.Attack, true);
            Game.Audio.PlaySfx("enemy_attack");
            Fx.Spawn("fx_slash", E.Center + dir * 0.9f, Vector2.zero, 0f, 0.15f, 0f, 10);
            yield return Hold(0.2f, CharacterAnim.Attack);
            attacking = false;
            nextSkillAt = Time.time + 0.6f;
            yield return Hold(Def.recover);
        }

        protected override void OnInterrupted() => attacking = false;
    }

    /// <summary>사령 토템: a static object. It never moves or attacks; the boss owns the revive rule.</summary>
    public sealed class TotemBehaviour : MonsterBehaviour
    {
        float pulseAt;

        public override bool SuperArmorNow => true;

        public override bool Tick(bool engaged)
        {
            E.MonsterHalt();
            E.MonsterAnim(CharacterAnim.Idle);
            if (Time.time >= pulseAt)
            {
                pulseAt = Time.time + 0.9f;
                SkillFx.Spawn("fx_glow", E.Position + new Vector2(0f, 1f), new Color(0.45f, 1f, 0.55f, 0.35f), 0.8f, SkillFx.At(E.Position.y, -1))
                    .Additive().Scale(1.2f, 1.8f).Fade(FxFade.InOut);
            }
            return true;
        }

        protected override void Engaged(PlayerController target, float dist) { }
    }

    /// <summary>Shared monster effects: summon circles and the "rise from the ground" pop.</summary>
    public static class MonsterFx
    {
        public static void SummonCircle(Vector2 at, float life)
        {
            SkillFx.Spawn("mon_summon", at + new Vector2(0f, 0.1f), new Color(1f, 1f, 1f, 0.9f), life, SkillFx.GroundOrder + 70)
                .Scale(0.4f, 1f).Spin(60f).Fade(FxFade.InOut);
            SkillFx.Spawn("fx_glow", at + new Vector2(0f, 0.4f), new Color(0.7f, 0.45f, 1f, 0.5f), life, SkillFx.At(at.y, 30))
                .Additive().Scale(0.8f, 1.6f).Fade(FxFade.InOut);
        }

        public static void Rise(EnemyController e)
        {
            if (e == null) return;
            Fx.Burst("fx_dust", e.Position + new Vector2(0f, 0.2f), 5, 2f, 0.5f);
            Fx.Burst("fx_bone", e.Position + new Vector2(0f, 0.3f), 3, 2f, 0.5f);
            SkillVisuals.Flash(e.Center, new Color(0.75f, 0.5f, 1f, 0.8f), 1.2f);
        }
    }
}
