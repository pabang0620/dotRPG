using UnityEngine;

namespace DotRPG
{
    /// <summary>Reference-inspired generated PNGs. Aliases are village-only; other biomes keep their art.</summary>
    public static class VillageNatureArt
    {
        public const string Prefix = "village_nature_";
        public sealed class Definition
        {
            public readonly string Name;
            public readonly float Height;
            public Definition(string name, float height) { Name = name; Height = height; }
            public string Key => Prefix + Name;
            public string ResourcePath => "Art/Nature/" + Name;
            public string FilePath => "Assets/Resources/" + ResourcePath + ".png";
        }
        public static readonly Definition[] All =
        {
            new Definition("broadleaf", 4.6f), new Definition("pine", 4.3f),
            new Definition("bush", .95f), new Definition("rock", .95f),
            new Definition("log", 1.25f), new Definition("grass", .42f),
            new Definition("mushrooms", .5f),
        };
        public static Definition Find(string key)
        {
            foreach (var d in All) if (d.Key == key) return d;
            return null;
        }
        public static bool IsGenerated(Sprite sprite)
        {
            if (sprite == null) return false;
            foreach (var d in All) if (sprite.name == d.Name) return true;
            return false;
        }
        public static string Resolve(string key)
        {
            if (key.StartsWith("town_tree_") || key.StartsWith("town_fruit_") || key == "town_chop") return Prefix + "broadleaf";
            if (key.StartsWith("town_pine_")) return Prefix + "pine";
            if (key.StartsWith("town_bush_")) return Prefix + "bush";
            if (key.StartsWith("town_rock_")) return Prefix + "rock";
            if (key == "town_log") return Prefix + "log";
            if (key == "town_shroom") return Prefix + "mushrooms";
            return key;
        }
    }
}
