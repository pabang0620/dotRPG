using UnityEngine;

namespace DotRPG
{
    public static partial class ProceduralArt
    {
        // Local material ramps keep these houses distinct from canyon adobe and snowy cabins.
        static readonly Color32[] CourtyardTile = Ramp("#171f20", "#253032", "#354445", "#50605d", "#78827a");
        static readonly Color32[] CourtyardJade = Ramp("#17251f", "#28372e", "#394b3f", "#576754", "#818b70");
        static readonly Color32[] CourtyardStone = Ramp("#57564a", "#777768", "#989b84", "#b9baa0", "#d4d0b5");
        static readonly Color32[] CourtyardTimber = Ramp("#30251b", "#513923", "#75512e", "#a27542", "#c49a61");

        static void CourtyardRoof(PixelCanvas c, int left, int right, int top, int bottom, int hip, Color32[] ramp, int seed)
        {
            int height = bottom - top;
            for (int y = top; y <= bottom + 3; y++) for (int x = left; x <= right; x++)
            {
                float across = Mathf.Abs((x - (left + right) * .5f) / ((right - left) * .5f));
                int lift = Mathf.RoundToInt(Mathf.Pow(across, 5) * 5);
                int yy = y + lift;
                if (yy > bottom || yy < top) continue;
                int inset = Mathf.RoundToInt(hip * (1 - (yy - top) / (float)height));
                if (x < left + inset || x > right - inset) continue;
                // Side roof facets run diagonally into the front slope; they are separate planes.
                bool sideFace = x < left + hip || x > right - hip;
                int shear = x < left + hip ? (yy - top) / 2 : x > right - hip ? -(yy - top) / 2 : 0;
                int ridge = (x - left + shear + 256) % 5, course = (yy - top + (sideFace ? Mathf.Abs(x - (left + right) / 2) / 2 : 0)) % 6;
                int tone = ridge == 0 ? 0 : ridge == 1 ? 3 : ridge == 2 ? 2 : 1;
                if (course == 5) tone = Mathf.Max(0, tone - 1);
                if (course == 0 && ridge == 1 && IHash(x / 5, yy / 6, seed) % 3 != 0) tone = 4;
                if (sideFace && x > right - hip) tone = Mathf.Max(0, tone - 1);
                if (yy >= bottom - 1) tone = ridge == 1 ? 2 : 0;
                c.Set(x, y, ramp[tone]);
            }
            // A pale ceramic fascia follows the curved eave; black tile end caps stay visible above it.
            for (int x = left; x <= right; x++)
            {
                float u = Mathf.Abs((x - (left + right) * .5f) / ((right - left) * .5f));
                int edge = bottom - Mathf.RoundToInt(Mathf.Pow(u, 5) * 5);
                c.Set(x, edge + 1, CourtyardStone[3]);
                c.Set(x, edge + 2, CourtyardStone[1]);
                c.Set(x, edge + 3, CourtyardTimber[0]);
            }
            // Raised ridge and light verge caps make the roof thickness legible from above.
            c.HLine(left + hip, right - hip, top, CourtyardStone[4]);
            c.HLine(left + hip, right - hip, top + 1, CourtyardStone[1]);
            for (int side = 0; side < 2; side++)
            {
                int a = side == 0 ? left + hip : right - hip, b = side == 0 ? left : right;
                c.Line(a, top, b, bottom - 5, CourtyardStone[3]);
                c.Line(a + (side == 0 ? 1 : -1), top + 2, b + (side == 0 ? 1 : -1), bottom - 4, ramp[0]);
            }
        }

        static void CourtyardLattice(PixelCanvas c, int x, int y, int width, int height, bool paper)
        {
            c.Rect(x - 2, y - 2, width + 4, height + 4, CourtyardTimber[0]);
            c.Rect(x - 1, y - 1, width + 2, height + 2, CourtyardTimber[3]);
            c.Rect(x, y, width, height, paper ? PixelCanvas.Hex("#adac85") : PixelCanvas.Hex("#303d37"));
            for (int xx = x + 2; xx < x + width; xx += 4)
            {
                c.VLine(xx, y, y + height - 1, CourtyardTimber[1]);
                c.Set(xx + 1, y, CourtyardTimber[4]);
            }
            for (int yy = y + 3; yy < y + height; yy += 4)
                c.HLine(x, x + width - 1, yy, CourtyardTimber[2]);
            c.HLine(x - 2, x + width + 1, y + height + 2, CourtyardStone[3]);
            c.HLine(x - 2, x + width + 1, y + height + 3, CourtyardTimber[0]);
        }

