using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// 검기 태세 (파이터, on/off): while on, every basic attack swing also throws a piercing crescent forward
    /// (three on the third swing of the combo). Each crescent costs the skill's MP; the stance ends when MP runs out.
    /// </summary>
    public sealed partial class CareerCombat
    {
        bool stanceOn;
        CareerSkill stanceSkill;
        SkillNumbers stanceNumbers;

        public bool StanceOn => stanceOn;

        void Stance(Run c)
        {
            stanceOn = true;
            stanceSkill = c.s;
            stanceNumbers = c.n;
            Sound("c_heavy", .7f);
            SkillVisuals.Flash(owner.Center, CareerFx.Steel, 1.6f, .2f);
            SkillVisuals.Sparks(owner.Center, CareerFx.Steel, 10, 4f, .3f);
            if (owner.IsLocal) GameEvents.RaiseToast(L.T("검기 태세 ON · 기본 공격이 검기 베기로 바뀝니다"));
        }

        public void StopStance(bool announce)
        {
            if (!stanceOn) return;
            stanceOn = false;
            if (announce && owner != null && owner.IsLocal) GameEvents.RaiseToast(L.T("검기 태세 OFF"));
        }

        void StanceTick()
        {
            if (!stanceOn) return;
            if (owner.IsDead || Mine != Career.Fighter || stanceSkill == null || !Prog.CareerUnlocked(stanceSkill)) { StopStance(false); return; }
            PowerAura.Play(owner, .2f, new Color(.45f, .7f, 1f), new Color(.85f, .95f, 1f));
        }

        /// <summary>Called at the contact of each basic swing (PlayerCombat). Spends MP and throws the crescents.</summary>
        public void OnBasicStrike(Vector2 dir, int comboStage)
        {
            if (!stanceOn || stanceSkill == null) return;
            var n = stanceNumbers;
            // A puppet only shows the look: its MP is not tracked, so it never pays or switches off here.
            if (!owner.NetPuppet && !owner.TrySpend(n.manaCost, n.usesLife))
            {
                StopStance(false);
                if (owner.IsLocal) GameEvents.RaiseToast(L.T("MP가 부족해 검기 태세가 풀렸습니다."));
                return;
            }
            // [CH3 SPEC] A 응축 태세: one crescent, 1.6 (3.3 on the third swing) of 1.1. B 수호 태세: one crescent of 1.0 whose
            // hits build a shield (1% a hit, up to 10%; CareerCombat.SpecWarrior.SpecLanded).
            if (n.spec == 'a') n.damage = Mathf.Max(1, Mathf.RoundToInt(n.damage * (comboStage >= 2 ? 3.3f : 1.6f) / 1.1f));
            else if (n.spec == 'b') n.damage = Mathf.Max(1, Mathf.RoundToInt(n.damage * 1.0f / 1.1f));
            var run = new Run { s = stanceSkill, n = n, dir = dir, map = Game.Session.MapId, version = castVersion, authority = !PartyNet.IsMember };
            int count = n.spec != '\0' ? 1 : comboStage >= 2 ? 3 : 1;
            for (int i = 0; i < count; i++)
            {
                float tilt = count == 1 ? 0f : (i - 1) * 18f;
                StartCoroutine(StanceWave(run, CareerFx.Tilt(dir, tilt)));
            }
            Sound("c_slash", .5f);
        }

        /// <summary>One crescent: flies straight out to the skill's range and cuts every monster it passes once.</summary>
        IEnumerator StanceWave(Run c, Vector2 dir)
        {
            const float speed = 16f;
            Vector2 at = owner.Center + dir * .4f;
            float size = c.n.radius / .95f;
            // [VFX] A thin quick azure slash (f2_stance_wave), so it doesn't read as 파쇄 검기; else the tinted f_wave.
            bool drawn = VfxLibrary.Has("f2_stance_wave");
            var blade = CareerFx.Gen("f2_stance_wave", "f_wave", at, dir, drawn ? c.n.radius * 2.1f : size, size, 16f, VfxLayer.Top, true,
                c.n.spec != '\0' ? Tint(c, Color.white) : drawn ? Color.white : new Color(.7f, .88f, 1f, .9f), 10f, true);
            var hit = new HashSet<EnemyController>();
            float travelled = 0f;
            while (travelled < c.n.range)
            {
                if (!Live(c)) { blade?.Stop(); yield break; }
                float step = speed * Time.deltaTime;
                Vector2 next = at + dir * step;
                blade?.Place(next);
                foreach (var e in Corridor(at, next, c.n.radius))
                    if (hit.Add(e)) Strike(c, e, c.n.damage, e.Center - dir, 4f, 1, "c_slash");
                at = next;
                travelled += step;
                yield return null;
            }
            blade?.Stop();
        }
    }
}
