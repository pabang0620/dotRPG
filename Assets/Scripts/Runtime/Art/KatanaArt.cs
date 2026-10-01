using System;
using UnityEngine;

namespace DotRPG
{
    public static class KatanaArt
    {
        public const float Ppu = 40;
        public const float GripY = 11.5f;
        static Texture2D sheet;
        public static bool Handles(string key) => key.StartsWith("wpn_sword_") || key.StartsWith("eqicon_sword_") || key == "tool_sword";
        public static Sprite Get(string key)
        {
            if (sheet == null) sheet = Resources.Load<Texture2D>("SilverWarrior/katanas");
            if (sheet == null) throw new InvalidOperationException("Katana sheet has not been baked.");
            int tier = key == "tool_sword" ? 1 : Mathf.Clamp(key[key.Length - 1] - '0', 0, 3);
            bool icon = key.StartsWith("eqicon");
            var sprite = Sprite.Create(sheet, new Rect(tier * 64, 0, 64, 64), new Vector2(.5f, icon ? .5f : GripY / 64), Ppu, 0, SpriteMeshType.FullRect);
            sprite.name = "katana_" + key;
            return sprite;
        }
    }
}
