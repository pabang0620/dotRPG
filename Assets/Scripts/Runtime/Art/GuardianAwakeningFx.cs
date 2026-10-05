using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    // Four original cels with a stable ground pivot. No gameplay mutation in this component.
    public sealed class GuardianAwakeningFx : MonoBehaviour
    {
        static readonly List<GuardianAwakeningFx> active = new List<GuardianAwakeningFx>();
        public static int Count => active.Count;
        PlayerController owner;
        string map;
        SpriteRenderer art;
        float age, duration, radius;
        bool charging;
        Vector2 ground;

        public static void Charge(PlayerController source, float seconds)
            => Make(source, source.Center, 2.1f, seconds, true);
        public static void Impact(PlayerController source, Vector2 at, float range)
            => Make(source, at, range, .92f, false);
        static void Make(PlayerController source, Vector2 at, float range, float seconds, bool charge)
        {
            if (active.Count >= 12 || CareerPulseArt.Frame(Career.Guardian, 3, 0) == null) return;
            var go = new GameObject(charge ? "Guardian quake charge" : "Guardian quake impact");
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            var fx = go.AddComponent<GuardianAwakeningFx>();
            fx.owner = source; fx.map = Game.Session.MapId; fx.ground = at;
            fx.radius = range; fx.duration = Mathf.Max(.05f, seconds); fx.charging = charge;
            fx.art = go.AddComponent<SpriteRenderer>();
            active.Add(fx); fx.Draw();
        }
        public static void Clear(PlayerController source)
        {
            foreach (var fx in active.ToArray()) if (fx != null && fx.owner == source) Destroy(fx.gameObject);
        }
        void Update()
        {
            age += Time.deltaTime;
            if (owner == null || owner.IsDead || !owner.gameObject.activeInHierarchy || Game.Session.MapId != map || age >= duration)
            { Destroy(gameObject); return; }
            if (charging) ground = owner.Center;
            Draw();
        }
        void Draw()
        {
            int cel = charging ? 0 : age < .10f ? 1 : age < .40f ? 2 : 3;
            var sprite = CareerPulseArt.Frame(Career.Guardian, 3, cel);
            var peak = CareerPulseArt.Frame(Career.Guardian, 3, 2);
            art.sprite = sprite;
            // Central shield is compact; the separate ring describes the full 12m damage footprint.
            float scale = radius * (charging ? 1.15f : .95f) / peak.bounds.size.x;
            if (charging) scale *= Mathf.Lerp(.72f, 1f, age / duration);
            transform.localScale = Vector3.one * scale;
            transform.position = ground + Vector2.up * (sprite.bounds.size.y * scale * .30f + (charging ? .35f : 0));
            art.sortingOrder = SkillFx.At(ground.y, 88);
            float alpha = charging ? Mathf.Lerp(.45f, .95f, age / duration) : age < .40f ? .95f : Mathf.Clamp01((duration - age) / .52f);
            art.color = new Color(1, 1, 1, alpha);
        }
        void OnDestroy() { active.Remove(this); }
    }
}
