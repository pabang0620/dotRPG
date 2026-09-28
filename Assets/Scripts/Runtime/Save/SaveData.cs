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
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;
        public string savedAtUtc;
        public float playTimeSeconds;

        public string mapId = "village";
        public float playerX;
        public float playerY;
        public int playerFacing;
        public int playerHealth;
        public int playerMaxHealth;

        public List<ItemStack> inventory = new List<ItemStack>();
        public QuestProgress quest = new QuestProgress();
    }
}
