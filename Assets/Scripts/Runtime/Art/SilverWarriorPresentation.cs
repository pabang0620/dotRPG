using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public static class SilverWarriorPresentation
    {
        static Material pointMaterial;
        static Sprite grip;
        static bool FlipBladeDrawn(Facing facing) => facing != Facing.Right && facing != Facing.Up && facing != Facing.UpRight;
        /// <summary>Mirrored facings flip the blade the opposite way of the facing they copy (their drawing is that one flipped).</summary>
        public static bool FlipBlade(Facing facing)
        {
            string twin = SilverWarriorArt.MirrorTwin(SilverWarriorArt.ViewKey(facing));
            return twin != null ? !FlipBladeDrawn(SilverWarriorArt.FacingOf(twin)) : FlipBladeDrawn(facing);
        }
        public static void ApplyMaterial(SpriteRenderer renderer)
        {
            if (pointMaterial == null) pointMaterial = new Material(Shader.Find("Sprites/Default"));
            renderer.sharedMaterial = pointMaterial;
        }
        public static Sprite Grip
        {
            get
            {
                if (grip != null) return grip;
                var t = new Texture2D(3, 3, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
                var p = new Color32[9];
                for (int i = 0; i < 9; i++) p[i] = PixelCanvas.Hex(i < 3 ? "#dfb6b1" : "#f5d6c1");
                t.SetPixels32(p); t.Apply();
                grip = Sprite.Create(t, new Rect(0, 0, 3, 3), new Vector2(.5f, .5f), SilverWarriorArt.Ppu);
                grip.name = "silver_grip"; return grip;
            }
        }
        public static void Pose(SpriteRenderer weapon, SpriteRenderer hand, CharacterAnimator animator, Facing facing, bool alive)
        {
            weapon.enabled = alive && weapon.sprite != null;
            hand.enabled = false; // [ART] the body drawing has its own hand; no code-drawn grip over the hilt
            if (!weapon.enabled || animator == null) return;
            string direction = SilverWarriorArt.ViewKey(facing), frame = animator.FrameKey;
            var anchor = SilverWarriorArt.Hand(direction, frame, animator.Look?.skinSheet, animator.Look?.id);
            float angle = SilverWarriorArt.Angle(direction, frame);
            weapon.transform.localPosition = anchor;
            weapon.transform.localRotation = Quaternion.Euler(0, 0, angle - 90);
            weapon.transform.localScale = Vector3.one * SilverWarriorArt.WeaponScale;
            weapon.flipX = FlipBlade(facing);
            ApplyMaterial(weapon);
            hand.transform.localPosition = anchor;
            // [ART] The drawn hand is on the near side in every view except from behind: the blade goes behind only facing up.
            int order = facing.IsUp() ? -1 : 1;
            weapon.sortingOrder = animator.Renderer.sortingOrder + order;
            hand.sortingOrder = animator.Renderer.sortingOrder + (order < 0 ? -1 : 2);
        }
        /// <summary>
        /// [ROGUE] The second dagger in the drawing's left hand: the same picture mirrored (flipped and its turn reversed),
        /// behind the body except when facing down, where both hands are in front.
        /// </summary>
        public static void PoseLeft(SpriteRenderer weapon, SpriteRenderer right, CharacterAnimator animator, Facing facing, bool alive)
        {
            weapon.enabled = alive && weapon.sprite != null && right != null && right.enabled;
            if (!weapon.enabled || animator == null) return;
            string direction = SilverWarriorArt.ViewKey(facing), frame = animator.FrameKey;
            float angle = SilverWarriorArt.Angle(direction, frame);
            weapon.transform.localPosition = SilverWarriorArt.HandLeft(direction, frame);
            weapon.transform.localRotation = Quaternion.Euler(0, 0, 90 - angle);
            weapon.transform.localScale = Vector3.one * SilverWarriorArt.WeaponScale;
            weapon.flipX = !FlipBlade(facing);
            ApplyMaterial(weapon);
            weapon.sortingOrder = animator.Renderer.sortingOrder + (facing == Facing.Down ? 1 : -1);
        }

        public static void Preview(Image character, Image weapon, string item, string frame, string skin = null, string lookId = null)
        {
            var body = character.sprite;
            // [ROGUE] The rogue sheets draw both daggers in the hands, so no weapon picture goes on top (same as in the world).
            if (SilverWarriorArt.IsRogue(lookId)) { weapon.enabled = false; return; }
            var sword = Game.Art.GetWarriorWeapon(item);
            weapon.enabled = body != null && sword != null;
            if (!weapon.enabled) return;
            weapon.sprite = sword;
            float scale;
            var origin = PreviewOrigin(character, body, out scale);
            var hand = SilverWarriorArt.Hand("down", frame, skin, lookId);
            var rt = weapon.rectTransform;
            rt.anchorMin = rt.anchorMax = character.rectTransform.anchorMin;
            rt.pivot = new Vector2(sword.pivot.x / sword.rect.width, sword.pivot.y / sword.rect.height);
            rt.sizeDelta = sword.rect.size * (body.pixelsPerUnit / sword.pixelsPerUnit) * scale * SilverWarriorArt.WeaponScale;
            rt.anchoredPosition = origin + hand * body.pixelsPerUnit * scale;
            rt.localRotation = Quaternion.Euler(0, 0, SilverWarriorArt.Angle("down", frame) - 90);
            rt.localScale = new Vector3(-1, 1, 1);
            rt.SetSiblingIndex(character.transform.GetSiblingIndex() + 1);
        }

        /// <summary>
        /// [FIX] Where the preview body's pivot (the feet) sits, in the character image's anchored space, and the UI pixels
        /// per art pixel. A preserveAspect image places the fitted sprite by its rect pivot (top for the bag's card), not
        /// in the middle of the box: assuming the middle put every held weapon about half the spare height too low.
        /// </summary>
        public static Vector2 PreviewOrigin(Image character, Sprite body, out float scale)
        {
            var crt = character.rectTransform;
            var box = crt.rect.size;
            scale = Mathf.Min(box.x / body.rect.width, box.y / body.rect.height);
            var drawn = body.rect.size * scale;
            var boxMin = crt.anchoredPosition - Vector2.Scale(crt.pivot, box);
            var drawnMin = boxMin + Vector2.Scale(box - drawn, crt.pivot);
            return drawnMin + body.pivot * scale;
        }
    }
}
