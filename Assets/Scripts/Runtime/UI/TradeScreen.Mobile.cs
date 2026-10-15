namespace DotRPG
{
    /// <summary>[TOUCH] Thumb-sized buttons on a phone (the 1280x720 window shows at 0.8 on the 1024x576 canvas).</summary>
    public partial class TradeScreen
    {
        static void FitTouch(TradeScreen w)
        {
            // The bottom row grows down into the window margin, so nothing above it moves.
            foreach (var name in new[] { "Request", "Accept", "Decline", "Lock", "Confirm", "Cancel" })
                MobileTouch.GrowNamed(w.content, name, MobileTouch.WindowTapHeight, MobileTouch.Grow.Down);
            // Bag page arrows: as tall as fits between the last bag row and the panel edge.
            MobileTouch.GrowNamed(w.content, "Prev", 54f, MobileTouch.Grow.Centre);
            MobileTouch.GrowNamed(w.content, "Next", 54f, MobileTouch.Grow.Centre);
        }
    }
}
