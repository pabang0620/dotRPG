using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Map-owned GPU water animation and a small, bounded school of ambient koi.</summary>
    public sealed class LivingWater : MonoBehaviour
    {
        sealed class Fish
        {
            public Transform Transform;
            public Vector2 Position, Target, Heading;
            public float Speed, Radius, Retarget, Phase;
        }
        public WaterField Field { get; private set; }
        public int FishCount => fish.Count;
        public float AnimationTime { get; private set; }
        public bool FreezeAnimation { get; set; }
        public IEnumerable<Vector2> FishPositions { get { foreach (var f in fish) yield return f.Position; } }
        public bool FishWithinWater { get { foreach (var f in fish) if (!Field.CanSwim(f.Position, f.Radius)) return false; return true; } }
        readonly List<Fish> fish = new List<Fish>();
        readonly List<Texture2D> masks = new List<Texture2D>();
        readonly List<Bounds> canopyBounds = new List<Bounds>();
        Material surface, koiMaterial;
        System.Random rng;
        static readonly int ClockId = Shader.PropertyToID("_WaterTime");

        public static LivingWater Create(Transform root, WaterField field, string mapId)
        {
            if (field.WetPixels == 0) return null;
            var live = root.gameObject.AddComponent<LivingWater>();
            live.Field = field;
            live.rng = new System.Random(mapId == MapRegistry.Village ? 73 : mapId == MapRegistry.Winter ? 193 : 137);
            live.surface = new Material(Resources.Load<Shader>("Shaders/LivingWater")) { name = "Living water " + mapId };
            var flow = mapId == MapRegistry.Village ? new Vector4(.09f, -.21f, 0, 0) : new Vector4(.06f, -.48f, 0, 0);
            live.surface.SetVector("_Flow", flow);
            foreach (var sr in root.GetComponentsInChildren<SpriteRenderer>()) live.Bind(sr);
            return live;
        }

        void Start()
        {
            // Forest-edge trees are added after the painted ground, so collect their final bounds now.
            var world = GetComponentInParent<WorldBuilder>();
            foreach (var sr in world.ObjectsRoot.GetComponentsInChildren<SpriteRenderer>())
                if (sr.GetComponentInParent<TreeFade>() != null || sr.name == "Tree" || sr.name == "EdgeTree"
                    || sr.GetComponent<BoxCollider2D>() != null && sr.bounds.size.y > 3)
                    canopyBounds.Add(sr.bounds);
            CreateFish();
        }

        void Bind(SpriteRenderer sr)
        {
            var texture = sr.sprite.texture;
            int w = texture.width, h = texture.height;
            int ox = Mathf.RoundToInt(sr.transform.position.x * 32), oy = Mathf.RoundToInt(sr.transform.position.y * 32);
            var block = new Color32[w * h];
            bool wet = false;
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            {
                var c = Field.Mask[(oy + y) * Field.Width + ox + x];
                block[y * w + x] = c;
                wet |= c.r > 0;
            }
            if (!wet) return;
            var mask = new Texture2D(w, h, TextureFormat.RGBA32, false, true)
                { name = "Water mask", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            mask.SetPixels32(block); mask.Apply(false, true); masks.Add(mask);
            var props = new MaterialPropertyBlock();
            sr.GetPropertyBlock(props);
            props.SetTexture("_WaterMask", mask);
            props.SetVector("_ChunkOrigin", new Vector4(ox, oy, 0, 0));
            sr.sharedMaterial = surface;
            sr.SetPropertyBlock(props);
        }

        void CreateFish()
        {
            var sprite = Resources.Load<Sprite>("Art/Nature/koi");
            if (sprite == null || Field.SwimPoints.Count == 0) return;
            koiMaterial = new Material(Resources.Load<Shader>("Shaders/SwimmingKoi")) { name = "Swimming koi" };
            int count = Mathf.Clamp(Field.WetPixels / (32 * 32 * 15), 3, 12);
            for (int attempt = 0; attempt < 150 && fish.Count < count; attempt++)
            {
                var p = Field.SwimPoints[rng.Next(Field.SwimPoints.Count)];
                if (!OpenWater(p)) continue;
                bool close = false;
                foreach (var f in fish) if (Vector2.Distance(p, f.Position) < 1.4f) { close = true; break; }
                if (close) continue;
                float length = fish.Count % 3 == 2 ? .6f : Range(.95f, 1.3f);
                var go = new GameObject("SwimmingKoi");
                go.transform.SetParent(transform, false);
                // Imported art uses width 1.4 units; vary size without recoloring the reference pattern.
                go.transform.localScale = Vector3.one * (length / 1.4f);
                var sr = go.AddComponent<SpriteRenderer>();
                sr.sprite = sprite; sr.sharedMaterial = koiMaterial; sr.sortingOrder = -29990;
                sr.color = new Color(.72f, .86f, .91f, .84f);
                var props = new MaterialPropertyBlock();
                float phase = Range(0, 6.28f);
                props.SetFloat("_Phase", phase); sr.SetPropertyBlock(props);
                var entry = new Fish { Transform = go.transform, Position = p, Target = p,
                    Heading = Vector2.right, Speed = Range(.3f, .52f), Radius = length * .55f,
                    Phase = phase };
                ChooseTarget(entry);
                if (entry.Target == p) { Destroy(go); continue; }
                fish.Add(entry); go.transform.position = p;
            }
        }
        float Range(float a, float b) => Mathf.Lerp(a, b, (float)rng.NextDouble());
        bool OpenWater(Vector2 p)
        {
            foreach (var b in canopyBounds)
                if (p.x >= b.min.x - .3f && p.x <= b.max.x + .3f && p.y >= b.min.y - .3f && p.y <= b.max.y + .3f) return false;
            return true;
        }
        void ChooseTarget(Fish f)
        {
            f.Retarget = Range(4, 8);
            for (int i = 0; i < 35; i++)
            {
                float angle = Range(-Mathf.PI, Mathf.PI);
                var target = f.Position + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * Range(.7f, 3.5f);
                if (!OpenWater(target) || !OpenWater((f.Position + target) * .5f)) continue;
                if (!Field.ClearSegment(f.Position, target, f.Radius + .05f)) continue;
                f.Target = target; return;
            }
            f.Target = f.Position; f.Retarget = .4f;
        }
        public void SetAnimationTime(float time)
        {
            AnimationTime = time;
            if (surface != null) surface.SetFloat(ClockId, time);
            if (koiMaterial != null) koiMaterial.SetFloat(ClockId, time);
        }
        void Update()
        {
            if (FreezeAnimation) return;
            float dt = Mathf.Min(Time.deltaTime, .05f);
            SetAnimationTime(AnimationTime + dt);
            foreach (var f in fish)
            {
                f.Retarget -= dt;
                if (f.Retarget <= 0 || Vector2.Distance(f.Position, f.Target) < .35f) ChooseTarget(f);
                Vector2 desired = (f.Target - f.Position).normalized;
                if (desired == Vector2.zero) continue;
                foreach (var other in fish)
                {
                    if (other == f) continue;
                    Vector2 away = f.Position - other.Position;
                    if (away.sqrMagnitude > .001f && away.sqrMagnitude < .8f)
                        desired += away.normalized * ((.8f - away.sqrMagnitude) * 1.4f);
                }
                desired.Normalize();
                float angle = Mathf.Atan2(f.Heading.y, f.Heading.x) * Mathf.Rad2Deg;
                float targetAngle = Mathf.Atan2(desired.y, desired.x) * Mathf.Rad2Deg;
                angle = Mathf.MoveTowardsAngle(angle, targetAngle, 85 * dt);
                f.Heading = new Vector2(Mathf.Cos(angle * Mathf.Deg2Rad), Mathf.Sin(angle * Mathf.Deg2Rad));
                float turn = Mathf.Max(.12f, Vector2.Dot(f.Heading, desired));
                var next = f.Position + f.Heading * (f.Speed * turn * dt);
                if (OpenWater(next) && Field.ClearSegment(f.Position, next + f.Heading * .16f, f.Radius)) f.Position = next;
                else if (!Field.ClearSegment(f.Position, f.Target, f.Radius)) ChooseTarget(f);
                f.Transform.position = f.Position;
                f.Transform.rotation = Quaternion.Euler(0, 0, angle);
            }
        }
        void OnDestroy()
        {
            foreach (var mask in masks) if (mask != null) Destroy(mask);
            if (surface != null) Destroy(surface);
            if (koiMaterial != null) Destroy(koiMaterial);
        }
    }
}
