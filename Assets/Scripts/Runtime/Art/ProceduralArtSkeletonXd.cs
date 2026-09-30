namespace DotRPG
{
    /// <summary>
    /// 64px-per-tile (density 4) skeleton monster: five views x eight frames, same world size as the 32px
    /// art (a frame is 64x80, feet pivot at (32, 6)) with twice the dots per side.
    /// Returns null until it is drawn, and the 32px skeleton is used instead.
    /// </summary>
    public static partial class ProceduralArt
    {
        static PixelCanvas DrawSkeletonXd(CharacterLook look, string dir, string frame) => null;
    }
}
