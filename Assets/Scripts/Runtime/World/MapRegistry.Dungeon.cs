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

        // [BGM] Room music by dungeon theme (Docs/BGM_PROMPTS.md); boss rooms share one track, the raid boss has its own.
        public const string MusicDgnCanyon = "music_dgn_canyon", MusicDgnForest = "music_dgn_forest", MusicDgnWinter = "music_dgn_winter";
        public const string MusicBoss = "music_boss", MusicRaid = "music_raid", MusicRaidEnrage = "music_raid_enrage";
        public const string MusicClear = "music_clear", MusicFail = "music_fail", MusicTitle = "music_title";

        static MapInfo Room(string id, string name, string file, MapTheme theme, string music) => new MapInfo
        {
            id = id, displayName = name, resource = "Maps/Dungeons/" + file, music = music, theme = theme,
            safe = false, instanced = true, hint = "던전 안. 방의 적을 모두 쓰러뜨리면 문이 열린다.",
        };

        static readonly MapInfo[] DungeonRooms =
        {
            Room(DgnCanyon1, "광산 갱도", "Canyon1", MapTheme.Canyon, MusicDgnCanyon),
            Room(DgnCanyon2, "지하 수로", "Canyon2", MapTheme.Canyon, MusicDgnCanyon),
            Room(DgnCanyon3, "무너진 채굴장", "Canyon3", MapTheme.Canyon, MusicDgnCanyon),
            Room(DgnCanyonBoss, "광산 심층부", "CanyonBoss", MapTheme.Canyon, MusicBoss),
            Room(DgnForest1, "무덤 길", "Forest1", MapTheme.Forest, MusicDgnForest),
            Room(DgnForest2, "안개 연못", "Forest2", MapTheme.Forest, MusicDgnForest),
            Room(DgnForest3, "폐허 제단", "Forest3", MapTheme.Forest, MusicDgnForest),
            Room(DgnForestBoss, "묘지 중심", "ForestBoss", MapTheme.Forest, MusicBoss),
            Room(DgnWinter1, "얼음 동굴 입구", "Winter1", MapTheme.Winter, MusicDgnWinter),
            Room(DgnWinter2, "얼어붙은 호수", "Winter2", MapTheme.Winter, MusicDgnWinter),
            Room(DgnWinter3, "무기 보관실", "Winter3", MapTheme.Winter, MusicDgnWinter),
            Room(DgnWinterBoss, "수문장의 방", "WinterBoss", MapTheme.Winter, MusicBoss),
            Room(DgnRaid1, "친위대 전초 · 기사단", "Raid1", MapTheme.Canyon, MusicDgnForest),
            Room(DgnRaid2, "친위대 전초 · 사령탑", "Raid2", MapTheme.Winter, MusicDgnForest),
            Room(DgnRaidBoss, "해골왕의 옥좌", "RaidBoss", MapTheme.Canyon, MusicRaid),
            Room(DgnBargas1, "흑철 진영 · 선봉대", "Raid2", MapTheme.Winter, MusicDgnWinter),
            Room(DgnBargas2, "흑철 진영 · 사령 부대", "Raid1", MapTheme.Winter, MusicDgnWinter),
            Room(DgnBargasBoss, "흑철 진영 · 지휘 천막", "RaidBoss", MapTheme.Winter, MusicRaid),
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
