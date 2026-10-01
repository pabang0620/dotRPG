using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Assigns the crisp-scaling material (<see cref="FxMaterials.Sharp"/>) to any runtime-created
    /// sprite renderer whose sprite is high-resolution (density 2 → pixelsPerUnit &gt; 16.5), so
    /// characters, monsters, held weapons/tools and their shadows stay sharp at the camera's
    /// non-integer scale — the same rule <see cref="SpriteLibrary"/> and WorldBuilder use for map art.
    /// A density-1 sprite is left on the default material (point filtered). Never called on additive
    /// glow sprites (those live in the skill-effect system, not here).
    /// </summary>
    public static class HdMaterial
    {
        public static void Apply(SpriteRenderer sr)
        {
            if (sr == null || sr.sprite == null) return;
            var sharp = FxMaterials.Sharp;
            if (sharp != null && sr.sprite.pixelsPerUnit > 16.5f)
            {
                if (sr.sharedMaterial != sharp) sr.sharedMaterial = sharp;
            }
        }
    }
}
