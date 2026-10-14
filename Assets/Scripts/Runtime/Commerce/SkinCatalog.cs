using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>The motes a costume skin leaves around its wearer (purely visual).</summary>
    public enum SkinTrailKind { GoldSparks, MoonMotes, Stars, Embers, None }

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
        /// <summary>Unique skins also leave a fading afterimage while moving; epic skins only have motes.</summary>
        public bool afterimage;
    }

    public static class SkinCatalog
    {
        static Color32 C(string hex) => PixelCanvas.Hex(hex);

        /// <summary>Common skin: the class body recoloured (Tools/art/skin_recolor.py), no motes.</summary>
        static SkinDef Plain(string id, string name, CharacterClass cls, string blurb) => new SkinDef
        {
            id = id, name = name, cls = cls, blurb = blurb, trail = SkinTrailKind.None,
            sheet = cls == CharacterClass.Warrior ? id.Substring("skin_".Length) : null,
        };

        /// <summary>Rare skin: a recoloured class body with a light mote trail.</summary>
        static SkinDef Tinted(string id, string name, CharacterClass cls, string blurb, Color a, Color b)
        {
            var s = Plain(id, name, cls, blurb);
            s.trail = SkinTrailKind.MoonMotes; s.trailA = a; s.trailB = b;
            return s;
        }

        public static readonly IReadOnlyList<SkinDef> All = new[]
        {
            Plain("skin_w_sand", L.T("모래빛 여행복"), CharacterClass.Warrior, L.T("먼 길을 걸어 온 모래빛 조끼와 바지.")),
            Plain("skin_w_moss", L.T("숲지기 옷"), CharacterClass.Warrior, L.T("숲을 지키는 이의 초록 조끼와 바지.")),
            Plain("skin_w_rust", L.T("적갈 견습복"), CharacterClass.Warrior, L.T("훈련장에서 닳은 적갈색 견습복.")),
            Plain("skin_m_ash", L.T("잿빛 견습 로브"), CharacterClass.Mage, L.T("마탑 견습생이 입는 잿빛 로브와 모자.")),
            Plain("skin_m_pilgrim", L.T("순례자의 로브"), CharacterClass.Mage, L.T("먼 길을 떠나는 순례자의 갈색 로브와 모자.")),
            Plain("skin_m_sprout", L.T("새싹 로브"), CharacterClass.Mage, L.T("봄 새싹 빛깔의 연둣빛 로브와 모자.")),
            Tinted("skin_w_crimson", L.T("진홍 용병복"), CharacterClass.Warrior, L.T("이름난 용병단의 진홍 조끼. 곁에 붉은 빛 조각이 떠오른다."), new Color(1f, .4f, .4f), new Color(1f, .85f, .8f)),
            Tinted("skin_w_amethyst", L.T("자수정 기사복"), CharacterClass.Warrior, L.T("자수정빛 기사 견습복. 곁에 보랏빛 빛 조각이 떠오른다."), new Color(.75f, .5f, 1f), new Color(.95f, .9f, 1f)),
            Tinted("skin_w_brass", L.T("황동 경비복"), CharacterClass.Warrior, L.T("성문 경비대의 황동빛 조끼. 곁에 금빛 빛 조각이 떠오른다."), new Color(1f, .8f, .4f), new Color(1f, .95f, .8f)),
            Tinted("skin_m_rose", L.T("장밋빛 로브"), CharacterClass.Mage, L.T("장밋빛 로브와 모자. 주위에 분홍 빛망울이 맴돈다."), new Color(1f, .55f, .75f), new Color(1f, .9f, .95f)),
            Tinted("skin_m_emerald", L.T("에메랄드 로브"), CharacterClass.Mage, L.T("에메랄드빛 로브와 모자. 주위에 초록 빛망울이 맴돈다."), new Color(.4f, 1f, .7f), new Color(.9f, 1f, .95f)),
            Tinted("skin_m_sunset", L.T("노을 로브"), CharacterClass.Mage, L.T("노을빛 로브와 모자. 주위에 주황 빛망울이 맴돈다."), new Color(1f, .6f, .3f), new Color(1f, .9f, .75f)),
            new SkinDef
            {
                id = "skin_lion", name = L.T("황금 사자 기사"), cls = CharacterClass.Warrior, sheet = "lion",
                blurb = L.T("금테 두른 은백 판금 갑옷과 진홍 망토, 금빛 서클릿. 걸음마다 금빛 불티가 흩날린다."),
                sleeve = C("#d1dae3"), leg = C("#bdc6d0"), boot = C("#8a96a6"), trim = C("#f6ab3e"), cape = C("#9c1d22"),
                trail = SkinTrailKind.GoldSparks, trailA = new Color(1f, .85f, .35f), trailB = new Color(1f, .97f, .8f), afterimage = true,
            },
            new SkinDef
            {
                id = "skin_moon", name = L.T("월광 검귀"), cls = CharacterClass.Warrior, sheet = "moon",
                blurb = L.T("청록 룬이 빛나는 남색 갑주와 별빛 망토. 달빛 조각이 곁을 맴돈다."),
                sleeve = C("#1d2547"), leg = C("#202a4e"), boot = C("#151b33"), trim = C("#6ff0ff"), cape = C("#1b2241"),
                trail = SkinTrailKind.MoonMotes, trailA = new Color(.45f, .95f, 1f), trailB = new Color(.9f, .97f, 1f), afterimage = true,
            },
            new SkinDef
            {
                id = "skin_starnight", name = L.T("성야의 마녀"), cls = CharacterClass.Mage,
                blurb = L.T("별자리가 수놓인 남빛 로브와 초승달 모자. 주위에 작은 별이 반짝인다."),
                trail = SkinTrailKind.Stars, trailA = new Color(1f, .9f, .55f), trailB = new Color(.75f, .9f, 1f), afterimage = true,
            },
            new SkinDef
            {
                id = "skin_crimson", name = L.T("홍염의 마녀"), cls = CharacterClass.Mage,
                blurb = L.T("금빛 불꽃 자수의 진홍 로브와 불사조 깃털. 발밑에서 불씨가 피어오른다."),
                trail = SkinTrailKind.Embers, trailA = new Color(1f, .45f, .12f), trailB = new Color(1f, .82f, .3f), afterimage = true,
            },
            new SkinDef
            {
                id = "skin_maple", name = L.T("단풍 무사"), cls = CharacterClass.Warrior, sheet = "maple",
                blurb = L.T("금빛 단풍 무늬 하오리와 옻칠 어깨받이. 걸을 때 단풍빛 불티가 인다."),
                sleeve = C("#bf512a"), leg = C("#522b22"), boot = C("#1c1a1a"), trim = C("#e8b84a"), cape = C("#bf512a"),
                trail = SkinTrailKind.Embers, trailA = new Color(1f, .5f, .18f), trailB = new Color(1f, .8f, .35f),
            },
            new SkinDef
            {
                // The warrior's first look (black bob, red eyes, black battle dress), kept as a skin when the default
                // became a plain novice warrior.
                id = "skin_noir", name = L.T("칠흑의 검희"), cls = CharacterClass.Warrior, sheet = "noir",
                blurb = L.T("검은 단발과 붉은 눈, 칠흑의 전투복. 곁에 붉은 빛 조각이 떠오른다."),
                sleeve = C("#23242a"), leg = C("#655861"), boot = C("#23242a"), trim = C("#71797e"),
                trail = SkinTrailKind.MoonMotes, trailA = new Color(1f, .35f, .4f), trailB = new Color(1f, .85f, .85f),
            },
            new SkinDef
            {
                id = "skin_obsidian", name = L.T("흑요 기사"), cls = CharacterClass.Warrior, sheet = "obsidian",
                blurb = L.T("은테 두른 흑요석 갑옷과 청록 보석. 곁에 푸른 빛 조각이 떠오른다."),
                sleeve = C("#343438"), leg = C("#3a3a40"), boot = C("#18181c"), trim = C("#c8d0d8"),
                trail = SkinTrailKind.MoonMotes, trailA = new Color(.35f, .9f, .85f), trailB = new Color(.85f, .95f, .95f),
            },
            new SkinDef
            {
                id = "skin_forest", name = L.T("숲의 정령"), cls = CharacterClass.Mage,
                blurb = L.T("덩굴 자수의 이끼빛 로브와 꽃 핀 모자. 주위에 연둣빛 빛망울이 맴돈다."),
                trail = SkinTrailKind.MoonMotes, trailA = new Color(.6f, 1f, .5f), trailB = new Color(1f, 1f, .85f),
            },
            new SkinDef
            {
                id = "skin_ice", name = L.T("얼음 여왕"), cls = CharacterClass.Mage,
                blurb = L.T("은빛 눈꽃 자수의 빙하빛 드레스와 얼음 티아라. 작은 눈송이가 반짝인다."),
                trail = SkinTrailKind.Stars, trailA = new Color(.75f, .92f, 1f), trailB = new Color(1f, 1f, 1f),
            },
        };

        public static SkinDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All) if (s.id == id) return s;
            return null;
        }

        /// <summary>
        /// [CAREER 2026-10-13] The base body of a promoted character with no skin on: the class body recoloured per career,
        /// shape unchanged (Tools/art/career_recolor.py). Warrior careers: SilverWarrior/Skins/career_&lt;career&gt;; mage
        /// careers: Art/char_career_&lt;career&gt;_&lt;dir&gt;_&lt;frame&gt;. Null before the career change.
        /// </summary>
        public static CharacterLook CareerLook(CharacterClass cls, Career career)
        {
            if (career == Career.None || CareerCatalog.Base(career) != cls) return null;
            string id = "career_" + career.ToString().ToLowerInvariant();
            if (cls == CharacterClass.Warrior)
            {
                var l = CharacterLook.Player.Clone();
                l.id = "player_" + id; // the silver warrior renderer handles every "player_*" look
                l.skinSheet = id;
                l.armor = ArmorStyle.None;
                l.bottomTier = -1;
                return l;
            }
            var m = CharacterLook.Mage.Clone();
            m.id = id;
            return m;
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
