using UnityEngine;

namespace DotRPG
{
    /// <summary>Career/common/rare colours share the approved body, pose and hand anchors.</summary>
    public static class FemaleWarriorPalette
    {
        public static readonly string[] Sheets = {
            "career_fighter", "career_guardian", "w_sand", "w_moss",
            "w_rust", "w_crimson", "w_amethyst", "w_brass"
        };

        public static bool UsesApprovedBody(string sheet)
        {
            foreach (var id in Sheets) if (sheet == id) return true;
            return false;
        }

        public static Color32 Apply(Color32 pixel, string sheet)
        {
            // Only cool/neutral cloth; warm hair, skin, ivory sash and gold trim stay intact.
            if (pixel.a == 0 || pixel.b < pixel.r || pixel.b < pixel.g || pixel.b < 24)
                return pixel;
            float hue, saturation, gain;
            switch (sheet)
            {
                case "career_fighter": hue = 215; saturation = .55f; gain = 1.45f; break;
                case "career_guardian": hue = 172; saturation = .6f; gain = 1.4f; break;
                case "w_sand": hue = 36; saturation = .3f; gain = 1.7f; break;
                case "w_moss": hue = 105; saturation = .4f; gain = 1.4f; break;
                case "w_rust": hue = 18; saturation = .5f; gain = 1.5f; break;
                case "w_crimson": hue = 356; saturation = .65f; gain = 1.6f; break;
                case "w_amethyst": hue = 282; saturation = .5f; gain = 1.6f; break;
                case "w_brass": hue = 44; saturation = .58f; gain = 1.8f; break;
                default: return pixel;
            }
            Color32 result = Color.HSVToRGB(hue / 360f, saturation, Mathf.Clamp01(pixel.b / 255f * gain));
            result.a = pixel.a;
            return result;
        }
    }
}
