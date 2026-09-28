using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Full-screen fade used for scene-like transitions (runs on unscaled time).</summary>
    public class ScreenFader : MonoBehaviour
    {
        Image image;

        public bool IsBusy { get; private set; }

        public static ScreenFader Create(Transform canvas)
        {
            var img = UIFactory.Overlay(canvas, "Fader", new Color(0.05f, 0.04f, 0.06f, 0f));
            img.raycastTarget = true;
            var fader = img.gameObject.AddComponent<ScreenFader>();
            fader.image = img;
            img.enabled = false;
            return fader;
        }

        public IEnumerator Fade(float to, float duration)
        {
            IsBusy = true;
            image.enabled = true;
            float from = image.color.a;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, to, t / duration));
                yield return null;
            }
            SetAlpha(to);
            image.enabled = to > 0.001f;
            IsBusy = false;
        }

        public void SetAlpha(float a)
        {
            var c = image.color;
            c.a = a;
            image.color = c;
            image.enabled = a > 0.001f;
        }
    }
}
