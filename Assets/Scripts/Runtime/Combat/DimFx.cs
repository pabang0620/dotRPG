using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Darkens the world (not the effects on top of it) for an awakening skill's big moment.</summary>
    public class DimFx : MonoBehaviour
    {
        static DimFx current;
        SpriteRenderer sr;
        float life, age, strength;

        public static void Show(float seconds, float alpha)
        {
            if (current == null)
            {
                var go = new GameObject("AwakeningDim");
                current = go.AddComponent<DimFx>();
                current.sr = go.AddComponent<SpriteRenderer>();
                current.sr.sprite = Game.Art.Get("ui_white");
                current.sr.sortingOrder = SkillFx.TopOrder - 1000; // over every character, under the skill's light
            }
            current.life = seconds;
            current.age = 0f;
            current.strength = alpha;
            current.LateUpdate();
        }

        void LateUpdate()
        {
            age += Time.deltaTime;
            float t = age / life;
            var cam = Game.Camera != null ? Game.Camera.Camera : Camera.main;
            if (t >= 1f || cam == null) { Destroy(gameObject); return; }
            float a = t < 0.15f ? t / 0.15f : t > 0.6f ? 1f - (t - 0.6f) / 0.4f : 1f;
            sr.color = new Color(0.02f, 0.02f, 0.08f, strength * a);
            var p = cam.transform.position;
            transform.position = new Vector3(p.x, p.y, 0f);
            float h = cam.orthographicSize * 2f + 2f, w = h * cam.aspect + 2f;
            transform.localScale = new Vector3(w / 0.25f, h / 0.25f, 1f); // ui_white is 0.25 units across
        }
    }
}
