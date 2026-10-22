namespace DotRPG
{
    /// <summary>
    /// [CH3] 사얍 마을's distrust (Docs/ch3/WORLD_AND_STORY.md 6): until 3-4 믿음의 값 (c3_trust) is completed, its shop,
    /// blacksmith and storage keepers refuse a human.
    /// </summary>
    public static class SayabTrust
    {
        public const string TrustQuest = "c3_trust";

        static bool InSayab()
        {
            string id = Game.Session?.MapId;
            if (id == MapRegistry.Sayab) return true;
            return MapRegistry.Get(id)?.exteriorMap == MapRegistry.Sayab;
        }

        /// <summary>A keeper of 사얍 turns the hero away right now.</summary>
        public static bool Refuses()
        {
            if (Game.Session == null || !InSayab()) return false;
            return !Game.Session.Journal.IsCompleted(TrustQuest);
        }

        public static string RefusalLine => L.T("인간에겐 팔 것도, 맡을 것도 없다. 익시 님의 허락을 받아 와.");
    }
}
