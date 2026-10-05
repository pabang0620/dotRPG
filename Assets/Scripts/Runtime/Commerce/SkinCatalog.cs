using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>The motes a costume skin leaves around its wearer (purely visual).</summary>
    public enum SkinTrailKind { GoldSparks, MoonMotes, Stars, Embers }

    /// <summary>
    /// One costume skin: a full repaint of a class's body in every direction and frame. Warrior skins swap the
    /// silver warrior sheets (SilverWarrior/Skins/&lt;sheet&gt;) and recolour the limbs the warrior draws in code;
    /// mage skins are their own frame set (Art/char_&lt;id&gt;_&lt;dir&gt;_&lt;frame&gt;). No stats, no hitbox change.
    /// </summary>
    public sealed class SkinDef
    {
        public string id, name, blurb, sheet;
        public CharacterClass cls;
        public Color32 sleeve, leg, boot, trim, cape;
        public SkinTrailKind trail;
        public Color trailA, trailB;
    }

    public static class SkinCatalog
    {
        static Color32 C(string hex) => PixelCanvas.Hex(hex);

        public static readonly IReadOnlyList<SkinDef> All = new[]
        {
            new SkinDef
            {
                id = "skin_lion", name = "황금 사자 기사", cls = CharacterClass.Warrior, sheet = "lion",
                blurb = "백금 갑옷에 진홍 망토, 황금 월계관. 걸음마다 금빛 불티가 흩날린다.",
                sleeve = C("#cdbb93"), leg = C("#d8caa6"), boot = C("#b8892e"), trim = C("#ffd86b"), cape = C("#9c1d22"),
                trail = SkinTrailKind.GoldSparks, trailA = new Color(1f, .85f, .35f), trailB = new Color(1f, .97f, .8f),
            },
            new SkinDef
            {
                id = "skin_moon", name = "월광 검귀", cls = CharacterClass.Warrior, sheet = "moon",
                blurb = "청록 룬이 빛나는 남색 갑주와 별빛 망토. 달빛 조각이 곁을 맴돈다.",
                sleeve = C("#1d2547"), leg = C("#202a4e"), boot = C("#151b33"), trim = C("#6ff0ff"), cape = C("#1b2241"),
                trail = SkinTrailKind.MoonMotes, trailA = new Color(.45f, .95f, 1f), trailB = new Color(.9f, .97f, 1f),
            },
            new SkinDef
            {
                id = "skin_starnight", name = "성야의 마녀", cls = CharacterClass.Mage,
                blurb = "별자리가 수놓인 남빛 로브와 초승달 모자. 주위에 작은 별이 반짝인다.",
                trail = SkinTrailKind.Stars, trailA = new Color(1f, .9f, .55f), trailB = new Color(.75f, .9f, 1f),
            },
            new SkinDef
            {
                id = "skin_crimson", name = "홍염의 마녀", cls = CharacterClass.Mage,
                blurb = "금빛 불꽃 자수의 진홍 로브와 불사조 깃털. 발밑에서 불씨가 피어오른다.",
                trail = SkinTrailKind.Embers, trailA = new Color(1f, .45f, .12f), trailB = new Color(1f, .82f, .3f),
            },
        };

        public static SkinDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All) if (s.id == id) return s;
            return null;
        }

        /// <summary>The body look of a class wearing a skin (null when the skin doesn't fit the class).</summary>
        public static CharacterLook LookFor(CharacterClass cls, string skinId)
        {
            var s = Find(skinId);
            if (s == null || s.cls != cls) return null;
            if (cls == CharacterClass.Warrior)
            {
                var l = CharacterLook.Player.Clone();
                l.id = "player_" + s.id; // the silver warrior renderer handles every "player_*" look
                l.skinSheet = s.sheet;
                l.armor = ArmorStyle.None;
                l.bottomTier = -1;
                l.skinSleeve = s.sleeve; l.skinLeg = s.leg; l.skinBoot = s.boot; l.skinTrim = s.trim; l.skinCape = s.cape;
                return l;
            }
            var m = CharacterLook.Mage.Clone();
            m.id = s.id; // Art/char_<skin>_<dir>_<frame>
            return m;
        }
    }
}
