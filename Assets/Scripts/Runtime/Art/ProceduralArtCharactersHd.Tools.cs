using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        // ---------- Held tools ----------

        static PixelCanvas DrawToolHd(string kind)
        {
            if (kind == "crate") return DrawCrateHd();
            var c = Hd(32, 32);
            var handle = PixelCanvas.Hex("#6b4226");
            var handleDark = PixelCanvas.Hex("#4a2c17");
            var handleLight = PixelCanvas.Hex("#8a5c38");
            var metalReel = PixelCanvas.Hex("#8f99a3");
            switch (kind)
            {
                case "sword":
                    c.Rect(14, 2, 4, 20, Steel);
                    c.VLine(14, 3, 21, White); c.VLine(17, 3, 21, SteelDark);
                    c.Rect(15, 0, 2, 2, Steel);
                    c.VLine(15, 3, 20, SteelDark); // fuller
                    c.Rect(10, 22, 12, 2, Gold);   // guard
                    c.HLine(10, 21, 22, Lit(Gold, 1.2f));
                    c.Rect(14, 24, 4, 6, handle); for (int y = 25; y < 30; y += 2) c.HLine(14, 17, y, handleDark);
                    c.Rect(14, 30, 4, 2, Gold);
                    break;
                case "axe":
                    c.Rect(14, 6, 3, 24, handle); c.VLine(14, 7, 29, handleLight); c.VLine(16, 7, 29, handleDark);
                    c.Rect(6, 4, 10, 10, Steel);
                    c.VLine(6, 4, 13, SteelDark);
                    c.Rect(4, 6, 3, 6, Steel);      // blade edge
                    c.HLine(4, 15, 4, White);
                    c.Set(8, 6, White); c.Set(9, 7, White); // shine
                    break;
                case "pickaxe":
                    c.Rect(14, 6, 3, 24, handle); c.VLine(14, 7, 29, handleLight); c.VLine(16, 7, 29, handleDark);
                    c.HLine(4, 27, 6, Steel); c.HLine(5, 26, 5, Steel);
                    c.HLine(6, 25, 7, SteelDark);
                    c.Set(4, 8, SteelDark); c.Set(27, 8, SteelDark);
                    c.HLine(10, 20, 5, White);
                    break;
                case "can":
                {
                    var body = PixelCanvas.Hex("#4a90d9");
                    var bodyDk = PixelCanvas.Hex("#2f6bb0");
                    var bodyLt = PixelCanvas.Hex("#8cc4ff");
                    var metal = SteelDark;
                    // Tank body (rounded).
                    c.Rect(9, 13, 12, 13, body);
                    c.Rect(8, 15, 1, 9, body); c.Rect(21, 15, 1, 9, body);
                    c.HLine(9, 20, 13, bodyLt); c.VLine(9, 14, 24, bodyLt); // top+left light
                    c.VLine(20, 15, 24, bodyDk); c.HLine(9, 20, 25, bodyDk); // right+bottom shade
                    c.Set(11, 16, White);                                     // sheen
                    // Curved spout to the upper-right ending in a rose.
                    c.Line(21, 18, 27, 11, body); c.Line(21, 17, 27, 10, bodyLt); c.Line(22, 19, 28, 12, bodyDk);
                    c.Rect(26, 8, 5, 3, metal);        // rose head
                    c.HLine(26, 30, 8, Lit(metal, 1.3f));
                    c.Set(27, 9, White); c.Set(29, 9, White); // spray holes glint
                    // Arched carry handle over the top.
                    c.Line(11, 12, 14, 8, metal); c.Line(14, 8, 18, 8, metal); c.Line(18, 8, 20, 12, metal);
                    c.HLine(14, 17, 7, Lit(metal, 1.3f));
                    break;
                }
                case "rod":
                {
                    var pole = PixelCanvas.Hex("#7a4a24");
                    var poleLt = PixelCanvas.Hex("#a56d3a");
                    var poleDk = BarkDark;
                    var line = PixelCanvas.Hex("#e8e8e8");
                    // Thicker tapering pole from grip (bottom-left) to tip (top-right).
                    c.Line(11, 30, 25, 3, pole); c.Line(12, 30, 26, 3, poleLt); c.Line(10, 30, 24, 4, poleDk);
                    c.Set(25, 2, pole); c.Set(26, 2, pole);
                    // Cork grip at the base.
                    c.Rect(9, 27, 4, 5, PixelCanvas.Hex("#c79a5a")); c.VLine(9, 28, 31, PixelCanvas.Hex("#a87c40"));
                    // Reel (side disc) just above the grip.
                    c.Circle(15f, 24f, 3.2f, metalReel); c.Circle(15f, 24f, 1.6f, PixelCanvas.Hex("#c0c6cf"));
                    c.Set(14, 23, White);
                    c.Rect(15, 26, 1, 3, SteelDark);   // reel foot to the pole
                    // Line from the tip down to a float, with a small hook.
                    c.VLine(25, 3, 9, line); c.Line(25, 9, 22, 15, line);
                    c.Rect(21, 15, 2, 2, Red); c.Set(21, 15, RedLight); // float
                    c.Set(22, 18, SteelDark);                           // hook
                    break;
                }
                case "hammer":
                    c.Rect(14, 10, 4, 20, handle); for (int y = 12; y < 28; y += 3) c.HLine(14, 17, y, handleDark);
                    c.Rect(8, 4, 16, 8, SteelDark);
                    c.HLine(8, 23, 4, Steel); c.HLine(8, 23, 5, Steel);
                    c.Rect(9, 6, 4, 4, Lit(SteelDark, 1.3f)); // face highlight
                    break;
                case "staff":
                    c.Rect(14, 10, 4, 22, handle); c.VLine(14, 11, 31, handleLight); c.VLine(17, 11, 31, BarkDark);
                    c.Rect(12, 8, 2, 2, Gold); c.Rect(18, 8, 2, 2, Gold);
                    c.Rect(11, 5, 2, 2, Gold); c.Rect(19, 5, 2, 2, Gold);
                    c.Circle(15.5f, 4.5f, 4.5f, Magic);
                    c.Circle(15.5f, 4.5f, 2.8f, MagicCore);
                    c.Rect(13, 2, 2, 2, White);
                    break;
                default:
                    return null;
            }
            c.Outline(Outline);
            return c.WithPivot(16, 3f);
        }

        /// <summary>Carried supply crate (tool_crate) at density 2 - a wooden box of carrots.</summary>
        static PixelCanvas DrawCrateHd()
        {
            var c = Hd(32, 34);
            var wood = Wood; var wl = WoodLight; var wd = WoodDark;
            for (int i = 0; i < 4; i++)
            {
                int x = 6 + i * 6;
                c.Rect(x, 2, 2, 6, Leaf); c.Set(x - 1, 4, LeafLight);
                c.Rect(x - 2, 8, 5, 5, Carrot); c.Set(x - 2, 8, CarrotLight);
            }
            c.Rect(2, 12, 28, 20, wood);
            c.HLine(2, 29, 12, wl);
            c.HLine(2, 29, 22, wd);
            c.HLine(2, 29, 31, wd);
            c.VLine(2, 12, 31, wd); c.VLine(29, 12, 31, wd);
            c.Line(4, 14, 27, 20, wd);
            c.Line(4, 24, 27, 30, wd);
            c.Outline(Outline);
            return c.WithPivot(16, 3f);
        }

        // ---------- Damage digits, HP bar, shadow ----------

        static readonly string[] DigitShapesHd =
        {
            // 5x7 glyphs upscaled to crisp 6x9 forms.
            "011110|110011|110011|110011|110011|110011|011110",
            "001100|011100|001100|001100|001100|001100|011110",
            "011110|110011|000011|000110|011000|110000|111111",
            "011110|110011|000011|001110|000011|110011|011110",
            "000110|001110|011110|110110|111111|000110|000110",
            "111111|110000|111110|000011|000011|110011|011110",
            "001110|011000|110000|111110|110011|110011|011110",
            "111111|000011|000110|001100|011000|011000|011000",
            "011110|110011|110011|011110|110011|110011|011110",
            "011110|110011|110011|011111|000011|000110|011100",
        };

        static PixelCanvas DrawDigitHd(int digit)
        {
            var c = Hd(8, 11);
            var rows = DigitShapesHd[Mathf.Clamp(digit, 0, 9)].Split('|');
            for (int y = 0; y < rows.Length; y++)
                for (int x = 0; x < rows[y].Length; x++)
                    if (rows[y][x] == '1') c.Set(x + 1, y + 1, White);
            c.Outline(Outline);
            return c;
        }

        /// <summary>Monster health bar at density 2. "bg" = dark frame (32px wide), "fill" = white strip, left pivot.</summary>
        static PixelCanvas DrawHpBarHd(string part)
        {
            var frame = PixelCanvas.Hex("#20140f");
            if (part == "bg")
            {
                var c = Hd(32, 8);
                // Rounded-end dark frame.
                c.Rect(1, 0, 30, 8, frame);
                c.Rect(0, 1, 32, 6, frame);
                c.Rect(0, 2, 1, 4, PixelCanvas.Clear); c.Rect(31, 2, 1, 4, PixelCanvas.Clear); // clip square corners
                // Empty channel with an inner bevel (dark bottom, faint top light).
                var empty = PixelCanvas.Hex("#3a2020");
                c.Rect(2, 2, 28, 4, empty);
                c.HLine(2, 29, 2, PixelCanvas.Hex("#4c2c2c"));   // bevel top light
                c.HLine(2, 29, 5, PixelCanvas.Hex("#281414"));   // bevel bottom shade
                return c;
            }
            // Fill: 28 wide, vertical gradient (light top, mid, shade bottom), left pivot so it scales from the left.
            var fill = Hd(28, 4);
            fill.HLine(0, 27, 0, White);
            fill.HLine(0, 27, 1, PixelCanvas.Hex("#f0f0f0"));
            fill.HLine(0, 27, 2, PixelCanvas.Hex("#dcdcdc"));
            fill.HLine(0, 27, 3, PixelCanvas.Hex("#c4c4c4"));
            return fill.WithPivot(0, 2);
        }

        static PixelCanvas DrawShadowHd()
        {
            var c = Hd(24, 10);
            c.Ellipse(12, 5, 11f, 4.4f, new Color32(20, 40, 20, 70));
            c.PaintEllipse(12, 5, 8f, 3f, new Color32(20, 40, 20, 55));
            return c;
        }
    }
}
