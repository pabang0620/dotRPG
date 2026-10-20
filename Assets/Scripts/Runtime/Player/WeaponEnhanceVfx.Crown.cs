using UnityEngine;

namespace DotRPG
{
    public sealed partial class WeaponEnhanceVfx
    {
        public const int CrownFrameCount = 8;
        sealed class CrownFrames { public Sprite[] shell, flow; }
        SpriteRenderer crown, stream;
        Masks currentMasks;
        public bool CrownVisible => crown != null && crown.enabled;
        public int AuraFrame { get; private set; } = -1;
        public static int CachedWeaponCount => cache.Count;

        void SetupCrown()
        {
            crown = Layer("WeaponAscendantAura");
            stream = Layer("WeaponAscendantFlow");
        }

        void UpdateCrown(bool high, float time, float opacity)
        {
            if (!high) { AuraFrame = -1; return; }
            // The cached atlas is only made for a +13 weapon, never for an ordinary sword.
            if (currentMasks.crown == null)
            {
                if (!Readable(lastSprite)) { AuraFrame = -1; return; } // only the flash copy seen so far: wait for the weapon
                currentMasks.crown = MakeCrown(lastSprite);
            }
            AuraFrame = Mathf.FloorToInt(time * 10f) % CrownFrameCount;
            crown.sprite = currentMasks.crown.shell[AuraFrame];
            stream.sprite = currentMasks.crown.flow[AuraFrame];
            Follow(crown); Follow(stream);
            // Stable light around the blade; moving silver crests carry the animation.
            // No scale breathing: the attachment point and the weapon silhouette stay fixed.
            crown.color = new Color(1f, 1f, 1f, (.87f + .08f * Mathf.Sin(time * 2.4f)) * opacity);
            stream.color = new Color(1f, 1f, 1f, opacity);
        }

        void OnDisable()
        {
            if (edge != null) edge.enabled = false;
            if (aura != null) aura.enabled = false;
            if (crown != null) crown.enabled = false;
            if (stream != null) stream.enabled = false;
            for (int i = 0; i < motes.Length; i++) if (motes[i] != null) motes[i].enabled = false;
            AuraFrame = -1;
        }

        static CrownFrames MakeCrown(Sprite sprite)
        {
            // Scale the authored pixel bands by density so a low-resolution mage staff and
            // the 40px/units katana have a comparable world-space crown, rather than a blur.
            int pad = Mathf.Clamp(Mathf.RoundToInt(sprite.pixelsPerUnit * .35f), 7, 18);
            int w = (int)sprite.rect.width, h = (int)sprite.rect.height;
            int tw = w + 2 * pad, th = h + 2 * pad;
            int atlasWidth = tw * CrownFrameCount, atlasHeight = th * 2;
            var pixels = new Color32[atlasWidth * atlasHeight];
            var sourcePixels = sprite.texture.GetPixels32();
            var mask = new bool[tw * th];
            var distance = new int[tw * th];
            int ox = (int)sprite.rect.x, oy = (int)sprite.rect.y;
            int minY = th, maxY = 0;
            for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
            {
                int sx = x - pad, sy = y - pad, at = y * tw + x;
                bool blade = sx >= 0 && sy > sprite.pivot.y + 3 && sx < w && sy < h
                    && sourcePixels[(oy + sy) * sprite.texture.width + ox + sx].a > 100;
                mask[at] = blade;
                distance[at] = blade ? 0 : tw + th;
                if (blade) { minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); }
            }
            if (maxY < minY) { minY = pad; maxY = th - pad - 1; }

            // Exact Manhattan distance in two passes, instead of searching a large radius
            // for every frame pixel. All allocation and texture upload happen once per art.
            for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
            {
                int at = y * tw + x;
                if (x > 0) distance[at] = Mathf.Min(distance[at], distance[at - 1] + 1);
                if (y > 0) distance[at] = Mathf.Min(distance[at], distance[at - tw] + 1);
            }
            for (int y = th - 1; y >= 0; y--) for (int x = tw - 1; x >= 0; x--)
            {
                int at = y * tw + x;
                if (x + 1 < tw) distance[at] = Mathf.Min(distance[at], distance[at + 1] + 1);
                if (y + 1 < th) distance[at] = Mathf.Min(distance[at], distance[at + tw] + 1);
            }

            float density = Mathf.Max(.5f, sprite.pixelsPerUnit / 40f);
            int length = Mathf.Max(1, maxY - minY);
            for (int frame = 0; frame < CrownFrameCount; frame++)
            {
                float phase = frame / (float)CrownFrameCount;
                for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
                {
                    int at = y * tw + x, d = distance[at];
                    if (d >= pad || mask[at]) continue; // Keep the actual blade readable.
                    float along = (y - minY) / (float)length;
                    float wave = Mathf.Sin((along * 2.1f - phase + x * .014f / density) * Mathf.PI * 2f);
                    float counter = Mathf.Sin((along * 3.3f + phase + x * .025f / density) * Mathf.PI * 2f);
                    float ridge = (4f + 2.3f * wave) * density;
                    float extent = (8f + 3f * counter) * density;
                    bool crest = Mathf.Abs(d - ridge) <= Mathf.Max(.7f, density);
                    bool outer = d < extent && d > 3 * density;
                    Color32 shell = default;
                    if (d <= Mathf.Max(1f, density)) shell = new Color32(135, 231, 255, 100);
                    else if (crest) shell = new Color32(34, 194, 255, 155);
                    else if (outer) shell = new Color32(17, 80, 211, (byte)(counter > .45f ? 92 : 58));
                    else if (d <= 3 * density) shell = new Color32(28, 129, 243, 88);

                    // Broken sharp tips interrupt the contour, with a long stroke moving
                    // hilt → point. They read as weapon energy, rather than fire or a slash.
                    float sweep = Mathf.Repeat(along - phase, 1f);
                    bool stroke = sweep > .035f && sweep < .18f;
                    bool fork = counter > .74f && d >= extent - 2f * density && d <= extent + density;
                    Color32 flow = default;
                    if (stroke && crest) flow = new Color32(205, 248, 255, 222);
                    else if (stroke && Mathf.Abs(d - ridge) <= 2f * density) flow = new Color32(57, 216, 255, 155);
                    else if (fork) flow = new Color32(58, 197, 255, 148);
                    // Leave a bright fragment at the point for less than half of the loop.
                    if (along > .95f && along < 1.16f && d < 3 * density && frame < 3)
                        flow = new Color32(176, 240, 255, 165);
                    pixels[y * atlasWidth + frame * tw + x] = shell;
                    pixels[(y + th) * atlasWidth + frame * tw + x] = flow;
                }
            }

            var texture = new Texture2D(atlasWidth, atlasHeight, TextureFormat.RGBA32, false)
            {
                name = "weapon_ascendant_aura_8frames", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels); texture.Apply(false, true);
            var pivot = new Vector2((sprite.pivot.x + pad) / tw, (sprite.pivot.y + pad) / th);
            var result = new CrownFrames { shell = new Sprite[CrownFrameCount], flow = new Sprite[CrownFrameCount] };
            for (int frame = 0; frame < CrownFrameCount; frame++)
            {
                result.shell[frame] = Sprite.Create(texture, new Rect(frame * tw, 0, tw, th), pivot, sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
                result.flow[frame] = Sprite.Create(texture, new Rect(frame * tw, th, tw, th), pivot, sprite.pixelsPerUnit, 0, SpriteMeshType.FullRect);
            }
            return result;
        }
    }
}
