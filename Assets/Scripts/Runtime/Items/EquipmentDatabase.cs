using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>What kind of equipment an item is (which slot(s) it fits).</summary>
    public enum EquipCategory
    {
        Weapon,
        Necklace,
        Ring,
        Top,
        Bottom,
    }

    /// <summary>The six equipment slots on the character.</summary>
    public enum EquipSlot
    {
        Weapon = 0,
        Necklace = 1,
        Ring1 = 2,
        Ring2 = 3,
        Top = 4,
        Bottom = 5,
    }

    /// <summary>Item grades, weakest to strongest: 커먼 → 언커먼 → 레어 → 에픽 → 유니크 → 레전더리.</summary>
    public enum ItemRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Unique,
        Legendary,
    }

    /// <summary>Stat block of a piece of gear (base + enhancement).</summary>
    public struct GearStats
    {
        public int attack, maxHealth, block, speed;

        public int Score => attack * 10 + maxHealth * 5 + block * 20 + speed * 10;
    }

    /// <summary>
    /// One piece of equipment. Numbers use the game's internal units: attack and max health are the
    /// same HP / damage points as PlayerStats,
    /// block is a % chance to shrug off a hit, speed is a % bonus to walking speed.
    /// </summary>
    public sealed class EquipmentItem
    {
        public string id;
        public string name;
        public EquipCategory category;
        public ItemRarity rarity;
        /// <summary>Null = any class.</summary>
        public CharacterClass? classOnly;
        public int attack;
        public int maxHealth;
        public int block;
        public int speed;
        public string iconKey;
        public string description;
        /// <summary>Starting gear is never dropped by monsters.</summary>
        public bool starter;
        /// <summary>Relative drop weight (0 = never drops).</summary>
        public int dropWeight;

        public bool UsableBy(CharacterClass cls) => classOnly == null || classOnly == cls;

        public string CategoryName => EquipmentDatabase.CategoryName(category);

        /// <summary>Stats at an enhancement level (+0 = base).</summary>
        public GearStats StatsAt(int level) => EquipmentDatabase.StatsAt(this, level);

        /// <summary>"철검 +3" (no suffix at +0).</summary>
        public string NameAt(int level) => level > 0 ? $"{name} +{level}" : name;

        /// <summary>"공격 +10  체력 +♥1" style one-liner (attack shown ×10 like damage numbers).</summary>
        public string StatLine(int level = 0)
        {
            var s = StatsAt(level);
            var parts = new List<string>();
            if (s.attack != 0) parts.Add($"공격 +{s.attack * DamageNumber.DisplayScale}");
            if (s.maxHealth != 0) parts.Add($"체력 +{EquipmentDatabase.Hearts(s.maxHealth)}");
            if (s.block != 0) parts.Add($"막기 {s.block}%");
            if (s.speed != 0) parts.Add($"이동 {(s.speed > 0 ? "+" : "")}{s.speed}%");
            return parts.Count > 0 ? string.Join("  ", parts) : "능력치 없음";
        }
    }

    /// <summary>Enhancement material dropped by monsters (shown in the bag's "기타" tab).</summary>
    public sealed class MaterialItem
    {
        public string id, name, iconKey, description;
        public ItemRarity rarity;
        /// <summary>Chance per monster kill.</summary>
        public float dropChance;
        public int minDrop = 1, maxDrop = 1;
    }

    /// <summary>All equipment and materials, the monster loot tables and the enhancement rules.</summary>
    public static class EquipmentDatabase
    {
        public const int MaxEnhance = 10;

        static readonly List<EquipmentItem> Items = new List<EquipmentItem>
        {
            // Weapons — warrior.
            W("eq_sword_wood", "나무 검", EquipCategory.Weapon, ItemRarity.Common, CharacterClass.Warrior, atk: 0, icon: "eqicon_sword_0", desc: "연습용 나무 검. 처음 받은 장비.", starter: true),
            W("eq_sword_iron", "철검", EquipCategory.Weapon, ItemRarity.Uncommon, CharacterClass.Warrior, atk: 10, icon: "eqicon_sword_1", desc: "잘 벼린 철검. 해골을 두 번 만에 쓰러뜨린다.", drop: 30),
            W("eq_sword_bone", "해골 대검", EquipCategory.Weapon, ItemRarity.Epic, CharacterClass.Warrior, atk: 20, hp: 10, icon: "eqicon_sword_2", desc: "해골 왕의 뼈로 만든 대검. 한 방에 해골을 부순다.", drop: 6),
            W("eq_sword_dragon", "용골 대검", EquipCategory.Weapon, ItemRarity.Legendary, CharacterClass.Warrior, atk: 30, hp: 20, icon: "eqicon_sword_3", desc: "고대 용의 뼈를 벼린 전설의 검. 휘두를 때마다 불꽃이 인다.", drop: 1),
            // Weapons — mage.
            W("eq_staff_oak", "참나무 지팡이", EquipCategory.Weapon, ItemRarity.Common, CharacterClass.Mage, atk: 0, icon: "eqicon_staff_0", desc: "견습 마법사의 지팡이. 처음 받은 장비.", starter: true),
            W("eq_staff_crystal", "수정 지팡이", EquipCategory.Weapon, ItemRarity.Uncommon, CharacterClass.Mage, atk: 10, icon: "eqicon_staff_1", desc: "푸른 수정이 마력을 모아 준다.", drop: 30),
            W("eq_staff_moon", "달빛 지팡이", EquipCategory.Weapon, ItemRarity.Epic, CharacterClass.Mage, atk: 20, spd: 5, icon: "eqicon_staff_2", desc: "달빛을 머금은 지팡이. 가볍고 강력하다.", drop: 6),
            W("eq_staff_star", "별의 지팡이", EquipCategory.Weapon, ItemRarity.Legendary, CharacterClass.Mage, atk: 30, spd: 10, icon: "eqicon_staff_3", desc: "떨어진 별 조각을 박아 넣은 전설의 지팡이.", drop: 1),
            // Necklaces.
            W("eq_neck_leaf", "잎사귀 목걸이", EquipCategory.Necklace, ItemRarity.Common, null, hp: 20, icon: "eqicon_neck_0", desc: "숲의 기운이 몸을 지켜 준다.", drop: 25),
            W("eq_neck_bone", "뼈 목걸이", EquipCategory.Necklace, ItemRarity.Rare, null, atk: 10, icon: "eqicon_neck_1", desc: "해골의 투지가 깃든 목걸이.", drop: 12),
            W("eq_neck_king", "해골왕의 목걸이", EquipCategory.Necklace, ItemRarity.Unique, null, atk: 10, hp: 20, block: 5, icon: "eqicon_neck_2", desc: "해골 무리를 다스리던 왕의 증표.", drop: 2),
            // Rings (two can be worn).
            W("eq_ring_copper", "구리 반지", EquipCategory.Ring, ItemRarity.Common, null, hp: 10, block: 5, icon: "eqicon_ring_0", desc: "흔한 구리 반지. 조금 든든하다.", drop: 30),
            W("eq_ring_wind", "바람 반지", EquipCategory.Ring, ItemRarity.Rare, null, spd: 12, icon: "eqicon_ring_1", desc: "발걸음이 바람처럼 가벼워진다.", drop: 14),
            W("eq_ring_ruby", "루비 반지", EquipCategory.Ring, ItemRarity.Unique, null, atk: 10, hp: 10, icon: "eqicon_ring_2", desc: "붉게 타오르는 보석. 촌장이 준 보물.", drop: 2),
            // Armour — tops.
            W("eq_top_cloth", "천 조끼", EquipCategory.Top, ItemRarity.Common, null, hp: 20, block: 5, icon: "eqicon_top_0", desc: "튼튼한 천으로 만든 조끼.", drop: 25),
            W("eq_top_leather", "가죽 갑옷", EquipCategory.Top, ItemRarity.Uncommon, null, hp: 20, block: 15, icon: "eqicon_top_1", desc: "질긴 가죽 갑옷. 공격을 잘 막아 낸다.", drop: 12),
            W("eq_top_iron", "철 흉갑", EquipCategory.Top, ItemRarity.Epic, null, hp: 40, block: 15, spd: -5, icon: "eqicon_top_2", desc: "무겁지만 무척 단단한 흉갑.", drop: 4),
            // Armour — bottoms.
            W("eq_bot_cloth", "천 바지", EquipCategory.Bottom, ItemRarity.Common, null, hp: 10, block: 5, icon: "eqicon_bot_0", desc: "움직이기 편한 천 바지.", drop: 25),
            W("eq_bot_leather", "가죽 바지", EquipCategory.Bottom, ItemRarity.Uncommon, null, hp: 20, block: 10, spd: 5, icon: "eqicon_bot_1", desc: "가볍고 질긴 가죽 바지.", drop: 12),
        };

        static readonly List<MaterialItem> Materials = new List<MaterialItem>
        {
            new MaterialItem { id = "mat_bone", name = "뼈 조각", iconKey = "maticon_bone", rarity = ItemRarity.Common, dropChance = 0.75f, minDrop = 1, maxDrop = 3,
                description = "해골이 떨어뜨린 단단한 뼈. 모든 강화에 쓰인다." },
            new MaterialItem { id = "mat_ore", name = "강화석", iconKey = "maticon_ore", rarity = ItemRarity.Rare, dropChance = 0.35f,
                description = "푸르게 빛나는 광석. 장비를 단단하게 벼린다." },
            new MaterialItem { id = "mat_essence", name = "마력 정수", iconKey = "maticon_essence", rarity = ItemRarity.Epic, dropChance = 0.08f,
                description = "해골 속에 남아 있던 마력의 결정. +5 이상 강화에 필요하다." },
        };

        static readonly Dictionary<string, EquipmentItem> ById = BuildIndex();

        static Dictionary<string, EquipmentItem> BuildIndex()
        {
            var map = new Dictionary<string, EquipmentItem>();
            foreach (var item in Items) map[item.id] = item;
            return map;
        }

        static EquipmentItem W(string id, string name, EquipCategory cat, ItemRarity rarity, CharacterClass? cls,
            int atk = 0, int hp = 0, int block = 0, int spd = 0, string icon = "", string desc = "", bool starter = false, int drop = 0)
        {
            return new EquipmentItem
            {
                id = id, name = name, category = cat, rarity = rarity, classOnly = cls,
                attack = atk, maxHealth = hp, block = block, speed = spd,
                iconKey = icon, description = desc, starter = starter, dropWeight = starter ? 0 : drop,
            };
        }

        public static IReadOnlyList<EquipmentItem> All => Items;
        public static IReadOnlyList<MaterialItem> AllMaterials => Materials;

        public static bool IsEquipment(string id) => !string.IsNullOrEmpty(id) && ById.ContainsKey(id);

        public static EquipmentItem Get(string id) => id != null && ById.TryGetValue(id, out var item) ? item : null;

        /// <summary>Visual tier of an item (the number at the end of its icon key), or -1 for none.</summary>
        public static int TierOf(string id)
        {
            var item = Get(id);
            if (item == null || string.IsNullOrEmpty(item.iconKey)) return -1;
            int u = item.iconKey.LastIndexOf('_');
            return u >= 0 && int.TryParse(item.iconKey.Substring(u + 1), out int tier) ? tier : 0;
        }

        /// <summary>Sprite of the weapon held in the hand: "wpn_sword_2" for the bone greatsword, etc.</summary>
        public static string WeaponSprite(string weaponId, CharacterClass cls)
        {
            string kind = cls == CharacterClass.Mage ? "staff" : "sword";
            int tier = Mathf.Max(0, TierOf(weaponId));
            return $"wpn_{kind}_{tier}";
        }

        public static MaterialItem GetMaterial(string id)
        {
            foreach (var m in Materials) if (m.id == id) return m;
            return null;
        }

        public static string StarterWeapon(CharacterClass cls) => cls == CharacterClass.Mage ? "eq_staff_oak" : "eq_sword_wood";

        public static bool Fits(EquipCategory category, EquipSlot slot)
        {
            switch (category)
            {
                case EquipCategory.Weapon: return slot == EquipSlot.Weapon;
                case EquipCategory.Necklace: return slot == EquipSlot.Necklace;
                case EquipCategory.Ring: return slot == EquipSlot.Ring1 || slot == EquipSlot.Ring2;
                case EquipCategory.Top: return slot == EquipSlot.Top;
                default: return slot == EquipSlot.Bottom;
            }
        }

        public static string CategoryName(EquipCategory c)
        {
            switch (c)
            {
                case EquipCategory.Weapon: return "무기";
                case EquipCategory.Necklace: return "목걸이";
                case EquipCategory.Ring: return "반지";
                case EquipCategory.Top: return "상의";
                default: return "하의";
            }
        }

        public static string SlotName(EquipSlot s)
        {
            switch (s)
            {
                case EquipSlot.Weapon: return "무기";
                case EquipSlot.Necklace: return "목걸이";
                case EquipSlot.Ring1: return "반지 1";
                case EquipSlot.Ring2: return "반지 2";
                case EquipSlot.Top: return "상의";
                default: return "하의";
            }
        }

        // ---------- Grades ----------

        static readonly string[] RarityHex = { "#d9d9d9", "#6fdc6f", "#5aa9ff", "#b77bff", "#ffd84a", "#ff8a3d" };
        static readonly string[] RarityNames = { "커먼", "언커먼", "레어", "에픽", "유니크", "레전더리" };

        public static string RarityColor(ItemRarity r) => RarityHex[(int)r];

        public static Color RarityTint(ItemRarity r)
        {
            ColorUtility.TryParseHtmlString(RarityHex[(int)r], out var c);
            return c;
        }

        public static string RarityName(ItemRarity r) => RarityNames[(int)r];

        /// <summary>HP amount as text (kept under its old name for callers).</summary>
        public static string Hearts(int hp) => hp.ToString();

        // ---------- Enhancement ----------

        /// <summary>
        /// Stats at an enhancement level. Every level raises something; each category grows differently:
        /// weapon: attack +1 on even levels, block +1% on odd levels ·
        /// armour: block +1% every level, hearts +½ on even levels ·
        /// necklace: hearts +½ on even levels, block +1% on odd levels, attack +1 at +5/+10 ·
        /// ring: block +1% on even levels, speed +1% on odd levels, attack +1 at +4/+8.
        /// </summary>
        public static GearStats StatsAt(EquipmentItem item, int level)
        {
            level = Mathf.Clamp(level, 0, MaxEnhance);
            int even = level / 2, odd = (level + 1) / 2;
            var s = new GearStats { attack = item.attack, maxHealth = item.maxHealth, block = item.block, speed = item.speed };
            switch (item.category)
            {
                case EquipCategory.Weapon: s.attack += even * 10; s.block += odd; break;
                case EquipCategory.Top:
                case EquipCategory.Bottom: s.maxHealth += even * 10; s.block += level; break;
                case EquipCategory.Necklace: s.maxHealth += even * 10; s.block += odd; s.attack += level / 5 * 10; break;
                case EquipCategory.Ring: s.block += even; s.speed += odd; s.attack += level / 4 * 10; break;
            }
            return s;
        }

        public struct EnhanceCost
        {
            public int bone, ore, essence;
            public int successPercent;
        }

        /// <summary>Materials and success chance to go from +level to +level+1. Failure keeps the level.</summary>
        public static EnhanceCost CostFor(EquipmentItem item, int level)
        {
            int grade = (int)item.rarity; // better gear costs a little more
            return new EnhanceCost
            {
                bone = 2 + level + grade / 2,
                ore = 1 + level / 2 + (grade >= 3 ? 1 : 0),
                essence = level >= 4 ? level - 3 : 0,
                successPercent = level < 3 ? 100 : Mathf.Max(30, 100 - (level - 2) * 10),
            };
        }

        // ---------- Loot ----------

        /// <summary>
        /// Rolls an equipment drop for the given class (weapons of the other class never drop).
        /// Returns null when nothing drops.
        /// </summary>
        public static string RollDrop(CharacterClass cls, float chance)
        {
            if (Random.value > chance) return null;
            int total = 0;
            foreach (var item in Items)
                if (item.dropWeight > 0 && item.UsableBy(cls)) total += item.dropWeight;
            if (total <= 0) return null;
            int roll = Random.Range(0, total);
            foreach (var item in Items)
            {
                if (item.dropWeight <= 0 || !item.UsableBy(cls)) continue;
                if (roll < item.dropWeight) return item.id;
                roll -= item.dropWeight;
            }
            return null;
        }
    }
}
