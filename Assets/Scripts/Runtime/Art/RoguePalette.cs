using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ROGUE] Career looks of the rogue (Docs/PLAN_CLASS_ROGUE.md 1.5): the same body recoloured, only the cool cloth
    /// (the red scarf and wraps of the ash-silver base). The ash-silver hair, skin and leather keep their colours, so the shape and the face
    /// stay the rogue's own. Assassin magenta, ninja orange.
    /// </summary>
    public static class RoguePalette
    {
        public static bool Handles(string sheet) => sheet == "career_assassin" || sheet == "career_ninja";

        public static Color32 Apply(Color32 pixel, string sheet)
        {
            if (pixel.a == 0 || !Handles(sheet)) return pixel;
            Color.RGBToHSV(pixel, out float h, out float s, out float v);
            // The base rogue (ash-silver bob, 2026-10-24) wears a saturated red scarf and wraps (hue about 330-360,
            // saturation 0.45+). Skin and leather sit at hue 0-20 with lower saturation and stay as they are.
            float hue = h * 360f;
            if (s < .45f || hue < 330f) return pixel;
            float target, gain, saturation;
            if (sheet == "career_assassin") { target = 300f; gain = 1.1f; saturation = Mathf.Min(1f, s * .95f); }
            else { target = 26f; gain = 1.25f; saturation = Mathf.Min(1f, s * 1.05f); }
            Color32 result = Color.HSVToRGB(target / 360f, saturation, Mathf.Clamp01(v * gain));
            result.a = pixel.a;
            return result;
        }
    }
}
