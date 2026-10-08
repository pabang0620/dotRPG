using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        /// <summary>
        /// Overview of the current map for the HUD minimap and the map window: the map itself rendered
        /// from above at <see cref="MinimapTexelsPerTile"/> texels per tile (characters, monsters and
        /// effects hidden), so it shows the real ground, roofs and trees. Falls back to one flat colour
        /// per tile when nothing can be rendered (no graphics device).
        /// </summary>
        public Texture2D Minimap { get; private set; }

        public const int MinimapTexelsPerTile = 16;

        /// <summary>Centres of every portal cell (drawn as exits on the minimap).</summary>
        public readonly List<Vector2> PortalPoints = new List<Vector2>();
        /// <summary>One point per exit: the centre of each connected group of portal cells (map markers).</summary>
        public readonly List<Vector2> PortalCenters = new List<Vector2>();

        void GroupPortals()
        {
            PortalCenters.Clear();
            var groups = new List<List<Vector2>>();
            foreach (var p in PortalPoints)
            {
                List<Vector2> home = null;
                foreach (var g in groups)
                {
                    foreach (var q in g) if (Vector2.Distance(p, q) < 1.6f) { home = g; break; }
                    if (home != null) break;
                }
                if (home == null) groups.Add(home = new List<Vector2>());
                home.Add(p);
            }
            foreach (var g in groups)
            {
                Vector2 sum = Vector2.zero;
                foreach (var q in g) sum += q;
                PortalCenters.Add(sum / g.Count);
            }
        }

        void BuildMinimap()
        {
            if (Minimap != null) Destroy(Minimap);
            PortalPoints.Clear();
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    if (WorldRoutes.Portal(cells[x,y])) PortalPoints.Add(new Vector2(x + 0.5f, y + 0.5f));
            GroupPortals();
            Minimap = SystemInfo.graphicsDeviceType != UnityEngine.Rendering.GraphicsDeviceType.Null ? RenderMinimap() : null;
            if (Minimap == null) Minimap = BuildFlatMinimap();
            WorldAtlas.Record(this);
        }

        Texture2D RenderMinimap()
        {
            int tw = width * MinimapTexelsPerTile, th = height * MinimapTexelsPerTile;
            // Render at twice the size and halve it: every texel is then the average of four, so fine
            // detail turns into clean colour instead of flickering noise.
            int rw = tw * 2, rh = th * 2;
            if (rw > SystemInfo.maxTextureSize || rh > SystemInfo.maxTextureSize) { rw = tw; rh = th; }
            var camGo = new GameObject("MinimapCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = height * 0.5f;
            cam.aspect = width / (float)height;
            cam.transform.position = new Vector3(width * 0.5f, height * 0.5f, -10f);
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = config.backgroundColor;
            cam.nearClipPlane = -50f;
            cam.farClipPlane = 50f;
            var big = RenderTexture.GetTemporary(rw, rh, 16, RenderTextureFormat.ARGB32);
            big.filterMode = FilterMode.Bilinear;
            var small = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
            var hidden = HideForMinimap();
            Texture2D tex = null;
            var previousActive = RenderTexture.active;
            try
            {
                cam.targetTexture = big;
                cam.Render();
                Graphics.Blit(big, small);

                RenderTexture.active = small;
                tex = new Texture2D(tw, th, TextureFormat.RGBA32, true)
                {
                    name = "Minimap_" + MapId,
                    filterMode = FilterMode.Trilinear,
                    wrapMode = TextureWrapMode.Clamp,
                };
                tex.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
                tex.Apply(true, false);
                RenderTexture.active = previousActive;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[dotRPG] Minimap render failed: {e.Message}");
                if (tex != null) Destroy(tex);
                tex = null;
            }
            finally
            {
                foreach (var r in hidden) if (r != null) r.forceRenderingOff = false;
                cam.targetTexture = null;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(big);
                RenderTexture.ReleaseTemporary(small);
                Destroy(camGo);
            }
            return tex;
        }

        /// <summary>Characters, drops, glows and exit arrows are left out of the minimap picture.</summary>
        List<Renderer> HideForMinimap()
        {
            var list = new List<Renderer>();
            void Hide(Component root)
            {
                if (root == null) return;
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                    if (!r.forceRenderingOff) { r.forceRenderingOff = true; list.Add(r); }
            }
            foreach (var npc in objectsRoot.GetComponentsInChildren<NpcController>(true)) Hide(npc);
            foreach (var e in objectsRoot.GetComponentsInChildren<EnemyController>(true)) Hide(e);
            foreach (var p in objectsRoot.GetComponentsInChildren<Pickup>(true)) Hide(p);
            if (Game.Player != null) Hide(Game.Player);
            if (Fx.Root != null) Hide(Fx.Root);
            foreach (var sr in objectsRoot.GetComponentsInChildren<SpriteRenderer>(true))
                if (!sr.forceRenderingOff && (sr.gameObject.name.StartsWith("arrow_") || sr.sharedMaterial == FxMaterials.Additive))
                {
                    sr.forceRenderingOff = true;
                    list.Add(sr);
                }
            return list;
        }

        /// <summary>One flat colour per tile (the minimap before it was rendered from the map art).</summary>
        Texture2D BuildFlatMinimap()
        {
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "Minimap_" + MapId,
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            var px = new Color32[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                    px[y * width + x] = MinimapColor(x, y);
            // Houses and the construction site cover a 3x3 footprint from their anchor.
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    char c = cells[x, y];
                    if (c == 'H' || c == 'G' || c == 'X')
                    {
                        var roof = c == 'X' ? new Color32(134, 224, 160, 255)
                            : c == 'G' ? new Color32(63, 143, 79, 255)
                            : Winter ? new Color32(168, 112, 74, 255)
                            : Canyon ? new Color32(192, 57, 43, 255) : new Color32(59, 143, 227, 255);
                        for (int yy = y; yy < Mathf.Min(height, y + 3); yy++)
                            for (int xx = x; xx < Mathf.Min(width, x + 3); xx++)
                                px[yy * width + xx] = roof;
                    }
                    else if (Winter && c == 'N')
                    {
                        for (int yy = y; yy < Mathf.Min(height, y + 4); yy++)
                            for (int xx = x; xx < Mathf.Min(width, x + 5); xx++)
                                px[yy * width + xx] = new Color32(201, 162, 122, 255);
                    }
                    else if (c == 'I' || c == 'M')
                    {
                        int w = c == 'I' ? 5 : 3, h = c == 'I' ? 4 : 2;
                        var roof = c == 'I' ? new Color32(184, 67, 47, 255) : new Color32(221, 106, 76, 255);
                        for (int yy = y; yy < Mathf.Min(height, y + h); yy++)
                            for (int xx = x; xx < Mathf.Min(width, x + w); xx++)
                                px[yy * width + xx] = roof;
                    }
                    else if (c == '>' || c == '<')
                    {
                        px[y * width + x] = new Color32(255, 211, 74, 255);
                    }
                }
            tex.SetPixels32(px);
            tex.Apply(false, false);
            return tex;
        }

        Color32 MinimapColor(int x, int y)
        {
            if (Winter) return WinterMinimapColor(x, y);
            char c = cells[x, y];
            switch (c)
            {
                case 'T': case 'O': return new Color32(47, 125, 50, 255);
                case 'R': return new Color32(154, 163, 174, 255);
                case 'F': return new Color32(142, 78, 42, 255);
                case 'B': return new Color32(62, 155, 67, 255);
            }
            switch (GroundAt(x, y))
            {
                case '~': return Canyon ? new Color32(47, 159, 166, 255) : new Color32(47, 127, 224, 255);
                case '=': return new Color32(217, 160, 102, 255);
                case '#': return new Color32(173, 112, 64, 255);
                case 'd': return new Color32(224, 160, 106, 255);
                case ',': return new Color32(201, 167, 124, 255);
                case 'W': return new Color32(70, 54, 46, 255);
                case 'L': return new Color32(156, 125, 88, 255);
                default: return Canyon ? new Color32(79, 154, 69, 255) : new Color32(99, 199, 77, 255);
            }
        }
    }
}
