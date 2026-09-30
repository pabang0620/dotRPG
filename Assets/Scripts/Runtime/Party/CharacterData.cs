using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Per-member character state: class, level/passives/gems (<see cref="Progression"/>), worn gear,
    /// base max HP, current MP and temporary buffs, plus the stat calculator built on top of them.
    /// The local member wraps <see cref="GameSession"/> (so saving and every existing screen keep
    /// working); companions own their data and only their mercenary id is saved.
    /// </summary>
    public sealed class CharacterData
    {
        /// <summary>Stable id used by the damage meters ("local", "merc_bron", ...).</summary>
        public readonly string Id;
        public readonly bool IsLocal;
        /// <summary>Mercenary table id (null for the local player).</summary>
        public readonly string MercenaryId;
        public string DisplayName;
        /// <summary>1 for players; companions deal a fraction of a same-level player's damage.</summary>
        public float DamageScale = 1f;
        /// <summary>Threat per point of damage dealt (tanks draw more attention).</summary>
        public float ThreatMultiplier = 1f;
        /// <summary>Base look (class look for the local player, the mercenary's colours for companions).</summary>
        public CharacterLook Look;

        readonly CharacterClass cls;
        readonly Progression progression;
        readonly Equipment equipment;
        int baseMaxHp;
        float mana;

        public readonly CharacterStatsCalc Stats;

        CharacterData(string id, bool local, string mercId, CharacterClass cls, Progression progression, Equipment equipment, int baseMaxHp)
        {
            Id = id;
            IsLocal = local;
            MercenaryId = mercId;
            this.cls = cls;
            this.progression = progression;
            this.equipment = equipment;
            this.baseMaxHp = baseMaxHp;
            Stats = new CharacterStatsCalc(this);
        }

        static CharacterData session;

        /// <summary>The local member's data (wraps <see cref="Game.Session"/>; always the same instance).</summary>
        public static CharacterData Session => session ?? (session = new CharacterData("local", true, null, CharacterClass.Warrior, null, null, 0));

        /// <summary>A companion's own data (own progression and gear, nothing saved but the mercenary id).</summary>
        public static CharacterData CreateCompanion(string mercId, string name, CharacterClass cls, int baseMaxHp)
        {
            var bag = new Inventory();
            return new CharacterData(mercId, false, mercId, cls, new Progression(), new Equipment(bag), baseMaxHp) { DisplayName = name };
        }

        public CharacterClass Class => IsLocal ? Game.Session.PlayerClass : cls;
        public Progression Progression => IsLocal ? Game.Session.Progression : progression;
        public Equipment Equipment => IsLocal ? Game.Session.Equipment : equipment;
        public int Level => Progression.Level;

        /// <summary>Base max HP before level, passives and gear (the local one is saved with the session).</summary>
        public int BaseMaxHp
        {
            get => IsLocal ? Game.Session.PlayerMaxHealth : baseMaxHp;
            set { if (IsLocal) Game.Session.PlayerMaxHealth = value; else baseMaxHp = value; }
        }

        /// <summary>Current MP (the local one mirrors <see cref="GameSession.PlayerMana"/>).</summary>
        public float Mana
        {
            get => IsLocal ? Game.Session.PlayerMana : mana;
            set { if (IsLocal) Game.Session.PlayerMana = value; else mana = value; }
        }

        // ---------- Temporary buff (전쟁 함성) ----------

        float buffUntil;
        int buffPct;

        public void ApplyDamageBuff(int pct, float seconds)
        {
            buffPct = pct;
            buffUntil = Time.time + seconds;
        }

        public int BuffDamage => Time.time < buffUntil ? buffPct : 0;
        public float BuffRemaining => Mathf.Max(0f, buffUntil - Time.time);
        public void ClearBuffs() => buffUntil = 0f;
    }
}
