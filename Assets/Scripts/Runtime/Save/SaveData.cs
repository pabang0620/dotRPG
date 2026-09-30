using System;
using System.Collections.Generic;

namespace DotRPG
{
    [Serializable]
    public class ItemStack
    {
        public string id;
        public int count;

        public ItemStack() { }

        public ItemStack(string id, int count)
        {
            this.id = id;
            this.count = count;
        }
    }

    [Serializable]
    public class QuestProgress
    {
        /// <summary>See <see cref="QuestStage"/>.</summary>
        public int stage;
        public int woodDelivered;
        public int stoneDelivered;
        public int skeletonsDefeated;
        public bool workshopBuilt;
    }

    /// <summary>
    /// Everything written to a save slot. Bump <see cref="CurrentVersion"/> and migrate in
    /// <see cref="SaveSystem.Migrate"/> when the format changes, so released saves keep loading.
    /// </summary>
    [Serializable]
    public class SaveData
    {
        public const int CurrentVersion = 4;

        public int version = CurrentVersion;
        public string savedAtUtc;
        public float playTimeSeconds;

        public string mapId = "village";
        /// <summary>Chosen character: "warrior" or "mage" (older saves have none = warrior).</summary>
        public string playerClass = "warrior";
        public float playerX;
        public float playerY;
        public int playerFacing;
        public int playerHealth;
        public int playerMaxHealth;

        /// <summary>Bag contents. Gear is stored by instance key ("eq_sword_iron+12", version 4+).</summary>
        public List<ItemStack> inventory = new List<ItemStack>();
        /// <summary>Items kept with the village storage keeper (version 3+).</summary>
        public List<ItemStack> storage = new List<ItemStack>();
        /// <summary>Worn gear keys in <see cref="EquipSlot"/> order (null/empty = nothing worn).</summary>
        public List<string> equipped = new List<string>();
        public List<string> openedChests = new List<string>();
        /// <summary>
        /// Version 3 and older: enhancement per equipment kind (count = +level). Only read to migrate old
        /// saves (<see cref="SaveSystem.MigrateEnhanceLevels"/>); always written empty.
        /// </summary>
        public List<ItemStack> enhanceLevels = new List<ItemStack>();
        /// <summary>Enhancement pity (version 4+): id = the key being attempted from, count = bonus %p.</summary>
        public List<ItemStack> enhancePity = new List<ItemStack>();
        // [PARTY] Mercenary ids in the party (MercenaryDatabase), max 3. Older saves have none = solo.
        public List<string> partyMercs = new List<string>();
        // [ENH] Not saved: protection tickets granted by the v3 → v4 enhancement migration of this read (0 = none), for a one-time toast.
        [NonSerialized] public int enhanceCompensation;

        // Progression (version 2+).
        public int level = 1;
        public int xp;
        public List<string> passives = new List<string>();
        /// <summary>2 slots × (active + 2 supports), "" = empty socket.</summary>
        public List<string> gemSlots = new List<string>();
        public QuestProgress quest = new QuestProgress();
    }
}
