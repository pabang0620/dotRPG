using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Shared materials for the uGUI layer. <see cref="Sharp"/> is the crisp pixel-art material used on
    /// every Image / RawImage that shows high-resolution (density-2) UI art (frames, slots, buttons,
    /// tooltips, icons, gems, the minimap). See UISharp.shader — it keeps uGUI masking, RectMask2D
    /// clipping and vertex-colour tint working, so it is a safe drop-in for the default UI material.
    /// Falls back to the default UI material (null) when the shader is missing or unsupported, in
    /// which case the art simply renders on the built-in shader (slightly softer, still correct).
    /// </summary>
    public static class UiMaterials
    {
        static Material sharp;
        static bool sharpLoaded;

        public static Material Sharp
        {
            get
            {
                if (sharpLoaded) return sharp;
                sharpLoaded = true;
                var shader = Resources.Load<Shader>("Shaders/UISharp");
                if (shader == null) shader = Shader.Find("DotRPG/UISharp");
                if (shader != null && shader.isSupported) sharp = new Material(shader) { name = "UISharp" };
                else Debug.LogWarning("[dotRPG] UI sharp shader missing; high-resolution UI art uses the default UI material.");
                return sharp;
            }
        }
    }
}
