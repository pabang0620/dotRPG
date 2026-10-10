using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        public int SurfaceHiddenGroundRendererCount { get; private set; }
        public int SurfaceHiddenUnsupportedLandmarkCount { get; private set; }
        public int SurfaceHiddenUnsupportedNatureCount => surfaceHiddenUnsupportedNature.Count;
        public IReadOnlyList<SurfaceHiddenNatureRecord> SurfaceHiddenUnsupportedNature => surfaceHiddenUnsupportedNature;
        readonly List<SurfaceHiddenNatureRecord> surfaceHiddenUnsupportedNature = new List<SurfaceHiddenNatureRecord>();

        public sealed class SurfaceHiddenNatureRecord
        {
            public SpriteRenderer Renderer { get; }
            public Vector3 OriginalPosition { get; }
            public Quaternion OriginalRotation { get; }
            public Vector3 OriginalScale { get; }
            public Vector2Int FootCell { get; }
            public char Ground { get; }

            internal SurfaceHiddenNatureRecord(SpriteRenderer renderer, Vector2Int footCell, char ground)
            {
                Renderer = renderer;
                OriginalPosition = renderer.transform.position;
                OriginalRotation = renderer.transform.rotation;
                OriginalScale = renderer.transform.localScale;
                FootCell = footCell;
                Ground = ground;
            }
        }

        /// <summary>Visual replacement only. Original terrain, water simulation and all
        /// scene objects remain alive, so travel, collision and services stay unchanged.</summary>
        void ApplySurfaceComposition()
        {
            SurfaceHiddenGroundRendererCount = 0;
            SurfaceHiddenUnsupportedLandmarkCount = 0;
            surfaceHiddenUnsupportedNature.Clear();
            if (map == null || map.worldLayer != WorldLayer.Surface || map.IsInterior || map.instanced
                || Array.IndexOf(Environment.GetCommandLineArgs(), "-surfaceGroundOnly") >= 0
                || !SurfaceWorldArt.HasArtwork(MapId)) return;
            var resolved = new char[width, height];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
                resolved[x, y] = GroundAt(x, y);
            if (!SurfaceWorldArt.TryBuild(objectsRoot, MapId, resolved, width, height, out var scene)) return;

            // Do not call the global ApplySharpMaterial here: it would overwrite the
            // original LivingWater material and affect independently rendered props.
            foreach (var layer in scene.Layers)
                if (layer != null && FxMaterials.Sharp != null) layer.sharedMaterial = FxMaterials.Sharp;
            if (scene.Layers.Length > 2 && scene.Layers[2] != null)
                foreach (var water in GetComponentsInChildren<LivingWater>())
                    if (water.BindComposition(scene.Layers[2])) break;

            foreach (var renderer in GetComponentsInChildren<Renderer>())
            {
                if (!IsOriginalSurfaceGround(renderer)) continue;
                if (renderer.enabled) SurfaceHiddenGroundRendererCount++;
                renderer.enabled = false;
            }
            HideUnsupportedSurfaceLandmarks(resolved);
            HideUnsupportedSurfaceNature(resolved);
            ApplyBiomePropVisuals(scene);
            GroundSurfaceProps(scene, resolved);
            if (MapId == MapRegistry.Village)
            {
                // Edge foliage is decorative and extends beyond its anchor cell.
                // Remove whole crowns at the authored exit, retaining organic silhouettes.
                var opening = new Bounds(new Vector3(2.5f, 22.5f, 0), new Vector3(6, 4, 100));
                foreach (var sr in objectsRoot.GetComponentsInChildren<SpriteRenderer>())
                    if ((sr.name == "EdgeTree" || sr.name == "Forest edge understory") && sr.bounds.Intersects(opening)
                        && sr.GetComponentInParent<ResourceNode>() == null && sr.GetComponentsInChildren<Collider2D>().Length == 0)
                        sr.enabled = false;
            }
        }

        void HideUnsupportedSurfaceNature(char[,] ground)
        {
            if (!UsesForestCanopyComposition) return;
            // The approved forest background already supplies canopy on blocked %
            // cells. Only suppress uncollidable edge overlays whose roots sit on it.
            foreach (Transform root in objectsRoot)
            {
                if (root.name != "EdgeTree" && root.name != "Forest edge understory") continue;
                var sr = root.GetComponent<SpriteRenderer>();
                if (sr == null || !sr.enabled || root.GetComponentsInChildren<Collider2D>(true).Length != 0
                    || root.GetComponentInChildren<ResourceNode>(true) != null
                    || root.GetComponentInChildren<NpcController>(true) != null
                    || root.GetComponentInChildren<ServiceDoor>(true) != null
                    || root.GetComponentInChildren<MapPortal>(true) != null) continue;
                int x = Mathf.FloorToInt(root.position.x), y = Mathf.FloorToInt(root.position.y);
                if (x < 0 || y < 0 || x >= width || y >= height || ground[x, y] != '%') continue;
                surfaceHiddenUnsupportedNature.Add(new SurfaceHiddenNatureRecord(sr, new Vector2Int(x, y), ground[x, y]));
                sr.enabled = false;
            }
        }

        void HideUnsupportedSurfaceLandmarks(char[,] ground)
        {
            if (!Canyon || HuntingGrounds.Get(MapId) == null) return;
            // These old five-tile decorative ruins were positioned on solid cells,
            // not a raised platform. The painted canyon now exposes those cells as
            // vertical cliff/void, so their bases have no supporting top surface.
            // Keep the objects and fallback art; suppress only this exact visual.
            foreach (var sr in objectsRoot.GetComponentsInChildren<SpriteRenderer>())
            {
                if (sr.transform.parent != objectsRoot || sr.name != "Fantasy landmark " + MapId
                    || sr.GetComponentsInChildren<Collider2D>(true).Length != 0
                    || sr.GetComponent<ResourceNode>() != null || sr.GetComponent<NpcController>() != null
                    || sr.GetComponent<ServiceDoor>() != null) continue;
                int x = Mathf.FloorToInt(sr.transform.position.x), y = Mathf.FloorToInt(sr.transform.position.y);
                if (x < 0 || y < 0 || x >= width || y >= height || !HuntingScenery.Solid(ground[x, y])) continue;
                if (sr.enabled) SurfaceHiddenUnsupportedLandmarkCount++;
                sr.enabled = false;
            }
        }

        bool IsOriginalSurfaceGround(Renderer renderer)
        {
            if (renderer.GetComponentInParent<SurfaceWorldScene>() != null) return false;
            var parent = renderer.transform.parent;
            if (renderer is TilemapRenderer)
                return parent != null && parent.name == "Grid" && parent.parent == transform
                    && (renderer.name == "Ground" || renderer.name == "GroundDetail" || renderer.name == "GrassEdge"
                        || renderer.name == "Water" || renderer.name == "Cliffs");
            if (!(renderer is SpriteRenderer sr) || sr.sprite == null) return false;
            if (parent != null && parent.name == "PaintedGround" && parent.parent == transform && sr.name == "Ground") return true;
            return parent == objectsRoot && (sr.name == "Sanctum terrain" || sr.name == "Layered sanctuary ground"
                || sr.name == "Cave floor and stratified rock");
        }
    }
}
