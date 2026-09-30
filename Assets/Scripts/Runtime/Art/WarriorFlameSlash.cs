using UnityEngine;

namespace DotRPG
{
    /// <summary>Cached pixel flames following the blade, with a hollow centre that leaves the body readable.</summary>
    public static class WarriorFlameSlash
    {
        public const int Size = 128, Frames = 10;
        static readonly Sprite[,] Sprites = new Sprite[3, Frames];
        static readonly Color32 Red = PixelCanvas.Hex("#b92b27"), Orange = PixelCanvas.Hex("#f05b20"),
            Gold = PixelCanvas.Hex("#ffab31"), Yellow = PixelCanvas.Hex("#ffe77b"), White = PixelCanvas.Hex("#fff5cd");

        public static PixelCanvas Draw(int stage, int frame)
        {
            var c = new PixelCanvas(Size, Size);
            float phase = frame / (float)(Frames - 1);
            float radius = stage == 2 ? 42 : 36;
            float start = stage == 2 ? -174 : -142;
            for (int y = 2; y < Size - 2; y++) for (int x = 2; x < Size - 2; x++)
            {
                float dx = x - 64, dy = 64 - y;
                float angle = Mathf.Atan2(dy, dx) * Mathf.Rad2Deg;
                if (angle < start || angle > 10) continue;
                float along = Mathf.InverseLerp(start, 10, angle);
                float taper = Mathf.Sin(along * Mathf.PI);
                float tongues = Mathf.Pow(Mathf.Max(0, Mathf.Sin(angle * .18f + phase * 8)), 4) * 10
                    + Mathf.Pow(Mathf.Max(0, Mathf.Sin(angle * .31f - phase * 13)), 6) * 5;
                float outer = radius + (tongues + 4) * taper;
                float inner = radius - (stage == 2 ? 16 : 11) * taper;
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                if (r < inner || r > outer) continue;
                // Tear the tail into embers as the swing dissipates, without blurred pixels.
                if (phase > .65f && along < (phase - .65f) * 1.8f && (x * 7 + y * 11 + frame) % 9 < 5) continue;
                float band = Mathf.InverseLerp(inner, outer, r);
                var color = band > .84f ? Red : band > .62f ? Orange : band > .36f ? Gold : band > .15f ? Yellow : White;
                c.Set(x, y, color);
            }
            for (int i = 0; i < (stage == 2 ? 18 : 10); i++)
            {
                float angle = (start + 12 + (i * 37 % 130) - phase * 20) * Mathf.Deg2Rad;
                float r = Mathf.Min(59, radius + 9 + (i % 3) * 2 + phase * 5);
                int x = Mathf.RoundToInt(64 + Mathf.Cos(angle) * r), y = Mathf.RoundToInt(64 - Mathf.Sin(angle) * r);
                c.Rect(x, y, i % 3 == 0 ? 2 : 1, 2, i % 2 == 0 ? Gold : Orange);
            }
            return c;
        }
        public static Sprite Get(int stage, float t)
        {
            int frame = Mathf.Clamp(Mathf.FloorToInt(Mathf.InverseLerp(.26f, .82f, t) * Frames), 0, Frames - 1);
            if (Sprites[stage, frame] != null) return Sprites[stage, frame];
            var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            { name = "warrior_flame_" + stage + "_" + frame, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(Draw(stage, frame).ToTexturePixels()); texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, Size, Size), new Vector2(.5f, .5f), SilverWarriorArt.Ppu);
            sprite.name = texture.name; Sprites[stage, frame] = sprite; return sprite;
        }
        public static void Burst(Vector2 origin, Vector2 direction, int stage)
        {
            for (int i = 0; i < (stage == 2 ? 16 : 8); i++)
            {
                var velocity = direction * Random.Range(.8f, 2f) + Random.insideUnitCircle * 1.2f;
                var particle = Fx.Spawn("fx_sparkle", origin, velocity, Random.Range(1, 3f), Random.Range(.18f, .4f), 10, 8);
                if (particle == null) continue;
                var sr = particle.GetComponent<SpriteRenderer>(); sr.color = i % 3 == 0 ? (Color)Yellow : (Color)Orange;
                SilverWarriorPresentation.ApplyMaterial(sr);
                particle.transform.localScale = Vector3.one * Random.Range(.18f, .35f);
            }
        }
    }
}
