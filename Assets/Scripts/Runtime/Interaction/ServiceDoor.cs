using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Front door of a town service building (general store, smithy, warehouse): using it enters the
    /// small room occupied by the NPC who runs it.
    /// </summary>
    public class ServiceDoor : Interactable
    {
        NpcService service;
        string prompt = "들어가기";

        public NpcService Service => service;
        public override string Prompt => prompt;

        public static ServiceDoor Attach(GameObject building, NpcService service, string prompt)
        {
            var go = new GameObject("Door");
            go.transform.SetParent(building.transform, false);
            var door = go.AddComponent<ServiceDoor>();
            door.service = service;
            door.prompt = prompt;
            door.ConfigureShape(Vector2.zero, 0.9f, new Vector2(0f, 1.7f));
            return door;
        }

        public override void Interact(PlayerController player)
        {
            string room = MapRegistry.InteriorFor(Game.World.MapId, service);
            if (room != null) Game.Flow.TravelTo(room);
        }
    }

    /// <summary>Plays a looping sprite animation from sprite keys (the plaza fountain's water).</summary>
    public class SpriteCycler : MonoBehaviour
    {
        SpriteRenderer sr;
        string[] keys;
        float fps = 6f, time;
        int frame;

        public void Setup(string[] frameKeys, float framesPerSecond)
        {
            keys = frameKeys;
            fps = framesPerSecond;
            sr = GetComponent<SpriteRenderer>();
            time = Random.value * 3f;
        }

        void Update()
        {
            if (sr == null || keys == null || keys.Length == 0) return;
            time += Time.deltaTime * fps;
            int f = (int)time % keys.Length;
            if (f == frame) return;
            frame = f;
            sr.sprite = Game.Art.Get(keys[f]);
        }
    }

    /// <summary>Cached water-only animation for the existing plaza fountain. Masonry, pivot and collision stay fixed.</summary>
    public sealed class FountainWater : MonoBehaviour
    {
        public const int FrameCount = 16;
        const float Fps = 12f;
        static readonly System.Collections.Generic.Dictionary<Sprite, Sprite[]> cache = new System.Collections.Generic.Dictionary<Sprite, Sprite[]>();
        SpriteRenderer target;
        Sprite[] frames;
        float elapsed;
        int current;
        public int CurrentFrame => current;
        public Sprite FrameAt(int index) => frames[index % frames.Length];
        public static bool IsWater(Color32 c) => c.a > 200 && c.b > 100 && c.b > c.r * 1.22f && c.g > c.r * 1.15f;

        public void Setup()
        {
            target = GetComponent<SpriteRenderer>();
            var source = target.sprite;
            if (source == null || !source.texture.isReadable) { enabled = false; return; }
            if (!cache.TryGetValue(source, out frames))
            {
                int w = (int)source.rect.width, h = (int)source.rect.height;
                var raw = source.texture.GetPixels((int)source.rect.x, (int)source.rect.y, w, h);
                var pixels = new Color32[raw.Length];
                var mask = new bool[raw.Length];
                for (int p = 0; p < raw.Length; p++) { pixels[p] = raw[p]; mask[p] = IsWater(pixels[p]); }
                frames = new Sprite[FrameCount];
                for (int f = 0; f < frames.Length; f++)
                {
                    var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "fountain_water_" + f, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
                    tex.SetPixels32(Paint(pixels, mask, w, h, f)); tex.Apply(false, false);
                    var sprite = Sprite.Create(tex, new Rect(0, 0, w, h), new Vector2(source.pivot.x / w, source.pivot.y / h), source.pixelsPerUnit);
                    sprite.name = tex.name; frames[f] = sprite;
                }
                cache[source] = frames;
            }
            target.sprite = frames[0];
        }
        static Color32[] Paint(Color32[] source, bool[] mask, int w, int h, int frame)
        {
            var result = (Color32[])source.Clone();
            float phase = frame / (float)FrameCount, angle = phase * Mathf.PI * 2;
            var foam = new Color32(199, 248, 255, 255);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                int index = y * w + x;
                if (!mask[index]) continue;
                float u = x / (float)w, v = 1 - y / (float)h;
                // Downward bands travel through all three falling streams, rather than blinking in place.
                float jet = Stream(u, v, .292f, .264f, .152f, .49f);
                jet = Mathf.Max(jet, Stream(u, v, .699f, .731f, .152f, .49f));
                jet = Mathf.Max(jet, Stream(u, v, .503f, .501f, .286f, .622f));
                if (jet >= 0)
                {
                    float band = Mathf.Repeat(jet * 4 - phase, 1);
                    Color c = Color.Lerp(new Color32(50, 157, 197, 255), new Color32(117, 219, 238, 255), source[index].g / 255f);
                    if (band < .22f) c = Color.Lerp(c, foam, .85f);
                    else if (band > .8f) c *= .9f;
                    c.a = 1; result[index] = c;
                    continue;
                }
                // Small coherent surface displacement; reject samples outside the original water mask.
                int sx = Mathf.Clamp(x + Mathf.RoundToInt(Mathf.Sin(y * .09f - angle) * 2), 0, w - 1);
                int sy = Mathf.Clamp(y + Mathf.RoundToInt(Mathf.Sin(x * .07f + angle) * 1.5f), 0, h - 1);
                if (mask[sy * w + sx]) result[index] = source[sy * w + sx];
                float ripple = Mathf.Max(Ripple(u, v, .264f, .49f, phase), Ripple(u, v, .731f, .49f, phase + .33f));
                ripple = Mathf.Max(ripple, Ripple(u, v, .501f, .622f, phase + .66f));
                ripple = Mathf.Max(ripple, Ripple(u, v, .5f, .188f, phase + .2f) * .75f);
                if (ripple > 0) result[index] = Color.Lerp(result[index], foam, ripple * .8f);
            }
            // Droplets rise then accelerate back toward each impact point. Pixel blocks keep the art crisp.
            foreach (var impact in new[] { new Vector2(.264f, .49f), new Vector2(.731f, .49f), new Vector2(.501f, .622f) })
                for (int k = 0; k < 5; k++)
                {
                    float t = Mathf.Repeat(phase + k / 5f, 1);
                    float side = k % 2 == 0 ? 1 : -1;
                    int dx = Mathf.RoundToInt((impact.x + side * (.01f + t * .035f)) * w);
                    int dy = Mathf.RoundToInt((1 - impact.y + .045f * 4 * t * (1 - t)) * h);
                    for (int py = 0; py < 2; py++) for (int px = 0; px < 2; px++)
                    {
                        int x = dx + px, y = dy + py;
                        if (x >= 0 && x < w && y >= 0 && y < h && mask[y * w + x]) result[y * w + x] = foam;
                    }
                }
            return result;
        }
        static float Stream(float x, float y, float startX, float endX, float top, float bottom)
        {
            float t = (y - top) / (bottom - top);
            if (t < 0 || t > 1) return -1;
            return Mathf.Abs(x - Mathf.Lerp(startX, endX, Mathf.Sqrt(t))) < .018f ? t : -1;
        }
        static float Ripple(float x, float y, float cx, float cy, float phase)
        {
            float radius = new Vector2((x - cx) / .086f, (y - cy) / .03f).magnitude;
            if (radius > 1) return 0;
            float wave = Mathf.Repeat(radius * 2 - phase, 1);
            return wave < .13f ? (1 - radius) * .9f : 0;
        }
        void Update()
        {
            if (frames == null) return;
            elapsed = Mathf.Repeat(elapsed + Time.deltaTime, FrameCount / Fps);
            int next = Mathf.FloorToInt(elapsed * Fps) % FrameCount;
            if (next == current) return;
            current = next; target.sprite = frames[current];
        }
    }
}
