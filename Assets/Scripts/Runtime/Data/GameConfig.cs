using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public static class ItemIds
    {
        public const string Wood = "wood";
        public const string Stone = "stone";
        public const string Carrot = "carrot";
    }

    [Serializable]
    public class ItemDefinition
    {
        public string id;
        public string displayName;
        [Tooltip("Sprite key; override with Resources/Art/{iconKey}.png")]
        public string iconKey;

        public ItemDefinition() { }

        public ItemDefinition(string id, string displayName, string iconKey)
        {
            this.id = id;
            this.displayName = displayName;
            this.iconKey = iconKey;
        }
    }

    /// <summary>
    /// Root configuration asset (Assets/Resources/Data/GameConfig.asset). Every tunable number of the
    /// vertical slice lives here or in the assets it references, so designers can tweak values in the
    /// Inspector without touching code. Missing references fall back to in-code defaults so the game
    /// always boots.
    /// </summary>
    [CreateAssetMenu(menuName = "dotRPG/Game Config", fileName = "GameConfig")]
    public class GameConfig : ScriptableObject
    {
        public const string ResourcePath = "Data/GameConfig";

        [Header("Data assets")]
        public PlayerStats playerStats;
        public EnemyStats skeletonStats;
        public QuestConfig mainQuest;
        [Tooltip("Map layout text file. Legend is documented in the file header.")]
        public TextAsset worldMap;
        [Tooltip("Dialogue database (JSON).")]
        public TextAsset dialogues;

        [Header("Rendering")]
        [Tooltip("Pixels per world unit. All placeholder art is authored on a 16px grid.")]
        public int pixelsPerUnit = 16;
        [Tooltip("Approximate vertical resolution of the game view in art pixels. The camera picks the closest integer scale.")]
        public int targetPixelHeight = 216;
        public float cameraSmoothTime = 0.12f;
        public Color backgroundColor = new Color(0.16f, 0.30f, 0.18f);

        [Header("World resources")]
        public int treeHealth = 3;
        public int treeWoodDrop = 2;
        public float treeRegrowSeconds = 60f;
        public int rockHealth = 4;
        public int rockStoneDrop = 2;
        public float rockRespawnSeconds = 75f;
        public float cropRegrowSeconds = 40f;

        [Header("Save")]
        [Tooltip("Save automatically at quest milestones.")]
        public bool autosave = true;

        [Header("Dialogue")]
        [Tooltip("Characters revealed per second by the typewriter effect.")]
        public float textCharsPerSecond = 45f;

        [Header("Items")]
        public List<ItemDefinition> items = new List<ItemDefinition>
        {
            new ItemDefinition(ItemIds.Wood, "목재", "icon_wood"),
            new ItemDefinition(ItemIds.Stone, "돌", "icon_stone"),
            new ItemDefinition(ItemIds.Carrot, "당근", "icon_carrot"),
        };

        [Header("NPCs (placed by map symbol)")]
        public List<NpcDefinition> npcs = DefaultNpcs();

        public ItemDefinition GetItem(string id)
        {
            foreach (var item in items)
                if (item.id == id) return item;
            return new ItemDefinition(id, id, "icon_" + id);
        }

        public NpcDefinition GetNpcBySymbol(char symbol)
        {
            foreach (var npc in npcs)
                if (!string.IsNullOrEmpty(npc.mapSymbol) && npc.mapSymbol[0] == symbol) return npc;
            return null;
        }

        /// <summary>Loads the config from Resources (or uses the assigned one) and fills any gaps with defaults.</summary>
        public static GameConfig LoadOrDefault(GameConfig assigned)
        {
            var config = assigned != null ? assigned : Resources.Load<GameConfig>(ResourcePath);
            if (config == null)
            {
                Debug.LogWarning("[dotRPG] GameConfig asset not found, using in-code defaults.");
                config = CreateInstance<GameConfig>();
            }
            config.EnsureDefaults();
            return config;
        }

        public void EnsureDefaults()
        {
            if (playerStats == null) playerStats = LoadOrCreate<PlayerStats>("Data/PlayerStats");
            if (skeletonStats == null) skeletonStats = LoadOrCreate<EnemyStats>("Data/SkeletonStats");
            if (mainQuest == null) mainQuest = LoadOrCreate<QuestConfig>("Data/QuestConfig");
            if (worldMap == null) worldMap = Resources.Load<TextAsset>("Maps/Village");
            if (dialogues == null) dialogues = Resources.Load<TextAsset>("Data/Dialogues");
            if (items == null || items.Count == 0) items = new List<ItemDefinition>
            {
                new ItemDefinition(ItemIds.Wood, "목재", "icon_wood"),
                new ItemDefinition(ItemIds.Stone, "돌", "icon_stone"),
                new ItemDefinition(ItemIds.Carrot, "당근", "icon_carrot"),
            };
            if (npcs == null || npcs.Count == 0) npcs = DefaultNpcs();
            pixelsPerUnit = Mathf.Max(1, pixelsPerUnit);
            targetPixelHeight = Mathf.Max(90, targetPixelHeight);
        }

        static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = Resources.Load<T>(path);
            return asset != null ? asset : CreateInstance<T>();
        }

        public static List<NpcDefinition> DefaultNpcs() => new List<NpcDefinition>
        {
            new NpcDefinition("1", "chief", "촌장 모리", CharacterLook.Chief, NpcBehaviour.Idle, NpcTool.None, Facing.Down, "chief_intro"),
            new NpcDefinition("2", "farmer", "농부 하나", CharacterLook.Farmer, NpcBehaviour.Work, NpcTool.WateringCan, Facing.Left, "farmer", "farmer_after"),
            new NpcDefinition("3", "fisher", "낚시꾼 도윤", CharacterLook.Fisher, NpcBehaviour.Work, NpcTool.FishingRod, Facing.Down, "fisher", "fisher_after"),
            new NpcDefinition("4", "builder", "목수 강철", CharacterLook.Builder, NpcBehaviour.Work, NpcTool.Hammer, Facing.Right, "builder_before"),
            new NpcDefinition("5", "lumberjack", "나무꾼 바우", CharacterLook.Lumberjack, NpcBehaviour.Work, NpcTool.Axe, Facing.Up, "lumberjack", "lumberjack_after"),
            new NpcDefinition("6", "miner", "광부 돌쇠", CharacterLook.Miner, NpcBehaviour.Work, NpcTool.Pickaxe, Facing.Right, "miner", "miner_after"),
            new NpcDefinition("7", "carrier", "짐꾼 소미", CharacterLook.Carrier, NpcBehaviour.Patrol, NpcTool.Crate, Facing.Right, "carrier", "carrier_after", new Vector2(9f, 0f)),
            new NpcDefinition("8", "kid", "꼬마 루이", new CharacterLook("kid", HairStyle.Spiky, new Color32(250, 205, 160, 255), new Color32(90, 60, 40, 255), new Color32(230, 200, 70, 255), new Color32(80, 110, 160, 255)), NpcBehaviour.Wander, NpcTool.None, Facing.Down, "kid", "kid_after"),
        };
    }
}
