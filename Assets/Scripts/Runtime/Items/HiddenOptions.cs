namespace DotRPG
{
    /// <summary>[HIDDEN 2026-10-29] One hidden option a unique / legendary weapon, top or bottom can carry (Docs/PLAN_GEAR_HIDDEN_2026-10-29.md).</summary>
    public enum HiddenOption
    {
        None,
        /// <summary>Weapon: this weapon's attack (with its enhancement) +30%.</summary>
        Damage,
        /// <summary>Weapon: skill / attack area +30 (the same Aoe stat as 스킬 범위).</summary>
        Range,
        /// <summary>Weapon: attack speed +15 (shorter cooldowns, like the passive tree's attack speed).</summary>
        AttackSpeed,
        /// <summary>Weapon: 10% chance per hit to deal x1.5.</summary>
        Crit,
        /// <summary>Top / bottom: this piece's max HP (with its enhancement) +30%.</summary>
        MaxHealth,
        /// <summary>Top / bottom: damage taken x0.85.</summary>
        DamageTaken,
        /// <summary>Top / bottom: move speed +10 (gear speed points).</summary>
        MoveSpeed,
    }

    /// <summary>
    /// [HIDDEN] Hidden gear options. Only the server rolls them (2% when a unique / legendary weapon, top or bottom is created by
    /// a drop); the key carries the option as a last "~letter": "eq_sword_20_un+0~d", "eq_sword_30_un+12^2~c" (+0 always written).
    /// Enhancing, promoting and inheriting keep it. Letters and numbers match server/src/utils/hiddenOption.ts.
    /// </summary>
    public static class HiddenOptions
    {
        public const char Separator = '~';
        public const int DamagePct = 30, RangePct = 30, AttackSpeedPct = 15, CritPct = 10, MaxHealthPct = 30, DamageTakenPct = 15, MoveSpeedPct = 10;
        /// <summary>Damage of a critical hit (x1.5).</summary>
        public const float CritMul = 1.5f;
        /// <summary>Text colour of the hidden option (teal, apart from every grade and +level colour).</summary>
        public const string Hex = "#3ff2d2";

        public static char CodeOf(HiddenOption o)
        {
            switch (o)
            {
                case HiddenOption.Damage: return 'd';
                case HiddenOption.Range: return 'r';
                case HiddenOption.AttackSpeed: return 's';
                case HiddenOption.Crit: return 'c';
                case HiddenOption.MaxHealth: return 'h';
                case HiddenOption.DamageTaken: return 'g';
                case HiddenOption.MoveSpeed: return 'm';
                default: return '\0';
            }
        }

        public static HiddenOption FromCode(char c)
        {
            switch (c)
            {
                case 'd': return HiddenOption.Damage;
                case 'r': return HiddenOption.Range;
                case 's': return HiddenOption.AttackSpeed;
                case 'c': return HiddenOption.Crit;
                case 'h': return HiddenOption.MaxHealth;
                case 'g': return HiddenOption.DamageTaken;
                case 'm': return HiddenOption.MoveSpeed;
                default: return HiddenOption.None;
            }
        }

        static bool IsWeaponOption(HiddenOption o) => o == HiddenOption.Damage || o == HiddenOption.Range || o == HiddenOption.AttackSpeed || o == HiddenOption.Crit;
        static bool IsArmorOption(HiddenOption o) => o == HiddenOption.MaxHealth || o == HiddenOption.DamageTaken || o == HiddenOption.MoveSpeed;

        /// <summary>Unique / legendary weapons take the weapon options, unique / legendary tops and bottoms the armour options.</summary>
        public static bool Allowed(EquipmentItem item, HiddenOption o)
        {
            if (item == null || o == HiddenOption.None || item.rarity < ItemRarity.Unique) return false;
            if (item.category == EquipCategory.Weapon) return IsWeaponOption(o);
            if (item.category == EquipCategory.Top || item.category == EquipCategory.Bottom) return IsArmorOption(o);
            return false;
        }

        /// <summary>The option of a gear key (None for plain keys and non-gear).</summary>
        public static HiddenOption Of(string key) => EquipmentDatabase.HiddenOfKey(key);

        /// <summary>"피해 +30%" and so on (translated).</summary>
        public static string Label(HiddenOption o)
        {
            switch (o)
            {
                case HiddenOption.Damage: return L.F("피해 +{0}%", DamagePct);
                case HiddenOption.Range: return L.F("공격 범위 +{0}%", RangePct);
                case HiddenOption.AttackSpeed: return L.F("공격 속도 +{0}%", AttackSpeedPct);
                case HiddenOption.Crit: return L.F("치명타 확률 +{0}%p", CritPct);
                case HiddenOption.MaxHealth: return L.F("최대 체력 +{0}%", MaxHealthPct);
                case HiddenOption.DamageTaken: return L.F("받는 피해 -{0}%", DamageTakenPct);
                case HiddenOption.MoveSpeed: return L.F("이동 속도 +{0}%", MoveSpeedPct);
                default: return "";
            }
        }

        /// <summary>"히든: 피해 +30%" in the hidden colour (rich text), or "" for none.</summary>
        public static string Tag(HiddenOption o) => o == HiddenOption.None ? "" : "<color=" + Hex + ">" + L.F("히든: {0}", Label(o)) + "</color>";

        /// <summary>Same for a key.</summary>
        public static string TagOf(string key) => Tag(Of(key));
    }

    /// <summary>[HIDDEN] The critical hit of the hidden weapon option (the only crit source): chance from the attacker's worn gear.</summary>
    public static class HiddenCrit
    {
        public static int Roll(PlayerController attacker, int amount)
        {
            int chance = attacker != null && attacker.Data != null ? attacker.Data.Stats.CritChance : 0;
            if (chance <= 0 || UnityEngine.Random.Range(0, 100) >= chance) return amount;
            return UnityEngine.Mathf.Max(1, UnityEngine.Mathf.RoundToInt(amount * HiddenOptions.CritMul));
        }
    }
}
