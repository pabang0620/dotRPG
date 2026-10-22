namespace DotRPG
{
    /// <summary>Playable characters the player picks on the character-select screen.</summary>
    public enum CharacterClass
    {
        /// <summary>The original hero: sword swing in front of the body.</summary>
        Warrior,
        /// <summary>Casts a magic bolt that flies forward and hits the first target.</summary>
        Mage,
        /// <summary>[ROGUE] Twin daggers: quick narrow stabs, a damaging dash (appended: saves store the id string).</summary>
        Rogue,
    }

    /// <summary>Display data and combat numbers per class. Warrior values come from PlayerStats.</summary>
    public sealed class CharacterClassInfo
    {
        public CharacterClass id;
        public string saveId;
        public string displayName;
        public string description;
        public string weaponSprite;
        public bool ranged;

        // Mage bolt tuning (ignored by melee classes).
        public float boltSpeed = 9f;
        public float boltRange = 6.5f;
        public float boltRadius = 0.32f;
        public float cooldown = 0.38f;     // 0.5 -> 0.38: faster orbs (each hit is weaker, PlayerCombat.BasicHitScale)
        public float castDuration = 0.2f; // 0.26 -> 0.2
        public int damage = 10;
        public float knockback = 4f;

        // [ROGUE] Melee basic attack per class (Docs/PLAN_CLASS_ROGUE.md 1.4). A value below 0 keeps the PlayerStats
        // number (the warrior), so the warrior's swing is unchanged.
        public float basicHitScale = 0.8f;
        public float meleeReach = -1f, meleeRadius = -1f, meleeKnockback = -1f;
        /// <summary>Seconds between melee strikes (the swing stages are scaled to it); below 0 = PlayerStats.attackCooldown.</summary>
        public float meleeInterval = -1f;
        /// <summary>[ROGUE] A dagger in each hand: the held weapon is drawn again in the left hand.</summary>
        public bool dualWield;

        public CharacterLook Look => id == CharacterClass.Mage ? CharacterLook.Mage : id == CharacterClass.Rogue ? CharacterLook.Rogue : CharacterLook.Player;

        static readonly CharacterClassInfo Warrior = new CharacterClassInfo
        {
            id = CharacterClass.Warrior,
            saveId = "warrior",
            displayName = L.T("전사"),
            description = L.T("검을 휘둘러 가까운 적을 한꺼번에 벤다.\n체력과 근접 공격이 믿음직한 기본 모험가."),
            weaponSprite = "tool_sword",
            ranged = false,
        };

        static readonly CharacterClassInfo MageInfo = new CharacterClassInfo
        {
            id = CharacterClass.Mage,
            saveId = "mage",
            displayName = L.T("마법사"),
            description = L.T("지팡이로 마법 구슬을 쏘아 멀리 있는 적을 맞힌다.\n해골의 공격 범위 밖에서 싸울 수 있지만 연사는 느리다."),
            weaponSprite = "tool_staff",
            ranged = true,
        };

        static readonly CharacterClassInfo RogueInfo = new CharacterClassInfo
        {
            id = CharacterClass.Rogue,
            saveId = "rogue",
            displayName = L.T("도적"),
            description = L.T("두 자루 단검으로 빠르게 찌르고, 적의 등 뒤를 노린다.\n이동기가 지나간 자리를 벤다."),
            weaponSprite = "tool_sword",
            ranged = false,
            // 0.65 a stab every 0.24 s, radius 0.45 at 0.55 (warrior 0.8 / 0.27 s, 0.62 at 0.7), knockback 3.
            basicHitScale = 0.65f,
            meleeReach = 0.55f,
            meleeRadius = 0.45f,
            meleeKnockback = 3f,
            meleeInterval = 0.24f,
            dualWield = true,
        };

        public static CharacterClassInfo Get(CharacterClass cls) => cls == CharacterClass.Mage ? MageInfo : cls == CharacterClass.Rogue ? RogueInfo : Warrior;

        /// <summary>Every playable class in pick order (character select, online creation).</summary>
        public static readonly CharacterClass[] All = { CharacterClass.Warrior, CharacterClass.Mage, CharacterClass.Rogue };

        /// <summary>Class colour of the party stripes and names: warrior orange, mage violet, rogue green (#50c878).</summary>
        public static UnityEngine.Color32 ColorOf(CharacterClass cls) => cls == CharacterClass.Mage ? new UnityEngine.Color32(160, 110, 240, 255)
            : cls == CharacterClass.Rogue ? new UnityEngine.Color32(80, 200, 120, 255) : new UnityEngine.Color32(230, 110, 60, 255);

        /// <summary>Class name for labels, looked up when shown (follows the current language).</summary>
        public static string NameOf(CharacterClass cls) => cls == CharacterClass.Mage ? L.T("마법사") : cls == CharacterClass.Rogue ? L.T("도적") : L.T("전사");

        public static CharacterClass Parse(string saveId) => saveId == "mage" ? CharacterClass.Mage : saveId == "rogue" ? CharacterClass.Rogue : CharacterClass.Warrior;
    }
}
