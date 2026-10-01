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
            new Definition("oak_round", 4.1f), new Definition("pine_layered", 4.5f),
            new Definition("willow", 4.5f), new Definition("cherry", 3.8f),
            new Definition("fruit", 3.35f), new Definition("golden", 4.2f),
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
            if (key.StartsWith("town_tree_") || key == "town_chop") return Prefix + "broadleaf";
            if (key.StartsWith("town_fruit_")) return Prefix + "fruit";
            if (key.StartsWith("town_pine_")) return Prefix + "pine";
            if (key.StartsWith("town_bush_")) return Prefix + "bush";
            if (key.StartsWith("town_rock_")) return Prefix + "rock";
            if (key == "town_log") return Prefix + "log";
            if (key == "town_shroom") return Prefix + "mushrooms";
            return key;
        }

        // Stable spatial selection: returning to the map keeps the same landscape and does not
        // consume gameplay RNG. Only existing tree locations change art, never their colliders.
        public static string Resolve(string key, Vector2 foot, bool forestEdge)
        {
            bool pine = key.StartsWith("town_pine_");
            if (!pine && !key.StartsWith("town_tree_")) return Resolve(key);
            int x = Mathf.FloorToInt(foot.x), y = Mathf.FloorToInt(foot.y);
            int hash = unchecked(x * 73856093 ^ y * 19349663) & int.MaxValue;
            int choice = hash % 10;
            if (forestEdge)
            {
                // Mostly green woodland, with broadleaf-dominant patches among mixed
                // pine groves. A small northern grove supplies a warm color accent.
                if (x >= 37 && x <= 46 && y >= 49 && choice < 3) return Prefix + "golden";
                int grove = ((x / 7) + (y / 6)) % 3;
                if (choice < (grove == 0 ? 5 : 3)) return Prefix + "oak_round";
                if (choice < 6) return Prefix + "broadleaf";
                return Prefix + (choice < 8 ? "pine_layered" : "pine");
            }
            if (pine) return Prefix + (x >= 30 ? "pine_layered" : "pine");
            // Pond banks, residential gardens, then the northern golden accent grove.
            if (y <= 13 && x >= 12 && x <= 32) return Prefix + "willow";
            if ((y >= 40 && y <= 47) || (x <= 8 && y >= 17 && y <= 21)) return Prefix + "cherry";
            if (y >= 36 && x >= 39) return Prefix + "golden";
            return Prefix + (choice < 6 ? "oak_round" : "broadleaf");
        }
    }
}
