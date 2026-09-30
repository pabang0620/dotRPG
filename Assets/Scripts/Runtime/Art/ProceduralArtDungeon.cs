using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Dungeon art ("dgn_*"): the room gate (closed / open), the skull of the boss room on the room map and
    /// the reward cards (back / front).
    /// </summary>
    public static partial class ProceduralArt
    {
        static PixelCanvas DrawDungeon(string[] p)
        {
            switch (p[1])
            {
                case "gate": return DrawDungeonGate(p.Length > 2 && p[2] == "open");
                case "skull": return DrawDungeonSkull();
                case "cardback": return DrawRewardCard(true);
                case "cardfront": return DrawRewardCard(false);
            }
            return null;
        }

        /// <summary>
        /// Stone archway 40x46 px (fills a 2-tile gap in the wall plus the pillars). Closed: iron-banded
        /// double door with a glowing red seal. Open: dark passage with a pale blue light at the far end.
        /// </summary>
        static PixelCanvas DrawDungeonGate(bool open)
        {
            const int W = 40, H = 46;
            var c = new PixelCanvas(W, H).WithBottomPivot();
            var stone = PixelCanvas.Hex("7a7382");
            var stoneLight = PixelCanvas.Hex("a39cab");
            var stoneDark = PixelCanvas.Hex("4a4452");
            var outline = PixelCanvas.Hex("241f2a");

            // Passage / door area (inside the arch).
            for (int y = 8; y < H; y++)
                for (int x = 6; x < W - 6; x++)
                {
                    // Round arch top.
                    float dx = (x + 0.5f - W * 0.5f) / (W * 0.5f - 6f);
                    float archY = 8f + 7f * (1f - Mathf.Sqrt(Mathf.Max(0f, 1f - dx * dx)));
                    if (y < archY) continue;
                    if (open)
                    {
                        float t = (y - 8f) / (H - 8f);
                        var deep = PixelCanvas.Hex("0c0a14");
                        var near = PixelCanvas.Hex("2a2536");
                        c.Set(x, y, Color32.Lerp(deep, near, t * t));
                    }
                    else
                    {
                        bool seam = x == W / 2 - 1 || x == W / 2;
                        bool plank = (x - 6) % 5 == 0;
                        bool band = y == 18 || y == 19 || y == 34 || y == 35;
                        var wood = PixelCanvas.Hex(((x / 5) & 1) == 0 ? "6b4228" : "5d3822");
                        var col = seam ? PixelCanvas.Hex("2e1b10") : band ? PixelCanvas.Hex("3c3a44") : plank ? PixelCanvas.Shade(wood, 0.8f) : wood;
                        if (band && (x % 6 == 2)) col = PixelCanvas.Hex("8c8a96");
                        c.Set(x, y, col);
                    }
                }
            if (open)
            {
                // Pale light far inside + floor glow spilling out.
                c.PaintEllipse(W * 0.5f, 20f, 7f, 5f, PixelCanvas.Hex("9ad6ff", 70));
                c.PaintEllipse(W * 0.5f, 20f, 3.5f, 2.5f, PixelCanvas.Hex("d8f2ff", 120));
                for (int x = 9; x < W - 9; x++) c.Set(x, H - 2, PixelCanvas.Hex("6fb8ff", 90));
            }
            else
            {
                // Red seal in the middle of the doors.
                c.PaintEllipse(W * 0.5f - 0.5f, 27f, 4f, 4f, PixelCanvas.Hex("3a0d10"));
                c.PaintEllipse(W * 0.5f - 0.5f, 27f, 2.8f, 2.8f, PixelCanvas.Hex("e0343a"));
                c.PaintEllipse(W * 0.5f - 0.5f, 26f, 1.2f, 1.2f, PixelCanvas.Hex("ffb0a0"));
            }

            // Pillars and the arch stones.
            for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    bool pillar = x < 6 || x >= W - 6;
                    float dx = (x + 0.5f - W * 0.5f) / (W * 0.5f - 6f);
                    float archY = 8f + 7f * (1f - Mathf.Sqrt(Mathf.Max(0f, 1f - dx * dx)));
                    bool arch = !pillar && y < archY && y >= 1;
                    if (!pillar && !arch) continue;
                    if (y == 0 && !pillar) continue;
                    int brick = pillar ? (y / 5) : (int)((x + 2) / 5f);
                    bool mortar = pillar ? y % 5 == 0 : (x + 2) % 5 == 0 || y == (int)archY - 1;
                    var col = mortar ? stoneDark : ((brick & 1) == 0 ? stone : PixelCanvas.Shade(stone, 0.9f));
                    if (pillar && (x == 0 || x == W - 1)) col = stoneDark;
                    else if (pillar && (x == 1 || x == W - 5)) col = stoneLight;
                    c.Set(x, y, col);
                }
            // Keystone.
            c.Rect(W / 2 - 3, 0, 6, 7, stoneLight);
            c.Rect(W / 2 - 2, 1, 4, 5, open ? PixelCanvas.Hex("7fd0ff") : PixelCanvas.Hex("c0404a"));
            c.Outline(outline);
            return c;
        }

        static PixelCanvas DrawDungeonSkull()
        {
            var c = new PixelCanvas(12, 12);
            var bone = PixelCanvas.Hex("f2ecd8");
            var dark = PixelCanvas.Hex("2a1e1e");
            c.PaintEllipse(6f, 5f, 4.6f, 4.2f, bone);
            c.Rect(3, 8, 6, 3, bone);
            c.Rect(3, 4, 2, 2, dark);
            c.Rect(7, 4, 2, 2, dark);
            c.Set(5, 7, dark);
            c.Set(6, 7, dark);
            c.Set(4, 10, dark);
            c.Set(6, 10, dark);
            c.Outline(dark);
            return c;
        }

        /// <summary>40x56 px reward card: navy back with a gold frame and emblem, or a cream front.</summary>
        static PixelCanvas DrawRewardCard(bool back)
        {
            const int W = 40, H = 56;
            var c = new PixelCanvas(W, H);
            var gold = PixelCanvas.Hex("d9a93a");
            var goldLight = PixelCanvas.Hex("ffe08a");
            var edge = PixelCanvas.Hex("2a1c10");
            var face = back ? PixelCanvas.Hex("1f2d52") : PixelCanvas.Hex("f4e6c4");
            for (int y = 1; y < H - 1; y++)
                for (int x = 1; x < W - 1; x++)
                {
                    bool corner = (x <= 1 || x >= W - 2) && (y <= 1 || y >= H - 2);
                    if (corner) continue;
                    bool frame = x <= 3 || x >= W - 4 || y <= 3 || y >= H - 4;
                    var col = frame ? gold : face;
                    if (frame && (x == 2 || y == 2)) col = goldLight;
                    if (!frame && back && ((x + y) % 8 == 0 || (x - y + 64) % 8 == 0)) col = PixelCanvas.Hex("2b3d6c");
                    if (!frame && !back && (y == 6 || y == H - 7)) col = PixelCanvas.Hex("e2cf9f");
                    c.Set(x, y, col);
                }
            if (back)
            {
                // Diamond emblem.
                for (int y = -9; y <= 9; y++)
                    for (int x = -9; x <= 9; x++)
                    {
                        int d = Mathf.Abs(x) + Mathf.Abs(y);
                        if (d > 9) continue;
                        c.Set(W / 2 + x, H / 2 + y, d >= 8 ? gold : d >= 6 ? PixelCanvas.Hex("7a1f2a") : d >= 3 ? PixelCanvas.Hex("b8323f") : goldLight);
                    }
            }
            c.Outline(edge);
            return c;
        }
    }
}
