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
            if (owner.IsLocal) GameEvents.RaiseToast("검기 태세 ON · 기본 공격이 검기 베기로 바뀝니다");
        }

        public void StopStance(bool announce)
        {
            if (!stanceOn) return;
            stanceOn = false;
            if (announce && owner != null && owner.IsLocal) GameEvents.RaiseToast("검기 태세 OFF");
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
            if (!owner.TrySpend(n.manaCost, n.usesLife))
            {
                StopStance(false);
                if (owner.IsLocal) GameEvents.RaiseToast("MP가 부족해 검기 태세가 풀렸습니다.");
                return;
            }
            var run = new Run { s = stanceSkill, n = n, dir = dir, map = Game.Session.MapId, version = castVersion, authority = !PartyNet.IsMember };
            int count = comboStage >= 2 ? 3 : 1;
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
            var blade = CareerFx.Clip("f_wave", at, dir, size, 16f, VfxLayer.Top, true, new Color(.7f, .88f, 1f, .9f), false, 10f, true);
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
