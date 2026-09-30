using UnityEngine;

namespace DotRPG
{
    /// <summary>Cached pixel flames following the blade, with a hollow centre that leaves the body readable.</summary>
    public static class WarriorFlameSlash
    {
        public const int Size = 128, Frames = 14;
        static readonly Sprite[,] Sprites = new Sprite[3, Frames];
        static readonly Sprite[] Embers = new Sprite[3];
        static readonly Color32 Red = PixelCanvas.Hex("#c73529"), Orange = PixelCanvas.Hex("#f05b20"),
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
                float r = Mathf.Sqrt(dx * dx + dy * dy);
                // Radial shear bends the tips along the sweep instead of forming straight teeth.
                float flow = angle + (r - radius) * 1.8f + phase * 34 + stage * 9;
                float tongues = Mathf.Pow(Mathf.Max(0, Mathf.Sin(flow * .16f)), 3) * 9
                    + Mathf.Pow(Mathf.Max(0, Mathf.Sin(flow * .29f + 1.7f)), 5) * 5;
                float outer = radius + (tongues + 4) * taper;
                float inner = radius - (stage == 2 ? 16 : 11) * taper
                    + Mathf.Sin(angle * .12f + phase * 12) * taper * 1.4f;
                // The tail burns away in coherent curls; never use screen-space stripe dithering.
                float burn = Mathf.SmoothStep(0, 1, Mathf.InverseLerp(.56f, 1, phase));
                float tail = 1 - Mathf.SmoothStep(0, .7f, along);
                inner += (outer - inner) * burn * tail * .86f;
                if (r < inner || r > outer) continue;
                float band = Mathf.InverseLerp(inner, outer, r);
                float curl = Mathf.Sin(flow * .23f + band * 7) * Mathf.Sin(flow * .09f - band * 4);
                if (band > .58f && curl > .72f && along < .84f) continue;
                // Broken hot filaments and gold eddies give depth with discrete palette bands.
                float heat = band + Mathf.Sin(flow * .14f + band * 8) * .10f;
                var color = heat > .92f ? Red : heat > .72f ? Orange : heat > .39f ? Gold : Yellow;
                if (band < .075f || band < .32f && curl < -.48f) color = White;
                c.Set(x, y, color);
            }
            for (int i = 0; i < (stage == 2 ? 18 : 10); i++)
            {
                float angle = (start + 12 + (i * 37 % 130) - phase * 26) * Mathf.Deg2Rad;
                float r = Mathf.Min(58, radius + 8 + (i % 3) * 2 + phase * 6);
                int x = Mathf.RoundToInt(64 + Mathf.Cos(angle) * r), y = Mathf.RoundToInt(64 - Mathf.Sin(angle) * r);
                float endAngle = angle + (i % 3 == 0 ? .08f : .04f);
                int tx = Mathf.RoundToInt(64 + Mathf.Cos(endAngle) * (r - 1)), ty = Mathf.RoundToInt(64 - Mathf.Sin(endAngle) * (r - 1));
                c.Line(tx, ty, x, y, Orange);
                c.Set(x, y, i % 3 == 0 ? Yellow : Gold);
            }
            return c;
        }
        static Sprite Ember(int variant)
        {
            if (Embers[variant] != null) return Embers[variant];
            var c = new PixelCanvas(7, 11);
            for (int y = 1; y < 10; y++)
            {
                int center = 3 + (y < 5 ? (variant - 1) * (5 - y) / 3 : 0);
                int width = y < 4 || y > 8 ? 0 : y < 6 ? 1 : 2;
                c.HLine(center - width, center + width, y, Orange);
                if (width > 0) c.HLine(center - width + 1, center + width - 1, y, Gold);
                if (y >= 6 && y <= 8) c.Set(center, y, Yellow);
            }
            var texture = new Texture2D(7, 11, TextureFormat.RGBA32, false)
            { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(c.ToTexturePixels()); texture.Apply();
            return Embers[variant] = Sprite.Create(texture, new Rect(0, 0, 7, 11), new Vector2(.5f, .5f), SilverWarriorArt.Ppu);
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
                var sr = particle.GetComponent<SpriteRenderer>(); sr.sprite = Ember(i % 3); sr.color = Color.white;
                SilverWarriorPresentation.ApplyMaterial(sr);
                particle.transform.localScale = Vector3.one * Random.Range(.65f, 1f);
                particle.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(velocity.y, velocity.x) * Mathf.Rad2Deg - 90);
            }
        }
    }
}
