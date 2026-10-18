namespace DotRPG
{
    /// <summary>
    /// [GROWTH] Mini dungeons in 성장 추천 and the 할 일 window (Docs/PLAN_GROWTH_GUIDE.md "미니 던전 연결 지점"): while the
    /// hero's level has a tier and today's 30 minutes are not used up, the guide offers the mini dungeon window.
    /// </summary>
    public static class MiniDungeonGrowth
    {
        public static void Register() => GrowthGuide.RegisterSource(EntryFor);

        static GrowthGuide.Entry EntryFor(int level)
        {
            var mini = Game.MiniDungeon;
            if (mini == null || mini.Active || level >= Progression.MaxLevel) return null;
            MiniDungeonDef tier = null;
            foreach (var d in MiniDungeonCatalog.All)
                if (level >= d.MinLevel && level <= d.MaxLevel) tier = d;
            if (tier == null || mini.RemainingSeconds <= 0f) return null;
            int minutes = UnityEngine.Mathf.CeilToInt(mini.RemainingSeconds / 60f);
            return new GrowthGuide.Entry
            {
                order = GrowthGuide.OrderNormal + 50, // after the normal dungeon, before hunting grounds
                shortText = L.T(tier.Name),
                text = L.F("미니던전 {0} · 오늘 {1}분 남음 (경험치 전용, 혼자)", L.T(tier.Name), minutes),
                button = L.T("미니던전"),
                action = () => Game.UI.MiniDungeon.Open(),
            };
        }
    }
}
