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
        /// <summary>
        /// Opacity (0-1) of the ground shadows cast by the sun, which is fixed in the top-left of the sky.
        /// Bright daytime maps set it; 0 (dark places such as dungeons) casts no sun shadows.
        /// </summary>
        public float sunShadow;

        /// <summary>Drawn with the 32px-per-tile art set.</summary>
        public bool HighRes => theme == MapTheme.Town || theme == MapTheme.Forest;
    }

    /// <summary>All maps of the game. Add an entry + a Resources/Maps/*.txt file to add a map.</summary>
    public static class MapRegistry
    {
        public const string Village = "village";
        public const string Forest = "forest";
        public const string Canyon = "canyon";
        public const string Winter = "winter";

        static readonly MapInfo[] Maps =
        {
            new MapInfo
            {
                id = Village, displayName = "해골 숲 옆 작은 마을", resource = "Maps/Village", music = "music_village",
                theme = MapTheme.Town, nextMap = Forest, previousMap = null, sunShadow = 0.26f,
                hint = "몬스터가 없는 안전한 마을.\n광장 둘레에 잡화점 · 대장간 · 창고가 있다.\n동쪽 큰길 끝 숲길로 나가면 사냥터 \"해골 숲\"이다.",
            },
            new MapInfo
            {
                id = Forest, displayName = "사냥터 · 해골 숲", resource = "Maps/Forest", music = "music_village",
                theme = MapTheme.Forest, nextMap = Canyon, previousMap = Village, safe = false, sunShadow = 0.22f,
                hint = "해골이 돌아다니는 사냥터. 나무와 바위에서 재료도 얻는다.\n서쪽 숲길: 작은 마을    동쪽 끝 숲길: 바위 협곡 마을\n마을 귀환 주문서 [T]로 언제든 마을로 돌아갈 수 있다.",
            },
            new MapInfo
            {
                id = Canyon, displayName = "바위 협곡 마을", resource = "Maps/Canyon", music = "music_village",
                theme = MapTheme.Canyon, nextMap = Winter, previousMap = Forest, sunShadow = 0.26f,
                hint = "남쪽 입구로 나가면 사냥터(해골 숲)로 이어진다.\n윗마을 돌문 오른쪽 눈길은 눈꽃 숲 마을로 이어진다.\n동쪽 샛길 전망대에 보물상자가 있다.",
            },
            new MapInfo
            {
                id = Winter, displayName = "눈꽃 숲 마을", resource = "Maps/Winter", music = "music_village",
                theme = MapTheme.Winter, nextMap = null, previousMap = Canyon, sunShadow = 0.24f,
                hint = "남쪽 입구로 나가면 바위 협곡 마을로 돌아간다.\n동쪽 모닥불 쉼터 너머 다리를 건너면 샛길이 있다.\n돌계단 위 윗마을에는 강이 내려다보이는 전망대가 있다.",
            },
        };

        /// <summary>Every map in travel order (for the map window).</summary>
        public static System.Collections.Generic.IEnumerable<MapInfo> All => Maps;

        public static MapInfo Get(string id)
        {
            foreach (var map in Maps)
                if (map.id == id) return map;
            return null;
        }

        public static bool Exists(string id) => Get(id) != null;

        public static MapInfo Default => Maps[0];
    }
}
