using UnityEngine;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        string NatureKey(string key) => MapId == MapRegistry.Village ? VillageNatureArt.Resolve(key) : key;

        void DecorateVillageNature()
        {
            if (MapId != MapRegistry.Village) return;
            Physics2D.SyncTransforms();
            // Coordinate hashing leaves NPC RNG and the existing collision layout unchanged.
            // Ground details never block movement; keep them off paths, farms and building footprints.
            for (int y = 3; y < height - 3; y++) for (int x = 3; x < width - 3; x++)
            {
                if (At(x, y) != '.' || At(x + 1, y) != '.' || At(x - 1, y) != '.') continue;
                var pos = new Vector2(x + .5f, y + .25f);
                if (Physics2D.OverlapCircle(pos + Vector2.up * .22f, .7f) != null) continue;
                int hash = (x * 73856093 ^ y * 19349663) & int.MaxValue;
                if (hash % 23 == 0) Decoration(VillageNatureArt.Prefix + "grass", pos);
                else if (hash % 71 == 0) Decoration(VillageNatureArt.Prefix + "mushrooms", pos);
            }
            // Fallen wood is decorative, so the village's walkable routes remain intact.
            foreach (var pos in new[] { new Vector2(6.5f, 39.5f), new Vector2(40.5f, 46.5f) })
            {
                if (Physics2D.OverlapBox(pos + Vector2.up * .35f, new Vector2(2.8f, 1.2f), 0f) != null) continue;
                Decoration(VillageNatureArt.Prefix + "log", pos);
            }
        }
    }
}
