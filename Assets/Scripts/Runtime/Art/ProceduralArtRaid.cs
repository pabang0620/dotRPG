using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Raid reward card art ("raid_*", Docs/server/phase13_raid_rewards.md §7.4): the crowned-skull back, the five front
    /// frames (material / ticket / epic / unique / legendary) and the soft light shapes behind and around a flipped card.
    /// </summary>
    public static partial class ProceduralArt
    {
        const int RaidCardW = 40, RaidCardH = 56;

        static PixelCanvas DrawRaid(string[] p)
        {
            switch (p[1])
            {
                case "cardback": return DrawRaidCardBack();
                case "front" when p.Length > 2: return DrawRaidCardFront(p[2]);
                case "glow": return DrawRaidGlow();
                case "ring": return DrawRaidRing();
                case "rays": return DrawRaidRays();
            }
            return null;
        }

        /// <summary>Distance of a pixel from the card edge, 0 = the outermost drawn pixel (the card spans 1..W-2).</summary>
        static int RaidEdgeDistance(int x, int y) => Mathf.Min(Mathf.Min(x - 1, RaidCardW - 2 - x), Mathf.Min(y - 1, RaidCardH - 2 - y));

        static bool RaidCorner(int x, int y) => (x <= 1 || x >= RaidCardW - 2) && (y <= 1 || y >= RaidCardH - 2);

        /// <summary>40x56 back: dark violet with a diagonal grid, crimson border with a bone-gold line and studs, crowned skull.</summary>
        static PixelCanvas DrawRaidCardBack()
        {
            var c = new PixelCanvas(RaidCardW, RaidCardH);
            var ground = PixelCanvas.Hex("1b1424");
            var grid = PixelCanvas.Hex("2a2036");
            var crimson = PixelCanvas.Hex("8f2a3a");
            var gold = PixelCanvas.Hex("d8b36a");
            for (int y = 1; y < RaidCardH - 1; y++)
                for (int x = 1; x < RaidCardW - 1; x++)
                {
                    if (RaidCorner(x, y)) continue;
                    int d = RaidEdgeDistance(x, y);
                    var col = d <= 2 ? crimson : d == 3 ? gold : ground;
                    if (d > 3 && ((x + y) % 8 == 0 || (x - y + 64) % 8 == 0)) col = grid;
                    c.Set(x, y, col);
                }
            // Studs in the four corners.
            foreach (int sx in new[] { 2, RaidCardW - 4 })
                foreach (int sy in new[] { 2, RaidCardH - 4 })
                    c.Rect(sx, sy, 2, 2, gold);

            // Crowned skull, 15x15, centred.
            int ox = (RaidCardW - 15) / 2, oy = (RaidCardH - 15) / 2;
            var bone = PixelCanvas.Hex("f2ecd8");
            c.Ellipse(ox + 7.5f, oy + 9f, 5.5f, 4.6f, bone);
            c.Rect(ox + 4, oy + 11, 7, 3, bone);
            c.Rect(ox + 4, oy + 8, 2, 3, ground);
            c.Rect(ox + 9, oy + 8, 2, 3, ground);
            c.Set(ox + 7, oy + 11, ground);
            foreach (int tx in new[] { 5, 7, 9 }) c.Set(ox + tx, oy + 13, ground);
            // Crown: band with three points.
            c.Rect(ox + 2, oy + 4, 11, 2, gold);
            c.Rect(ox + 2, oy + 2, 2, 2, gold);
            c.Rect(ox + 6, oy + 1, 3, 3, gold);
            c.Set(ox + 7, oy, gold);
            c.Rect(ox + 11, oy + 2, 2, 2, gold);
            c.Outline(PixelCanvas.Hex("2a1c10"));
            return c;
        }

        /// <summary>40x56 front: the same frame for every kind, only the colours (and the corner / crown decoration) differ.</summary>
        static PixelCanvas DrawRaidCardFront(string kind)
        {
            Color32 ground, outer, inner, line;
            switch (kind)
            {
                case "ticket": ground = PixelCanvas.Hex("e4eef7"); outer = PixelCanvas.Hex("5aa9ff"); inner = PixelCanvas.Hex("a9d3ff"); line = PixelCanvas.Hex("c7dff2"); break;
                case "epic": ground = PixelCanvas.Hex("efe6ff"); outer = PixelCanvas.Hex("b77bff"); inner = PixelCanvas.Hex("dcc2ff"); line = PixelCanvas.Hex("d7c5f2"); break;
                case "unique": ground = PixelCanvas.Hex("fff3c6"); outer = PixelCanvas.Hex("ffd84a"); inner = PixelCanvas.Hex("fff1a0"); line = PixelCanvas.Hex("eedc8c"); break;
                case "legend": ground = PixelCanvas.Hex("2a1608"); outer = PixelCanvas.Hex("ff8a3d"); inner = PixelCanvas.Hex("ffe0a0"); line = PixelCanvas.Hex("5a3414"); break;
                default: ground = PixelCanvas.Hex("e4e9ee"); outer = PixelCanvas.Hex("8fa3b8"); inner = PixelCanvas.Hex("c7d3df"); line = PixelCanvas.Hex("cfd8e2"); break;
            }
            bool legend = kind == "legend";
            var c = new PixelCanvas(RaidCardW, RaidCardH);
            for (int y = 1; y < RaidCardH - 1; y++)
                for (int x = 1; x < RaidCardW - 1; x++)
                {
                    if (RaidCorner(x, y)) continue;
                    int d = RaidEdgeDistance(x, y);
                    Color32 col;
                    if (legend) col = d <= 1 ? outer : d == 2 ? ground : d == 3 ? inner : ground; // double border
                    else col = d <= 1 ? outer : d == 2 ? inner : ground;
                    if (d > (legend ? 3 : 2) && (y == 10 || y == RaidCardH - 6)) col = line; // thin dividers under the grade band and above the foot
                    c.Set(x, y, col);
                }
            switch (kind)
            {
                case "epic":
                    foreach (int sx in new[] { 5, RaidCardW - 7 })
                        foreach (int sy in new[] { 5, RaidCardH - 7 })
                            c.Rect(sx, sy, 2, 2, outer);
                    break;
                case "unique":
                    // Corner diamonds.
                    foreach (int cx in new[] { 6, RaidCardW - 7 })
                        foreach (int cy in new[] { 6, RaidCardH - 7 })
                        {
                            var dia = PixelCanvas.Hex("b58a1c");
                            c.Set(cx, cy, dia); c.Set(cx - 1, cy, dia); c.Set(cx + 1, cy, dia); c.Set(cx, cy - 1, dia); c.Set(cx, cy + 1, dia);
                        }
                    break;
                case "legend":
                    var flame = PixelCanvas.Hex("ffb347");
                    foreach (int cx in new[] { 6, RaidCardW - 8 })
                        foreach (int cy in new[] { 6, RaidCardH - 8 })
                        {
                            c.Rect(cx, cy, 3, 3, flame);
                            c.Set(cx + 1, cy - 1, flame); c.Set(cx + 1, cy + 3, flame); c.Set(cx - 1, cy + 1, flame); c.Set(cx + 3, cy + 1, flame);
                        }
                    // Small crown, 9x5, on the top edge.
                    int ox = (RaidCardW - 9) / 2;
                    var crown = PixelCanvas.Hex("ffb347");
                    c.Rect(ox, 2, 9, 3, crown);
                    foreach (int px in new[] { 0, 4, 8 }) { c.Set(ox + px, 1, crown); c.Set(ox + px, 0, crown); }
                    c.Set(ox + 2, 1, crown); c.Set(ox + 6, 1, crown);
                    break;
            }
            c.Outline(PixelCanvas.Hex("2a1c10"));
            return c;
        }

        /// <summary>48x48 soft white square light (tinted with the image colour).</summary>
        static PixelCanvas DrawRaidGlow()
        {
            const int N = 48;
            var c = new PixelCanvas(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float nx = Mathf.Abs((x + 0.5f) / N * 2f - 1f), ny = Mathf.Abs((y + 0.5f) / N * 2f - 1f);
                    float e = Mathf.Max(nx, ny);
                    float a = Mathf.Clamp01((1f - e) / 0.45f);
                    c.Set(x, y, new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a * a)));
                }
            return c;
        }

        /// <summary>64x64 thin white ring with soft edges (it grows and fades when a unique card lands).</summary>
        static PixelCanvas DrawRaidRing()
        {
            const int N = 64;
            var c = new PixelCanvas(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f - N * 0.5f) / (N * 0.5f), dy = (y + 0.5f - N * 0.5f) / (N * 0.5f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    float a = Mathf.Clamp01(1f - Mathf.Abs(r - 0.86f) / 0.07f);
                    if (a > 0f) c.Set(x, y, new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a)));
                }
            return c;
        }

        /// <summary>64x64 eight light beams from the centre, fading outwards (rotates behind a legendary card).</summary>
        static PixelCanvas DrawRaidRays()
        {
            const int N = 64;
            var c = new PixelCanvas(N, N);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float dx = (x + 0.5f - N * 0.5f) / (N * 0.5f), dy = (y + 0.5f - N * 0.5f) / (N * 0.5f);
                    float r = Mathf.Sqrt(dx * dx + dy * dy);
                    if (r >= 1f) continue;
                    float beam = Mathf.Pow(Mathf.Max(0f, Mathf.Cos(8f * Mathf.Atan2(dy, dx))), 4f);
                    float a = beam * Mathf.Pow(1f - r, 1.2f);
                    if (a > 0.01f) c.Set(x, y, new Color32(255, 255, 255, (byte)Mathf.RoundToInt(255f * a)));
                }
            return c;
        }
    }
}
