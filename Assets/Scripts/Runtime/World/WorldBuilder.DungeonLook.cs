using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [DGNTERRAIN] Dungeon rooms of the canyon (mine) and winter (ice cave) themes use the 32px town painters
    /// (<see cref="CanyonTerrain"/>, <see cref="WinterTerrain"/>) like their towns, but read as underground:
    /// the painted ground and the props are tinted darker/cooler and a soft vignette darkens the room edges.
    /// Characters, monsters, doors and effects are spawned later and stay untinted; the UI is a separate canvas.
    /// Mine rooms also get lamp posts ('p', the canyon set has none of its own).
    /// </summary>
    public partial class WorldBuilder
    {
        // ---- Tuning: multiply colours for dungeon rooms (1 = unchanged) ----
        static readonly Color MineGroundTint = new Color(0.60f, 0.54f, 0.50f, 1f);
        static readonly Color MinePropTint = new Color(0.78f, 0.74f, 0.70f, 1f);
        static readonly Color CaveGroundTint = new Color(0.56f, 0.63f, 0.80f, 1f);
        static readonly Color CavePropTint = new Color(0.76f, 0.82f, 0.92f, 1f);
        /// <summary>Darkness of the vignette at the room corners (0..1) and where it starts (0 = centre, 1 = edge).</summary>
        const float VignetteStrength = 0.62f, VignetteStart = 0.45f;
        /// <summary>Above every world sprite (y-sorted orders stay within a few thousand), below the UI canvas.</summary>
        const int VignetteOrder = 31000;
        const int VignetteSize = 64;

        static Sprite vignetteSprite;

        /// <summary>Canyon/winter themed dungeon room (drawn darker than the sunny towns).</summary>
        bool DarkDungeonRoom => map != null && map.instanced && (CanyonHd || WinterHd);

        /// <summary>Mine-only props in canyon dungeon rooms. True when the symbol was handled.</summary>
        bool SpawnDungeonLookProp(char c, int x, int y)
        {
            if (!(map != null && map.instanced && CanyonHd)) return false;
            if (c != 'p') return false;
            var lamp = StaticProp("Lamp", "town_lamp", new Vector2(x + 0.5f, y + 0.2f), new Vector2(0.35f, 0.3f), new Vector2(0f, 0.15f));
            WarmGlow.Attach(lamp.transform, new Vector2(0f, 1.95f), 1.6f, 0.32f);
            PointsOfInterest.Add(new Vector2(x + 0.5f, y + 0.5f));
            return true;
        }

        /// <summary>
        /// Picture-only ground of a cell: a spawn digit set into a wall (boss entry marks on the top rows) is
        /// painted as rock instead of a floor notch. Colliders still use <see cref="GroundAt"/>, so nothing
        /// becomes solid that was not before.
        /// </summary>
        char DungeonPaintGround(int x, int y, char ground)
        {
            if (map == null || !map.instanced) return ground;
            char c = At(x, y);
            if (c < '1' || c > '9') return ground;
            int walls = 0;
            if (At(x - 1, y) == 'W') walls++;
            if (At(x + 1, y) == 'W') walls++;
            if (At(x, y + 1) == 'W' || At(x, y + 1) == '\0') walls++;
            if (At(x, y - 1) == 'W') walls++;
            return walls >= 3 ? 'W' : ground;
        }

        /// <summary>Tints the painted ground and every map prop of a dark dungeon room (glows stay bright).</summary>
        void ApplyDungeonTint()
        {
            if (!DarkDungeonRoom) return;
            Color ground = CanyonHd ? MineGroundTint : CaveGroundTint;
            Color prop = CanyonHd ? MinePropTint : CavePropTint;
            var painted = transform.Find("PaintedGround");
            if (painted != null)
                foreach (var sr in painted.GetComponentsInChildren<SpriteRenderer>(true)) sr.color = ground;
            foreach (var sr in objectsRoot.GetComponentsInChildren<SpriteRenderer>(true))
                if (sr.sharedMaterial != FxMaterials.Additive) sr.color = sr.color * prop;
        }

        /// <summary>A world-space vignette stretched over the room (added after the minimap is rendered).</summary>
        void AddDungeonVignette()
        {
            if (!DarkDungeonRoom) return;
            var go = new GameObject("DungeonVignette");
            go.transform.SetParent(transform, false);
            go.transform.position = new Vector3(width * 0.5f, height * 0.5f, 0f);
            go.transform.localScale = new Vector3(width / (float)VignetteSize * 1.04f, height / (float)VignetteSize * 1.04f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = VignetteSprite();
            sr.color = WinterHd ? new Color(0.02f, 0.03f, 0.08f, 1f) : new Color(0.04f, 0.02f, 0.01f, 1f);
            sr.sortingOrder = VignetteOrder;
        }

        static Sprite VignetteSprite()
        {
            if (vignetteSprite != null) return vignetteSprite;
            var px = new Color32[VignetteSize * VignetteSize];
            for (int y = 0; y < VignetteSize; y++)
                for (int x = 0; x < VignetteSize; x++)
                {
                    // Rounded-rectangle distance so a wide room darkens along all four walls evenly.
                    float u = Mathf.Abs((x + 0.5f) / VignetteSize * 2f - 1f), v = Mathf.Abs((y + 0.5f) / VignetteSize * 2f - 1f);
                    float d = Mathf.Pow(Mathf.Pow(u, 4f) + Mathf.Pow(v, 4f), 0.25f);
                    float t = Mathf.Clamp01((d - VignetteStart) / (1f - VignetteStart));
                    byte a = (byte)Mathf.RoundToInt(255f * VignetteStrength * t * t * (3f - 2f * t));
                    px[y * VignetteSize + x] = new Color32(255, 255, 255, a);
                }
            var tex = new Texture2D(VignetteSize, VignetteSize, TextureFormat.RGBA32, false)
            {
                name = "DungeonVignette", filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            tex.SetPixels32(px);
            tex.Apply();
            vignetteSprite = Sprite.Create(tex, new Rect(0, 0, VignetteSize, VignetteSize), new Vector2(0.5f, 0.5f), 1f);
            vignetteSprite.hideFlags = HideFlags.DontSave;
            return vignetteSprite;
        }
    }
}
