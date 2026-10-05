using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [ONLINE] Every result-affecting roll or grant that a server must decide once the game is online
    /// (PLAN_ONLINE §3.5): enhancement rolls, monster drops, dungeon rank / cards / clear XP, XP and gold.
    /// Offline this is <see cref="LocalAuthority"/>, which makes exactly the same UnityEngine.Random calls in
    /// the same order as the code did before, so seeded runs are unchanged. Combat rolls (block) stay on the host.
    /// </summary>
    public interface IAuthority
    {
        /// <summary>Enhancement roll 0..99 (Equipment.TryEnhance compares it with the success chance).</summary>
        int EnhanceRoll();
        /// <summary>Gold dropped by a monster, minInclusive..maxExclusive.</summary>
        int DropGold(int minInclusive, int maxExclusive);
        /// <summary>True when a drop with this chance (0..1) happens.</summary>
        bool DropChance(float chance);
        /// <summary>How many of a material drop, min..max inclusive.</summary>
        int DropCount(int min, int max);
        /// <summary>The equipment id a monster drops for this class, or null.</summary>
        string DropEquipment(CharacterClass cls, float chance, int level = 1);
        /// <summary>Dungeon rank, clear XP and reward cards.</summary>
        IDungeonAuthority Dungeon { get; }
        /// <summary>XP actually granted for <paramref name="reason"/> (offline: unchanged).</summary>
        int GrantXp(string reason, int amount);
        /// <summary>Gold change actually applied for <paramref name="reason"/> (offline: unchanged).</summary>
        int ApplyGold(string reason, int delta);
        /// <summary>False offline: results are final immediately; an online authority answers later.</summary>
        bool IsRemote { get; }
    }

    /// <summary>[ONLINE] Offline authority: rolls on this PC with the same calls as before.</summary>
    public sealed class LocalAuthority : IAuthority
    {
        public int EnhanceRoll() => UnityEngine.Random.Range(0, 100);
        public int DropGold(int minInclusive, int maxExclusive) => UnityEngine.Random.Range(minInclusive, maxExclusive);
        // The old code skipped a material when Random.value > chance.
        public bool DropChance(float chance) => !(UnityEngine.Random.value > chance);
        public int DropCount(int min, int max) => UnityEngine.Random.Range(min, max + 1);
        public string DropEquipment(CharacterClass cls, float chance, int level = 1) => EquipmentDatabase.RollDrop(cls, chance, level);
        public IDungeonAuthority Dungeon => DungeonAuthority.Current;
        public int GrantXp(string reason, int amount) => amount;
        public int ApplyGold(string reason, int delta) => delta;
        public bool IsRemote => false;
    }

    /// <summary>[ONLINE] The authority in use. Tests may swap it; the online build installs a server-backed one.</summary>
    public static class Authority
    {
        public static IAuthority Current = new LocalAuthority();
    }
}
