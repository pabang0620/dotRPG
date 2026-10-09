using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public partial class WorldBuilder
    {
        // Map-owned contacts link independent sprites to the painted floor. Static
        // props share one layer; resource patches only observe their visual lifetime.
        void GroundSurfaceProps(SurfaceWorldScene scene, char[,] ground)
        {
            if (scene == null || scene.GetComponent<SurfacePropGrounding>() != null) return;
            const int ppu = 32;
            int pw = width * ppu, ph = height * ppu;
            var alpha = new byte[pw * ph];
            int count = 0;
            var state = scene.gameObject.AddComponent<SurfacePropGrounding>();
            Color32 ink = Winter ? new Color32(40, 58, 79, 0) : Canyon
                ? new Color32(55, 43, 35, 0) : new Color32(24, 38, 29, 0);
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
            float BankStrength(float wx, float wy)
            {
                if (!Ground(wx, wy)) return 0;
                // Finish the contact before a shore or cliff. A hard stencil alone
                // introduces a square seam over the curved painted bank.
                float bank = .125f;
                for (int ring = 1; ring <= 7; ring++)
                {
                    float d = ring * .045f, diagonal = d * .7071f;
                    if (!Ground(wx - d, wy) || !Ground(wx + d, wy) || !Ground(wx, wy - d) || !Ground(wx, wy + d)
                        || !Ground(wx - diagonal, wy - diagonal) || !Ground(wx + diagonal, wy - diagonal)
                        || !Ground(wx - diagonal, wy + diagonal) || !Ground(wx + diagonal, wy + diagonal)) break;
                    bank = (ring + 1) / 8f;
                }
                return bank;
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
                    if (distance >= 1) continue;
                    float bank = BankStrength(wx, wy);
                    // Eight restrained pixel bands; no blur shader or large black discs.
                    int value = Mathf.RoundToInt(strength * Mathf.Ceil((1 - distance) * 8) / 8 * bank);
                    int at = y * pw + x;
                    alpha[at] = (byte)Mathf.Max(alpha[at], value); // Overlapping trees never compound to black.
                }
            }

            Sprite ResourcePatch(Vector2 foot, float rx, float ry, float extentX, float extentY, int strength, string label)
            {
                // Both tree states share a snapped world rectangle. Swapping the
                // picture cannot shift its contact or its ground/water stencil.
                int left = Mathf.Max(0, Mathf.FloorToInt((foot.x - extentX) * ppu));
                int right = Mathf.Min(pw, Mathf.CeilToInt((foot.x + extentX) * ppu));
                int bottom = Mathf.Max(0, Mathf.FloorToInt((foot.y - extentY) * ppu));
                int top = Mathf.Min(ph, Mathf.CeilToInt((foot.y + extentY) * ppu));
                int w = right - left, h = top - bottom;
                if (w <= 0 || h <= 0) return null;
                var colors = new Color32[w * h];
                int visible = 0;
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                {
                    float wx = (left + x + .5f) / ppu, wy = (bottom + y + .5f) / ppu;
                    float nx = (wx - foot.x - .03f) / rx, ny = (wy - foot.y + .015f) / ry;
                    float distance = nx * nx + ny * ny;
                    if (distance >= 1) continue;
                    var color = ink;
                    color.a = (byte)Mathf.RoundToInt(strength * Mathf.Ceil((1 - distance) * 8) / 8 * BankStrength(wx, wy));
                    if (color.a == 0) continue;
                    colors[y * w + x] = color; visible++;
                }
                if (visible == 0) return null;
                var texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
                { name = MapId + " " + label, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                state.Own(texture); texture.SetPixels32(colors); texture.Apply(false, true);
                var pivot = new Vector2((foot.x * ppu - left) / w, (foot.y * ppu - bottom) / h);
                var sprite = Sprite.Create(texture, new Rect(0, 0, w, h), pivot, ppu, 0, SpriteMeshType.FullRect);
                sprite.name = texture.name; state.Own(sprite); state.ResourceShadowPixelCount += visible;
                return sprite;
            }

            foreach (var node in objectsRoot.GetComponentsInChildren<ResourceNode>(true))
            {
                var visual = node.VisualRenderer;
                if (visual == null || node.FullVisualSprite == null) continue;
                Vector2 foot = node.transform.position;
                var circle = node.GetComponent<CircleCollider2D>();
                // Use the gameplay foot, not the canopy's width. Depleted rocks have
                // disabled colliders, so bounds would incorrectly return zero here.
                float diameter = circle != null ? circle.radius * 2 * Mathf.Abs(node.transform.lossyScale.x) : .75f;
                bool tree = node.Kind == ResourceKind.Tree;
                float rx = Mathf.Clamp(diameter * (tree ? .66f : .59f), .30f, .66f);
                float ry = Mathf.Clamp(rx * (tree ? .36f : .40f), .12f, .26f);
                float extentX = rx + .07f, extentY = ry + .045f;
                var full = ResourcePatch(foot, rx, ry, extentX, extentY, Winter ? 30 : 37, node.name + " standing contact");
                var stump = tree ? ResourcePatch(foot, rx * .77f, ry * .82f, extentX, extentY,
                    Winter ? 26 : 32, node.name + " stump contact") : null;
                if (full == null && stump == null) continue;
                var resourceContactGo = new GameObject("Resource contact " + node.NodeId);
                resourceContactGo.transform.SetParent(scene.transform, false); resourceContactGo.transform.position = foot;
                var resourceContactRenderer = resourceContactGo.AddComponent<SpriteRenderer>(); resourceContactRenderer.sortingOrder = -21000;
                if (FxMaterials.Sharp != null) resourceContactRenderer.sharedMaterial = FxMaterials.Sharp;
                var contact = resourceContactGo.AddComponent<SurfaceResourceContact>();
                contact.Initialize(node, resourceContactRenderer, full, stump); state.AddResourceContact(contact);
            }

            foreach (Transform prop in objectsRoot)
            {
                if (!GroundedPropKind(prop.name, out bool tree, out bool structure)) continue;
                // These floor decals now sit behind the terrace walls. Their authored
                // moss/stone footing is sufficient; the shared actor-level contact
                // layer would otherwise reappear on the front of an occluding wall.
                if (prop.name == "rubble") continue;
                // Resources use the small, state-aware patches above instead.
                if (prop.GetComponent<ResourceNode>() != null || prop.GetComponent<NpcController>() != null) continue;
                var sr = prop.GetComponent<SpriteRenderer>();
                if (sr == null || sr.sprite == null || !sr.enabled || sr.forceRenderingOff
                    || sr.color.a <= .01f || !prop.gameObject.activeInHierarchy) continue;
                Vector2 foot = prop.position;
                if ((prop.name == "Broken pillar" || prop.name == "rubble" || prop.name == "long_remnant"
                    || prop.name == "Submerged colonnade") && !Ground(foot.x, foot.y)) continue;
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
            state.PropCount = count; state.ShadowPixelCount = pixels;
            if (pixels == 0) return; // A resource-only map still keeps its dynamic contacts.
            var colors = new Color32[alpha.Length];
            for (int i = 0; i < colors.Length; i++) { ink.a = alpha[i]; colors[i] = ink; }
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
            tree = name == "Tree" || name == "EdgeTree" || name == "Gateway woodland";
            structure = name == "House" || name == "Inn" || name == "Barn" || name == "Warehouse"
                || name == "Stall" || name == "Gate" || name == "Fountain" || name == "Well";
            return tree || structure || name == "Stone" || name == "Rock" || name == "Stump"
                || name == "Bush" || name == "Log" || name == "LogSeat" || name == "Ruin"
                || name == "Bench" || name == "NoticeBoard" || name == "Lamp" || name == "Barrel"
                || name == "Crate" || name == "Box" || name == "Woodpile" || name == "Grave"
                // Explicit small ruins only. Painted walls, arches, stairs, altar and
                // underground foreground columns retain their original baked shading.
                || name == "Broken pillar" || name == "rubble" || name == "long_remnant"
                || name == "Submerged colonnade";
        }
    }

    public sealed class SurfacePropGrounding : MonoBehaviour
    {
        public int PropCount { get; internal set; }
        public int ShadowPixelCount { get; internal set; }
        public int ResourceShadowPixelCount { get; internal set; }
        public SpriteRenderer Renderer { get; internal set; }
        public IReadOnlyList<SurfaceResourceContact> ResourceContacts => resourceContacts;
        public int ResourceContactCount => resourceContacts.Count;
        readonly List<SurfaceResourceContact> resourceContacts = new List<SurfaceResourceContact>();
        readonly List<UnityEngine.Object> owned = new List<UnityEngine.Object>();
        public UnityEngine.Object[] OwnedResources => owned.ToArray();
        internal void Own(UnityEngine.Object value) => owned.Add(value);
        internal void AddResourceContact(SurfaceResourceContact value) => resourceContacts.Add(value);
        void OnDestroy() { foreach (var value in owned) if (value != null) Destroy(value); owned.Clear(); }
    }

    /// <summary>Visual-only observer; never changes harvest, respawn or collision state.
    /// Kept outside TreeFade's hierarchy so fading foliage cannot overwrite the contact alpha.</summary>
    public sealed class SurfaceResourceContact : MonoBehaviour
    {
        public ResourceNode Node { get; private set; }
        public SpriteRenderer Renderer { get; private set; }
        public Sprite StandingSprite { get; private set; }
        public Sprite DepletedSprite { get; private set; }
        Vector3 fullVisualScale;

        internal void Initialize(ResourceNode node, SpriteRenderer renderer, Sprite full, Sprite stump)
        {
            Node = node; Renderer = renderer; StandingSprite = full; DepletedSprite = stump;
            fullVisualScale = node.VisualRenderer.transform.localScale;
            SyncVisual();
        }

        public void SyncVisual()
        {
            if (Renderer == null) return;
            var visual = Node != null ? Node.VisualRenderer : null;
            bool show = Node != null && Node.gameObject.activeInHierarchy && visual != null
                && visual.enabled && !visual.forceRenderingOff && visual.sprite != null
                && visual.gameObject.activeInHierarchy && visual.color.a > .001f;
            Sprite picture = Node != null && Node.Available ? StandingSprite : DepletedSprite;
            Renderer.sprite = picture; Renderer.enabled = show && picture != null;
            if (!Renderer.enabled) return;
            // Regrowth eases the contact's opacity without moving its shoreline mask.
            // The normal TreeFade .45 alpha is intentionally not inherited.
            float scale = Mathf.Abs(fullVisualScale.y) > .001f
                ? Mathf.Clamp01(Mathf.Abs(visual.transform.localScale.y / fullVisualScale.y)) : 1;
            Renderer.color = new Color(1, 1, 1, scale);
        }

        void LateUpdate() => SyncVisual();
    }
}
