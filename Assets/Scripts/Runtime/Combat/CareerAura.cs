using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Shows a character's career states: a crest for a shield, a ring under the feet for the guard stance and the
    /// counter stance, rising sparks for a blessing, a soft glow for a healing sanctuary, and the oath ward circle.
    /// It only reads <see cref="CareerCombat"/>; it never changes a state.
    /// </summary>
    public sealed class CareerAura : MonoBehaviour
    {
        PlayerController player;
        CareerCombat state;
        SpriteRenderer crest, ring, glow, ward;
        float nextSpark, hitUntil;

        public static CareerAura For(PlayerController p, CareerCombat c)
        {
            var a = p.GetComponent<CareerAura>();
            if (a == null) a = p.gameObject.AddComponent<CareerAura>();
            a.player = p;
            a.state = c;
            return a;
        }

        public void ShieldHit() => hitUntil = Time.time + 0.15f;

        SpriteRenderer Make(string name, string sprite, bool additive)
        {
            var sr = new GameObject(name).AddComponent<SpriteRenderer>();
            sr.transform.SetParent(transform, false);
            sr.sprite = Game.Art.Get(sprite);
            if (additive) sr.sharedMaterial = FxMaterials.Additive;
            sr.enabled = false;
            return sr;
        }

        public void Clear()
        {
            foreach (var sr in new[] { crest, ring, glow, ward }) if (sr != null) sr.enabled = false;
        }

        void LateUpdate()
        {
            if (player == null || state == null || player.IsDead || !player.gameObject.activeInHierarchy) { Clear(); return; }
            if (crest == null)
            {
                crest = Make("CareerCrest", "fx_aegis", false);
                ring = Make("CareerRing", "fx_rune", false);
                glow = Make("CareerGlow", "fx_glow", true);
                ward = Make("CareerWard", "fx_ring", false);
            }
            Vector2 c = player.Center, feet = player.Position;
            int order = SkillFx.At(c.y, 30);

            // Shield: a faint crest in front of the body, brighter for a moment when it soaks a hit.
            bool shielded = state.Shield > 0;
            crest.enabled = shielded;
            if (shielded)
            {
                var col = CareerFx.Main(state.ShieldCareer);
                float a = Time.time < hitUntil ? 0.85f : 0.32f + 0.06f * Mathf.Sin(Time.time * 6f);
                crest.color = new Color(col.r, col.g, col.b, a);
                crest.transform.position = c + Vector2.up * 0.05f;
                crest.transform.localScale = Vector3.one * (Time.time < hitUntil ? 1.25f : 1.1f);
                crest.sortingOrder = order + 5;
            }

            // Guard / counter stance: a turning ring under the feet.
            bool guard = state.GuardVisible || state.CounterVisible;
            ring.enabled = guard;
            if (guard)
            {
                var col = state.CounterVisible ? new Color(1f, 0.85f, 0.45f, 0.75f) : new Color(CareerFx.Teal.r, CareerFx.Teal.g, CareerFx.Teal.b, 0.6f);
                ring.color = col;
                ring.transform.position = feet;
                ring.transform.rotation = Quaternion.Euler(0f, 0f, Time.time * (state.CounterVisible ? 220f : 90f));
                ring.transform.localScale = new Vector3(0.75f, 0.75f, 1f) * (state.CounterVisible ? 1f + 0.08f * Mathf.Sin(Time.time * 14f) : 1f);
                ring.sortingOrder = SkillFx.GroundOrder + 20;
            }

            // Sanctuary healing: a warm glow at the feet.
            glow.enabled = state.HotVisible;
            if (glow.enabled)
            {
                glow.color = new Color(1f, 0.9f, 0.55f, 0.35f + 0.1f * Mathf.Sin(Time.time * 5f));
                glow.transform.position = feet + Vector2.up * 0.1f;
                glow.transform.localScale = new Vector3(1.4f, 0.9f, 1f);
                glow.sortingOrder = SkillFx.GroundOrder + 21;
            }

            // Oath ward: the protected circle following the guardian.
            ward.enabled = state.OathRadius > 0f;
            if (ward.enabled)
            {
                ward.color = new Color(CareerFx.Teal.r, CareerFx.Teal.g, CareerFx.Teal.b, 0.45f + 0.1f * Mathf.Sin(Time.time * 4f));
                ward.transform.position = feet;
                ward.transform.localScale = new Vector3(state.OathRadius, state.OathRadius * 0.8f, 1f);
                ward.sortingOrder = SkillFx.GroundOrder + 19;
            }

            // Blessing: gold sparks rising off the body.
            if (state.BlessVisible && Time.time >= nextSpark)
            {
                nextSpark = Time.time + 0.09f;
                Vector2 p = feet + new Vector2(Random.Range(-0.35f, 0.35f), Random.Range(0.1f, 0.6f));
                SkillFx.Spawn("fx_spark", p, new Color(1f, Random.Range(0.8f, 0.95f), 0.45f, 1f), Random.Range(0.35f, 0.55f), SkillFx.At(feet.y, 31))
                    .Move(new Vector2(Random.Range(-0.2f, 0.2f), Random.Range(1.2f, 2f)), 1f).Scale(1f, 0.4f);
            }
        }

        void OnDisable() => Clear();
    }
}
