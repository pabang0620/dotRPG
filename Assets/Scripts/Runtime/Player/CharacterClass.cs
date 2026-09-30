namespace DotRPG
{
    /// <summary>Playable characters the player picks on the character-select screen.</summary>
    public enum CharacterClass
    {
        /// <summary>The original hero: sword swing in front of the body.</summary>
        Warrior,
        /// <summary>Casts a magic bolt that flies forward and hits the first target.</summary>
        Mage,
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
        public float cooldown = 0.5f;
        public float castDuration = 0.26f;
        public int damage = 10;
        public float knockback = 4f;

        public CharacterLook Look => id == CharacterClass.Mage ? CharacterLook.Mage : CharacterLook.Player;

        static readonly CharacterClassInfo Warrior = new CharacterClassInfo
        {
            id = CharacterClass.Warrior,
            saveId = "warrior",
            displayName = "전사",
            description = "검을 휘둘러 가까운 적과 나무·바위를 한꺼번에 벤다.\n체력과 근접 공격이 믿음직한 기본 모험가.",
            weaponSprite = "tool_sword",
            ranged = false,
        };

        static readonly CharacterClassInfo MageInfo = new CharacterClassInfo
        {
            id = CharacterClass.Mage,
            saveId = "mage",
            displayName = "마법사",
            description = "지팡이로 마법 구슬을 쏘아 멀리 있는 적을 맞힌다.\n해골의 공격 범위 밖에서 싸울 수 있지만 연사는 느리다.",
            weaponSprite = "tool_staff",
            ranged = true,
        };

        public static CharacterClassInfo Get(CharacterClass cls) => cls == CharacterClass.Mage ? MageInfo : Warrior;

        public static CharacterClass Parse(string saveId) => saveId == "mage" ? CharacterClass.Mage : CharacterClass.Warrior;
    }
}
