namespace DotRPG
{
    /// <summary>Visual palette a map is painted with (ground tiles, water colour, default floor).</summary>
    public enum MapTheme
    {
        /// <summary>Old 16px grass village art (kept for reference; the village now uses <see cref="Town"/>).</summary>
        Village,
        /// <summary>Rocky canyon town: flagstone floor, dark cliffs, teal water (Canyon.txt).</summary>
        Canyon,
        /// <summary>Snowy forest village: seamless snow, cobble paths, grey-violet cliffs, blue river (Winter.txt).</summary>
        Winter,
        /// <summary>High-resolution (32px per tile) town: painted grass, cobblestone streets, shops (Village.txt).</summary>
        Town,
        /// <summary>High-resolution hunting ground: dark forest floor, dirt trails, ruins (Forest.txt).</summary>
        Forest,
    }

    /// <summary>One playable map. Portals '&gt;' lead to <see cref="nextMap"/>, '&lt;' to <see cref="previousMap"/>.</summary>
    public sealed class MapInfo
    {
        public string id;
        public string displayName;
        /// <summary>Resources path of the text layout.</summary>
        public string resource;
        public string music;
        public MapTheme theme;
        public string nextMap;
        public string previousMap;
        /// <summary>One-line tip shown in the map window.</summary>
        public string hint;
        /// <summary>Town (no monsters) vs. hunting ground; shown in the map window.</summary>
        public bool safe = true;
        // [DUNGEON] Dungeon room: not listed in the map window / portal chain, never saved as a position.
        public bool instanced;

        /// <summary>Drawn with the 32px-per-tile art set.</summary>
        public bool HighRes => theme == MapTheme.Town || theme == MapTheme.Forest;
    }

    /// <summary>All maps of the game. Add an entry + a Resources/Maps/*.txt file to add a map.</summary>
    public static partial class MapRegistry
    {
        public const string Village = "village";
        public const string Forest = "forest";
        public const string Canyon = "canyon";
        public const string Winter = "winter";

        static readonly MapInfo[] Maps = BuildMaps();

        static MapInfo[] BuildMaps()
        {
            var maps = new System.Collections.Generic.List<MapInfo>();
            void Town(string id, string name, string resource, MapTheme theme, string music)
            {
                maps.Add(new MapInfo { id = id, displayName = name, resource = resource, theme = theme, music = music,
                    hint = "안전한 마을. 이어지는 사냥터 3곳에서 입장 제한 없이 성장할 수 있다.\n지도에서 권장 레벨과 귀환 마을을 확인하자." });
                foreach (var z in HuntingGrounds.All)
                    if (z.village == id) maps.Add(new MapInfo { id = z.id, displayName = z.name, theme = z.theme, safe = false,
                        resource = z.id == Forest ? "Maps/Forest" : null, music = theme == MapTheme.Town ? "music_forest" : music,
                        hint = $"권장 Lv.{z.minLevel}~{z.maxLevel} · 몬스터 Lv.{z.monsterLevel}\n처치 경험치 {z.KillXp} · 재생성 {HuntingGrounds.RespawnSeconds:0}초\n던전 클리어·입장권 없이 반복 사냥 가능.\n귀환 주문서: {name}\n서쪽: 이전 지역 / 동쪽: 다음 지역" });
            }
            Town(Village, "해골 숲 옆 작은 마을", "Maps/Village", MapTheme.Town, "music_village");
            Town(Canyon, "바위 협곡 마을", "Maps/Canyon", MapTheme.Canyon, "music_canyon");
            Town(Winter, "눈꽃 숲 마을", "Maps/Winter", MapTheme.Winter, "music_winter");
            for (int i = 0; i < maps.Count; i++)
            {
                maps[i].previousMap = i > 0 ? maps[i-1].id : null;
                maps[i].nextMap = i + 1 < maps.Count ? maps[i+1].id : Winter;
            }
            return maps.ToArray();
        }

        /// <summary>Every map in travel order (for the map window).</summary>
        public static System.Collections.Generic.IEnumerable<MapInfo> All => Maps;

        public static MapInfo Get(string id)
        {
            foreach (var map in Maps)
                if (map.id == id) return map;
            // [DUNGEON] Instanced dungeon rooms (MapRegistry.Dungeon.cs).
            return GetRoom(id);
        }

        public static bool Exists(string id) => Get(id) != null;

        public static MapInfo Default => Maps[0];
    }
}
