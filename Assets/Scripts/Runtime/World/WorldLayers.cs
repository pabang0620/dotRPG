using System.Collections.Generic;

namespace DotRPG
{
    public enum WorldLayer { Surface, Underground }

    /// <summary>Atlas grouping only: changing tabs never travels, changes saves, or loads a live map.</summary>
    public static class WorldLayers
    {
        public static WorldLayer Of(string id) => MapRegistry.Get(id)?.worldLayer ?? WorldLayer.Surface;
        public static bool IsUnderground(string id) => Of(id) == WorldLayer.Underground;
        public static int Depth(string id) => MapRegistry.Get(id)?.depth ?? 0;
        public static string Name(WorldLayer layer) => layer == WorldLayer.Underground ? "지하월드" : "지상월드";

        public static IEnumerable<MapInfo> Maps(WorldLayer layer)
        {
            foreach (var map in MapRegistry.All)
                if (map.worldLayer == layer) yield return map;
        }
    }
}
