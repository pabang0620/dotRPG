using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [ROGUE] 허수아비 (Docs/PLAN_CLASS_ROGUE.md 2.2, user feedback 2026-10-23): a straw scarecrow that stands for a few
    /// seconds and lures the monsters around it (EnemyController.Lure), so they turn their backs on the rogue. It has HP,
    /// takes the lured monsters' swings and breaks when the HP runs out. Bosses are lured once, for a short time.
    /// Art: Resources/Art/char_scarecrow (not drawn yet), else a small straw figure drawn here.
    /// </summary>
    public sealed class ScarecrowDecoy : MonoBehaviour
    {
        public const string ArtKey = "char_scarecrow";
        /// <summary>Monsters coming into the circle later are lured too; checked this often.</summary>
        const float PulseGap = .5f;
        const float FadeTime = .3f;

        PlayerController owner;
        SpriteRenderer sr;
        int hp, maxHp, cap;
        float end, radius, monsterSeconds, bossSeconds, nextPulse, shakeUntil;
        bool broken;
        Color tint = Color.white;
        readonly HashSet<EnemyController> lured = new HashSet<EnemyController>();

        /// <summary>Feet position (what the monsters walk to).</summary>
        public Vector2 Position => transform.position;
        public Vector2 Center => Position + new Vector2(0f, .5f);
        /// <summary>Still up (not broken, not timed out).</summary>
        public bool Standing => this != null && !broken && Time.time < end;

        static Sprite fallback;

        /// <param name="maxTargets">How many monsters it may lure at once (-1 = every one in the circle).</param>
        public static ScarecrowDecoy Spawn(PlayerController owner, Vector2 feet, int hp, float seconds, float radius, int maxTargets, float bossSeconds, Color? tint)
        {
            var go = new GameObject("Scarecrow");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = feet;
            var d = go.AddComponent<ScarecrowDecoy>();
            d.owner = owner;
            d.hp = d.maxHp = Mathf.Max(1, hp);
            d.end = Time.time + seconds;
            d.radius = radius;
            d.cap = maxTargets;
            d.monsterSeconds = seconds;
            d.bossSeconds = bossSeconds;
            d.tint = tint ?? Color.white;
            d.sr = go.AddComponent<SpriteRenderer>();
            d.sr.sprite = Resources.Load<Sprite>(SpriteLibrary.OverrideFolder + ArtKey) ?? Fallback();
            d.sr.color = d.tint;
            d.sr.sortingOrder = YSort.OrderFor(feet.y);
            NinjaClone.Puff(d.Center);
            SkillFx.Spawn("fx_ring", feet, new Color(.95f, .8f, .35f, .6f), .45f, SkillFx.GroundOrder + 14)
                .Scale(new Vector2(radius * .3f, radius * .2f), new Vector2(radius, radius * .7f)).Fade(FxFade.Late);
            d.Pulse();
            return d;
        }

        /// <summary>A monster's swing. Breaks the scarecrow when its HP runs out.</summary>
        public void TakeHit(int damage)
        {
            if (!Standing) return;
            hp -= Mathf.Max(1, damage);
            shakeUntil = Time.time + .15f;
            SkillVisuals.Sparks(Center, new Color(.95f, .82f, .4f, 1f), 4, 2.4f);
            if (hp <= 0) Break();
        }

        void Break()
        {
            broken = true;
            SkillVisuals.Sparks(Center, new Color(.95f, .82f, .4f, 1f), 14, 4f, .35f);
            NinjaClone.Puff(Center);
            Destroy(gameObject);
        }

        /// <summary>Lures the monsters in the circle that are not lured yet (up to the cap). A boss only once.</summary>
        void Pulse()
        {
            int count = 0;
            foreach (var e in lured) if (e != null && !e.IsDead && e.LuredBy(this)) count++;
            var near = new List<EnemyController>();
            foreach (var e in EnemyController.Active)
                if (e != null && !e.IsDead && e.isActiveAndEnabled && !lured.Contains(e) && Vector2.Distance(e.Center, Center) <= radius) near.Add(e);
            near.Sort((a, b) => (a.Center - Center).sqrMagnitude.CompareTo((b.Center - Center).sqrMagnitude));
            foreach (var e in near)
            {
                if (cap > 0 && count >= cap) break;
                float left = end - Time.time;
                e.Lure(this, Mathf.Min(left, e.IsBoss ? bossSeconds : monsterSeconds));
                lured.Add(e);
                count++;
                SkillFx.Spawn("fx_alert", e.Center + Vector2.up * .9f, Color.white, .6f, SkillFx.TopOrder + 5).Pop().Fade(FxFade.Late);
            }
        }

        void Update()
        {
            if (broken) return;
            if (owner == null || Time.time >= end)
            {
                float fade = Time.time - end;
                if (fade >= FadeTime || owner == null) { Destroy(gameObject); return; }
                sr.color = new Color(tint.r, tint.g, tint.b, tint.a * (1f - fade / FadeTime));
                return;
            }
            if (Time.time >= nextPulse) { nextPulse = Time.time + PulseGap; Pulse(); }
            // A small wobble when hit.
            float wobble = Time.time < shakeUntil ? Mathf.Sin(Time.time * 70f) * 6f : 0f;
            transform.rotation = Quaternion.Euler(0f, 0f, wobble);
        }

        /// <summary>A 24x40 straw figure on a pole (bottom pivot, 36 px per unit like the characters).</summary>
        static Sprite Fallback()
        {
            if (fallback != null) return fallback;
            var c = new PixelCanvas(24, 40);
            Color32 pole = PixelCanvas.Hex("#6b4a2b"), straw = PixelCanvas.Hex("#e2c060"), strawDark = PixelCanvas.Hex("#b8913a"),
                sack = PixelCanvas.Hex("#c9a46a"), hat = PixelCanvas.Hex("#7a5a34"), patch = PixelCanvas.Hex("#8c4a3a"), eye = PixelCanvas.Hex("#3a2a1a");
            // PixelCanvas rows run top to bottom.
            c.VLine(11, 9, 39, pole); c.VLine(12, 9, 39, pole);
            c.HLine(2, 21, 18, pole); c.HLine(2, 21, 19, pole);
            c.Rect(7, 18, 10, 12, sack);
            c.Rect(13, 23, 3, 3, patch);
            for (int x = 7; x < 17; x += 2) c.VLine(x, 30, 32, straw);
            c.Set(1, 18, straw); c.Set(1, 19, strawDark); c.Set(22, 18, straw); c.Set(22, 19, strawDark);
            c.Circle(11.5f, 13f, 4.6f, sack);
            c.Set(10, 12, eye); c.Set(13, 12, eye);
            c.HLine(10, 13, 15, eye);
            c.HLine(5, 18, 8, hat); c.Rect(8, 4, 8, 4, hat);
            c.Set(6, 9, straw); c.Set(17, 9, straw); c.Set(8, 17, strawDark); c.Set(15, 17, strawDark);
            c.Outline(PixelCanvas.Hex("#2a1e12"));
            var tex = new Texture2D(c.Width, c.Height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            tex.SetPixels32(c.ToTexturePixels());
            tex.Apply();
            fallback = Sprite.Create(tex, new Rect(0, 0, c.Width, c.Height), new Vector2(.5f, 0f), 36f);
            return fallback;
        }
    }
}