        static void CourtyardLantern(PixelCanvas c, int x, int y)
        {
            c.VLine(x, y - 3, y, CourtyardTimber[0]);
            c.Ellipse(x + .5f, y + 4, 4, 5, PixelCanvas.Hex("#792a20"));
            c.Ellipse(x, y + 3.5f, 3, 4, PixelCanvas.Hex("#be3d25"));
            c.VLine(x - 1, y + 1, y + 6, PixelCanvas.Hex("#ef7e35"));
            c.VLine(x + 2, y + 2, y + 6, PixelCanvas.Hex("#932d22"));
            c.HLine(x - 2, x + 2, y - 1, PixelCanvas.Hex("#d3a354"));
            c.HLine(x - 2, x + 2, y + 8, PixelCanvas.Hex("#d3a354"));
            c.VLine(x, y + 9, y + 11, PixelCanvas.Hex("#b94b28"));
        }

        static void CourtyardTray(PixelCanvas c, int x, int y, int seed)
        {
            c.Ellipse(x, y + 1, 6, 4, CourtyardTimber[0]);
            c.Ellipse(x, y, 6, 4, CourtyardTimber[3]);
            c.Ellipse(x, y, 4.5f, 2.6f, PixelCanvas.Hex("#7c7840"));
            for (int yy = -2; yy <= 2; yy++) for (int xx = -4; xx <= 4; xx++)
            {
                if (xx * xx / 16f + yy * yy / 5f > 1) continue;
                int h = IHash(xx + 7, yy + 4, seed);
                c.Set(x + xx, y + yy, h % 4 == 0 ? PixelCanvas.Hex("#b8572b") : h % 3 == 0 ? PixelCanvas.Hex("#9ba447") : PixelCanvas.Hex("#4b6936"));
            }
            c.HLine(x - 3, x + 2, y - 3, CourtyardTimber[4]);
        }

