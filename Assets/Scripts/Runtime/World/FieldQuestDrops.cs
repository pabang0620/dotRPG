using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [CH3] Quest pickups that fall in the dry canyon fields (Docs/ch3/WORLD_AND_STORY.md 2, 5): 공허 정수 for 3-4, 마른 가죽,
    /// 재 속 약초 and 마른 꽃 for the 사얍 side quests and the daily 리아의 약초밭. Exported per map (maps.json "fieldDrops");
    /// online the server rolls them with the kill, offline EnemyController.DropLoot does.
    /// </summary>
    public static class FieldQuestDrops
    {
        public const string VoidEssence = "qi_void_essence", DryHide = "qi_dry_hide", AshHerb = "qi_ash_herb", DryFlower = "qi_dry_flower";

        public sealed class Drop
        {
            public string item;
            public float chance;
            /// <summary>Only these monsters drop it (empty = any monster of the field).</summary>
            public string[] monsters = new string[0];
        }

        static readonly Dictionary<string, Drop[]> byMap = new Dictionary<string, Drop[]>
        {
            [MapRegistry.DryFloor] = new[]
            {
                new Drop { item = DryHide, chance = .3f, monsters = new[] { MonsterDatabase.VoidHound } },
                new Drop { item = DryFlower, chance = .06f },
            },
            [MapRegistry.DryStones] = new[]
            {
                new Drop { item = VoidEssence, chance = .25f, monsters = new[] { MonsterDatabase.VoidSentry, MonsterDatabase.VoidCaller } },
                new Drop { item = AshHerb, chance = .15f },
            },
        };

        static readonly Drop[] none = new Drop[0];

        public static Drop[] For(string mapId) => mapId != null && byMap.TryGetValue(mapId, out var d) ? d : none;

        /// <summary>Whether this monster can drop this pickup on this map.</summary>
        public static bool Applies(Drop d, string monsterId) => d.monsters.Length == 0 || System.Array.IndexOf(d.monsters, monsterId) >= 0;
    }
}
