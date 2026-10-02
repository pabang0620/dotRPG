using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public enum TelegraphShape
    {
        Circle,
        /// <summary>Rectangle from an origin along a direction (charges, arrow lines).</summary>
        Rect,
        /// <summary>Fan in front of the origin.</summary>
        Cone,
        /// <summary>Ring: the middle is safe.</summary>
        Donut,
    }

    /// <summary>
    /// Party-member queries used by monster attacks (Party.MembersIn* with a solo fallback). Shapes are
    /// tested against the members' body centres, like the party helpers.
    /// </summary>
    public static class MonsterHits
    {
        static List<PlayerController> Solo(Func<PlayerController, bool> inside)
        {
            var list = new List<PlayerController>();
            var p = Game.Player;
            if (p != null && !p.IsDead && p.isActiveAndEnabled && inside(p)) list.Add(p);
            return list;
        }

        public static List<PlayerController> InCircle(Vector2 center, float radius) =>
            Game.Party != null ? Game.Party.MembersInCircle(center, radius) : Solo(m => Vector2.Distance(m.Center, center) < radius);

        public static List<PlayerController> InArc(Vector2 origin, Vector2 dir, float radius, float halfAngle) =>
            Game.Party != null ? Game.Party.MembersInArc(origin, dir, radius, halfAngle)
                : Solo(m => (m.Center - origin).magnitude <= radius && Vector2.Angle(dir, m.Center - origin) <= halfAngle);

        public static List<PlayerController> InRect(Vector2 origin, Vector2 dir, float length, float halfWidth)
        {
            if (Game.Party != null) return Game.Party.MembersInRect(origin, dir, length, halfWidth);
            Vector2 d = dir.normalized;
            return Solo(m =>
            {
                Vector2 to = m.Center - origin;
                float along = Vector2.Dot(to, d);
                return along >= 0f && along <= length && Mathf.Abs(to.x * d.y - to.y * d.x) <= halfWidth;
            });
        }

        public static List<PlayerController> InDonut(Vector2 center, float inner, float outer)
        {
            var list = InCircle(center, outer);
            list.RemoveAll(m => Vector2.Distance(m.Center, center) < inner);
            return list;
        }

        /// <summary>Every alive member (전멸기).</summary>
        public static List<PlayerController> All()
        {
            var list = new List<PlayerController>();
            if (Game.Party != null) list.AddRange(Game.Party.AliveMembers);
            else if (Game.Player != null && !Game.Player.IsDead) list.Add(Game.Player);
            return list;
        }

        /// <summary>Applies a monster hit to each member (attacker = the monster, for threat / logs).</summary>
        public static int Apply(IEnumerable<PlayerController> members, int damage, Vector2 source, float knockback, bool unblockable, GameObject attacker)
        {
            int landed = 0;
            if (damage <= 0) return 0;
            foreach (var m in members)
            {
                var info = new DamageInfo(damage, source, knockback, Team.Enemy, attacker) { unblockable = unblockable };
                if (m.TakeDamage(info)) landed++;
            }
            return landed;
        }
    }

    /// <summary>
    /// Ground warning of a monster attack (예고 장판): a bright outline shows the whole area at once and a
    /// red fill grows over the wind-up; when it is full the members inside take the hit (unblockable by
    /// default: the block roll is skipped). The area is registered in <see cref="CompanionBrain.DangerZones"/>
    /// so AI companions step out. Cancelled automatically when its owner dies; owners also cancel their
    /// own telegraphs when interrupted (stagger, groggy).
    /// </summary>
    public sealed class Telegraph : MonoBehaviour
    {
        // ---------- Look ----------
        // [I] Colour-blind mode swaps the red for violet with a white edge (readable over green grass).
        static Color EdgeColor => UiTheme.ColorBlind ? new Color(1f, 1f, 1f, 0.95f) : new Color(1f, 0.32f, 0.26f, 0.95f);
        static Color FillColor => UiTheme.ColorBlind ? new Color(0.62f, 0.2f, 0.95f, 0.4f) : new Color(0.95f, 0.08f, 0.05f, 0.34f);
        static Color FlashColor => UiTheme.ColorBlind ? new Color(0.85f, 0.7f, 1f, 0.85f) : new Color(1f, 0.75f, 0.45f, 0.85f);
        /// <summary>Telegraph textures are 64px = 4 world units across (rect: 16px = 1 unit, 9-sliced).</summary>
        const float TextureUnits = 4f;
        const float FlashTime = 0.22f;
        /// <summary>Member hit tests use body centres; ground shapes are lifted by this much to match.</summary>
        public const float HitHeight = 0.45f;
        const int Order = SkillFx.GroundOrder + 60;

        /// <summary>Every live (not yet resolved) telegraph.</summary>
        public static readonly List<Telegraph> Active = new List<Telegraph>();

        public TelegraphShape Shape { get; private set; }
        public Vector2 Origin { get; private set; }
        public Vector2 Direction { get; private set; }
        public float Radius { get; private set; }
        public float Inner { get; private set; }
        public float Length { get; private set; }
        public float Width { get; private set; }
        public float Angle { get; private set; }
        public float Windup { get; private set; }
        public float Age { get; private set; }
        public float Progress => Windup > 0f ? Mathf.Clamp01(Age / Windup) : 1f;
        public bool Resolved { get; private set; }
        public bool Cancelled { get; private set; }
        public EnemyController Owner { get; private set; }

        public int damage;
        public float knockback = 7f;
        public bool unblockable = true;
        /// <summary>Called on resolve with the members inside (even when <see cref="damage"/> is 0).</summary>
        public event Action<Telegraph, List<PlayerController>> OnResolve;

        SpriteRenderer edge, fill;
        float flashAge = -1f;

        // =============================== Factories ===============================

        public static Telegraph Circle(EnemyController owner, Vector2 center, float radius, float windup, int damage, float knockback = 7f)
        {
            var t = Create(owner, TelegraphShape.Circle, center, Vector2.right, windup, damage, knockback);
            t.Radius = radius;
            t.Build();
            return t;
        }

        public static Telegraph Rect(EnemyController owner, Vector2 origin, Vector2 dir, float length, float width, float windup, int damage, float knockback = 8f)
        {
            var t = Create(owner, TelegraphShape.Rect, origin, dir, windup, damage, knockback);
            t.Length = length;
            t.Width = width;
            t.Build();
            return t;
        }

        public static Telegraph Cone(EnemyController owner, Vector2 origin, Vector2 dir, float radius, float angleDeg, float windup, int damage, float knockback = 7f)
        {
            var t = Create(owner, TelegraphShape.Cone, origin, dir, windup, damage, knockback);
            t.Radius = radius;
            t.Angle = Mathf.Clamp(Mathf.Round(angleDeg / 10f) * 10f, 30f, 180f);
            t.Build();
            return t;
        }

        public static Telegraph Donut(EnemyController owner, Vector2 center, float inner, float outer, float windup, int damage, float knockback = 7f)
        {
            var t = Create(owner, TelegraphShape.Donut, center, Vector2.right, windup, damage, knockback);
            t.Radius = outer;
            // Texture variants in 5% steps.
            t.Inner = Mathf.Round(inner / outer * 20f) / 20f * outer;
            t.Build();
            return t;
        }

        static Telegraph Create(EnemyController owner, TelegraphShape shape, Vector2 origin, Vector2 dir, float windup, int damage, float knockback)
        {
            var go = new GameObject("Telegraph_" + shape);
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = origin;
            var t = go.AddComponent<Telegraph>();
            t.Owner = owner;
            t.Shape = shape;
            t.Origin = origin;
            t.Direction = dir.sqrMagnitude > 0.0001f ? dir.normalized : Vector2.down;
            t.Windup = Mathf.Max(0.05f, windup);
            t.damage = damage;
            t.knockback = knockback;
            Active.Add(t);
            return t;
        }

        void Build()
        {
            string edgeKey, fillKey;
            switch (Shape)
            {
                case TelegraphShape.Rect: edgeKey = "mon_tele_rect"; fillKey = "mon_tele_rectfill"; break;
                case TelegraphShape.Cone: edgeKey = $"mon_tele_cone_{Angle:0}"; fillKey = $"mon_tele_conefill_{Angle:0}"; break;
                case TelegraphShape.Donut:
                    int pct = Mathf.RoundToInt(Inner / Radius * 100f);
                    edgeKey = $"mon_tele_donut_{pct}"; fillKey = $"mon_tele_donutfill_{pct}"; break;
                default: edgeKey = "mon_tele_ring"; fillKey = "mon_tele_disc"; break;
            }
            fill = Part("Fill", fillKey, FillColor, Order);
            edge = Part("Edge", edgeKey, EdgeColor, Order + 1);
            float rot = Mathf.Atan2(Direction.y, Direction.x) * Mathf.Rad2Deg;
            if (Shape == TelegraphShape.Rect)
            {
                edge.drawMode = SpriteDrawMode.Sliced;
                fill.drawMode = SpriteDrawMode.Sliced;
                edge.size = new Vector2(Length, Width);
                edge.transform.localPosition = (Vector3)(Direction * Length * 0.5f);
                edge.transform.rotation = Quaternion.Euler(0f, 0f, rot);
                fill.transform.rotation = edge.transform.rotation;
            }
            else
            {
                float s = Radius * 2f / TextureUnits;
                edge.transform.localScale = new Vector3(s, s, 1f);
                if (Shape == TelegraphShape.Cone)
                {
                    edge.transform.rotation = Quaternion.Euler(0f, 0f, rot);
                    fill.transform.rotation = edge.transform.rotation;
                }
            }
            RegisterDanger();
            Apply();
        }

        SpriteRenderer Part(string partName, string key, Color color, int order)
        {
            var sr = new GameObject(partName).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.sprite = Game.Art.Get(key);
            sr.color = color;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Approximates the area with circles for the companions' danger avoidance.</summary>
        void RegisterDanger()
        {
            float life = Windup + 0.15f;
            switch (Shape)
            {
                case TelegraphShape.Circle:
                    CompanionBrain.AddDangerZone(Origin, Radius, life);
                    break;
                case TelegraphShape.Rect:
                {
                    float r = Width * 0.5f + 0.2f;
                    int n = Mathf.Max(1, Mathf.CeilToInt(Length / Mathf.Max(0.4f, r * 1.4f)));
                    for (int i = 0; i <= n; i++) CompanionBrain.AddDangerZone(Origin + Direction * (Length * i / n), r, life);
                    break;
                }
                case TelegraphShape.Cone:
                    CompanionBrain.AddDangerZone(Origin + Direction * Radius * 0.35f, Radius * 0.4f, life);
                    CompanionBrain.AddDangerZone(Origin + Direction * Radius * 0.72f, Radius * Mathf.Min(0.45f, Mathf.Sin(Angle * 0.5f * Mathf.Deg2Rad) * 0.72f + 0.1f), life);
                    break;
                case TelegraphShape.Donut:
                {
                    float mid = (Inner + Radius) * 0.5f, half = (Radius - Inner) * 0.5f;
                    int n = Mathf.Clamp(Mathf.CeilToInt(2f * Mathf.PI * mid / Mathf.Max(0.5f, half * 1.4f)), 6, 24);
                    for (int i = 0; i < n; i++)
                    {
                        float a = i * Mathf.PI * 2f / n;
                        CompanionBrain.AddDangerZone(Origin + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * mid, half, life);
                    }
                    break;
                }
            }
        }

        // =============================== Queries ===============================

        /// <summary>Is a ground point (feet) inside the area?</summary>
        public bool Contains(Vector2 feet)
        {
            Vector2 to = feet - Origin;
            switch (Shape)
            {
                case TelegraphShape.Rect:
                    float along = Vector2.Dot(to, Direction);
                    return along >= 0f && along <= Length && Mathf.Abs(to.x * Direction.y - to.y * Direction.x) <= Width * 0.5f;
                case TelegraphShape.Cone:
                    return to.magnitude <= Radius && (to.sqrMagnitude < 0.0001f || Vector2.Angle(Direction, to) <= Angle * 0.5f);
                case TelegraphShape.Donut:
                    return to.magnitude <= Radius && to.magnitude >= Inner;
                default:
                    return to.magnitude < Radius;
            }
        }

        /// <summary>Alive party members inside (through the Party.MembersIn* helpers).</summary>
        public List<PlayerController> MembersInside()
        {
            Vector2 o = Origin + Vector2.up * HitHeight;
            switch (Shape)
            {
                case TelegraphShape.Rect: return MonsterHits.InRect(o, Direction, Length, Width * 0.5f);
                case TelegraphShape.Cone: return MonsterHits.InArc(o, Direction, Radius, Angle * 0.5f);
                case TelegraphShape.Donut: return MonsterHits.InDonut(o, Inner, Radius);
                default: return MonsterHits.InCircle(o, Radius);
            }
        }

        // =============================== Life ===============================

        /// <summary>Removes the warning without hitting anyone (owner staggered / groggy / dead).</summary>
        public void Cancel()
        {
            if (this == null || Resolved || Cancelled) return;
            Cancelled = true;
            Active.Remove(this);
            // Drop the matching danger circles early.
            float now = Time.time;
            CompanionBrain.DangerZones.RemoveAll(z => z.expiresAt <= now + Windup - Age + 0.2f && Contains(z.center));
            Destroy(gameObject);
        }

        /// <summary>Resolves right now (tests, scripted events).</summary>
        public void ResolveNow()
        {
            if (!Resolved && !Cancelled) Resolve();
        }

        void Update()
        {
            if (Resolved)
            {
                flashAge += Time.deltaTime;
                float k = Mathf.Clamp01(flashAge / FlashTime);
                var c = FlashColor;
                c.a *= 1f - k;
                fill.color = c;
                var e = EdgeColor;
                e.a *= 1f - k;
                edge.color = e;
                if (k >= 1f) Destroy(gameObject);
                return;
            }
            if (Owner != null && Owner.IsDead)
            {
                Cancel();
                return;
            }
            if (Game.IsPlaying) Age += Time.deltaTime;
            Apply();
            if (Age >= Windup) Resolve();
        }

        void Apply()
        {
            float t = Progress;
            // Grows with an ease-out so the last moment is readable; the edge brightens as it fills.
            float g = 1f - (1f - t) * (1f - t);
            switch (Shape)
            {
                case TelegraphShape.Rect:
                    fill.size = new Vector2(Mathf.Max(0.05f, Length * g), Width);
                    fill.transform.localPosition = (Vector3)(Direction * Length * g * 0.5f);
                    break;
                case TelegraphShape.Donut:
                {
                    float s = Radius * 2f / TextureUnits;
                    fill.transform.localScale = new Vector3(s, s, 1f);
                    var c = FillColor;
                    c.a *= 0.25f + 0.75f * g;
                    fill.color = c;
                    break;
                }
                default:
                {
                    float s = Radius * 2f / TextureUnits * Mathf.Max(0.02f, g);
                    fill.transform.localScale = new Vector3(s, s, 1f);
                    break;
                }
            }
            var ec = Color.Lerp(EdgeColor, new Color(1f, 0.85f, 0.7f, 1f), t > 0.8f ? (Mathf.Sin(Time.time * 40f) * 0.5f + 0.5f) * 0.6f : 0f);
            edge.color = ec;
        }

        void Resolve()
        {
            Resolved = true;
            Active.Remove(this);
            var inside = MembersInside();
            GameObject attacker = Owner != null ? Owner.gameObject : null;
            MonsterHits.Apply(inside, damage, Origin, knockback, unblockable, attacker);
            OnResolve?.Invoke(this, inside);
            flashAge = 0f;
            if (Shape != TelegraphShape.Donut && Shape != TelegraphShape.Rect)
            {
                float s = Radius * 2f / TextureUnits;
                fill.transform.localScale = new Vector3(s, s, 1f);
            }
            else if (Shape == TelegraphShape.Rect)
            {
                fill.size = new Vector2(Length, Width);
                fill.transform.localPosition = (Vector3)(Direction * Length * 0.5f);
            }
            if (damage > 0 && Game.Player != null && Vector2.Distance(Game.Player.Position, Origin) < Mathf.Max(Radius, Length) + 4f)
                Game.Camera?.Shake(0.08f, 0.12f);
        }

        void OnDestroy() => Active.Remove(this);
    }
}