        static void CourtyardTerrace(PixelCanvas c, int variant)
        {
            // Foreshortened paving, not vertical brickwork: the rear edge is narrower and farther away.
            for (int y = 10; y <= 43; y++)
            {
                float depth = (y - 10) / 33f;
                int left = Mathf.RoundToInt(Mathf.Lerp(25, 19, depth));
                int right = 109 - left;
                float scale = Mathf.Lerp(.83f, 1, depth);
                int row = (y - 10) / 4;
                for (int x = left; x <= right; x++)
                {
                    int pavingX = Mathf.FloorToInt((x - 55) / scale + 60) + (row % 2) * 6;
                    int joint = pavingX % 12;
                    int tone = IHash(pavingX / 12, row, 814 + variant) % 4 == 0 ? 2 : 3;
                    if ((y - 10) % 4 == 3 || joint == 0) tone = 1;
                    else if ((y - 10) % 4 == 0) tone = Mathf.Min(4, tone + 1);
                    c.Set(x, y, CourtyardStone[tone]);
                }
            }
            // Rear parapet: bright horizontal cap, shaded inner face and a cast shadow on the floor.
            c.Rect(23, 5, 64, 3, CourtyardStone[4]);
            c.HLine(23, 86, 5, CourtyardStone[2]);
            c.Rect(25, 8, 60, 4, CourtyardStone[1]);
            c.HLine(25, 84, 12, CourtyardStone[0]);
            // The two return walls visibly extend backwards; their caps and inner faces differ in tone.
            for (int y = 7; y <= 43; y++)
            {
                int left = Mathf.RoundToInt(Mathf.Lerp(24, 18, (y - 7) / 36f));
                c.HLine(left - 1, left + 1, y, CourtyardStone[4]);
                c.Set(left + 2, y, CourtyardStone[1]);
                c.Set(109 - left - 2, y, CourtyardStone[0]);
                c.HLine(109 - left - 1, 109 - left + 1, y, CourtyardStone[2]);
            }

            // A stairwell opening in the floor, with exposed inner walls and horizontal treads.
            // This replaces the old upright lattice window, which made the roof read as a facade.
            for (int y = 18; y <= 36; y++)
            {
                int left = Mathf.RoundToInt(Mathf.Lerp(48, 43, (y - 18) / 18f));
                int right = Mathf.RoundToInt(Mathf.Lerp(70, 75, (y - 18) / 18f));
                c.HLine(left, right, y, CourtyardTimber[0]);
                c.Set(left + 1, y, CourtyardTimber[2]);
                c.Set(right - 1, y, CourtyardStone[0]);
                if (y >= 24)
                {
                    int step = (y - 24) / 3;
                    c.HLine(left + 3, right - 3, y,
                        (y - 24) % 3 == 0 ? CourtyardTimber[3] : CourtyardTimber[1 + (step > 2 ? 1 : 0)]);
                }
            }
            c.Line(47, 17, 71, 17, CourtyardTimber[3]);
            c.Line(47, 18, 42, 37, CourtyardTimber[3]);
            c.Line(71, 18, 76, 37, CourtyardTimber[1]);
            c.HLine(42, 76, 37, CourtyardTimber[3]);
            c.HLine(42, 76, 38, CourtyardTimber[0]);
            // Low safety rail only on the rear rim, with visible posts (never a window grid).
            for (int x = 48; x <= 70; x += 11) c.VLine(x, 13, 18, CourtyardTimber[1]);
            c.HLine(48, 70, 13, CourtyardTimber[3]);

            if (variant != 1)
            {
                CourtyardTray(c, 33, 21, 83 + variant);
                CourtyardTray(c, 30, 34, 93 + variant);
            }
            else
            {
                // Folded textiles show their upper plane and a darker vertical front edge.
                c.Rect(29, 18, 10, 5, PixelCanvas.Hex("#afa583"));
                c.HLine(29, 38, 18, PixelCanvas.Hex("#ded2ad"));
                c.HLine(29, 38, 23, PixelCanvas.Hex("#79745d"));
                c.Rect(27, 31, 11, 5, PixelCanvas.Hex("#708478"));
                c.HLine(27, 37, 31, PixelCanvas.Hex("#a5b6a2"));
                c.HLine(27, 37, 36, PixelCanvas.Hex("#4d6055"));
            }
            // Front curb: broad top slab, short front face, then contact shadow onto the sloping tiles.
            c.Rect(17, 42, 76, 3, CourtyardStone[3]);
            c.HLine(18, 91, 42, CourtyardStone[4]);
            c.HLine(17, 92, 45, CourtyardStone[1]);
            c.HLine(18, 91, 46, CourtyardStone[0]);
            c.Set(83, 40, TMoss); c.Set(84, 41, TMoss); c.Set(85, 40, TMossLight);
        }

