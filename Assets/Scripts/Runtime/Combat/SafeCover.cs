using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [RAID] Cover against the 섬멸 (annihilation) blast: a guardian's 거대 방패 (planted by 강철의 보루, or by an AI tank) blocks
    /// the line of fire behind it, and the boss's own safe circles (teal <see cref="Telegraph"/> zones) protect whoever stands inside.
    /// The blast's damage is decided on the simulating PC (host / solo) through <see cref="Shelters"/>.
    /// </summary>
    public static class SafeCover
    {
        public struct Wall
        {
            public Vector2 center, facing;
            public float halfWidth, until;
        }

        /// <summary>Half the width of a 거대 방패 and how long it stands.</summary>
        public const float WallHalfWidth = 1.5f, WallTime = 7f;
        /// <summary>How far behind a wall the shelter guide reaches (the line-of-fire test itself has no depth limit).</summary>
        public const float ShelterDepth = 4f;

        static readonly List<Wall> walls = new List<Wall>();

        /// <summary>A 섬멸 is being cast (AI companions run for cover; an AI tank plants a shield).</summary>
        public static bool Annihilating => Time.time < annihilateUntil;
        public static Vector2 AnnihilateSource { get; private set; }
        /// <summary>Increases with every 섬멸 so each AI tank raises one shield per cast.</summary>
        public static int AnnihilateId { get; private set; }
        static float annihilateUntil = -1f;

        public static void BeginAnnihilate(Vector2 source, float seconds)
        {
            AnnihilateSource = source;
            annihilateUntil = Time.time + seconds;
            AnnihilateId++;
        }

        public static void EndAnnihilate() => annihilateUntil = -1f;

        /// <summary>Plants a 거대 방패 at <paramref name="center"/> facing <paramref name="facing"/> (the side it guards against).</summary>
        public static void AddWall(Vector2 center, Vector2 facing, float halfWidth = WallHalfWidth, float seconds = WallTime, bool showShelter = true)
        {
            if (facing.sqrMagnitude < .0001f) facing = Vector2.down;
            facing = facing.normalized;
            walls.RemoveAll(w => w.until <= Time.time);
            walls.Add(new Wall { center = center, facing = facing, halfWidth = halfWidth, until = Time.time + seconds });
            TowerShieldFx.Spawn(center, facing, halfWidth, seconds);
            // The shelter behind the wall as a teal ground zone: the host's telegraphs reach member PCs, so only the
            // simulating PC draws it (a member's own replayed wall would draw it twice).
            if (showShelter && !PartyNet.IsMember)
                Telegraph.SafeRect(center - facing * .1f, -facing, ShelterDepth, halfWidth * 2f, seconds);
        }

        /// <summary>Is the straight line from <paramref name="source"/> to <paramref name="target"/> cut by a standing wall?</summary>
        public static bool BehindWall(Vector2 source, Vector2 target)
        {
            float now = Time.time;
            foreach (var w in walls)
            {
                if (w.until <= now) continue;
                Vector2 side = new Vector2(-w.facing.y, w.facing.x) * w.halfWidth;
                if (SegmentsCross(source, target, w.center - side, w.center + side)) return true;
            }
            return false;
        }

        /// <summary>Is <paramref name="feet"/> inside one of the boss's safe circles?</summary>
        public static bool InSafeZone(Vector2 feet)
        {
            foreach (var t in Telegraph.Active)
                if (t != null && t.Safe && !t.ShelterGuide && !t.Cancelled && t.Contains(feet)) return true;
            return false;
        }

        /// <summary>Protected from a blast fired from <paramref name="source"/>: behind a wall or in a safe circle.</summary>
        public static bool Shelters(Vector2 source, PlayerController m) =>
            m != null && (InSafeZone(m.Position) || BehindWall(source, m.Center));

        /// <summary>The nearest place to hide from <paramref name="source"/>: just behind a wall, else the centre of a safe circle.</summary>
        public static bool SuggestSpot(Vector2 from, Vector2 source, out Vector2 spot)
        {
            spot = from;
            float best = float.MaxValue;
            float now = Time.time;
            foreach (var w in walls)
            {
                if (w.until <= now) continue;
                Vector2 away = w.center - source;
                Vector2 dir = away.sqrMagnitude > .0001f ? away.normalized : -w.facing;
                Vector2 s = w.center + dir * 1.1f;
                float d = Vector2.Distance(from, s);
                if (d < best) { best = d; spot = s; }
            }
            foreach (var t in Telegraph.Active)
            {
                if (t == null || !t.Safe || t.ShelterGuide || t.Cancelled) continue;
                float d = Vector2.Distance(from, t.Origin);
                if (d < best) { best = d; spot = t.Origin; }
            }
            return best < float.MaxValue;
        }

        static bool SegmentsCross(Vector2 a, Vector2 b, Vector2 c, Vector2 d)
        {
            float Cross(Vector2 o, Vector2 p, Vector2 q) => (p.x - o.x) * (q.y - o.y) - (p.y - o.y) * (q.x - o.x);
            float d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return d1 * d2 < 0f && d3 * d4 < 0f;
        }
    }

    /// <summary>[VFX] The 거대 방패: three tower-shield panes stuck in the ground in a row, fading out at the end.</summary>
    public sealed class TowerShieldFx : MonoBehaviour
    {
        const int Panes = 3;
        const float PaneHeight = 1.7f;
        readonly List<SpriteRenderer> panes = new List<SpriteRenderer>();
        float until;

        public static void Spawn(Vector2 center, Vector2 facing, float halfWidth, float seconds)
        {
            var go = new GameObject("TowerShield");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = center;
            var fx = go.AddComponent<TowerShieldFx>();
            fx.until = Time.time + seconds;
            Sprite sprite = null;
            foreach (var id in new[] { "g5_shield", "g4_wall" })
                if (VfxLibrary.Has(id) && VfxLibrary.Get(id).Length > 0) { sprite = VfxLibrary.Get(id)[0]; break; }
            if (sprite == null) sprite = Game.Art.Get("fx_glow");
            Vector2 side = new Vector2(-facing.y, facing.x);
            for (int i = 0; i < Panes; i++)
            {
                float k = Panes == 1 ? 0f : (i / (float)(Panes - 1)) * 2f - 1f;
                var sr = new GameObject("Pane").AddComponent<SpriteRenderer>();
                sr.transform.SetParent(go.transform, false);
                Vector2 at = center + side * (k * halfWidth * .8f);
                sr.transform.position = at;
                sr.sprite = sprite;
                if (FxMaterials.Alpha != null) sr.sharedMaterial = FxMaterials.Alpha;
                float h = sprite != null ? Mathf.Max(.01f, sprite.bounds.size.y) : 1f;
                sr.transform.localScale = Vector3.one * (PaneHeight / h);
                sr.color = new Color(.75f, 1f, .95f, .9f);
                sr.sortingOrder = SkillFx.At(at.y, 2);
                fx.panes.Add(sr);
            }
            SkillFx.Spawn("fx_ring", center, new Color(.5f, 1f, .9f, .8f), .5f, SkillFx.GroundOrder + 14).Scale(new Vector2(.3f, .2f), new Vector2(halfWidth * 1.6f, halfWidth * 1.1f)).Fade(FxFade.Late);
            Game.Audio?.PlaySfx("c_shield", .8f);
        }

        void Update()
        {
            float left = until - Time.time;
            if (left <= 0f) { Destroy(gameObject); return; }
            float a = left < .6f ? left / .6f : 1f;
            float pulse = .85f + .15f * Mathf.Sin(Time.time * 5f);
            foreach (var sr in panes) if (sr != null) sr.color = new Color(.75f, 1f, .95f, .9f * a * pulse);
        }
    }
}
