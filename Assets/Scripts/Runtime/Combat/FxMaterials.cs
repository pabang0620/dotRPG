using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Materials shared by skill effects: the normal sprite blend and an additive "light" blend.</summary>
    public static class FxMaterials
    {
        static Material alpha, additive, softAlpha, softAdditive;
        static bool loaded;

        /// <summary>The default sprite material (normal alpha blending).</summary>
        public static Material Alpha { get { Load(); return alpha; } }

        /// <summary>Adds light to whatever is behind it. Falls back to <see cref="Alpha"/> if the shader is missing.</summary>
        public static Material Additive { get { Load(); return additive; } }

        /// <summary>For line renderers: a line with soft edges, normal blending.</summary>
        public static Material SoftAlpha { get { Load(); return softAlpha; } }

        /// <summary>For line renderers: a line with soft edges, additive.</summary>
        public static Material SoftAdditive { get { Load(); return softAdditive; } }

        static Material sharp;
        static bool sharpLoaded;

        /// <summary>
        /// Crisp scaling for the 32px-per-tile art (see SpriteSharp.shader). Falls back to the default
        /// sprite material, in which case the art's textures are switched back to point filtering.
        /// </summary>
        public static Material Sharp
        {
            get
            {
                if (sharpLoaded) return sharp;
                sharpLoaded = true;
                var shader = Resources.Load<Shader>("Shaders/SpriteSharp");
                if (shader == null) shader = Shader.Find("DotRPG/SpriteSharp");
                if (shader != null && shader.isSupported) sharp = new Material(shader) { name = "SpriteSharp" };
                else Debug.LogWarning("[dotRPG] Sharp sprite shader missing; high-resolution art uses point filtering.");
                return sharp;
            }
        }

        static void Load()
        {
            if (loaded) return;
            loaded = true;
            var probe = new GameObject("FxMaterialProbe");
            alpha = probe.AddComponent<SpriteRenderer>().sharedMaterial;
            Object.Destroy(probe);
            if (alpha == null) alpha = new Material(Shader.Find("Sprites/Default"));

            var shader = Resources.Load<Shader>("Shaders/SpriteAdditive");
            if (shader == null) shader = Shader.Find("DotRPG/SpriteAdditive");
            additive = shader != null && shader.isSupported ? new Material(shader) { name = "FxAdditive" } : alpha;
            if (additive == alpha) Debug.LogWarning("[dotRPG] Additive FX shader missing; glows fall back to normal blending.");

            // 1x16 texture that fades out towards both edges of a line.
            var soft = new Texture2D(1, 16, TextureFormat.RGBA32, false)
            {
                name = "FxSoftLine",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            for (int i = 0; i < 16; i++)
            {
                float edge = 1f - Mathf.Abs((i + 0.5f) / 8f - 1f);
                soft.SetPixel(0, i, new Color(1f, 1f, 1f, edge * edge));
            }
            soft.Apply(false, true);
            softAlpha = new Material(alpha) { name = "FxSoftLineAlpha", mainTexture = soft };
            softAdditive = new Material(additive) { name = "FxSoftLineAdd", mainTexture = soft };
        }

        /// <summary>A world-space line renderer child for effects.</summary>
        public static LineRenderer NewLine(Transform parent, Material material, int order)
        {
            var go = new GameObject("Line");
            go.transform.SetParent(parent, false);
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.sharedMaterial = material;
            line.numCapVertices = 2;
            line.numCornerVertices = 2;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sortingOrder = order;
            line.positionCount = 0;
            return line;
        }
    }
}
