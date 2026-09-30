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
        /// <summary>전투력 weights of one point of each stat.</summary>
        public const int AttackWeight = 10, HealthWeight = 5, BlockWeight = 20, SpeedWeight = 10;

        public int attack, maxHealth, block, speed;
        /// <summary>The part of <see cref="attack"/> / <see cref="maxHealth"/> added by enhancement (rounded).</summary>
        public int enhanceAttack, enhanceHealth;
        /// <summary>
        /// Exact 전투력 of the enhancement bonus before rounding. <see cref="Score"/> counts it instead of the
        /// rounded bonus, so every +level raises 전투력 even while the rounded stat has not moved yet.
        /// </summary>
        public float enhanceScore;

        public int Score => Mathf.RoundToInt((attack - enhanceAttack) * AttackWeight + (maxHealth - enhanceHealth) * HealthWeight
                                             + block * BlockWeight + speed * SpeedWeight + enhanceScore);
    }

    /// <summary>
    /// One piece of equipment. Numbers use the game's internal units: attack and max health are the
    /// same HP / damage points as PlayerStats,
    /// block is a % chance to shrug off a hit, speed is a % bonus to walking speed.
    /// The bag and the slots hold instance keys of it: the id at +0, "{id}+{level}" when enhanced.
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
        /// <summary>Enhancement growth per reinforcement coefficient point (0 = default for the category and tier).</summary>
        public float enhanceSeed;

        /// <summary>Visual tier: the number at the end of the icon key (0 = weakest look), -1 = no icon.</summary>
        public int Tier { get; internal set; } = -1;
        /// <summary>Position in <see cref="EquipmentDatabase.All"/> (bag order).</summary>
        public int Order { get; internal set; }
        /// <summary>Instance key per level (index 0 = the bare id), filled by the database.</summary>
        internal string[] keys;

        public bool UsableBy(CharacterClass cls) => classOnly == null || classOnly == cls;

        public string CategoryName => EquipmentDatabase.CategoryName(category);

        /// <summary>Stats at an enhancement level (+0 = base).</summary>
        public GearStats StatsAt(int level) => EquipmentDatabase.StatsAt(this, level);

        /// <summary>Instance key at a level: "eq_sword_iron" (+0), "eq_sword_iron+12".</summary>
        public string KeyAt(int level) => EquipmentDatabase.KeyFor(id, level);

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

    /// <summary>
    /// All equipment and materials, the monster loot tables and the enhancement growth. Gear is stored
    /// as instance keys: the base id at +0 and "{baseId}+{level}" for +1..+20 ("eq_sword_iron+12"), so
    /// every piece carries its own +level and identical keys stack. Base ids never contain '+'.
    /// Attempt rules (chances, costs, failures) live in <see cref="EnhanceRules"/>.
    /// </summary>
    public static class EquipmentDatabase
    {
        public const int MaxEnhance = 20;
        /// <summary>Separates the base id from the +level in an instance key.</summary>
        public const char KeySeparator = '+';

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
                description = "푸르게 빛나는 광석. +5 강화 시도부터 필요하다." },
            new MaterialItem { id = "mat_essence", name = "마력 정수", iconKey = "maticon_essence", rarity = ItemRarity.Epic, dropChance = 0.08f,
                description = "해골 속에 남아 있던 마력의 결정. +11 강화 시도부터 필요하다." },
        };

        /// <summary>A valid instance key: which item and which +level.</summary>
        struct KeyInfo
        {
            public EquipmentItem item;
            public int level;
        }

        /// <summary>Every valid key (21 per item), so lookups never parse or allocate.</summary>
        static readonly Dictionary<string, KeyInfo> ByKey = BuildIndex();

        static Dictionary<string, KeyInfo> BuildIndex()
        {
            var map = new Dictionary<string, KeyInfo>();
            for (int i = 0; i < Items.Count; i++)
            {
                var item = Items[i];
                item.Order = i;
                item.keys = new string[MaxEnhance + 1];
                for (int level = 0; level <= MaxEnhance; level++)
                {
                    string key = level == 0 ? item.id : $"{item.id}{KeySeparator}{level}";
                    item.keys[level] = key;
                    map[key] = new KeyInfo { item = item, level = level };
                }
            }
            return map;
        }

        static EquipmentItem W(string id, string name, EquipCategory cat, ItemRarity rarity, CharacterClass? cls,
            int atk = 0, int hp = 0, int block = 0, int spd = 0, string icon = "", string desc = "", bool starter = false, int drop = 0, float seed = 0f)
        {
            return new EquipmentItem
            {
                id = id, name = name, category = cat, rarity = rarity, classOnly = cls,
                attack = atk, maxHealth = hp, block = block, speed = spd,
                iconKey = icon, description = desc, starter = starter, dropWeight = starter ? 0 : drop,
                enhanceSeed = seed, Tier = TierFromIcon(icon),
            };
        }

        static int TierFromIcon(string iconKey)
        {
            if (string.IsNullOrEmpty(iconKey)) return -1;
            int u = iconKey.LastIndexOf('_');
            return u >= 0 && int.TryParse(iconKey.Substring(u + 1), out int tier) ? tier : 0;
        }

        public static IReadOnlyList<EquipmentItem> All => Items;
        public static IReadOnlyList<MaterialItem> AllMaterials => Materials;

        // ---------- Instance keys ----------

        /// <summary>True for any valid gear key ("eq_sword_iron", "eq_sword_iron+12").</summary>
        public static bool IsEquipment(string key) => !string.IsNullOrEmpty(key) && ByKey.ContainsKey(key);

        /// <summary>The base item of a key (null for anything that is not a valid gear key).</summary>
        public static EquipmentItem Get(string key) => key != null && ByKey.TryGetValue(key, out var info) ? info.item : null;

        /// <summary>"eq_sword_iron+12" → "eq_sword_iron". Anything else comes back unchanged (up to a '+').</summary>
        public static string BaseId(string key)
        {
            if (string.IsNullOrEmpty(key)) return key;
            if (ByKey.TryGetValue(key, out var info)) return info.item.id;
            int plus = key.IndexOf(KeySeparator);
            return plus >= 0 ? key.Substring(0, plus) : key;
        }

        /// <summary>+level of a key ("eq_sword_iron+12" → 12); 0 for bare ids and anything that is not gear.</summary>
        public static int LevelOfKey(string key) => key != null && ByKey.TryGetValue(key, out var info) ? info.level : 0;

        /// <summary>Key of an item at a level (clamped to 0..<see cref="MaxEnhance"/>; +0 = the bare id). Also accepts a key.</summary>
        public static string KeyFor(string baseId, int level)
        {
            if (string.IsNullOrEmpty(baseId)) return baseId;
            level = Mathf.Clamp(level, 0, MaxEnhance);
            if (ByKey.TryGetValue(baseId, out var info)) return info.item.keys[level];
            string root = BaseId(baseId);
            return level > 0 ? $"{root}{KeySeparator}{level}" : root;
        }

        /// <summary>
        /// Repairs odd spellings of a gear key from old or hand-edited saves: "eq_sword_iron+0" → "eq_sword_iron",
        /// "+05" → "+5", past <see cref="MaxEnhance"/> → +20. Valid keys and anything else come back unchanged.
        /// </summary>
        public static string Canonicalize(string key)
        {
            if (string.IsNullOrEmpty(key) || ByKey.ContainsKey(key)) return key;
            int plus = key.IndexOf(KeySeparator);
            if (plus <= 0) return key;
            if (!ByKey.TryGetValue(key.Substring(0, plus), out var info) || info.level != 0) return key;
            string digits = key.Substring(plus + 1);
            if (digits.Length == 0) return info.item.id;
            foreach (char ch in digits) if (ch < '0' || ch > '9') return key;
            // Long digit runs are simply "past the max".
            int level = digits.TrimStart('0').Length > 3 ? MaxEnhance : int.Parse(digits, System.Globalization.CultureInfo.InvariantCulture);
            return info.item.keys[Mathf.Clamp(level, 0, MaxEnhance)];
        }

        /// <summary>"철검 +12" for a key (null when it is not gear).</summary>
        public static string NameOfKey(string key)
        {
            var item = Get(key);
            return item != null ? item.NameAt(LevelOfKey(key)) : null;
        }

        /// <summary>Name in the grade colour with the +level in its level colour (rich text).</summary>
        public static string RichName(string key)
        {
            var item = Get(key);
            if (item == null) return key;
            int level = LevelOfKey(key);
            string name = $"<color={RarityColor(item.rarity)}>{item.name}</color>";
            return level > 0 ? $"{name} {LevelTag(level)}" : name;
        }

        /// <summary>Stats of a key at its own +level (default for null / non-gear).</summary>
        public static GearStats StatsOfKey(string key)
        {
            var item = Get(key);
            return item != null ? StatsAt(item, LevelOfKey(key)) : default;
        }

        /// <summary>Bag order of gear keys: <see cref="All"/> order, then the higher +level first. Non-gear sorts last.</summary>
        public static int CompareKeys(string a, string b)
        {
            var ia = Get(a);
            var ib = Get(b);
            if (ia == null || ib == null) return (ia == null).CompareTo(ib == null);
            int byItem = ia.Order.CompareTo(ib.Order);
            return byItem != 0 ? byItem : LevelOfKey(b).CompareTo(LevelOfKey(a));
        }

        /// <summary>The gear keys held in an inventory, in bag order (a new list, safe to change the inventory while using it).</summary>
        public static List<string> GearKeys(Inventory bag)
        {
            var keys = new List<string>();
            foreach (var id in bag.Ids)
                if (IsEquipment(id)) keys.Add(id);
            keys.Sort(CompareKeys);
            return keys;
        }

        /// <summary>Visual tier of an item key (the number at the end of its icon key), or -1 for none.</summary>
        public static int TierOf(string key)
        {
            var item = Get(key);
            return item != null ? item.Tier : -1;
        }

        /// <summary>Sprite of the weapon held in the hand: "wpn_sword_2" for the bone greatsword, etc.</summary>
        public static string WeaponSprite(string weaponKey, CharacterClass cls)
        {
            string kind = cls == CharacterClass.Mage ? "staff" : "sword";
            int tier = Mathf.Max(0, TierOf(weaponKey));
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

        // ---------- Enhancement level colours ----------

        /// <summary>
        /// Colour of a "+N" label, in the bands of Dungeon&amp;Fighter's weapon glow:
        /// +1–6 green, +7–8 yellow, +9–12 blue, +13–14 pink, +15–16 orange, +17 and up gold.
        /// </summary>
        public static string LevelColor(int level)
        {
            if (level <= 6) return "#6fdc6f";
            if (level <= 8) return "#ffe066";
            if (level <= 12) return "#5aa9ff";
            if (level <= 14) return "#ff7ad9";
            if (level <= 16) return "#ff8a3d";
            return "#ffd84a";
        }

        public static Color LevelTint(int level)
        {
            ColorUtility.TryParseHtmlString(LevelColor(level), out var c);
            return c;
        }

        /// <summary>"+12" in its level colour (rich text).</summary>
        public static string LevelTag(int level) => $"<color={LevelColor(level)}>+{level}</color>";

        // ---------- Enhancement growth ----------

        /// <summary>
        /// Reinforcement coefficient per +level, after Dungeon&amp;Fighter's weapon reinforcement table
        /// (+8..+20 are the published values, +1..+7 interpolated). Bonus at +L = seed × coefficient[L].
        /// </summary>
        static readonly float[] EnhanceCoef =
        {
            0f, 1.1f, 2.2f, 3.3f, 4.5f, 5.7f, 7.0f, 8.3f, 11.11f, 14.7f, 18.9f,
            27.25f, 37.13f, 43.43f, 49.8f, 56.11f, 62.38f, 68.59f, 74.77f, 80.9f, 86.98f,
        };

        public static float EnhanceCoefficient(int level) => EnhanceCoef[Mathf.Clamp(level, 0, MaxEnhance)];

        /// <summary>
        /// Growth per coefficient point: the item's own <see cref="EquipmentItem.enhanceSeed"/>, or the default
        /// for its category and tier t: weapon 0.135 × (t + 1) (≈ +50% of base + weapon attack at +12),
        /// top / bottom 0.5 + 0.25 t, necklace / ring 0.3 + 0.15 t.
        /// </summary>
        public static float EnhanceSeedOf(EquipmentItem item)
        {
            if (item.enhanceSeed > 0f) return item.enhanceSeed;
            int t = Mathf.Max(0, item.Tier);
            switch (item.category)
            {
                case EquipCategory.Weapon: return 0.135f * (t + 1);
                case EquipCategory.Top:
                case EquipCategory.Bottom: return 0.5f + 0.25f * t;
                default: return 0.3f + 0.15f * t;
            }
        }

        /// <summary>
        /// Stats at an enhancement level: weapons gain attack, everything else max HP, both
        /// round(seed × coefficient[level]). Block, speed and accessory attack never grow.
        /// </summary>
        public static GearStats StatsAt(EquipmentItem item, int level)
        {
            var s = new GearStats { attack = item.attack, maxHealth = item.maxHealth, block = item.block, speed = item.speed };
            level = Mathf.Clamp(level, 0, MaxEnhance);
            if (level == 0) return s;
            float exact = EnhanceSeedOf(item) * EnhanceCoef[level];
            int bonus = Mathf.FloorToInt(exact + 0.5f);
            if (item.category == EquipCategory.Weapon)
            {
                s.attack += bonus;
                s.enhanceAttack = bonus;
                s.enhanceScore = exact * GearStats.AttackWeight;
            }
            else
            {
                s.maxHealth += bonus;
                s.enhanceHealth = bonus;
                s.enhanceScore = exact * GearStats.HealthWeight;
            }
            return s;
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
