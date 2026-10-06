using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// The equipment table (Docs/PLAN_GEAR_RENEWAL.md): 8 level tiers (Lv.1, 10, 15 ... 40, the tier level is the equip
    /// level) x grades (Lv.1: common to rare, the rest common to unique, legendary at Lv.20 and Lv.40) x 8 kinds
    /// (warrior sword / plate / greaves, mage staff / robe / skirt, shared necklace / ring). Names follow each tier's
    /// region theme; stats are the tier's rare baseline x a grade multiplier. Ids: eq_{kind}_{level}_{grade}.
    /// </summary>
    public static class GearCatalog
    {
        public static readonly int[] TierLevels = { 1, 10, 15, 20, 25, 30, 35, 40 };
        /// <summary>Rare-grade baselines per tier: weapon attack and body-armour HP.</summary>
        static readonly int[] WeaponBase = { 6, 10, 14, 18, 23, 28, 34, 40 };
        static readonly int[] HealthBase = { 20, 40, 55, 70, 90, 110, 135, 160 };
        static readonly int[] BlockBase = { 3, 4, 5, 6, 7, 8, 9, 10 };

        static readonly string[] GradeIds = { "c", "u", "r", "e", "un", "l" };
        static float GradeMul(ItemRarity r) => r switch
        {
            ItemRarity.Common => 0.6f,
            ItemRarity.Uncommon => 0.8f,
            ItemRarity.Rare => 1f,
            ItemRarity.Epic => 1.25f,
            // [BALANCE 2026-10-06] Unique and legendary are a clear jump over epic: one piece alone changes a fight.
            ItemRarity.Unique => 2.3f,
            _ => 3.2f,
        };
        /// <summary>Block grows slower than attack and HP (it is a % chance, capped at 75).</summary>
        static float BlockMul(ItemRarity r) => r == ItemRarity.Unique ? 1.8f : r == ItemRarity.Legendary ? 2.3f : GradeMul(r);
        /// <summary>Walking speed % of leg armour (and rings) by grade.</summary>
        static int SpeedOf(ItemRarity r) => r switch { ItemRarity.Rare => 2, ItemRarity.Epic => 4, ItemRarity.Unique => 8, ItemRarity.Legendary => 12, _ => 0 };

        enum Kind { Sword, Staff, Plate, Greaves, Robe, Skirt, Neck, Ring }

        static readonly Kind[] Kinds = { Kind.Sword, Kind.Plate, Kind.Greaves, Kind.Staff, Kind.Robe, Kind.Skirt, Kind.Neck, Kind.Ring };

        static string KindId(Kind k) => k switch
        {
            Kind.Sword => "sword", Kind.Staff => "staff", Kind.Plate => "plate", Kind.Greaves => "greaves",
            Kind.Robe => "robe", Kind.Skirt => "skirt", Kind.Neck => "neck", _ => "ring",
        };

        // Names: [tier][grade c,u,r,e,un]; Lv.1 has three.
        static readonly string[][] SwordNames =
        {
            new[] { "무딘 철검", "견습 기사의 검", "마을 수비대 장검" },
            new[] { "녹슨 사냥검", "뼈손잡이 장검", "해골 숲 사냥검", "망령 베기", "묘지기의 처형검" },
            new[] { "적석 단검", "붉은 돌 장검", "고개지기 대검", "협곡 파쇄검", "붉은 거인의 송곳니" },
            new[] { "기사단 연습검", "강철 장검", "기사단 정예검", "해골왕 토벌검", "기사단장의 대검" },
            new[] { "서리솔 단검", "서리솔 장검", "설원 사냥꾼의 검", "서리 수호검", "얼어붙은 숲의 왕검" },
            new[] { "얼음날 검", "빙결 장검", "호숫가 기사검", "빙하 절단검", "얼음 여제의 칼날" },
            new[] { "눈보라 단검", "은빛 장검", "봉우리 파수검", "눈보라 가르는 검", "설산 군주의 대검" },
            new[] { "별철 장검", "성광 장검", "성좌 기사검", "유성 대검", "북극성의 맹세" },
        };
        static readonly string[][] StaffNames =
        {
            new[] { "견습 지팡이", "자작나무 지팡이", "마을 현자의 지팡이" },
            new[] { "마른 가지 지팡이", "뼈 장식 지팡이", "해골 숲 주술봉", "망령 부르는 지팡이", "묘지기의 영혼 지팡이" },
            new[] { "적석 지팡이", "붉은 수정 지팡이", "고개 현자의 지팡이", "용암 맥동 지팡이", "붉은 거인의 심장봉" },
            new[] { "기사단 견습 지팡이", "강철 테 지팡이", "기사단 사제 지팡이", "해골왕 봉인봉", "대사제의 성광 지팡이" },
            new[] { "서리솔 가지", "서리솔 지팡이", "설원 무녀의 지팡이", "서리꽃 지팡이", "얼어붙은 숲의 정령봉" },
            new[] { "얼음 막대", "빙결 지팡이", "호수 현자의 지팡이", "빙하 결정 지팡이", "얼음 여제의 홀" },
            new[] { "눈보라 지팡이", "은빛 지팡이", "봉우리 예언자의 지팡이", "눈보라 부르는 지팡이", "설산 마녀의 홀" },
            new[] { "별철 지팡이", "성광 지팡이", "성좌 마도사의 지팡이", "유성 지팡이", "북극성의 홀" },
        };
        static readonly string[][] PlateNames =
        {
            new[] { "누빔 조끼", "가죽 흉갑", "수비대 사슬 갑옷" },
            new[] { "뼈 비늘 조끼", "뼈판 흉갑", "해골 숲 사냥꾼 갑옷", "망령 수호 갑옷", "묘지기의 판금 갑옷" },
            new[] { "적석 가죽 갑옷", "붉은 돌 흉갑", "고개지기 판금 갑옷", "협곡 파수 갑옷", "붉은 거인의 갑주" },
            new[] { "기사단 훈련복", "강철 흉갑", "기사단 정예 갑옷", "해골왕 토벌 갑주", "기사단장의 판금 갑옷" },
            new[] { "서리솔 가죽 갑옷", "서리솔 흉갑", "설원 사냥꾼 갑옷", "서리 수호 갑주", "얼어붙은 숲의 왕갑" },
            new[] { "얼음 비늘 조끼", "빙결 흉갑", "호숫가 기사 갑옷", "빙하 판금 갑옷", "얼음 여제의 갑주" },
            new[] { "눈보라 가죽 갑옷", "은빛 흉갑", "봉우리 파수 갑옷", "눈보라 가르는 갑주", "설산 군주의 갑옷" },
            new[] { "별철 흉갑", "성광 흉갑", "성좌 기사 갑옷", "유성 판금 갑옷", "북극성의 갑주" },
        };
        static readonly string[][] GreavesNames =
        {
            new[] { "천 각반", "가죽 각반", "수비대 사슬 각반" },
            new[] { "뼈 비늘 각반", "뼈판 각반", "해골 숲 사냥꾼 각반", "망령 수호 각반", "묘지기의 판금 각반" },
            new[] { "적석 가죽 각반", "붉은 돌 각반", "고개지기 판금 각반", "협곡 파수 각반", "붉은 거인의 각반" },
            new[] { "기사단 훈련 각반", "강철 각반", "기사단 정예 각반", "해골왕 토벌 각반", "기사단장의 각반" },
            new[] { "서리솔 가죽 각반", "서리솔 각반", "설원 사냥꾼 각반", "서리 수호 각반", "얼어붙은 숲의 왕 각반" },
            new[] { "얼음 비늘 각반", "빙결 각반", "호숫가 기사 각반", "빙하 판금 각반", "얼음 여제의 각반" },
            new[] { "눈보라 가죽 각반", "은빛 각반", "봉우리 파수 각반", "눈보라 가르는 각반", "설산 군주의 각반" },
            new[] { "별철 각반", "성광 각반", "성좌 기사 각반", "유성 판금 각반", "북극성의 각반" },
        };
        static readonly string[][] RobeNames =
        {
            new[] { "견습생 로브", "자작나무 수 로브", "마을 현자의 로브" },
            new[] { "잿빛 로브", "뼈 장식 로브", "해골 숲 주술사 로브", "망령 부르는 로브", "묘지기의 영혼 법의" },
            new[] { "적석 로브", "붉은 수정 로브", "고개 현자의 로브", "용암 맥동 로브", "붉은 거인의 법의" },
            new[] { "기사단 견습 법의", "강철 실 로브", "기사단 사제 로브", "해골왕 봉인 법의", "대사제의 성광 법의" },
            new[] { "서리솔 로브", "서리솔 자수 로브", "설원 무녀의 로브", "서리꽃 로브", "얼어붙은 숲의 정령 법의" },
            new[] { "얼음실 로브", "빙결 로브", "호수 현자의 로브", "빙하 결정 로브", "얼음 여제의 법의" },
            new[] { "눈보라 로브", "은빛 로브", "봉우리 예언자의 로브", "눈보라 부르는 로브", "설산 마녀의 법의" },
            new[] { "별실 로브", "성광 로브", "성좌 마도사의 로브", "유성 로브", "북극성의 법의" },
        };
        static readonly string[][] SkirtNames =
        {
            new[] { "견습생 치마", "자작나무 수 치마", "마을 현자의 치마" },
            new[] { "잿빛 치마", "뼈 장식 치마", "해골 숲 주술사 치마", "망령 부르는 치마", "묘지기의 영혼 치마" },
            new[] { "적석 치마", "붉은 수정 치마", "고개 현자의 치마", "용암 맥동 치마", "붉은 거인의 치마" },
            new[] { "기사단 견습 치마", "강철 실 치마", "기사단 사제 치마", "해골왕 봉인 치마", "대사제의 성광 치마" },
            new[] { "서리솔 치마", "서리솔 자수 치마", "설원 무녀의 치마", "서리꽃 치마", "얼어붙은 숲의 정령 치마" },
            new[] { "얼음실 치마", "빙결 치마", "호수 현자의 치마", "빙하 결정 치마", "얼음 여제의 치마" },
            new[] { "눈보라 치마", "은빛 치마", "봉우리 예언자의 치마", "눈보라 부르는 치마", "설산 마녀의 치마" },
            new[] { "별실 치마", "성광 치마", "성좌 마도사의 치마", "유성 치마", "북극성의 치마" },
        };
        static readonly string[][] NeckNames =
        {
            new[] { "끈 목걸이", "조개 목걸이", "마을 수호 부적" },
            new[] { "뼈 구슬 목걸이", "해골 이빨 목걸이", "해골 숲 부적", "망령 결속 목걸이", "묘지기의 목걸이" },
            new[] { "적석 목걸이", "붉은 수정 목걸이", "고개지기 부적", "용암 심장 목걸이", "붉은 거인의 목걸이" },
            new[] { "기사단 인식표", "강철 사슬 목걸이", "기사단 문장 목걸이", "해골왕 봉인 부적", "해골왕의 목걸이" },
            new[] { "솔방울 목걸이", "서리솔 목걸이", "설원 사냥꾼 부적", "서리꽃 목걸이", "얼어붙은 숲의 정령석" },
            new[] { "얼음 구슬 목걸이", "빙결 목걸이", "호수 현자의 부적", "빙하 결정 목걸이", "얼음 여제의 목걸이" },
            new[] { "눈송이 목걸이", "은빛 목걸이", "봉우리 파수 부적", "눈보라 결정 목걸이", "설산 군주의 목걸이" },
            new[] { "별철 목걸이", "성광 목걸이", "성좌 부적", "유성 목걸이", "북극성의 목걸이" },
        };
        static readonly string[][] RingNames =
        {
            new[] { "구리 반지", "은 반지", "마을 수호 반지" },
            new[] { "뼈 반지", "해골 이빨 반지", "해골 숲 사냥 반지", "망령 결속 반지", "묘지기의 반지" },
            new[] { "적석 반지", "붉은 수정 반지", "고개지기 반지", "용암 심장 반지", "붉은 거인의 루비 반지" },
            new[] { "기사단 반지", "강철 반지", "기사단 문장 반지", "해골왕 봉인 반지", "기사단장의 반지" },
            new[] { "솔잎 반지", "서리솔 반지", "설원 사냥꾼 반지", "서리 바람 반지", "얼어붙은 숲의 정령 반지" },
            new[] { "얼음 반지", "빙결 반지", "호수 현자의 반지", "빙하 결정 반지", "얼음 여제의 반지" },
            new[] { "눈송이 반지", "은빛 반지", "봉우리 파수 반지", "눈보라 결정 반지", "설산 군주의 반지" },
            new[] { "별철 반지", "성광 반지", "성좌 반지", "유성 반지", "북극성의 반지" },
        };
        /// <summary>Legendary names: [Lv.20, Lv.40].</summary>
        static string[] LegendNames(Kind k) => k switch
        {
            Kind.Sword => new[] { "용골 대검", "창세의 별검" },
            Kind.Staff => new[] { "별의 지팡이", "창세의 별 지팡이" },
            Kind.Plate => new[] { "용골 판금 갑옷", "창세의 별 갑주" },
            Kind.Greaves => new[] { "용골 각반", "창세의 별 각반" },
            Kind.Robe => new[] { "별빛 마도 법의", "창세의 별 법의" },
            Kind.Skirt => new[] { "별빛 마도 치마", "창세의 별 치마" },
            Kind.Neck => new[] { "용의 눈 목걸이", "창세의 별 목걸이" },
            _ => new[] { "용의 심장 반지", "창세의 별 반지" },
        };

        static string[][] NamesOf(Kind k) => k switch
        {
            Kind.Sword => SwordNames, Kind.Staff => StaffNames, Kind.Plate => PlateNames, Kind.Greaves => GreavesNames,
            Kind.Robe => RobeNames, Kind.Skirt => SkirtNames, Kind.Neck => NeckNames, _ => RingNames,
        };

        static EquipCategory CategoryOf(Kind k) => k switch
        {
            Kind.Sword or Kind.Staff => EquipCategory.Weapon,
            Kind.Plate or Kind.Robe => EquipCategory.Top,
            Kind.Greaves or Kind.Skirt => EquipCategory.Bottom,
            Kind.Neck => EquipCategory.Necklace,
            _ => EquipCategory.Ring,
        };

        static CharacterClass? ClassOf(Kind k) => k switch
        {
            Kind.Sword or Kind.Plate or Kind.Greaves => CharacterClass.Warrior,
            Kind.Staff or Kind.Robe or Kind.Skirt => CharacterClass.Mage,
            _ => null,
        };

        /// <summary>
        /// Stand-in art until each item's own icon exists (Art/Gear/eqicon_{id}): the old icon set by tier band. Weapons
        /// keep 0-3 (held sprite cells), tops 0-2, bottoms 0-1, accessories 0-2.
        /// </summary>
        static int ArtTier(Kind k, int tier, ItemRarity r)
        {
            int band = r == ItemRarity.Legendary ? 3 : tier <= 0 ? 0 : tier <= 3 ? 1 : tier <= 5 ? 2 : 3;
            switch (CategoryOf(k))
            {
                case EquipCategory.Weapon: return Mathf.Max(1, band);
                case EquipCategory.Bottom: return Mathf.Min(1, band);
                default: return Mathf.Min(2, band);
            }
        }

        static string OldIcon(Kind k, int art) => k switch
        {
            Kind.Sword => $"eqicon_sword_{art}",
            Kind.Staff => $"eqicon_staff_{art}",
            Kind.Plate or Kind.Robe => $"eqicon_top_{art}",
            Kind.Greaves or Kind.Skirt => $"eqicon_bot_{art}",
            Kind.Neck => $"eqicon_neck_{art}",
            _ => $"eqicon_ring_{art}",
        };

        /// <summary>The highest tier at or below a level (0 = Lv.1 ... 7 = Lv.40), same as the server's tierOfLevel.</summary>
        public static int TierOfLevel(int level)
        {
            int t = 0;
            for (int i = 0; i < TierLevels.Length; i++) if (TierLevels[i] <= level) t = i;
            return t;
        }

        public static string Id(string kind, int level, ItemRarity r) => $"eq_{kind}_{level}_{GradeIds[(int)r]}";

        public static List<EquipmentItem> Build()
        {
            var list = new List<EquipmentItem>();
            foreach (var k in Kinds)
            {
                var names = NamesOf(k);
                for (int t = 0; t < TierLevels.Length; t++)
                {
                    int grades = t == 0 ? 3 : 5;
                    for (int g = 0; g < grades; g++) list.Add(Make(k, t, (ItemRarity)g, names[t][g]));
                    if (TierLevels[t] == 20 || TierLevels[t] == 40)
                        list.Add(Make(k, t, ItemRarity.Legendary, LegendNames(k)[TierLevels[t] == 20 ? 0 : 1]));
                }
            }
            return list;
        }

        static EquipmentItem Make(Kind k, int t, ItemRarity r, string name)
        {
            float m = GradeMul(r);
            int w = WeaponBase[t], h = HealthBase[t], b = BlockBase[t];
            int atk = 0, hp = 0, block = 0, spd = 0, xp = 0, aoe = 0;
            switch (k)
            {
                case Kind.Sword:
                case Kind.Staff:
                    atk = Mathf.RoundToInt(w * m);
                    if (r == ItemRarity.Unique) aoe = 5; else if (r == ItemRarity.Legendary) aoe = 12;
                    break;
                case Kind.Plate: hp = Mathf.RoundToInt(h * m); block = Mathf.RoundToInt(b * BlockMul(r)); break;
                case Kind.Greaves: hp = Mathf.RoundToInt(h * 0.6f * m); spd = SpeedOf(r); break;
                case Kind.Robe: hp = Mathf.RoundToInt(h * 0.7f * m); atk = Mathf.RoundToInt(w * 0.25f * m); break;
                case Kind.Skirt: hp = Mathf.RoundToInt(h * 0.45f * m); spd = SpeedOf(r); break;
                case Kind.Neck:
                    atk = Mathf.RoundToInt(w * 0.3f * m); hp = Mathf.RoundToInt(h * 0.3f * m);
                    if (r == ItemRarity.Unique) xp = 5; else if (r == ItemRarity.Legendary) xp = 10;
                    break;
                default:
                    atk = Mathf.RoundToInt(w * 0.35f * m); spd = r >= ItemRarity.Epic ? SpeedOf(r) - 1 : 0;
                    if (r == ItemRarity.Unique) aoe = 8; else if (r == ItemRarity.Legendary) aoe = 14;
                    break;
            }
            int level = TierLevels[t];
            int art = ArtTier(k, t, r);
            return new EquipmentItem
            {
                id = Id(KindId(k), level, r), name = name, category = CategoryOf(k), rarity = r, classOnly = ClassOf(k),
                attack = atk, maxHealth = hp, block = block, speed = spd, xpBonus = xp, aoeBonus = aoe,
                iconKey = "gear:" + Id(KindId(k), level, r), fallbackIcon = OldIcon(k, art), description = Describe(k, level, r),
                dropWeight = r <= ItemRarity.Rare ? 30 - 8 * (int)r : r == ItemRarity.Epic ? 3 : 0,
                reqLevel = level, levelTier = t, Tier = art,
            };
        }

        static string Describe(Kind k, int level, ItemRarity r)
        {
            string who = ClassOf(k) == CharacterClass.Warrior ? "전사 전용" : ClassOf(k) == CharacterClass.Mage ? "마법사 전용" : "모든 직업";
            return $"Lv.{level} 장비 · {who}";
        }

        /// <summary>
        /// Old equipment (before the renewal) -> new id, for saves and the server migration. Body armour depends on the
        /// wearer's class (warrior plate / mage robe).
        /// </summary>
        public static string Legacy(string oldId, CharacterClass cls)
        {
            bool mage = cls == CharacterClass.Mage;
            switch (oldId)
            {
                case "eq_sword_iron": return "eq_sword_10_u";
                case "eq_sword_bone": return "eq_sword_15_e";
                case "eq_sword_dragon": return "eq_sword_20_l";
                case "eq_staff_crystal": return "eq_staff_10_u";
                case "eq_staff_moon": return "eq_staff_15_e";
                case "eq_staff_star": return "eq_staff_20_l";
                case "eq_neck_leaf": return "eq_neck_1_c";
                case "eq_neck_bone": return "eq_neck_10_r";
                case "eq_neck_king": return "eq_neck_20_un";
                case "eq_ring_copper": return "eq_ring_1_c";
                case "eq_ring_wind": return "eq_ring_10_r";
                case "eq_ring_ruby": return "eq_ring_10_un";
                case "eq_top_cloth": return mage ? "eq_robe_1_c" : "eq_plate_1_c";
                case "eq_top_leather": return mage ? "eq_robe_1_u" : "eq_plate_1_u";
                case "eq_top_iron": return mage ? "eq_robe_10_e" : "eq_plate_10_e";
                case "eq_bot_cloth": return mage ? "eq_skirt_1_c" : "eq_greaves_1_c";
                case "eq_bot_leather": return mage ? "eq_skirt_1_u" : "eq_greaves_1_u";
                default: return null;
            }
        }

        public static readonly string[] LegacyIds =
        {
            "eq_sword_iron", "eq_sword_bone", "eq_sword_dragon", "eq_staff_crystal", "eq_staff_moon", "eq_staff_star",
            "eq_neck_leaf", "eq_neck_bone", "eq_neck_king", "eq_ring_copper", "eq_ring_wind", "eq_ring_ruby",
            "eq_top_cloth", "eq_top_leather", "eq_top_iron", "eq_bot_cloth", "eq_bot_leather",
        };
    }
}
