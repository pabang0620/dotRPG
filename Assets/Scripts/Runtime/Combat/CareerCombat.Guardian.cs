using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class CareerCombat
    {
        // The saved skill ID stays g_awake; its former protection field becomes an offensive slam.
        IEnumerator GuardianQuake(CareerSkill skill, SkillNumbers numbers, string map, bool authority)
        {
            int version = NewField(skill.id);
            Vector2 origin = owner.Center;
            float radius = Mathf.Max(.1f, numbers.radius);
            var struck = new HashSet<EnemyController>();
            GuardianAwakeningFx.Impact(owner, origin, radius);
            CareerAreaView.Show(skill, origin, radius, .65f, owner);
            if (owner.IsLocal) Game.Camera?.CareerImpulse(Vector2.down, .09f, .16f);
            const float propagation = .32f;
            var wave = CareerRenewalFx.Make(skill, origin, 1, Vector2.one * .2f, .55f, owner);
            for (float elapsed = 0; elapsed < propagation; elapsed += Time.deltaTime)
            {
                if (!FieldValid(map, skill.id, version)) yield break;
                float waveRadius = radius * Mathf.Clamp01((elapsed + Time.deltaTime) / propagation);
                if (wave != null) wave.Resize(new Vector2(waveRadius * 2, waveRadius * 1.05f));
                foreach (var enemy in Enemies(origin, waveRadius))
                    if (struck.Add(enemy))
                        StrikeEnemy(skill, enemy, numbers.damage, origin,
                            (enemy.Center - origin).normalized, authority);
                yield return null;
            }
        }
    }
}
