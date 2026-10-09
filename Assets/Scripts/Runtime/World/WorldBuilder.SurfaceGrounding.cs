using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        // A single map-owned contact layer links independent sprites to the painted
        // floor. It never changes a prop, renderer, collider, resource or service.
        void GroundSurfaceProps(SurfaceWorldScene scene, char[,] ground)
        {
            if (scene == null || scene.GetComponent<SurfacePropGrounding>() != null) return;
            const int ppu = 32;
            int pw = width * ppu, ph = height * ppu;
            var alpha = new byte[pw * ph];
            int count = 0;
            var animatedWater = GetComponentsInChildren<LivingWater>();
            bool Ground(float x, float y)
            {
                int tx = Mathf.FloorToInt(x), ty = Mathf.FloorToInt(y);
                if (tx < 0 || ty < 0 || tx >= width || ty >= height) return false;
                if (SanctumShore != null) return SanctumShore.IsLand(x, y);
                char c = ground[tx, ty];
                if (c == 'W' || c == '%' || c == '~' || c == 'V' || c == '\0') return false;
                // Rounded painted banks may contain water pixels inside a nominally
                // dry cell. Reuse the very same 32ppu mask as the water shader there.
                foreach (var water in animatedWater)
                {
                    var field = water.Field; if (field == null) continue;
                    int sx = Mathf.FloorToInt(x * 32), sy = Mathf.FloorToInt(y * 32);
                    if (sx >= 0 && sy >= 0 && sx < field.Width && sy < field.Height
                        && field.Mask[sy * field.Width + sx].r > 0) return false;
                }
                return true;
            }
            void Ellipse(Vector2 center, float rx, float ry, int strength)
            {
                int left = Mathf.Max(0, Mathf.FloorToInt((center.x - rx) * ppu));
                int right = Mathf.Min(pw - 1, Mathf.CeilToInt((center.x + rx) * ppu));
                int bottom = Mathf.Max(0, Mathf.FloorToInt((center.y - ry) * ppu));
                int top = Mathf.Min(ph - 1, Mathf.CeilToInt((center.y + ry) * ppu));
                for (int y = bottom; y <= top; y++) for (int x = left; x <= right; x++)
                {
                    float wx = (x + .5f) / ppu, wy = (y + .5f) / ppu;
                    float nx = (wx - center.x) / rx, ny = (wy - center.y) / ry;
                    float distance = nx * nx + ny * ny;
                    if (distance >= 1 || !Ground(wx, wy)) continue;
                    // Finish the contact before a shore or cliff. Hard stencil clipping
                    // alone would introduce a fresh square seam over a rounded painting.
                    float bank = .125f;
                    for (int ring = 1; ring <= 7; ring++)
                    {
                        float d = ring * .045f, diagonal = d * .7071f;
                        if (!Ground(wx - d, wy) || !Ground(wx + d, wy) || !Ground(wx, wy - d) || !Ground(wx, wy + d)
                            || !Ground(wx - diagonal, wy - diagonal) || !Ground(wx + diagonal, wy - diagonal)
                            || !Ground(wx - diagonal, wy + diagonal) || !Ground(wx + diagonal, wy + diagonal)) break;
                        bank = (ring + 1) / 8f;
                    }
                    // Eight restrained pixel bands; no blur shader or large black discs.
                    int value = Mathf.RoundToInt(strength * Mathf.Ceil((1 - distance) * 8) / 8 * bank);
                    int at = y * pw + x;
                    alpha[at] = (byte)Mathf.Max(alpha[at], value); // Overlapping trees never compound to black.
                }
            }

            foreach (Transform prop in objectsRoot)
            {
                if (!GroundedPropKind(prop.name, out bool tree, out bool structure)) continue;
                // Choppable resources have their own changing sprite and lifetime.
                // They are deliberately excluded from this static contact layer.
                if (prop.GetComponent<ResourceNode>() != null || prop.GetComponent<NpcController>() != null) continue;
                var sr = prop.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null || !sr.enabled || sr.forceRenderingOff
                    || sr.color.a <= .01f || !prop.gameObject.activeInHierarchy) continue;
                Vector2 foot = prop.position;
                float visualWidth = sr.bounds.size.x;
                var box = prop.GetComponent<BoxCollider2D>();
                float baseWidth = box != null ? box.bounds.size.x : visualWidth * (tree ? .27f : .48f);
                if (baseWidth <= .1f || visualWidth <= .1f) continue;
                // Most decorative sprites use a feet pivot. Building boxes include their
                // whole footprint, so their southern face gives the contact baseline.
                if (structure && box != null) foot = new Vector2(box.bounds.center.x, box.bounds.min.y + .06f);
                float rx = Mathf.Clamp(baseWidth * (structure ? .60f : .78f), .28f, 3.4f);
                float ry = Mathf.Clamp(rx * (structure ? .24f : .36f), .12f, .58f);
                Ellipse(foot + new Vector2(.07f, -.015f), rx, ry, Winter ? 43 : 50);
                if (tree)
                {
                    // Upper-left light: broad weak leaf shade runs towards lower-right.
                    // Clipping prevents a tree beside a cliff casting onto the deep painting.
                    float crown = Mathf.Clamp(visualWidth * .40f, .48f, 1.75f);
                    Ellipse(foot + new Vector2(crown * .26f, -.16f), crown, crown * .44f, Winter ? 16 : 21);
                }
                count++;
            }
            int pixels = 0;
            for (int i = 0; i < alpha.Length; i++) if (alpha[i] > 0) pixels++;
            if (pixels == 0) return;
            var colors = new Color32[alpha.Length];
            Color32 ink = Winter ? new Color32(40, 58, 79, 0) : Canyon
                ? new Color32(55, 43, 35, 0) : new Color32(24, 38, 29, 0);
            for (int i = 0; i < colors.Length; i++) { ink.a = alpha[i]; colors[i] = ink; }
            var state = scene.gameObject.AddComponent<SurfacePropGrounding>();
            state.PropCount = count; state.ShadowPixelCount = pixels;
            var texture = new Texture2D(pw, ph, TextureFormat.RGBA32, false)
            { name = MapId + " prop contact", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            state.Own(texture); texture.SetPixels32(colors); texture.Apply(false, true);
            var sprite = Sprite.Create(texture, new Rect(0, 0, pw, ph), Vector2.zero, ppu, 0, SpriteMeshType.FullRect);
            sprite.name = MapId + " prop contact"; state.Own(sprite);
            var go = new GameObject("Surface prop contact shadows"); go.transform.SetParent(scene.transform, false);
            var renderer = go.AddComponent<SpriteRenderer>(); renderer.sprite = sprite; renderer.sortingOrder = -21000;
            if (FxMaterials.Sharp != null) renderer.sharedMaterial = FxMaterials.Sharp;
            state.Renderer = renderer;
        }

        static bool GroundedPropKind(string name, out bool tree, out bool structure)
        {
            tree = name == "Tree" || name == "EdgeTree";
            structure = name == "House" || name == "Inn" || name == "Barn" || name == "Warehouse"
                || name == "Stall" || name == "Gate" || name == "Fountain" || name == "Well";
            return tree || structure || name == "Stone" || name == "Rock" || name == "Stump"
                || name == "Bush" || name == "Log" || name == "LogSeat" || name == "Ruin"
                || name == "Bench" || name == "NoticeBoard" || name == "Lamp" || name == "Barrel"
                || name == "Crate" || name == "Box" || name == "Woodpile" || name == "Grave";
        }
    }

    public sealed class SurfacePropGrounding : MonoBehaviour
    {
        public int PropCount { get; internal set; }
        public int ShadowPixelCount { get; internal set; }
        public SpriteRenderer Renderer { get; internal set; }
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        public UnityEngine.Object[] OwnedResources => owned.ToArray();
        internal void Own(UnityEngine.Object value) => owned.Add(value);
        void OnDestroy() { foreach (var value in owned) if (value != null) Destroy(value); owned.Clear(); }
    }
}
