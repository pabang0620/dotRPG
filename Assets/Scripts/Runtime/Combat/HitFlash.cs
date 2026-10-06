using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Brief white flash on hit, plus optional blinking during invulnerability.
    /// Swaps sprites for white silhouettes in LateUpdate so it composes with any animator.
    /// </summary>
    public class HitFlash : MonoBehaviour
    {
        [SerializeField] SpriteRenderer[] targets;

        float flashUntil;
        float blinkUntil;
        bool wasBlinking;
        readonly Dictionary<SpriteRenderer, Sprite> originals = new Dictionary<SpriteRenderer, Sprite>();
        readonly HashSet<Sprite> silhouettes = new HashSet<Sprite>();

        public void SetTargets(params SpriteRenderer[] renderers) => targets = renderers;

        public void Flash(float duration = 0.1f) => flashUntil = Mathf.Max(flashUntil, Time.time + duration);

        /// <summary>
        /// [FEEL] A heavy blow: the body turns into a black silhouette for a blink, then white, then back. Reads at a
        /// glance even in a crowd (only for skills' big hits and finishers; normal hits keep the plain white flash).
        /// </summary>
        public void FlashHeavy()
        {
            darkUntil = Time.time + 0.05f;
            flashUntil = Time.time + 0.15f;
        }

        float darkUntil;

        public void Blink(float duration) => blinkUntil = Time.time + duration;

        public void Stop()
        {
            flashUntil = blinkUntil = 0f;
            Restore();
            SetVisible(true);
        }

        void LateUpdate()
        {
            if (targets == null) return;
            bool flashing = Time.time < flashUntil;
            if (flashing)
            {
                foreach (var sr in targets)
                {
                    if (sr == null) continue;
                    if (!silhouettes.Contains(sr.sprite)) originals[sr] = sr.sprite;
                    var white = Game.Art != null ? Game.Art.GetSilhouette(originals[sr]) : null;
                    if (white != null)
                    {
                        silhouettes.Add(white);
                        sr.sprite = white;
                        sr.color = Time.time < darkUntil ? new Color(0.06f, 0.05f, 0.1f, sr.color.a) : new Color(1f, 1f, 1f, sr.color.a);
                    }
                    else
                    {
                        sr.color = new Color(1f, 0.45f, 0.45f, sr.color.a);
                    }
                }
            }
            else if (originals.Count > 0)
            {
                Restore();
            }

            // Only touch alpha while blinking so other effects (death fades) keep control of it.
            bool blinking = Time.time < blinkUntil;
            if (blinking) SetVisible(Mathf.Repeat(Time.time * 14f, 1f) > 0.35f);
            else if (wasBlinking) SetVisible(true);
            wasBlinking = blinking;
        }

        void Restore()
        {
            foreach (var pair in originals)
            {
                if (pair.Key == null) continue;
                if (silhouettes.Contains(pair.Key.sprite)) pair.Key.sprite = pair.Value;
                var c = pair.Key.color;
                pair.Key.color = new Color(1f, 1f, 1f, c.a);
            }
            originals.Clear();
        }

        void SetVisible(bool visible)
        {
            if (targets == null) return;
            foreach (var sr in targets)
            {
                if (sr == null) continue;
                var c = sr.color;
                c.a = visible ? 1f : 0.25f;
                sr.color = c;
            }
        }
    }
}
