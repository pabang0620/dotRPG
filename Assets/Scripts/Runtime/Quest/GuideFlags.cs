namespace DotRPG
{
    /// <summary>
    /// [GUIDE] Flags the 안내 의뢰 (Docs/PLAN_GUIDE_QUESTS.md) wait for: each is set by the real action it teaches
    /// (equip, enhance, sell, store, open a window...). Setting one again is a no-op; a flag set before the quest was
    /// taken still counts (flag objectives read the journal).
    /// </summary>
    public static class GuideFlags
    {
        public const string Equip = "guide_equip", Enhance = "guide_enhance", Sell = "guide_sell", Storage = "guide_storage",
            WorldMap = "guide_worldmap", AutoQuest = "guide_autoquest", AutoHunt = "guide_autohunt", Gem = "guide_gem",
            Auction = "guide_auction", Party = "guide_party", Daily = "guide_daily", Recommend = "guide_recommend", Style = "guide_style";

        public static void Mark(string flag)
        {
            if (Game.Quest == null || Game.Session == null || string.IsNullOrEmpty(flag) || Game.Quest.HasFlag(flag)) return;
            Game.Quest.SetFlag(flag);
        }

        /// <summary>Windows that count as "opened" for a guide (by type name, so no screen needs to know about quests).</summary>
        public static void WindowShown(object screen)
        {
            switch (screen)
            {
                case AuctionScreen _: Mark(Auction); break;
                case PartyFinderScreen _: Mark(Party); break;
                case WorldMapScreen _: Mark(WorldMap); break;
            }
        }
    }
}
