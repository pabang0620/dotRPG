using UnityEngine;

namespace DotRPG
{
    /// <summary>Imported village architecture, with measured image pivots and walkable front steps.</summary>
    public static class VillageBuildingArt
    {
        public sealed class Definition
        {
            public readonly string Key, Asset;
            public readonly float Ppu, Bottom, DoorX, WallFront, WallWidth, WallDepth, WallX;
            public Definition(string key, string asset, float ppu, float bottom, float doorX,
                float wallFront, float wallWidth, float wallDepth, float wallX = 0f)
            {
                Key = key; Asset = asset; Ppu = ppu; Bottom = bottom; DoorX = doorX;
                WallFront = wallFront; WallWidth = wallWidth; WallDepth = wallDepth; WallX = wallX;
            }
            public string ResourcePath => "Art/Town/" + Asset;
            public string FilePath => "Assets/Resources/" + ResourcePath + ".png";
            public Vector2 Pivot => new Vector2(.5f, Bottom / 1024f);
            public Vector2 Entrance => new Vector2((DoorX - 768f) / Ppu, 0f);
            public Vector2 ColliderSize => new Vector2(WallWidth, WallDepth);
            public Vector2 ColliderOffset => new Vector2(WallX, WallFront + WallDepth * .5f);
        }

        public static readonly Definition[] All =
        {
            new Definition("town_house_0", "courtyard_house", 120, 42, 892, .75f, 8, 5.6f, -8f / 120f),
            new Definition("town_house_1", "tea_house", 136, 35, 810, 1.1f, 8.4f, 4.8f),
            new Definition("town_house_2", "weaver_house", 128, 22, 768, 1.05f, 8.4f, 5.2f),
            new Definition("town_hall", "chief_hall", 140, 30, 768, 1.1f, 8.5f, 4.8f),
            new Definition("town_store", "general_store", 132, 56, 768, .95f, 8.4f, 5.1f),
            new Definition("town_smithy", "smithy", 136, 44, 768, 1.05f, 8.6f, 4.9f),
            new Definition("town_warehouse", "warehouse", 140, 50, 768, 1.1f, 8.4f, 4.9f),
        };

        public static Definition Find(string key)
        {
            foreach (var definition in All) if (definition.Key == key) return definition;
            return null;
        }

        public static Definition FromSprite(Sprite sprite)
        {
            if (sprite == null) return null;
            foreach (var definition in All) if (definition.Asset == sprite.name) return definition;
            return null;
        }
    }
}
