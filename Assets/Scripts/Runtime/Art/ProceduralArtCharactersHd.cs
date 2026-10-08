using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// High-resolution (density 2, 32px per tile) characters and monsters. Same world size as the old
    /// 16x20 art - a frame is 32x40 with doubled pivot - but twice the detail: eyes with highlights,
    /// hair strands and shine, cloth folds, armour plates and rivets, belts and buckles, a clean 1px
    /// dark outline. Five views (down, up, side, downside, upside; left = flipped by the renderer) x
    /// eight frames (idle0, idle1, walk0-3, attack, hurt). Also the held weapons wpn_*, held tools
    /// tool_*, damage digits num_*, monster HP bars hpbar_* and the ground shadow.
    ///
    /// The old 16px functions stay in ProceduralArt.cs / ProceduralArtCharacters.cs untouched; the
    /// DrawCharacter and DrawCharacterFamily routers point here.
    /// </summary>
    public static partial class ProceduralArt
    {
        // ---------- Palette helpers (light from the top-left, soft ramps) ----------

        // Frame is 32 wide x 40 tall. Old layout x2: head rows 4..23, body 24..31, legs 32..37.
        const int W = 32, HGT = 40;

        static readonly Color32 Blush = PixelCanvas.Hex("#f0968a");
        static readonly Color32 EyeWhite = PixelCanvas.Hex("#f7f2e8");
        static readonly Color32 ShoeCol = PixelCanvas.Hex("#4a3226");
        static readonly Color32 ShoeDark = PixelCanvas.Hex("#33231b");
        static readonly Color32 RobeShoe = PixelCanvas.Hex("#3a2a40");

        static Color32 Lit(Color32 c, float f) => PixelCanvas.Shade(c, f);

        // ---------- Router entry points (called by ProceduralArt.Draw) ----------

        static PixelCanvas DrawCharacterHd(CharacterLook look, string dir, string frame)
        {
            // The 64px characters (ProceduralArtCharactersXd.cs / ProceduralArtSkeletonXd.cs) take over as
            // soon as they return a frame; until then the 32px art below is used.
            var xd = look.body == BodyKind.Skeleton ? DrawSkeletonXd(look, dir, frame) : DrawHumanXd(look, dir, frame);
            if (xd != null) return xd;

            var c = Hd(W, HGT);
            var v = new View(dir);
            if (look.body == BodyKind.Skeleton) DrawSkeletonBodyHd(c, look, v, frame);
            else DrawHumanBodyHd(c, look, v, frame);

            if (look.body == BodyKind.Human && look.hat == HatKind.Wizard)
            {
                // Pointed hat does not fit; move up into a taller canvas (pivot from the bottom, so the
                // feet stay put) and add the cone. 10 extra rows (5 old rows x2).
                const int extra = 10;
                var tall = Hd(W, HGT + extra);
                for (int y = 0; y < HGT; y++)
                    for (int x = 0; x < W; x++)
                        tall.Pixels[(y + extra) * W + x] = c.Pixels[y * W + x];
                FrameInfo(frame, out int bob, out _);
                DrawWizardConeHd(tall, look, v, 6, 4 + bob * 2 + extra);
                c = tall;
            }
            c.Outline(Outline);
            return c.WithPivot(16, 3f);
        }

        static PixelCanvas DrawCharacterFamilyHd(string key, string[] parts)
        {
            switch (parts[0])
            {
                case "wpn":
                {
                    int tier = parts.Length > 2 ? int.Parse(parts[2]) : 0;
                    return DrawWeaponXd(parts[1], tier) ?? DrawWeaponHd(parts[1], tier);
                }
                case "tool": return DrawToolXd(parts[1]) ?? DrawToolHd(parts[1]);
                case "num": return DrawDigitHd(int.Parse(parts[1]));
                case "hpbar": return DrawHpBarHd(parts[1]);
                case "shadow": return DrawShadowHd();
            }
            return null;
        }
    }
}
