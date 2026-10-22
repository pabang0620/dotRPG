using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] Art for the chapter 3 field monsters (Docs/ch3/ART_LIST.md 3). Each gets its own regional atlas,
    /// StreamingAssets/RegionalMonsters/&lt;id&gt;.png + .json (the RegionalMonsterArt layout, 5 directions x 8 frames).
    /// While an atlas is missing: the void kinds borrow the underground production sheet of the same role,
    /// and the Grey Hand fall back to their armoured human look (procedural), so nothing is ever invisible.
    /// </summary>
    public static class Chapter3MonsterArt
    {
        static readonly Dictionary<string, string> voidStandIn = new Dictionary<string, string>
        {
            [MonsterDatabase.VoidHound] = "hollow_scarab",
            [MonsterDatabase.VoidBurster] = "hollow_scarab",
            [MonsterDatabase.VoidSentry] = "hollow_guard",
            [MonsterDatabase.VoidCaller] = "hollow_hexer",
        };
        static readonly HashSet<string> grey = new HashSet<string>
        {
            MonsterDatabase.GreyRanger, MonsterDatabase.GreyKnight, MonsterDatabase.GreyPriest, MonsterDatabase.GreyCaptain,
        };
        static readonly Dictionary<string, bool> atlas = new Dictionary<string, bool>();

        public static bool Owns(string id) => id != null && (voidStandIn.ContainsKey(id) || grey.Contains(id));

        /// <summary>The generated atlas is in the build.</summary>
        public static bool HasAtlas(string id)
        {
            if (!Owns(id)) return false;
            if (!atlas.TryGetValue(id, out bool has))
                atlas[id] = has = StreamingFiles.Exists(Path.Combine(Application.streamingAssetsPath, "RegionalMonsters", id + ".png"));
            return has;
        }

        /// <summary>Drawn from an atlas (its own or the stand-in): the void kinds always, the Grey Hand once their atlas exists.</summary>
        public static bool Supports(string id) => HasAtlas(id) || (id != null && voidStandIn.ContainsKey(id));

        static Sprite bursterGlow;
        static bool bursterGlowTried;

        /// <summary>
        /// 공허 폭발체's swollen glowing frame shown during its fuse (RegionalMonsters/void_burster_glow.png, one frame at the
        /// atlas scale, feet at the bottom). Null while the file is missing.
        /// </summary>
        public static Sprite BursterGlow()
        {
            if (bursterGlowTried) return bursterGlow;
            bursterGlowTried = true;
            string path = Path.Combine(Application.streamingAssetsPath, "RegionalMonsters", MonsterDatabase.VoidBurster + "_glow.png");
            if (!StreamingFiles.Exists(path)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "void_burster_glow", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            if (!ImageConversion.LoadImage(texture, StreamingFiles.ReadAllBytes(path), false)) { Object.Destroy(texture); return null; }
            texture.filterMode = FilterMode.Point;
            bursterGlow = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height), new Vector2(.5f, .02f), GlowPpu, 0, SpriteMeshType.FullRect);
            bursterGlow.name = "void_burster_glow";
            return bursterGlow;
        }

        /// <summary>Pixels per unit of the chapter 3 atlases (their .json pixelsPerUnit).</summary>
        const float GlowPpu = 96f;

        /// <summary>The stand-in frame while the own atlas is missing (null for the Grey Hand: their human look is drawn).</summary>
        public static Sprite StandIn(string id, string direction, string frame) =>
            voidStandIn.TryGetValue(id, out var standIn) ? UnderworldMonsterArt.Get(standIn, direction, frame) : null;
    }
}
