using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ROGUE] Career looks of the rogue (Docs/PLAN_CLASS_ROGUE.md 1.5): the same body recoloured, only the cool cloth
    /// (the navy hood cloak and wraps). The ash-silver hair, skin and leather keep their colours, so the shape and the face
    /// stay the rogue's own. Assassin magenta, ninja orange.
    /// </summary>
    public static class RoguePalette
    {
        public static bool Handles(string sheet) => sheet == "career_assassin" || sheet == "career_ninja";

        public static Color32 Apply(Color32 pixel, string sheet)
        {
            if (pixel.a == 0 || !Handles(sheet)) return pixel;
            Color.RGBToHSV(pixel, out float h, out float s, out float v);
            // The cloth is a saturated blue (hue about 205-225, saturation 0.5+); the silver hair is blue-grey but nearly
            // unsaturated (under 0.15), so it is left alone.
            float hue = h * 360f;
            if (s < .3f || hue < 185f || hue > 250f) return pixel;
            float target, gain, saturation;
            if (sheet == "career_assassin") { target = 311f; gain = 1.45f; saturation = Mathf.Min(1f, s * .95f); }
            else { target = 26f; gain = 1.6f; saturation = Mathf.Min(1f, s * 1.05f); }
            Color32 result = Color.HSVToRGB(target / 360f, saturation, Mathf.Clamp01(v * gain));
            result.a = pixel.a;
            return result;
        }
    }
}
