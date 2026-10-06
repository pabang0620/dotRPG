using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Frame-animated effect clips (flipbooks). Each clip is a list of frames drawn facing right; <see cref="VfxArt"/>
    /// generates them in high resolution (Density 4). Clips are built once and cached.
    /// </summary>
    public static class VfxLibrary
    {
        static readonly Dictionary<string, Sprite[]> clips = new Dictionary<string, Sprite[]>();

        public static Sprite[] Get(string name)
        {
            if (clips.TryGetValue(name, out var frames)) return frames;
            var canvases = VfxArt.Frames(name);
            if (canvases == null || canvases.Length == 0) { Debug.LogWarning("[dotRPG] Missing vfx clip: " + name); return clips[name] = new Sprite[0]; }
            frames = new Sprite[canvases.Length];
            for (int i = 0; i < canvases.Length; i++) frames[i] = ToSprite(name + "_" + i, canvases[i]);
            return clips[name] = frames;
        }

        static Sprite ToSprite(string key, PixelCanvas c)
        {
            var texture = new Texture2D(c.Width, c.Height, TextureFormat.RGBA32, false)
            { name = key, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            texture.SetPixels32(c.ToTexturePixels());
            texture.Apply(false, true);
            var pivot = new Vector2(c.PivotX / c.Width, c.PivotY / c.Height);
            var sprite = Sprite.Create(texture, new Rect(0, 0, c.Width, c.Height), pivot, 16f * Mathf.Max(1, c.Density), 0, SpriteMeshType.FullRect);
            sprite.name = key;
            return sprite;
        }
    }

    /// <summary>How a clip sorts against the world.</summary>
    public enum VfxLayer { Top, Ground, AtFeet }

    /// <summary>
    /// Plays one clip. Direction rule: clips are drawn facing right. Facing left is drawn as a mirror image (flip X),
    /// never as a 180 degree turn, so tilt, light and the order of a combo look the same on both sides.
    /// Up/down aims tilt the right-facing (or mirrored) art by at most 90 degrees.
    /// </summary>
    public sealed class VfxPlayer : MonoBehaviour
    {
        SpriteRenderer sr;
        Sprite[] frames;
        float fps, age, life, fadeOut;
        bool loop;
        Color color;
        Transform follow;
        Vector3 followOffset;
        Vector2 velocity;
        VfxLayer layer;
        Vector2 scaleFrom, scaleTo;

        /// <summary>
        /// Plays <paramref name="clip"/> at <paramref name="at"/> aimed along <paramref name="dir"/> (Vector2.zero = no turn).
        /// <paramref name="life"/> 0 = one pass of the frames.
        /// </summary>
        public static VfxPlayer Play(string clip, Vector2 at, Vector2 dir, Color color, float scale = 1f, float fps = 24f,
            bool additive = false, float life = 0f, bool loop = false, VfxLayer layer = VfxLayer.Top, bool turn = true, int orderBoost = 0)
        {
            var frames = VfxLibrary.Get(clip);
            if (frames.Length == 0) return null;
            var go = new GameObject("Vfx " + clip);
            if (Fx.Root != null) go.transform.SetParent(Fx.Root, false);
            go.transform.position = at;
            var p = go.AddComponent<VfxPlayer>();
            p.sr = go.AddComponent<SpriteRenderer>();
            if (additive) p.sr.sharedMaterial = FxMaterials.Additive;
            p.frames = frames;
            p.fps = fps;
            p.loop = loop;
            p.life = life > 0f ? life : frames.Length / fps;
            p.color = color;
            p.layer = layer;
            p.scaleFrom = p.scaleTo = Vector2.one * scale;
            p.sr.sprite = frames[0];
            p.sr.color = color;
            p.Orient(dir, turn);
            p.order = orderBoost;
            p.Sort();
            return p;
        }

        int order;

        void Orient(Vector2 dir, bool turn)
        {
            if (dir.sqrMagnitude < 0.0001f) return;
            bool left = dir.x < -0.01f;
            sr.flipX = left;
            if (!turn) return;
            float a = Mathf.Atan2(dir.y, Mathf.Abs(dir.x)) * Mathf.Rad2Deg; // -90..90 for the right-facing art
            transform.rotation = Quaternion.Euler(0f, 0f, left ? -a : a);
        }

        public VfxPlayer Follow(Transform target, Vector2 offset) { follow = target; followOffset = offset; return this; }
        public VfxPlayer Move(Vector2 speed) { velocity = speed; return this; }
        public VfxPlayer Grow(float from, float to) { scaleFrom = Vector2.one * from; scaleTo = Vector2.one * to; transform.localScale = scaleFrom; return this; }
        public VfxPlayer Squash(float x, float y) { scaleFrom = new Vector2(scaleFrom.x * x, scaleFrom.y * y); scaleTo = new Vector2(scaleTo.x * x, scaleTo.y * y); transform.localScale = scaleFrom; return this; }
        public VfxPlayer FadeOut(float seconds) { fadeOut = seconds; return this; }
        public VfxPlayer FlipY(bool on) { sr.flipY = on; return this; }
        public void Place(Vector2 at) => transform.position = at;
        public void Stop() { if (this != null) Destroy(gameObject); }

        void Sort()
        {
            Vector2 p = transform.position;
            sr.sortingOrder = (layer == VfxLayer.Ground ? SkillFx.GroundOrder + 12 : layer == VfxLayer.AtFeet ? SkillFx.At(p.y, 40) : SkillFx.TopOrder) + order;
        }

        void LateUpdate()
        {
            age += Time.deltaTime;
            if (age >= life) { Destroy(gameObject); return; }
            int i = (int)(age * fps);
            sr.sprite = frames[loop ? i % frames.Length : Mathf.Min(i, frames.Length - 1)];
            float t = Mathf.Clamp01(age / life);
            transform.localScale = Vector2.Lerp(scaleFrom, scaleTo, t);
            if (follow != null) transform.position = follow.position + followOffset;
            else if (velocity != Vector2.zero) transform.position += (Vector3)(velocity * Time.deltaTime);
            float a = fadeOut > 0f ? Mathf.Clamp01((life - age) / fadeOut) : 1f;
            sr.color = new Color(color.r, color.g, color.b, color.a * a);
            if (layer == VfxLayer.AtFeet) Sort();
        }
    }
}
