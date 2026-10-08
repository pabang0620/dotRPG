using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Flickering fire for story scenes (the burning village): flame sprite, ember sparks, warm light.</summary>
    public class StoryFire : MonoBehaviour
    {
        SpriteRenderer sr;
        float seed, nextEmber;

        void Start()
        {
            seed = UnityEngine.Random.value * 10f;
            sr = gameObject.AddComponent<SpriteRenderer>();
            sr.sprite = Game.Art.Get("fx_flame");
            sr.sortingOrder = 20000;
            HdMaterial.Apply(sr);
        }

        void Update()
        {
            float t = Time.time * 9f + seed;
            float s = 1.6f + Mathf.Sin(t) * 0.18f + Mathf.Sin(t * 2.3f) * 0.1f;
            transform.localScale = new Vector3(s * (Mathf.Sin(t * 0.7f) > 0f ? 1f : -1f), s * 1.15f, 1f);
            sr.color = Color.Lerp(new Color(1f, 0.55f, 0.2f), new Color(1f, 0.85f, 0.4f), 0.5f + 0.5f * Mathf.Sin(t * 1.7f));
            if (Time.time >= nextEmber)
            {
                nextEmber = Time.time + UnityEngine.Random.Range(0.15f, 0.4f);
                Fx.Burst("fx_spark", (Vector2)transform.position + Vector2.up * 0.6f, 1, 1.2f, 0.7f);
            }
        }
    }
}
