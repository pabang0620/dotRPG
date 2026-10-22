namespace DotRPG
{
    /// <summary>
    /// Dungeon rooms (plan §6.0): small instanced maps in Resources/Maps/Dungeons. They are found by
    /// <see cref="MapRegistry.Get"/> but are not part of <see cref="MapRegistry.All"/> (map window, portal chain).
    /// Three layouts per theme are shared between the weekday dungeons of that theme.
    /// </summary>
    public static partial class MapRegistry
    {
        public const string DgnCanyon1 = "dgn_canyon_1", DgnCanyon2 = "dgn_canyon_2", DgnCanyon3 = "dgn_canyon_3", DgnCanyonBoss = "dgn_canyon_boss";
        public const string DgnForest1 = "dgn_forest_1", DgnForest2 = "dgn_forest_2", DgnForest3 = "dgn_forest_3", DgnForestBoss = "dgn_forest_boss";
        public const string DgnWinter1 = "dgn_winter_1", DgnWinter2 = "dgn_winter_2", DgnWinter3 = "dgn_winter_3", DgnWinterBoss = "dgn_winter_boss";
        public const string DgnRaid1 = "dgn_raid_1", DgnRaid2 = "dgn_raid_2", DgnRaidBoss = "dgn_raid_boss";
        // [RAID] 흑철 진영 reuses the fortress layouts under its own names and music.
        public const string DgnBargas1 = "dgn_bargas_1", DgnBargas2 = "dgn_bargas_2", DgnBargasBoss = "dgn_bargas_boss";
        public const string DgnGolem1 = "dgn_golem_1", DgnGolem2 = "dgn_golem_2", DgnGolemBoss = "dgn_golem_boss";
        public const string DgnGrah1 = "dgn_grah_1", DgnGrahBoss = "dgn_grah_boss";
        // [CH3] 지바크 at the broken guardian stone and 카마츠's camp: existing layouts in the dry canyon theme (Docs/ch3/RAIDS.md).
        public const string DgnXibak1 = "dgn_xibak_1", DgnXibak2 = "dgn_xibak_2", DgnXibakBoss = "dgn_xibak_boss";
        public const string DgnKamatz1 = "dgn_kamatz_1", DgnKamatzBoss = "dgn_kamatz_boss";

        /// <summary>[CH3] The chapter 3 raid rooms (closed with the chapter, MapRegistry.IsChapter3).</summary>
        static bool IsChapter3Room(string id) =>
            id == DgnXibak1 || id == DgnXibak2 || id == DgnXibakBoss || id == DgnKamatz1 || id == DgnKamatzBoss;

        // [BGM] Room music by dungeon theme (Docs/BGM_PROMPTS.md); boss rooms share one track, the raid boss has its own.
        public const string MusicDgnCanyon = "music_dgn_canyon", MusicDgnForest = "music_dgn_forest", MusicDgnWinter = "music_dgn_winter";
        public const string MusicBoss = "music_boss", MusicRaid = "music_raid", MusicRaidEnrage = "music_raid_enrage";
        public const string MusicClear = "music_clear", MusicFail = "music_fail", MusicTitle = "music_title";

        static MapInfo Room(string id, string name, string file, MapTheme theme, string music) => new MapInfo
        {
            id = id, displayName = name, resource = "Maps/Dungeons/" + file, music = music, theme = theme,
            safe = false, instanced = true, hint = L.T("던전 안. 방의 적을 모두 쓰러뜨리면 문이 열립니다."),
        };

        static readonly MapInfo[] DungeonRooms =
        {
            Room(DgnCanyon1, L.T("광산 갱도"), "Canyon1", MapTheme.Canyon, MusicDgnCanyon),
            Room(DgnCanyon2, L.T("지하 수로"), "Canyon2", MapTheme.Canyon, MusicDgnCanyon),
            Room(DgnCanyon3, L.T("무너진 채굴장"), "Canyon3", MapTheme.Canyon, MusicDgnCanyon),
            Room(DgnCanyonBoss, L.T("광산 심층부"), "CanyonBoss", MapTheme.Canyon, MusicBoss),
            Room(DgnForest1, L.T("무덤 길"), "Forest1", MapTheme.Forest, MusicDgnForest),
            Room(DgnForest2, L.T("안개 연못"), "Forest2", MapTheme.Forest, MusicDgnForest),
            Room(DgnForest3, L.T("폐허 제단"), "Forest3", MapTheme.Forest, MusicDgnForest),
            Room(DgnForestBoss, L.T("묘지 중심"), "ForestBoss", MapTheme.Forest, MusicBoss),
            Room(DgnWinter1, L.T("얼음 동굴 입구"), "Winter1", MapTheme.Winter, MusicDgnWinter),
            Room(DgnWinter2, L.T("얼어붙은 호수"), "Winter2", MapTheme.Winter, MusicDgnWinter),
            Room(DgnWinter3, L.T("무기 보관실"), "Winter3", MapTheme.Winter, MusicDgnWinter),
            Room(DgnWinterBoss, L.T("수문장의 방"), "WinterBoss", MapTheme.Winter, MusicBoss),
            Room(DgnRaid1, L.T("친위대 전초 · 기사단"), "Raid1", MapTheme.Canyon, MusicDgnForest),
            Room(DgnRaid2, L.T("친위대 전초 · 사령탑"), "Raid2", MapTheme.Winter, MusicDgnForest),
            Room(DgnRaidBoss, L.T("해골왕의 옥좌"), "RaidBoss", MapTheme.Canyon, MusicRaid),
            Room(DgnBargas1, L.T("흑철 진영 · 선봉대"), "Raid2", MapTheme.Winter, MusicDgnWinter),
            Room(DgnBargas2, L.T("흑철 진영 · 사령 부대"), "Raid1", MapTheme.Winter, MusicDgnWinter),
            Room(DgnBargasBoss, L.T("흑철 진영 · 지휘 천막"), "RaidBoss", MapTheme.Winter, MusicRaid),
            Room(DgnGolem1, L.T("외곽 석실 · 갱도"), "Canyon2", MapTheme.Canyon, MusicDgnCanyon),
            Room(DgnGolem2, L.T("외곽 석실 · 수문"), "Canyon3", MapTheme.Canyon, MusicDgnCanyon),
            Room(DgnGolemBoss, L.T("외곽 석실 · 바위 심장"), "CanyonBoss", MapTheme.Canyon, MusicRaid),
            Room(DgnGrah1, L.T("수호석 석실 · 회랑"), "Raid1", MapTheme.Canyon, MusicDgnCanyon),
            Room(DgnGrahBoss, L.T("수호석 석실 · 수호석"), "RaidBoss", MapTheme.Canyon, MusicRaidEnrage),
            Room(DgnXibak1, L.T("깨진 수호석 자리 · 파수석 길"), "Canyon2", MapTheme.DryCanyon, MusicDgnCanyon),
            Room(DgnXibak2, L.T("깨진 수호석 자리 · 들개 굴"), "Canyon3", MapTheme.DryCanyon, MusicDgnCanyon),
            Room(DgnXibakBoss, L.T("깨진 수호석 자리 · 침식의 중심"), "CanyonBoss", MapTheme.DryCanyon, MusicRaid),
            Room(DgnKamatz1, L.T("잿빛 손 진영 · 야영지"), "Raid1", MapTheme.DryCanyon, MusicDgnCanyon),
            Room(DgnKamatzBoss, L.T("잿빛 손 진영 · 지휘관의 우리"), "RaidBoss", MapTheme.DryCanyon, MusicRaidEnrage),
        };

        /// <summary>Every dungeon room map (tests, preloading).</summary>
        public static System.Collections.Generic.IEnumerable<MapInfo> Rooms => DungeonRooms;

        static MapInfo GetRoom(string id)
        {
            foreach (var map in DungeonRooms)
                if (map.id == id) return map;
            return null;
        }
    }
}
