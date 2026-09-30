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
        /// <summary>Items left with the village storage keeper (창고). Survives death and travel.</summary>
        public readonly Inventory Storage = new Inventory();
        /// <summary>Worn gear; spare gear sits in <see cref="Inventory"/>.</summary>
        public readonly Equipment Equipment;

        public int Gold => Inventory.Count(ConsumableDatabase.Gold);

        public GameSession()
        {
            Equipment = new Equipment(Inventory);
        }
        public QuestProgress Quest { get; private set; } = new QuestProgress();
        /// <summary>Base max HP (class base + quest rewards). Level, passives and gear are added by <see cref="CharacterStats"/>.</summary>
        public int PlayerMaxHealth { get; set; }
        /// <summary>Level, experience, passive tree and skill gems.</summary>
        public readonly Progression Progression = new Progression();
        /// <summary>Current MP (not saved: refills on load).</summary>
        public float PlayerMana { get; set; }
        public int PlayerHealth { get; set; }
        public float PlayTimeSeconds { get; set; }

        /// <summary>Map the player is on (see <see cref="MapRegistry"/>).</summary>
        public string MapId { get; set; } = MapRegistry.Village;
        /// <summary>Treasure chests already opened this playthrough ("map:x:y").</summary>
        public readonly System.Collections.Generic.HashSet<string> OpenedChests = new System.Collections.Generic.HashSet<string>();
        /// <summary>Character picked on the character-select screen.</summary>
        public CharacterClass PlayerClass { get; set; } = CharacterClass.Warrior;

        /// <summary>Where the player appears when the world is built. Null = map spawn point.</summary>
        public Vector2? StartPosition { get; set; }
        public Facing StartFacing { get; set; } = Facing.Down;
        // [PARTY] Mercenary ids of the AI companions (saved). The party itself is Game.Party.
        public readonly System.Collections.Generic.List<string> PartyRoster = new System.Collections.Generic.List<string>();
        // [DUNGEON] Daily entries, weekly raid lock, best ranks, cleared difficulties (saved).
        public readonly DungeonProgress Dungeons = new DungeonProgress();

        public void ResetForNewGame(GameConfig config, CharacterClass playerClass = CharacterClass.Warrior)
        {
            // Gear first: equipment events update the player's health, which the lines below then reset.
            Inventory.Clear();
            Storage.Clear();
            Equipment.Clear();
            Equipment.LoadPity(null);
            Equipment.Set(EquipSlot.Weapon, EquipmentDatabase.StarterWeapon(playerClass));
            foreach (var (id, count) in ConsumableDatabase.StarterPack) Inventory.Add(id, count);
            Quest = new QuestProgress();
            OpenedChests.Clear();
            PlayerMaxHealth = config.playerStats.maxHealth;
            PlayTimeSeconds = 0f;
            MapId = MapRegistry.Village;
            PlayerClass = playerClass;
            Progression.Reset(playerClass);
            PlayerHealth = CharacterStats.MaxHp;
            PlayerMana = CharacterStats.MaxMp;
            StartPosition = null;
            StartFacing = Facing.Down;
            PartyRoster.Clear(); // [PARTY]
            Dungeons.Reset(); // [DUNGEON]
        }

        public SaveData Capture(Vector2 playerPosition, Facing facing)
        {
            var data = new SaveData
            {
                playTimeSeconds = PlayTimeSeconds,
                mapId = MapId,
                playerClass = CharacterClassInfo.Get(PlayerClass).saveId,
                playerX = playerPosition.x,
                playerY = playerPosition.y,
                playerFacing = (int)facing,
                playerHealth = PlayerHealth,
                playerMaxHealth = PlayerMaxHealth,
                inventory = Inventory.ToList(),
                storage = Storage.ToList(),
                equipped = Equipment.ToList(),
                // Gear keys carry their own +level since v4; the per-kind list is only read from old saves.
                enhanceLevels = new System.Collections.Generic.List<ItemStack>(),
                enhancePity = Equipment.PityToList(),
                openedChests = new System.Collections.Generic.List<string>(OpenedChests),
                quest = JsonUtility.FromJson<QuestProgress>(JsonUtility.ToJson(Quest)),
            };
            Progression.Capture(data);
            data.partyMercs = new System.Collections.Generic.List<string>(PartyRoster); // [PARTY]
            Dungeons.Capture(data); // [DUNGEON]
            return data;
        }

        public void Restore(SaveData data, GameConfig config)
        {
            // Gear first (see ResetForNewGame).
            Inventory.Load(data.inventory);
            Storage.Load(data.storage);
            Equipment.LoadPity(data.enhancePity);
            Equipment.Load(data.equipped);
            Equipment.EnsureUsable(CharacterClassInfo.Parse(data.playerClass)); // old saves have no gear: hand out the starter weapon
            Quest = data.quest ?? new QuestProgress();
            OpenedChests.Clear();
            if (data.openedChests != null) OpenedChests.UnionWith(data.openedChests);
            PlayerMaxHealth = data.playerMaxHealth > 0 ? data.playerMaxHealth : config.playerStats.maxHealth;
            PlayTimeSeconds = data.playTimeSeconds;
            PlayerClass = CharacterClassInfo.Parse(data.playerClass);
            Progression.Restore(data, PlayerClass);
            PlayerHealth = Mathf.Clamp(data.playerHealth, 1, CharacterStats.MaxHp);
            PlayerMana = CharacterStats.MaxMp;
            Dungeons.Restore(data); // [DUNGEON]
            // [DUNGEON] A dungeon room is never a place to continue from (saves are blocked there anyway).
            if (MapRegistry.Exists(data.mapId) && !MapRegistry.Get(data.mapId).instanced)
            {
                MapId = data.mapId;
                // Negative coordinates mark "use the map's start point" (see SaveSystem.Migrate).
                StartPosition = data.playerX >= 0f && data.playerY >= 0f ? new Vector2(data.playerX, data.playerY) : (Vector2?)null;
            }
            else
            {
                // Unknown map (e.g. removed in a later version): start over at the village spawn.
                MapId = MapRegistry.Village;
                StartPosition = null;
            }
            StartFacing = (Facing)Mathf.Clamp(data.playerFacing, 0, 3);
            // [PARTY] Known mercenaries only, no duplicates, at most three.
            PartyRoster.Clear();
            if (data.partyMercs != null)
                foreach (var id in data.partyMercs)
                    if (MercenaryDatabase.Get(id) != null && !PartyRoster.Contains(id) && PartyRoster.Count < PartyManager.MaxCompanions) PartyRoster.Add(id);
        }
    }
}
