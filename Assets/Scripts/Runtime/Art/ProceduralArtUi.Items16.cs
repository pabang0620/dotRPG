using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        // ===================================================================================
        //  Legacy helpers still referenced by the shared ProceduralArt.cs (do not remove).
        //  DrawItemIcon16 backs the 16px ticket icon; DrawAnvil is the "anvil" world sprite
        //  fallback (the HD town anvil comes from the town stream's town_* keys).
        // ===================================================================================

        static readonly Color32 handleColor = PixelCanvas.Hex("#6b4226");

        /// <summary>16x16 item icons (only the ticket icon still routes here).</summary>
        static PixelCanvas DrawItemIcon16(string kind)
        {
            switch (kind)
            {
                case "gold":
                {
                    var c = new PixelCanvas(16, 16);
                    var rim = PixelCanvas.Hex("#b07a1c");
                    var face = PixelCanvas.Hex("#f2c14e");
                    var shine = PixelCanvas.Hex("#ffe89a");
                    c.Ellipse(10.5f, 6.5f, 4.6f, 4.6f, rim);
                    c.Ellipse(10.5f, 6.5f, 3.6f, 3.6f, face);
                    c.Ellipse(7f, 9.5f, 5.8f, 5.8f, rim);
                    c.Ellipse(7f, 9.5f, 4.8f, 4.8f, face);
                    c.PaintEllipse(5.5f, 8f, 2.6f, 2.2f, shine);
                    c.Rect(6, 8, 3, 3, rim);
                    c.Rect(7, 9, 1, 1, PixelCanvas.Clear);
                    c.Set(4, 6, White);
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1.5f);
                }
                case "potion_hp":
                case "potion_mp":
                {
                    bool hp = kind == "potion_hp";
                    var c = new PixelCanvas(16, 16);
                    var glass = PixelCanvas.Hex("#d8eef6");
                    var liquid = hp ? PixelCanvas.Hex("#e0413a") : PixelCanvas.Hex("#3f7ee8");
                    var liquidLight = hp ? PixelCanvas.Hex("#ff8a72") : PixelCanvas.Hex("#86b8ff");
                    var liquidDark = hp ? PixelCanvas.Hex("#a82a2a") : PixelCanvas.Hex("#2a53b0");
                    c.Circle(8f, 10.5f, 5.2f, glass);
                    c.Rect(6, 3, 4, 4, glass);
                    for (int y = 8; y < 16; y++)
                        for (int x = 0; x < 16; x++)
                            if (c.IsOpaque(x, y)) c.Set(x, y, y >= 13 ? liquidDark : liquid);
                    c.PaintEllipse(6f, 10f, 1.8f, 1.2f, liquidLight);
                    c.Rect(6, 1, 4, 2, PixelCanvas.Hex("#9a6a3c"));
                    c.HLine(6, 9, 1, PixelCanvas.Hex("#c49058"));
                    c.Set(5, 9, White); c.Set(5, 10, White);
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1f);
                }
                case "scroll":
                {
                    var c = new PixelCanvas(16, 16);
                    var paper = PixelCanvas.Hex("#f3e2b8");
                    var paperDark = PixelCanvas.Hex("#d7bf8a");
                    var roll = PixelCanvas.Hex("#c9a46a");
                    c.Rect(3, 4, 10, 8, paper);
                    c.HLine(3, 12, 11, paperDark);
                    c.Rect(1, 3, 3, 10, roll); c.VLine(1, 4, 11, PixelCanvas.Hex("#a8844e"));
                    c.Rect(12, 3, 3, 10, roll); c.VLine(14, 4, 11, PixelCanvas.Hex("#a8844e"));
                    var rune = PixelCanvas.Hex("#4f8fe8");
                    c.Circle(8f, 7.5f, 2.6f, PixelCanvas.Hex("#9fd0ff"));
                    c.VLine(8, 5, 10, rune); c.HLine(6, 10, 7, rune);
                    c.Rect(7, 11, 2, 2, PixelCanvas.Hex("#c8323a"));
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1f);
                }
                case "ticket":
                {
                    // 장비 보호권: a gold-edged voucher with coupon notches, a blue shield crest and a red wax seal.
                    var c = new PixelCanvas(16, 16);
                    var edge = PixelCanvas.Hex("#c8901e");
                    var edgeLight = PixelCanvas.Hex("#ffd84a");
                    var paper = PixelCanvas.Hex("#f6e7c8");
                    var paperDark = PixelCanvas.Hex("#dcc596");
                    c.Rect(1, 3, 14, 10, edge);
                    c.HLine(2, 13, 3, edgeLight);
                    c.Rect(2, 4, 12, 8, paper);
                    c.HLine(2, 13, 11, paperDark);
                    // Notches halfway down both short sides.
                    c.Rect(1, 7, 1, 2, PixelCanvas.Clear); c.Rect(14, 7, 1, 2, PixelCanvas.Clear);
                    c.VLine(2, 7, 8, edge); c.VLine(13, 7, 8, edge);
                    // Shield crest with a gold gem.
                    var shield = PixelCanvas.Hex("#3f7ee8");
                    var shieldLight = PixelCanvas.Hex("#86b8ff");
                    c.Rect(5, 4, 6, 4, shield);
                    c.HLine(6, 9, 8, shield);
                    c.HLine(7, 8, 9, shield);
                    c.VLine(5, 4, 7, shieldLight); c.Set(6, 8, shieldLight);
                    c.Rect(7, 5, 2, 2, edgeLight);
                    // Wax seal on the corner.
                    c.Circle(12.5f, 11.5f, 2.3f, PixelCanvas.Hex("#c8323a"));
                    c.Set(12, 11, PixelCanvas.Hex("#ff7a6a"));
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1f);
                }
                case "chest":
                {
                    var c = new PixelCanvas(16, 16);
                    c.Rect(1, 6, 14, 9, Wood);
                    c.Rect(1, 3, 14, 4, WoodLight);
                    c.HLine(1, 14, 6, WoodDark);
                    c.VLine(4, 3, 14, Gold); c.VLine(11, 3, 14, Gold);
                    c.Rect(7, 6, 2, 3, Yellow);
                    c.HLine(1, 14, 14, WoodDark);
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1f);
                }
                case "anvil":
                {
                    var c = new PixelCanvas(16, 16);
                    c.Rect(3, 4, 12, 3, Steel); c.HLine(3, 14, 4, White);
                    c.Rect(0, 5, 4, 2, Steel);
                    c.Rect(6, 7, 6, 3, SteelDark);
                    c.Rect(4, 10, 10, 3, SteelDark); c.HLine(4, 13, 10, Steel);
                    c.Set(6, 2, Carrot); c.Set(10, 1, Yellow);
                    c.Outline(Outline);
                    return c.WithPivot(8f, 1f);
                }
            }
            return null;
        }

        /// <summary>Blacksmith anvil on a stump with a hammer - the "anvil" world sprite fallback.</summary>
        static PixelCanvas DrawAnvil()
        {
            var c = new PixelCanvas(28, 24);
            c.Rect(8, 14, 12, 9, Bark); c.HLine(8, 19, 14, WoodLight); c.VLine(10, 15, 22, BarkDark); c.VLine(17, 15, 22, BarkDark);
            c.Rect(4, 6, 20, 5, SteelDark); c.HLine(4, 23, 6, Steel);
            c.Rect(1, 7, 4, 3, SteelDark); c.Set(0, 8, SteelDark);
            c.Rect(10, 11, 8, 3, SteelDark);
            c.Line(20, 2, 25, 7, handleColor); c.Rect(18, 0, 5, 3, Steel);
            c.Set(7, 5, Carrot); c.Set(13, 4, Yellow); c.Set(16, 5, Carrot);
            c.Outline(Outline);
            return c.WithPivot(14, 1.5f);
        }
    }
}
