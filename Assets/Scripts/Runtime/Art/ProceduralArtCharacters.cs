using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Chibi character generator. Five views cover the eight facings: down, up, side, downside
    /// (3/4 front) and upside (3/4 back); the left-facing ones are the right-facing art flipped by the
    /// renderer. Frames: idle0, idle1, walk0..walk3, attack, hurt. Worn gear shows on the body:
    /// vest / leather / plate tops, trouser colours and robe colours. Also draws the held weapons.
    /// </summary>
    public static partial class ProceduralArt
    {
        /// <summary>Which way a view looks, decoded from its sprite key.</summary>
        readonly struct View
        {
            public readonly bool front, back, side, diag;

            public View(string dir)
            {
                side = dir == "side";
                diag = dir == "downside" || dir == "upside";
                back = dir == "up" || dir == "upside";
                front = !side && !back;
            }
        }

        // ---------- Characters ----------

        /// <summary>
        /// Sprite keys that belong with the characters: held weapons (wpn_*), held tools (tool_*), damage
        /// digits (num_*), monster HP bars (hpbar_*) and the ground shadow, so they can be swapped in one place.
        /// </summary>
        static PixelCanvas DrawCharacterFamily(string key, string[] parts)
        {
            // Routed to the 32px (density 2) art in ProceduralArtCharactersHd.cs.
            return DrawCharacterFamilyHd(key, parts);
        }

        /// <summary>
        /// Draws one frame of a chibi character at 32px (density 2). Right-facing views face right;
        /// the renderer flips them for left. Delegates to the HD art.
        /// </summary>
        public static PixelCanvas DrawCharacter(CharacterLook look, string dir, string frame)
        {
            // [MONSTER] Dungeon monsters / bosses (bigger canvases for bosses).
            // [MONHD] HD (density 2) monsters on the HD skeleton body.
            if (look.body == BodyKind.Monster) return DrawMonsterCharacterHd(look, dir, frame);
            return DrawCharacterHd(look, dir, frame);
        }

        static void FrameInfo(string frame, out int bob, out int step)
        {
            bob = 0;
            step = 0; // -1 left foot forward, +1 right foot forward
            switch (frame)
            {
                case "idle1": bob = 1; break;
                case "walk0": step = -1; break;
                case "walk1": bob = 1; break;
                case "walk2": step = 1; break;
                case "walk3": bob = 1; break;
                case "attack": step = 1; break;
                case "hurt": bob = 1; break;
            }
        }
    }
}
