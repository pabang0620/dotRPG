using System;
using UnityEngine;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        void ApplyBiomePropVisuals(SurfaceWorldScene surface)
        {
            if (surface == null || map == null || map.worldLayer != WorldLayer.Surface || map.IsInterior || map.instanced) return;
            string biome = Canyon ? "canyon" : Winter ? "winter"
                : map.theme == MapTheme.SunkenSanctum || map.theme == MapTheme.SanctumField ? "sanctum" : "forest";
            bool forestFields = UsesForestCanopyComposition;
            var state = BiomePropArt.Attach(surface.gameObject, MapId, biome, forestFields ? "forest_fields" : null);
            // An older build without the optional field atlas retains its approved
            // regional props. Towns and every other biome always take that same path.
            if (state == null && forestFields) state = BiomePropArt.Attach(surface.gameObject, MapId, biome);
            if (state == null || state.EligibleCount != 0) return;
            forestFields = forestFields && state.AtlasId == "forest_fields";

            foreach (var node in objectsRoot.GetComponentsInChildren<ResourceNode>(true))
            {
                var renderer = node.VisualRenderer;
                if (renderer == null || node.FullVisualSprite == null) continue;
                state.EligibleCount++;
                string slot = node.Kind == ResourceKind.Tree ? TreePropSlot(biome, node.FullVisualSprite.name) : RockPropSlot(biome, node.transform.position);
                if (forestFields) slot = ForestFieldSlot(slot, node.FullVisualSprite.name, node.transform.position);
                var full = state.View(slot, node.FullVisualSprite);
                var stump = node.Kind == ResourceKind.Tree ? state.View("stump", node.DepletedVisualSprite) : null;
                if (full == null || node.Kind == ResourceKind.Tree && stump == null) { state.SkippedCount++; continue; }
                var record = new BiomePropRecord(renderer, node.transform, node.FullVisualSprite, full, slot, true);
                node.ConfigureVisualOverride(full, stump);
                state.Record(record);
                // Rebind the existing fade's renderer list without changing opacity or RGB.
                if (node.GetComponent<TreeFade>() != null) TreeFade.Attach(node.gameObject);
            }

            // Select exact existing small-prop names. Large architecture, the descent
            // tree/portal, background sprites, actors and embedded B3 columns never qualify.
            foreach (Transform root in objectsRoot)
            {
                if (root.GetComponent<ResourceNode>() != null || root.GetComponent<NpcController>() != null
                    || root.GetComponent<ServiceDoor>() != null || root.GetComponent<MapPortal>() != null) continue;
                var renderer = root.GetComponent<SpriteRenderer>();
                if (renderer == null || renderer.sprite == null || !renderer.enabled || renderer.forceRenderingOff) continue;
                string slot = StaticPropSlot(biome, root.name, renderer.sprite.name, root.position);
                if (forestFields && root.name == "Ruin") slot = "ruin";
                if (slot == null) continue;
                if (forestFields) slot = ForestFieldSlot(slot, renderer.sprite.name, root.position);
                state.EligibleCount++;
                var replacement = state.View(slot, renderer.sprite);
                if (replacement == null) { state.SkippedCount++; continue; }
                bool floorDecoration = biome == "sanctum" && root.name == "rubble";
                var record = new BiomePropRecord(renderer, root, renderer.sprite, replacement, slot, false, floorDecoration);
                renderer.sprite = replacement;
                // Flat sanctuary fragments belong above the floor but behind terrace
                // faces; their former Y sort would paste them onto the front of a wall.
                if (floorDecoration) renderer.sortingOrder = record.ExpectedSortingOrder;
                state.Record(record);
                if (root.GetComponent<TreeFade>() != null) TreeFade.Attach(root.gameObject);
            }
        }

        static string ForestFieldSlot(string slot, string originalName, Vector2 foot)
        {
            // A small shared grove bias plus a per-root seed mixes authored silhouettes
            // without checkerboard alternation, mutable RNG, tinting or moving a trunk.
            uint grove = CanopyHash(Mathf.FloorToInt(foot.x / 2.75f), Mathf.FloorToInt(foot.y / 2.75f), 0x4f524553u);
            uint detail = CanopyHash(Mathf.RoundToInt(foot.x * 32), Mathf.RoundToInt(foot.y * 32), 0x54524545u);
            bool alternate = (grove % 3u + detail % 5u) % 3u == 0;
            switch (slot)
            {
                case "broadleaf": return alternate ? "broadleaf_open" : slot;
                case "oak": return alternate ? "oak_lean" : slot;
                case "pine": return originalName.IndexOf("pine_layered", StringComparison.OrdinalIgnoreCase) >= 0 || alternate ? "pine_open" : slot;
                case "dead": return (grove ^ detail) % 2u == 0 ? "dead_bent" : slot;
                case "rock": return (grove ^ detail) % 2u == 0 ? "rock_slab" : slot;
                case "bush": return alternate ? "fern" : slot;
                case "cherry": case "fruit": case "golden": return "broadleaf_open";
                // Water-bank willows and all remaining prop identities stay intact.
                default: return slot;
            }
        }

        static string RockPropSlot(string biome, Vector2 foot)
        {
            int hash = unchecked(Mathf.FloorToInt(foot.x) * 73856093 ^ Mathf.FloorToInt(foot.y) * 19349663) & int.MaxValue;
            return biome == "forest" ? "rock" : biome == "canyon" ? new[] { "rockA", "rockB", "rockC" }[hash % 3]
                : (hash % 2 == 0 ? "rockA" : "rockB");
        }

        static string TreePropSlot(string biome, string name)
        {
            name = name.ToLowerInvariant();
            if (name.Contains("dead")) return "dead";
            if (biome == "winter") return name.Contains("fir") ? "fir" : name.Contains("pine") ? "pine" : "broadleaf";
            if (biome == "sanctum") return "dead";
            if (name.Contains("pine")) return "pine";
            if (name.Contains("oak")) return "oak";
            if (biome == "forest")
            {
                if (name.Contains("willow")) return "willow";
                if (name.Contains("cherry")) return "cherry";
                if (name.Contains("fruit")) return "fruit";
                if (name.Contains("golden")) return "golden";
            }
            return "broadleaf";
        }

        static string StaticPropSlot(string biome, string name, string spriteName, Vector2 foot)
        {
            switch (name)
            {
                case "Tree": case "EdgeTree": case "Gateway woodland": return TreePropSlot(biome, spriteName);
                case "Rock": case "Stone": return RockPropSlot(biome, foot);
                case "Stump": return "stump";
                case "Bush": case "Forest edge understory": return biome == "canyon" || biome == "winter" ? "shrub" : "bush";
                case "Log": case "LogSeat": case "village_nature_log": return "log";
                case "Ruin": return biome == "canyon" || biome == "winter" ? "ruin" : null;
                case "Broken pillar": case "Submerged colonnade": return biome == "sanctum" ? "pillar" : null;
                case "rubble": return biome == "sanctum" ? "rubble" : null;
                case "long_remnant": return biome == "sanctum" ? "longremnant" : null;
                case "Moss lit shore": return biome == "sanctum" ? "moss" : null;
                case "Bank accent": return biome == "winter" || biome == "canyon" ? "pebbles" : null;
            }
            if (biome != "forest" && name.StartsWith("town_pebbles_", StringComparison.Ordinal)) return "pebbles";
            if (biome == "winter" && name.StartsWith("wnt_grass_", StringComparison.Ordinal)) return "grass";
            return null;
        }
    }
}
