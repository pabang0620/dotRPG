using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class BossBrain
    {
        // =============================== Patterns ===============================

        PlayerController Target => E.MonsterTarget;

        Vector2 AimDir() => Target != null ? Dir(E.Position, Target.Position) : E.MonsterFacing.ToVector();

        IEnumerator Slash3(BossPattern p)
        {
            for (int i = 0; i < p.count; i++)
            {
                Vector2 dir = AimDir();
                E.MonsterFace(E.Position + dir);
                float windup = (i == 0 ? 0.6f : 0.42f) * (Enraged ? 0.85f : 1f);
                Track(Telegraph.Cone(E, E.Position, dir, 3.0f * Scale, 110f, windup, E.MonsterDamage(p.damageMul), 7f));
                Game.Audio.PlaySfx("enemy_windup");
                yield return Hold(windup, CharacterAnim.Attack, true);
                Game.Audio.PlaySfx("enemy_attack");
                Fx.Spawn("fx_slash", E.Center + dir * 1.2f * Scale, Vector2.zero, 0f, 0.18f, 0f, 10);
                yield return Hold(0.12f, CharacterAnim.Attack);
            }
            yield return Hold(0.5f);
        }

        IEnumerator BossCharge(BossPattern p)
        {
            Vector2 dir = AimDir();
            yield return Charge(dir, 9f, 1.8f * Scale, Enraged ? 0.8f : 1.0f, 14f, 1.1f * Scale, p.damageMul, 10f, 0.7f);
        }

        /// <summary>[RAID] 파동 throw speed and time (about 11 tiles: the walls of a raid room stop it).</summary>
        public const float RepelSpeed = 20f, RepelTime = .55f, RepelWindup = 1.4f;

        /// <summary>
        /// [RAID] 수호자 그라흐's 파동: the boss gathers force (a room-wide warning), then everyone takes a hit and is thrown straight away
        /// from it to the walls. Only career invulnerability avoids it (the dash has none).
        /// </summary>
        IEnumerator Repel(BossPattern p)
        {
            Announce?.Invoke(this, L.T("파동!"), new Color(1f, .6f, .3f));
            SkillFx.Spawn("fx_ring", E.Position, new Color(1f, .55f, .25f, .8f), RepelWindup, SkillFx.GroundOrder + 12)
                .Scale(new Vector2(1f, .7f), new Vector2(9f * Scale, 6.3f * Scale));
            Game.Audio.PlaySfx("enemy_windup");
            yield return Hold(RepelWindup, CharacterAnim.Attack, true);
            Game.Audio.PlaySfx("rock_break");
            Game.Camera?.Shake(.35f, .4f);
            SkillVisuals.Flash(E.Center, new Color(1f, .6f, .3f, .8f), 10f, .3f);
            int damage = E.MonsterDamage(p.damageMul);
            foreach (var m in MonsterHits.All())
            {
                if (m == null || m.IsDead || m.Health.IsInvulnerable || CareerCombat.For(m).InvulnerableLeft > 0f) continue;
                Vector2 away = m.Position - E.Position;
                if (away.sqrMagnitude < .0001f) away = Vector2.down;
                MonsterHits.Apply(new[] { m }, damage, E.Position, 0f, true, E.gameObject);
                if (m.IsDead) continue;
                m.Launch(away.normalized * RepelSpeed, RepelTime);
                RaidSeal.RaiseLaunch(m, away.normalized * RepelSpeed, RepelTime);
            }
            yield return Hold(.6f, CharacterAnim.Attack);
        }

        public const float AnnihilateCast = 3.5f, AnnihilateSafeRadius = 1.4f;

        /// <summary>
        /// [RAID] 섬멸: a long cast that hits the whole room for most of a mage's HP. Cover is a guardian's 거대 방패 (강철의 보루:
        /// the boss's line of fire is cut by the shield) or one of the two teal safe circles the boss leaves open, so a party
        /// without a guardian still has a way out. Career invulnerability (1 s guard) also avoids it; the dash does not.
        /// </summary>
        IEnumerator Annihilate(BossPattern p)
        {
            float cast = AnnihilateCast * (Enraged ? .9f : 1f);
            string line = L.T("섬멸! 방패 뒤나 빛의 원 안으로 숨으세요!");
            RaidSeal.ShowLine(E, line);
            RaidSeal.RaiseLine(E, line);
            Track(Telegraph.Warning(E, E.Position, 13f * Scale, cast));
            float first = NextFloat(0f, Mathf.PI * 2f);
            for (int i = 0; i < 2; i++)
            {
                float a = first + i * Mathf.PI * NextFloat(.7f, 1.3f), r = NextFloat(3.5f, 5.5f) * Scale;
                Track(Telegraph.SafeCircle(E, FreeNear(E.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r), AnnihilateSafeRadius, cast + .3f));
            }
            SafeCover.BeginAnnihilate(E.Position, cast + .3f);
            Game.Audio.PlaySfx("enemy_windup");
            yield return Hold(cast, CharacterAnim.Attack, true);
            Game.Audio.PlaySfx("rock_break");
            Game.Camera?.Shake(.4f, .45f);
            SkillVisuals.Flash(E.Center, new Color(1f, .45f, .3f, .85f), 14f, .35f);
            // The same blast in every phase: enrage only makes it come sooner, not hit harder (numbers in MonsterDatabase).
            int damage = E.MonsterDamage(p.damageMul / DamageMul);
            foreach (var m in MonsterHits.All())
            {
                if (m == null || m.IsDead || m.Health.IsInvulnerable || CareerCombat.For(m).InvulnerableLeft > 0f) continue;
                if (SafeCover.Shelters(E.Position, m)) continue;
                MonsterHits.Apply(new[] { m }, damage, E.Position, 9f, true, E.gameObject);
            }
            SafeCover.EndAnnihilate();
            yield return Hold(.6f, CharacterAnim.Attack);
        }

        IEnumerator Slam(BossPattern p, float radius)
        {
            Track(Telegraph.Circle(E, E.Position, radius * Scale, 1.0f, E.MonsterDamage(p.damageMul), 9f));
            Game.Audio.PlaySfx("enemy_windup");
            yield return Hold(1.0f, CharacterAnim.Attack, true);
            Game.Audio.PlaySfx("rock_break");
            Game.Camera?.Shake(0.18f, 0.2f);
            Fx.Burst("fx_dust", E.Position + new Vector2(0f, 0.2f), 10, 4f, 0.6f);
            yield return Hold(0.6f, CharacterAnim.Attack);
        }

        IEnumerator Fields(BossPattern p, bool gold)
        {
            var spots = new List<Vector2>();
            foreach (var m in MonsterHits.All()) if (spots.Count < p.count) spots.Add(m.Position);
            while (spots.Count < p.count)
            {
                float a = NextFloat(0f, Mathf.PI * 2f), r = NextFloat(1.5f, 5f);
                spots.Add(FreeNear(E.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r));
            }
            float windup = 1.3f * (Enraged ? 0.85f : 1f);
            foreach (var s in spots)
            {
                var t = Track(Telegraph.Circle(E, s, 1.4f, windup, E.MonsterDamage(p.damageMul), 5f));
                if (gold) t.OnResolve += (tele, hit) => GoldNugget(tele.Origin);
                else t.OnResolve += (tele, hit) => SkillVisuals.Flash(tele.Origin + Vector2.up * 0.3f, SoulGlow, 2.2f, 0.25f);
            }
            Game.Audio.PlaySfx("magic");
            yield return Hold(windup, CharacterAnim.Attack, true);
            yield return Hold(0.5f, CharacterAnim.Attack);
        }

        void GoldNugget(Vector2 at)
        {
            Fx.Burst("fx_chip", at + new Vector2(0f, 0.2f), 4, 2.5f, 0.5f);
            SkillVisuals.Flash(at + Vector2.up * 0.3f, GoldGlow, 2.2f, 0.25f);
            // A little gold for the brave (deterministic: every other nugget).
            if (E.Rng.Next(2) == 0 && Game.World != null)
                Pickup.Create(ConsumableDatabase.Gold, E.Rng.Next(2, 6), at, Game.World.ObjectsRoot);
        }

        IEnumerator DonutPattern(BossPattern p)
        {
            Track(Telegraph.Donut(E, E.Position, 2.2f * Scale, 6.5f * Scale, 1.5f, E.MonsterDamage(p.damageMul), 8f));
            Game.Audio.PlaySfx("enemy_windup");
            yield return Hold(1.5f, CharacterAnim.Attack, true);
            Game.Audio.PlaySfx("rock_break");
            Game.Camera?.Shake(0.15f, 0.2f);
            yield return Hold(0.5f, CharacterAnim.Attack);
        }

        IEnumerator Volley(BossPattern p)
        {
            Vector2 dir = AimDir();
            E.MonsterFace(E.Position + dir);
            var dirs = new List<Vector2>();
            for (int i = 0; i < p.count; i++) dirs.Add(Rotate(dir, (i - (p.count - 1) * 0.5f) * 14f));
            foreach (var d in dirs) Track(Telegraph.Rect(E, E.Position, d, 10f, 0.6f, 0.8f, 0));
            yield return Hold(0.8f, CharacterAnim.Attack, true);
            Game.Audio.PlaySfx("swing");
            foreach (var d in dirs)
                MonsterProjectile.Fire(E, "mon_arrow", E.Center + d * 0.6f, d, 11f, 10f, 0.25f, E.MonsterDamage(p.damageMul), 5f, new Color(0f, 0f, 0f, 0f));
            yield return Hold(0.5f, CharacterAnim.Attack);
        }

        IEnumerator BoltRing(BossPattern p)
        {
            MonsterFx.SummonCircle(E.Position, 1f);
            Game.Audio.PlaySfx("magic");
            yield return Hold(0.8f, CharacterAnim.Attack, true);
            float offset = NextFloat(0f, 360f);
            for (int i = 0; i < p.count; i++)
            {
                Vector2 d = Rotate(Vector2.right, offset + i * 360f / p.count);
                MonsterProjectile.Fire(E, "mon_bolt_big", E.Center + d * 0.8f, d, 3.6f, 11f, 0.35f, E.MonsterDamage(p.damageMul), 4f, SoulGlow);
            }
            yield return Hold(0.6f, CharacterAnim.Attack);
        }

        IEnumerator Summon(int count)
        {
            int room = Def.maxMinions - NecroBehaviour.CountMinions(E);
            count = Mathf.Min(count, room);
            if (count <= 0) yield break;
            var spots = new List<Vector2>();
            for (int i = 0; i < count; i++)
            {
                float a = (i * 360f / count + NextFloat(0f, 40f)) * Mathf.Deg2Rad;
                spots.Add(FreeNear(E.Position + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * 0.7f) * 2.4f));
            }
            foreach (var s in spots) MonsterFx.SummonCircle(s, 1.3f);
            Game.Audio.PlaySfx("magic");
            yield return Hold(1.0f, CharacterAnim.Attack, true);
            string minion = Def.raid && Def.chapter < 3 ? (E.Rng.Next(3) == 0 ? "skel_knight" : "skel_warrior") : Def.minionId; // [CH3] 지바크 summons void hounds
            foreach (var s in spots)
            {
                var m = MonsterDatabase.SpawnMinion(minion, s, E.transform.parent, E);
                MonsterFx.Rise(m);
                Summoned++;
            }
            yield return Hold(0.4f);
        }

        IEnumerator Guard(BossPattern p)
        {
            Guarding = true;
            Announce?.Invoke(this, L.T("방패 태세!"), new Color(0.75f, 0.85f, 1f));
            float t = 0f;
            while (t < 2.2f)
            {
                if (E.CanAct)
                {
                    t += Time.deltaTime;
                    if (Target != null) E.MonsterFace(Target.Position);
                    E.MonsterHalt();
                    E.MonsterAnim(CharacterAnim.Idle);
                }
                yield return null;
            }
            Guarding = false;
            yield return Slam(p, 3.4f);
        }
    }
}
