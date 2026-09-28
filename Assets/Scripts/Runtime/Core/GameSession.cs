using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Mutable state of the current playthrough (what gets saved). World objects read from it when
    /// the world is (re)built; gameplay writes to it. Kept free of MonoBehaviours so it is trivial
    /// to serialize and to reset for a new game.
    /// </summary>
    public sealed class GameSession
    {
        public readonly Inventory Inventory = new Inventory();
        public QuestProgress Quest { get; private set; } = new QuestProgress();
        public int PlayerMaxHealth { get; set; }
        public int PlayerHealth { get; set; }
        public float PlayTimeSeconds { get; set; }

        /// <summary>Where the player appears when the world is built. Null = map spawn point.</summary>
        public Vector2? StartPosition { get; set; }
        public Facing StartFacing { get; set; } = Facing.Down;

        public void ResetForNewGame(GameConfig config)
        {
            Inventory.Clear();
            Quest = new QuestProgress();
            PlayerMaxHealth = config.playerStats.maxHealth;
            PlayerHealth = PlayerMaxHealth;
            PlayTimeSeconds = 0f;
            StartPosition = null;
            StartFacing = Facing.Down;
        }

        public SaveData Capture(Vector2 playerPosition, Facing facing)
        {
            return new SaveData
            {
                playTimeSeconds = PlayTimeSeconds,
                playerX = playerPosition.x,
                playerY = playerPosition.y,
                playerFacing = (int)facing,
                playerHealth = PlayerHealth,
                playerMaxHealth = PlayerMaxHealth,
                inventory = Inventory.ToList(),
                quest = JsonUtility.FromJson<QuestProgress>(JsonUtility.ToJson(Quest)),
            };
        }

        public void Restore(SaveData data, GameConfig config)
        {
            Inventory.Load(data.inventory);
            Quest = data.quest ?? new QuestProgress();
            PlayerMaxHealth = data.playerMaxHealth > 0 ? data.playerMaxHealth : config.playerStats.maxHealth;
            PlayerHealth = Mathf.Clamp(data.playerHealth, 1, PlayerMaxHealth);
            PlayTimeSeconds = data.playTimeSeconds;
            StartPosition = new Vector2(data.playerX, data.playerY);
            StartFacing = (Facing)Mathf.Clamp(data.playerFacing, 0, 3);
        }
    }
}
