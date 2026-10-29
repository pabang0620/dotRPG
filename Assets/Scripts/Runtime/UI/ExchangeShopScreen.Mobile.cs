namespace DotRPG
{
    /// <summary>[TOUCH] Thumb-sized tabs and buttons on a phone (the 1280x720 window shows at 0.8 on the 1024x576 canvas).</summary>
    public partial class ExchangeShopScreen
    {
        static void FitTouch(ExchangeShopScreen w)
        {
            MobileTouch.GrowNamed(w.content, "TabGoods", MobileTouch.WindowTapHeight, MobileTouch.Grow.Up);
            MobileTouch.GrowNamed(w.content, "TabGear", MobileTouch.WindowTapHeight, MobileTouch.Grow.Up);
            MobileTouch.GrowNamed(w.content, "TabStar", MobileTouch.WindowTapHeight, MobileTouch.Grow.Up);
            // Each stays clear of its neighbours: the balance line above 도감 기록, the gear rows under the page row.
            MobileTouch.GrowNamed(w.content, "CodexRecord", 52f, MobileTouch.Grow.Centre);
            MobileTouch.GrowNamed(w.content, "DismantleAll", 48f, MobileTouch.Grow.Centre);
            MobileTouch.GrowNamed(w.content, "PageNext", 44f, MobileTouch.Grow.Centre);
            MobileTouch.GrowNamed(w.content, "PagePrev", 44f, MobileTouch.Grow.Centre);
            // 1개 분해 / 모두 분해 inside their 60 px rows.
            MobileTouch.GrowNamed(w.content, "One", 54f, MobileTouch.Grow.Centre);
            MobileTouch.GrowNamed(w.content, "All", 54f, MobileTouch.Grow.Centre);
            // 별조각으로 바꾸기 (goods bar under the tabs, above the rows) and the convert page buttons.
            MobileTouch.GrowNamed(w.content, "Convert", 52f, MobileTouch.Grow.Centre);
            for (int i = 0; i < ConvSteps.Length; i++) MobileTouch.GrowNamed(w.content, "ConvStep" + i, 56f, MobileTouch.Grow.Centre);
            MobileTouch.GrowNamed(w.content, "ConvMax", 56f, MobileTouch.Grow.Centre);
            MobileTouch.GrowNamed(w.content, "ConvOk", 56f, MobileTouch.Grow.Up);
            MobileTouch.GrowNamed(w.content, "ConvCancel", 56f, MobileTouch.Grow.Up);
        }
    }
}
