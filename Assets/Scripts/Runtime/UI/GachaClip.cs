using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Video;

namespace DotRPG
{
    /// <summary>
    /// [GACHA VFX 2026-10-30] A pre-rendered light clip (Resources/Video/summon_*.mp4, black background, no audio) shown
    /// on the UI with a screen blend (Shaders/UIScreen), so black is transparent and only the light shows. The bonghwa
    /// UI.vfx() in Unity terms: VideoPlayer -> RenderTexture -> RawImage, invisible until a real frame is drawn, then a
    /// 0.15 s fade in; destroyed when it ends; never takes a click. Docs/PLAN_GACHA_VFX_2026-10-30.md.
    /// Made prepared in advance (<see cref="Create"/>) so a reveal starts at once, then placed with
    /// <see cref="PlayOver"/> (cover the area) or <see cref="PlaySpot"/> (on one card, spilling past its edges).
    /// </summary>
    public sealed class GachaClip : MonoBehaviour
    {
        public const string Unique = "Video/summon_unique", Legendary = "Video/summon_legendary";
        const float FadeIn = 0.15f, FadeOut = 0.15f;

        static readonly Dictionary<string, VideoClip> clips = new Dictionary<string, VideoClip>();
        static Material material;
        static bool materialTried;

        VideoPlayer player;
        RenderTexture texture;
        RawImage image;
        float width, height;
        bool playing, visible, closing;
        float visibleAt, closeAt, deadline;

        /// <summary>Clips are skipped while the screen-shake setting is off (the game's "reduce motion").</summary>
        public static bool Enabled => Game.Settings == null || Game.Settings.Data.screenShake;

        static VideoClip Clip(string key)
        {
            if (!clips.TryGetValue(key, out var clip))
            {
                clip = Resources.Load<VideoClip>(key);
                clips[key] = clip;
            }
            return clip;
        }

        static Material ScreenMaterial()
        {
            if (!materialTried)
            {
                materialTried = true;
                var shader = Resources.Load<Shader>("Shaders/UIScreen");
                if (shader != null) material = new Material(shader) { name = "GachaClip screen" };
                else Debug.LogWarning("[dotRPG] Shaders/UIScreen missing: gacha clips are off");
            }
            return material;
        }

        /// <summary>A hidden clip parked under <paramref name="park"/> (an active object) and preparing. Null: clip or shader missing.</summary>
        public static GachaClip Create(string key, Transform park)
        {
            var clip = Clip(key);
            var mat = ScreenMaterial();
            if (clip == null || mat == null || park == null) return null;
            var go = new GameObject("GachaClip", typeof(RectTransform));
            go.transform.SetParent(park, false);
            var c = go.AddComponent<GachaClip>();
            c.Build(clip, mat);
            return c;
        }

        void Build(VideoClip clip, Material mat)
        {
            width = clip.width;
            height = clip.height;
            texture = new RenderTexture((int)clip.width, (int)clip.height, 0) { name = "GachaClip" };
            image = gameObject.AddComponent<RawImage>();
            image.texture = texture;
            image.material = mat;
            image.raycastTarget = false;
            image.color = new Color(1f, 1f, 1f, 0f);
            image.enabled = false; // nothing on screen until the first real frame
            var rt = (RectTransform)transform;
            rt.sizeDelta = Vector2.zero;

            player = gameObject.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.isLooping = false;
            player.waitForFirstFrame = true;
            player.skipOnDrop = true;
            player.clip = clip;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = texture;
            player.timeUpdateMode = VideoTimeUpdateMode.UnscaledGameTime;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.loopPointReached += _ => Close(true);
            player.errorReceived += (_, message) => { Debug.LogWarning("[dotRPG] Gacha clip: " + message); Close(true); };
            player.Prepare();
        }

        /// <summary>Covers <paramref name="area"/> (cropping the clip's sides or top, like object-fit: cover) above its other children.</summary>
        public void PlayOver(RectTransform area)
        {
            var rt = (RectTransform)transform;
            rt.SetParent(area, false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
            var fit = gameObject.AddComponent<AspectRatioFitter>();
            fit.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fit.aspectRatio = width / height;
            Begin();
        }

        /// <summary>On one card: a box 230% wide and 200% high centred on it, the clip fitted inside (bonghwa .vfx-spot).</summary>
        public void PlaySpot(RectTransform card)
        {
            var rt = (RectTransform)transform;
            rt.SetParent(card.parent, false);
            rt.anchorMin = card.anchorMin;
            rt.anchorMax = card.anchorMax;
            rt.pivot = new Vector2(0.5f, 0.5f);
            var size = card.rect.size;
            var center = card.anchoredPosition + Vector2.Scale(new Vector2(0.5f, 0.5f) - card.pivot, size);
            float k = Mathf.Min(size.x * 2.3f / width, size.y * 2f / height);
            rt.sizeDelta = new Vector2(width * k, height * k);
            rt.anchoredPosition = center;
            Begin();
        }

        void Begin()
        {
            transform.SetAsLastSibling();
            playing = true;
            deadline = Time.unscaledTime + (float)player.clip.length + 3f; // a decoder that never starts does not leave it behind
            player.Play();
        }

        /// <summary>Ends it: a short fade (or at once), then the object is destroyed.</summary>
        public void Close(bool now = false)
        {
            if (this == null) return;
            if (now || !visible) { Destroy(gameObject); return; }
            if (closing) return;
            closing = true;
            closeAt = Time.unscaledTime;
        }

        void Update()
        {
            if (!playing) return;
            float t = Time.unscaledTime;
            if (t > deadline) { Close(true); return; }
            if (!visible)
            {
                if (!player.isPlaying || player.frame < 1) return; // the texture holds a real frame from here on
                visible = true;
                visibleAt = t;
                image.enabled = true;
            }
            float a = Mathf.Clamp01((t - visibleAt) / FadeIn);
            if (closing)
            {
                float left = 1f - (t - closeAt) / FadeOut;
                if (left <= 0f) { Destroy(gameObject); return; }
                a *= left;
            }
            image.color = new Color(1f, 1f, 1f, a);
        }

        void OnDestroy()
        {
            if (player != null) player.Stop();
            if (texture != null) { texture.Release(); Destroy(texture); }
        }
    }
}