        static PixelCanvas CourtyardHouse(int variant)
        {
            // Same 110x110 canvas, 32px/tile density and (55,5) pivot as the original house.
            var c = Hd(110, 110);
            var tile = variant == 2 ? CourtyardJade : CourtyardTile;
            const int cx = 55, baseY = 105;
            GroundShadow(c, 7, 102, 63, baseY);

            // Recessed timber frontage, warm plaster panels and dressed stone footing.
            PlasterWall(c, 10, 99, 48, 100, 481 + variant);
            c.Rect(12, 56, 86, 11, CourtyardTimber[0]);
            for (int x = 15; x <= 95; x += 4)
            {
                c.VLine(x, 57, 65, CourtyardTimber[2]);
                c.VLine(x + 1, 57, 65, CourtyardTimber[1]);
            }
            c.HLine(12, 98, 58, CourtyardTimber[3]);
            c.HLine(12, 98, 62, CourtyardTimber[3]);
            StoneWall(c, 9, 100, 97, 102, CourtyardStone, 60, 4);
            foreach (int x in new[] { 11, 39, 68, 96 })
            {
                c.Rect(x, 76, 3, 25, CourtyardTimber[1]);
                c.VLine(x, 77, 99, CourtyardTimber[3]);
                c.Rect(x - 1, 99, 5, 3, CourtyardStone[1]);
                c.HLine(x - 1, x + 3, 99, CourtyardStone[4]);
            }
            CourtyardLattice(c, 19, 84, 14, 9, variant != 0);
            CourtyardLattice(c, 78, 84, 13, 9, variant == 1);
            Door(c, cx, 100, 18, 25, false, true);
            // Layered entrance steps stop at the existing front edge instead of occupying a new tile.
            for (int step = 0; step < 3; step++)
            {
                int half = 14 + step * 3, y = 101 + step * 2;
                c.Rect(cx - half, y, half * 2 + 1, 2, CourtyardStone[1]);
                c.HLine(cx - half + 1, cx + half - 1, y, CourtyardStone[4 - step / 2]);
            }

            // More than half the silhouette is now visible roof depth, with a horizontal terrace.
            CourtyardRoof(c, 3, 106, 5, 56, 20, tile, 61 + variant);
            CourtyardTerrace(c, variant);

            // A second tiled eave shades the door and supports a small carved plaque.
            CourtyardRoof(c, 3, 106, 64, 79, 11, tile, 110 + variant);
            for (int x = 13; x < 98; x += 5) c.Rect(x, 82, 2, 2, CourtyardTimber[2]);
            for (int y = 83; y <= 86; y++) for (int x = 13; x <= 96; x++)
                c.Set(x, y, new Color32(22, 24, 21, (byte)(100 - (y - 83) * 20)));
            c.Rect(cx - 10, 58, 21, 13, CourtyardTimber[0]);
            c.Rect(cx - 9, 59, 19, 11, CourtyardTimber[3]);
            c.Rect(cx - 7, 61, 15, 7, CourtyardTimber[1]);
            c.Line(cx - 3, 66, cx + 3, 62, CourtyardStone[4]);
            c.Ellipse(cx - 2, 63, 3, 1.7f, variant == 0 ? PixelCanvas.Hex("#b05c3e") : PixelCanvas.Hex("#8d9a60"));
            c.Ellipse(cx + 3, 65, 3, 1.7f, CourtyardStone[3]);
            CourtyardLantern(c, 15, 82);
            CourtyardLantern(c, 94, 82);
            // Short shop curtain, split in the centre so the entrance remains obvious.
            var cloth = variant == 0 ? PixelCanvas.Hex("#c9c398") : variant == 1 ? PixelCanvas.Hex("#768c99") : PixelCanvas.Hex("#899269");
            c.Rect(45, 83, 20, 5, cloth);
            c.HLine(45, 64, 83, CourtyardStone[4]);
            c.HLine(45, 64, 88, CourtyardTimber[1]);
            c.VLine(cx, 86, 88, CourtyardTimber[1]);
            c.Line(cx - 4, 87, cx + 3, 84, PixelCanvas.Hex("#52633c"));
            c.Set(cx - 2, 84, PixelCanvas.Hex("#52633c"));

            // Small grounded household details stay clear of the central interaction area.
            if (variant == 1)
            {
                c.Ellipse(23, 100, 5, 3, CourtyardTimber[0]);
                c.Ellipse(23, 97, 4, 5, PixelCanvas.Hex("#797967"));
                c.Ellipse(23, 93, 3, 1.5f, PixelCanvas.Hex("#b9b69c"));
                c.Ellipse(23, 93, 2, 1, PixelCanvas.Hex("#536a68"));
            }
            else
            {
                c.Rect(18, 96, 14, 5, CourtyardTimber[2]);
                c.HLine(18, 31, 96, CourtyardTimber[4]);
                for (int i = 0; i < 4; i++)
                {
                    c.Ellipse(20 + i * 3, 95, 1.5f, 2, PixelCanvas.Hex("#d5c9a3"));
                    c.Set(20 + i * 3, 93, PixelCanvas.Hex("#5a7150"));
                }
            }
            // Outline the opaque architecture without converting soft ground shadows into black boxes.
            c.Outline(TOutline);
            return c.WithPivot(cx, 5);
        }
    }
}
