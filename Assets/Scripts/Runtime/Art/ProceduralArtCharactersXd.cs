namespace DotRPG
{
    /// <summary>
    /// 64px-per-tile (density 4) people: the warrior and mage with every worn-gear variant and every NPC,
    /// five views x eight frames, plus the held weapons (wpn_*) and tools (tool_*). Same world size as the
    /// 32px art (a frame is 64x80, feet pivot at (32, 6)) with twice the dots per side.
    /// Each method returns null until it is drawn, and the 32px art is used instead.
    /// </summary>
    public static partial class ProceduralArt
    {
        static PixelCanvas DrawHumanXd(CharacterLook look, string dir, string frame) => null;

        static PixelCanvas DrawWeaponXd(string kind, int tier) => null;

        static PixelCanvas DrawToolXd(string kind) => null;
    }
}
