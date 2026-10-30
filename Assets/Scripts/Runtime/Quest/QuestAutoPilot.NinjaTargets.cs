using UnityEngine;

namespace DotRPG
{
    public sealed partial class QuestAutoPilot
    {
        // Cumulative diagnostics: callers record per-run deltas. Requests are not confirmed casts or hits.
        internal static int NinjaLeapRequests, NinjaLeapNoTargetSkips;

        /// <summary>A promoted ninja (kunai thrower) whose throw at <paramref name="target"/> would hit a wall first.</summary>
        static bool NinjaLineBlocked(PlayerController p, EnemyController target) =>
            BasicReach(p) == NinjaReach && CareerCombat.KunaiPathBlocked(p.Center, target.Center);

        static bool HasNinjaLeapTarget(PlayerController player, SkillGem gem, int slot)
        {
            if (gem == null || gem.id != "leap" || player.Class != CharacterClass.Rogue
                || !CareerCombat.For(player).ThrowsKunai) return true;
            float radius = player.Skills.Numbers(slot).radius;
            // Match NinjaLeap's EnemiesInRadius search, including center offsets and the exact radius boundary.
            foreach (var enemy in EnemyController.Active)
                if (enemy != null && !enemy.IsDead && enemy.isActiveAndEnabled
                    && Vector2.Distance(enemy.Center, player.Center) <= radius)
                {
                    NinjaLeapRequests++;
                    return true;
                }
            NinjaLeapNoTargetSkips++;
            MeasureDecision("ninja_leap_no_target");
            return false;
        }
    }
}
