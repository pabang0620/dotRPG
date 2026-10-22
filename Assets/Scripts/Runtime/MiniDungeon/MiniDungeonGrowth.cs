namespace DotRPG
{
    /// <summary>
    /// [GROWTH] Mini dungeons in 성장 추천 and the 할 일 window (Docs/PLAN_GROWTH_GUIDE.md "미니 던전 연결 지점"). Only while the
    /// next main quest waits on the hero's level (catch-up, x10 XP) and today's 30 minutes are not used up: then it is the
    /// fastest way forward and heads the list. Otherwise the mini dungeon is slower than hunting and is not suggested.
    /// </summary>
    public static class MiniDungeonGrowth
    {
        public static void Register() => GrowthGuide.RegisterSource(EntryFor);

        static GrowthGuide.Entry EntryFor(int level)
        {
            var mini = Game.MiniDungeon;
            if (mini == null || mini.Active || level >= Progression.LevelCap || !ContentUnlocks.IsOpen(ContentUnlocks.Feature.MiniDungeon)) return null;
            MiniDungeonDef tier = null;
            foreach (var d in MiniDungeonCatalog.All)
                if (level >= d.MinLevel && level <= d.MaxLevel) tier = d;
            if (tier == null || mini.RemainingSeconds <= 0f) return null;
            int gate = MiniDungeonCatalog.CatchUpGate(level);
            if (gate <= 0) return null;
            int minutes = UnityEngine.Mathf.CeilToInt(mini.RemainingSeconds / 60f);
            return new GrowthGuide.Entry
            {
                order = GrowthGuide.OrderCatchUp, // the quickest way to the waiting main quest comes first
                shortText = L.T(tier.Name),
                text = L.F("따라잡기 x{0} · 다음 메인 퀘스트 Lv.{1}까지 미니던전 {2} · 오늘 {3}분 남음", MiniDungeonCatalog.CatchUpMultiplier, gate, L.T(tier.Name), minutes),
                button = L.T("미니던전"),
                action = () => Game.UI.MiniDungeon.Open(),
            };
        }
    }
}
