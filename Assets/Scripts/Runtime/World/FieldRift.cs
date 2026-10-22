using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] Field void rift of 공허의 틈 (Docs/ch3/WORLD_AND_STORY.md 7): every 90 s one opens on a spawn point. While open,
    /// monsters within 6 m take 30% less damage. A party member standing in it for 3 s closes it. It teaches the raid's
    /// rift rule before 시바크. Auto hunting and the measure bot find it like a seal circle (<see cref="SpotFor"/>).
    /// Runs on the PC that simulates the field (solo or the field host), like the monsters it hardens.
    /// </summary>
    public sealed class FieldRift : MonoBehaviour
    {
        public const float Every = 90f, FirstAfter = 20f, CloseSeconds = 3f, Radius = 1.2f, Reach = 6f, DamageCut = .3f;
        const int MaxOpen = 2;
        /// <summary>Quest signal of a closed rift (chapter3.json c3_stones: interact c3_rift_closed x3).</summary>
        public const string ClosedSignal = "c3_rift_closed";
        const float SearchRange = 18f, SealSeconds = 6f;

        sealed class Rift { public Vector2 at; public float held; public SpriteRenderer sr, ring; }
        static readonly List<Rift> open = new List<Rift>();
        static Sprite riftSprite, ringSprite;

        readonly List<Vector2> spots = new List<Vector2>();
        float next;

        public static FieldRift Create(Transform parent, IEnumerable<Vector2> spawnPoints)
        {
            var go = new GameObject("FieldRifts");
            go.transform.SetParent(parent, false);
            var rifts = go.AddComponent<FieldRift>();
            rifts.spots.AddRange(spawnPoints);
            rifts.next = Time.time + FirstAfter;
            return rifts;
        }

        /// <summary>An open rift hardens monsters around this point.</summary>
        public static bool Hardens(Vector2 p)
        {
            for (int i = 0; i < open.Count; i++) if ((open[i].at - p).sqrMagnitude <= Reach * Reach) return true;
            return false;
        }

        /// <summary>Nearest open rift to close (auto hunting, measure bot).</summary>
        public static bool SpotFor(PlayerController p, out Vector2 spot)
        {
            spot = default;
            if (p == null || open.Count == 0) return false;
            float best = SearchRange * SearchRange;
            bool found = false;
            foreach (var r in open)
            {
                float d = (r.at - p.Position).sqrMagnitude;
                if (d < best) { best = d; spot = r.at; found = true; }
            }
            return found;
        }

        void OnDestroy()
        {
            foreach (var r in open) if (r.sr != null) Destroy(r.sr.gameObject);
            open.Clear();
        }

        void Update()
        {
            if (!Game.IsWorldRunning || spots.Count == 0) return;
            if (Time.time >= next)
            {
                next = Time.time + Every;
                if (open.Count < MaxOpen) Open(spots[Random.Range(0, spots.Count)]);
            }
            for (int i = open.Count - 1; i >= 0; i--)
            {
                var r = open[i];
                bool someone = false;
                foreach (var m in Members())
                    if (m != null && !m.IsDead && Vector2.Distance(m.Position, r.at) <= Radius) { someone = true; break; }
                r.held = someone ? r.held + Time.deltaTime : Mathf.Max(0f, r.held - Time.deltaTime * .5f);
                if (r.ring != null) r.ring.transform.localScale = Vector3.one * Mathf.Lerp(.35f, 1f, r.held / CloseSeconds);
                if (r.sr != null) r.sr.transform.localScale = Vector3.one * (1f + .06f * Mathf.Sin(Time.time * 4f));
                if (r.held < CloseSeconds) continue;
                open.RemoveAt(i);
                Fx.Burst("fx_sparkle", r.at, 10, 2.5f, 0.5f);
                Game.Audio?.PlaySfx("select");
                GameEvents.RaiseToast(L.T("공허 균열을 닫았습니다."));
                GameEvents.RaiseInteracted(ClosedSignal); // 3-8 잔해의 길 counts closed rifts
                if (r.sr != null) Seal(r);
            }
        }

        /// <summary>The closed scar (field_rift_closed) stays on the ground for a few seconds; without it the rift just goes.</summary>
        static void Seal(Rift r)
        {
            var closed = Game.Art.Optional("field_rift_closed");
            if (closed == null) { Destroy(r.sr.gameObject); return; }
            if (r.ring != null) Destroy(r.ring.gameObject);
            r.sr.sprite = closed;
            r.sr.transform.localScale = Vector3.one;
            Destroy(r.sr.gameObject, SealSeconds);
        }

        static IEnumerable<PlayerController> Members()
        {
            if (Game.Party != null && Game.Party.Members.Count > 0) return Game.Party.Members;
            return Game.Player != null ? new[] { Game.Player } : new PlayerController[0];
        }

        void Open(Vector2 at)
        {
            var go = new GameObject("FieldRift");
            go.transform.SetParent(transform, false);
            go.transform.position = at;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = Game.Art.Optional("field_rift_open") ?? RiftSprite();
            sr.sortingOrder = -19000; // on the ground, under characters
            var ringGo = new GameObject("CloseRing");
            ringGo.transform.SetParent(go.transform, false);
            var ring = ringGo.AddComponent<SpriteRenderer>();
            ring.sprite = RingSprite();
            ring.color = new Color(.85f, .75f, 1f, .8f);
            ring.sortingOrder = -18990;
            ringGo.transform.localScale = Vector3.one * .35f;
            open.Add(new Rift { at = at, sr = sr, ring = ring });
            GameEvents.RaiseToast(L.T("<color=#c9a2ff>공허 균열</color>이 열렸습니다: 주변 몬스터가 단단해집니다. 3초 서 있으면 닫힙니다."));
        }

        // Drawn in code while ART_LIST's field_rift_open sprite is missing: a violet crack with a pale core.
        static Sprite RiftSprite()
        {
            if (riftSprite != null) return riftSprite;
            var p = new PixelCanvas(48, 48);
            Color32 dark = new Color32(46, 20, 70, 220), mid = new Color32(122, 60, 176, 230), core = new Color32(232, 214, 255, 255);
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                {
                    float dx = (x - 23.5f) / 22f, dy = (y - 23.5f) / 14f, d = dx * dx + dy * dy;
                    if (d > 1f) continue;
                    p.Set(x, y, d > .7f ? dark : d > .25f ? mid : core);
                }
            riftSprite = Make(p, "field_rift_code");
            return riftSprite;
        }

        static Sprite RingSprite()
        {
            if (ringSprite != null) return ringSprite;
            var p = new PixelCanvas(48, 48);
            var c = new Color32(255, 255, 255, 255);
            for (int y = 0; y < 48; y++)
                for (int x = 0; x < 48; x++)
                {
                    float dx = x - 23.5f, dy = y - 23.5f, d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > 20f && d < 23f) p.Set(x, y, c);
                }
            ringSprite = Make(p, "field_rift_ring");
            return ringSprite;
        }

        static Sprite Make(PixelCanvas p, string key)
        {
            var t = new Texture2D(p.Width, p.Height, TextureFormat.RGBA32, false) { name = key, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            t.SetPixels32(p.ToTexturePixels());
            t.Apply();
            return Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), 24, 0, SpriteMeshType.FullRect);
        }
    }
}
