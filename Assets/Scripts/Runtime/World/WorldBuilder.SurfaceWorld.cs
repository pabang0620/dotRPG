using System;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        public int SurfaceHiddenGroundRendererCount { get; private set; }
        public int SurfaceHiddenUnsupportedLandmarkCount { get; private set; }

        /// <summary>Visual replacement only. Original terrain, water simulation and all
        /// scene objects remain alive, so travel, collision and services stay unchanged.</summary>
        void ApplySurfaceComposition()
        {
            SurfaceHiddenGroundRendererCount = 0;
            SurfaceHiddenUnsupportedLandmarkCount = 0;
            if (map == null || map.worldLayer != WorldLayer.Surface || map.IsInterior || map.instanced
                || Array.IndexOf(Environment.GetCommandLineArgs(), "-surfaceGroundOnly") >= 0
                || !StreamingFiles.Exists(SurfaceWorldArt.SourcePath(MapId))) return;
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
            GroundSurfaceProps(scene, resolved);
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
