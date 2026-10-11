using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public static class SilverWarriorPresentation
    {
        static Material pointMaterial;
        static Sprite grip;
        public static bool FlipBlade(Facing facing) => facing != Facing.Right && facing != Facing.Up && facing != Facing.UpRight;
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
            var anchor = SilverWarriorArt.Hand(direction, frame);
            float angle = SilverWarriorArt.Angle(direction, frame);
            weapon.transform.localPosition = anchor;
            weapon.transform.localRotation = Quaternion.Euler(0, 0, angle - 90);
            weapon.transform.localScale = Vector3.one * SilverWarriorArt.WeaponScale;
            weapon.flipX = FlipBlade(facing);
            ApplyMaterial(weapon);
            hand.transform.localPosition = anchor;
            int order = SilverWarriorArt.Pose(direction, frame).rightHandBack ? -1 : 1;
            weapon.sortingOrder = animator.Renderer.sortingOrder + order;
            hand.sortingOrder = animator.Renderer.sortingOrder + (order < 0 ? -1 : 2);
        }
        public static void Preview(Image character, Image weapon, string item, string frame)
        {
            var body = character.sprite;
            var sword = Game.Art.GetWarriorWeapon(item);
            weapon.enabled = body != null && sword != null;
            if (!weapon.enabled) return;
            weapon.sprite = sword;
            var box = character.rectTransform.rect.size;
            float scale = Mathf.Min(box.x / body.rect.width, box.y / body.rect.height);
            var center = character.rectTransform.anchoredPosition + new Vector2(0, -box.y * .5f);
            float feet = center.y - body.rect.height * scale * .5f + body.pivot.y * scale;
            var hand = SilverWarriorArt.Hand("down", frame);
            var rt = weapon.rectTransform;
            rt.anchorMin = rt.anchorMax = character.rectTransform.anchorMin;
            rt.pivot = new Vector2(sword.pivot.x / sword.rect.width, sword.pivot.y / sword.rect.height);
            rt.sizeDelta = sword.rect.size * (body.pixelsPerUnit / sword.pixelsPerUnit) * scale * SilverWarriorArt.WeaponScale;
            rt.anchoredPosition = new Vector2(center.x + hand.x * body.pixelsPerUnit * scale, feet + hand.y * body.pixelsPerUnit * scale);
            rt.localRotation = Quaternion.Euler(0, 0, SilverWarriorArt.Angle("down", frame) - 90);
            rt.localScale = new Vector3(-1, 1, 1);
            rt.SetSiblingIndex(character.transform.GetSiblingIndex() + 1);
        }
    }
}
