using UnityEngine;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        string NatureKey(string key, Vector2 foot, bool forestEdge = false)
        {
            if (MapId == MapRegistry.Village) return VillageNatureArt.Resolve(key, foot, forestEdge);
            int hash = unchecked(Mathf.FloorToInt(foot.x) * 73856093 ^ Mathf.FloorToInt(foot.y) * 19349663) & int.MaxValue;
            if (Winter)
            {
                string winter = null;
                if (key.StartsWith("wnt_tree_") || key.StartsWith("snow_tree_")) winter = hash % 3 == 0 ? "pine" : "broadleaf";
                else if (key.StartsWith("wnt_pine_") || key.StartsWith("snow_pine_")) winter = hash % 3 == 0 ? "pine" : "fir";
                else if (key.StartsWith("wnt_rock_")) winter = "rock";
                else if (key.StartsWith("wnt_fence_"))
                {
                    int.TryParse(key.Substring("wnt_fence_".Length), out int mask);
                    winter = (mask & 3) != 0 || mask == 0 ? "fence_h" : "fence_v";
                }
                else if (key == "wnt_house") winter = "house";
                else if (key == "wnt_barn") winter = "barn";
                else if (key == "wnt_gate") winter = "gate";
                if (winter != null && WinterVillageArt.Get(WinterVillageArt.Prefix + winter) != null) return WinterVillageArt.Prefix + winter;
            }
            if (HuntingGrounds.Get(MapId) != null || Canyon)
            {
                if (key.StartsWith("town_pine_")) return VillageNatureArt.Prefix + (hash % 2 == 0 ? "pine" : "pine_layered");
                if (key.StartsWith("town_tree_")) return VillageNatureArt.Prefix + (Canyon ? (hash % 3 == 0 ? "golden" : "pine_layered") : new[] { "broadleaf", "oak_round", "pine_layered", "pine" }[hash % 4]);
                return VillageNatureArt.Resolve(key);
            }
            return key;
        }

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
