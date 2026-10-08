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
        /// <summary>[FIELD BOSS] Growth options (not raised by enhancing): % more XP from monsters, % larger skill area.</summary>
        public int xpBonus, aoeBonus;
        /// <summary>[FIELD BOSS] Dropped only by a field boss: never in drops, cards, the cash shop or promotion.</summary>
        public bool bossOnly;
        /// <summary>Character level needed to put it on (the tier level: 1, 10, 15 ... 40). Owning and trading need none.</summary>
        public int reqLevel = 1;
        /// <summary>Icon used until the item's own icon is drawn (icon keys "gear:&lt;id&gt;", see SpriteLibrary).</summary>
        public string fallbackIcon;
        /// <summary>Index of the level tier in GearCatalog.TierLevels (0 = Lv.1 ... 7 = Lv.40).</summary>
        public int levelTier;

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
            if (xpBonus != 0) parts.Add($"<color=#8fe28f>경험치 +{xpBonus}%</color>");
            if (aoeBonus != 0) parts.Add($"<color=#8fe28f>스킬 범위 +{aoeBonus}%</color>");
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
}
