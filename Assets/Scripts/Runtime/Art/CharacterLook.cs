using System;
using UnityEngine;

namespace DotRPG
{
    public enum BodyKind
    {
        Human,
        Skeleton,
    }

    public enum HairStyle
    {
        Short,
        Spiky,
        Long,
        Bun,
        Curly,
        Bald,
    }

    public enum HatKind
    {
        None,
        Straw,
        Cap,
        Bandana,
        /// <summary>Tall pointed mage hat.</summary>
        Wizard,
    }

    /// <summary>Worn top drawn over the torso.</summary>
    public enum ArmorStyle
    {
        None,
        /// <summary>천 조끼: cloth vest panels, shirt showing in the middle.</summary>
        Vest,
        /// <summary>가죽 갑옷: leather with a chest strap and belt.</summary>
        Leather,
        /// <summary>철 흉갑: iron breastplate with shoulder plates.</summary>
        Plate,
    }

    /// <summary>
    /// Parameters for the placeholder character generator. The <see cref="id"/> is also the sprite
    /// key prefix: drop "Resources/Art/char_{id}_{down|up|side}_{frame}.png" into the project to
    /// replace the generated frames with hand-made art (see SpriteLibrary).
    /// </summary>
    [Serializable]
    public class CharacterLook
    {
        public string id = "villager";
        public BodyKind body = BodyKind.Human;
        public HairStyle hairStyle = HairStyle.Short;
        public HatKind hat = HatKind.None;
        public Color32 skin = new Color32(247, 196, 146, 255);
        public Color32 hair = new Color32(120, 70, 40, 255);
        public Color32 shirt = new Color32(214, 64, 58, 255);
        public Color32 pants = new Color32(62, 72, 110, 255);
        public Color32 hatColor = new Color32(226, 184, 94, 255);
        [Tooltip("Long robe over the legs (mage). Uses the shirt colour.")]
        public bool robe;
        [Tooltip("Worn top drawn over the torso.")]
        public ArmorStyle armor;
        public Color32 armorColor;
        [Tooltip("Robe skirt colour (alpha 0 = same as the shirt).")]
        public Color32 robeColor;

        public CharacterLook() { }

        public CharacterLook Clone() => (CharacterLook)MemberwiseClone();

        /// <summary>
        /// The look with the worn top and bottom applied, so changing clothes shows on the character.
        /// Necklaces and rings are not drawn. Returns the base look when nothing is worn.
        /// </summary>
        public static CharacterLook WithGear(CharacterLook baseLook, string topId, string bottomId)
        {
            int top = EquipmentDatabase.TierOf(topId), bottom = EquipmentDatabase.TierOf(bottomId);
            if (top < 0 && bottom < 0) return baseLook;
            var l = baseLook.Clone();
            l.id = $"{baseLook.id}_t{top}_b{bottom}";
            switch (top)
            {
                case 0: l.armor = ArmorStyle.Vest; l.armorColor = C(201, 169, 120); break;
                case 1: l.armor = ArmorStyle.Leather; l.armorColor = C(138, 90, 50); break;
                case 2: l.armor = ArmorStyle.Plate; l.armorColor = C(184, 194, 206); break;
            }
            switch (bottom)
            {
                case 0: l.pants = C(168, 138, 90); if (l.robe) l.robeColor = C(150, 118, 78); break;
                case 1: l.pants = C(96, 60, 36); if (l.robe) l.robeColor = C(110, 70, 42); break;
            }
            return l;
        }

        public CharacterLook(string id, HairStyle hairStyle, Color32 skin, Color32 hair, Color32 shirt, Color32 pants,
            HatKind hat = HatKind.None, Color32 hatColor = default)
        {
            this.id = id;
            this.hairStyle = hairStyle;
            this.skin = skin;
            this.hair = hair;
            this.shirt = shirt;
            this.pants = pants;
            this.hat = hat;
            this.hatColor = hatColor.a == 0 ? new Color32(226, 184, 94, 255) : hatColor;
        }

        static Color32 C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

        static readonly Color32 SkinLight = C(250, 205, 160);
        static readonly Color32 SkinTan = C(222, 160, 110);
        static readonly Color32 SkinDark = C(150, 96, 62);

        public static CharacterLook Player => new CharacterLook("player", HairStyle.Spiky, SkinLight, C(214, 108, 48), C(64, 132, 214), C(70, 62, 92));

        /// <summary>Selectable mage: violet robe, pointed indigo hat, long silver hair.</summary>
        public static CharacterLook Mage => new CharacterLook("mage", HairStyle.Long, SkinLight, C(214, 214, 236), C(116, 70, 190), C(70, 44, 130),
            HatKind.Wizard, C(58, 72, 170))
        {
            robe = true,
        };

        public static CharacterLook Skeleton => new CharacterLook
        {
            id = "skeleton",
            body = BodyKind.Skeleton,
            hairStyle = HairStyle.Bald,
            skin = C(240, 232, 212),
            hair = C(240, 232, 212),
            shirt = C(240, 232, 212),
            pants = C(200, 190, 170),
        };

        public static CharacterLook Chief => new CharacterLook("chief", HairStyle.Bald, SkinTan, C(232, 232, 232), C(126, 84, 160), C(88, 64, 52));
        public static CharacterLook Farmer => new CharacterLook("farmer", HairStyle.Short, SkinLight, C(200, 90, 40), C(232, 160, 60), C(70, 90, 140), HatKind.Straw);
        public static CharacterLook Fisher => new CharacterLook("fisher", HairStyle.Bun, SkinLight, C(230, 120, 40), C(60, 150, 190), C(60, 70, 110));
        public static CharacterLook Builder => new CharacterLook("builder", HairStyle.Curly, SkinDark, C(60, 40, 30), C(200, 60, 50), C(70, 60, 50));
        public static CharacterLook Lumberjack => new CharacterLook("lumberjack", HairStyle.Short, SkinTan, C(90, 50, 30), C(180, 50, 50), C(60, 60, 80));
        public static CharacterLook Miner => new CharacterLook("miner", HairStyle.Short, SkinTan, C(60, 40, 30), C(120, 110, 90), C(80, 70, 60), HatKind.Straw, C(240, 200, 120));
        public static CharacterLook Carrier => new CharacterLook("carrier", HairStyle.Curly, SkinDark, C(40, 30, 25), C(90, 160, 90), C(90, 70, 60), HatKind.Bandana, C(210, 60, 60));
    }
}
