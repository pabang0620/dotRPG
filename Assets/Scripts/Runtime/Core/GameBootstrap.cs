using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Entry point. Lives in Assets/Scenes/Main.unity and wires every service in a fixed order,
    /// then shows the title screen. If you press Play in an empty scene it is created automatically.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GameBootstrap : MonoBehaviour
    {
        [Tooltip("Root configuration. Leave empty to load Resources/Data/GameConfig.")]
        [SerializeField] GameConfig config;

        void Awake()
        {
            if (Game.Bootstrap != null && Game.Bootstrap != this)
            {
                Destroy(gameObject);
                return;
            }
            Game.Bootstrap = this;

            var cfg = GameConfig.LoadOrDefault(config);
            Game.Config = cfg;
            Application.targetFrameRate = 60;

            // Order matters: later services use earlier ones.
            Game.State = new GameStateMachine();
            Game.Settings = new SettingsManager();
            Game.Settings.Load();

            Game.Input = gameObject.AddComponent<InputReader>();
            Game.Input.Initialize(Game.Settings.Data.bindingOverridesJson);
            InputReader.LoadKeyOverrides(Game.Settings.Data.keyOverrides); // [I]
            Game.Audio = AudioManager.Create(transform);
            Game.Settings.Apply();

            Game.Saves = new SaveSystem();
            Game.Cosmetics = new CosmeticStore(new StarShopProvider()); // 별조각 캐시샵(서버)
            Game.Session = new GameSession();
            Game.Session.ResetForNewGame(cfg);
            Game.Art = new SpriteLibrary(cfg.pixelsPerUnit);
            Game.Dialogues = new DialogueDatabase(cfg.dialogues);
            Game.Quest = new QuestManager(cfg.mainQuest, new QuestDatabase(Resources.Load<TextAsset>(QuestDatabase.ResourcePath)));
            Game.Flow = gameObject.AddComponent<GameFlow>();
            Game.Dialogue = gameObject.AddComponent<DialogueManager>();

            Game.Camera = CameraFollow.Create(cfg);
            Game.World = WorldBuilder.Create(cfg);

            var gameplayRoot = new GameObject("Gameplay").transform;
            gameplayRoot.SetParent(transform, false);
            Game.Player = PlayerController.Create(cfg, gameplayRoot);
            CosmeticAura.Attach(Game.Player, Game.Cosmetics);
            Game.Player.gameObject.SetActive(false);
            Game.Party = PartyManager.Create(gameplayRoot, Game.Player); // [PARTY]
            Game.Dungeon = DungeonDirector.Create(gameplayRoot); // [DUNGEON]
            Game.Cutscenes = CutscenePlayer.Create(transform, new CutsceneDatabase(Resources.Load<TextAsset>(CutsceneDatabase.ResourcePath))); // [STORY]
            Game.Quest.Bind();

            Game.UI = UIRoot.Create(transform);

            Game.State.Set(GameState.Title);
            Game.Audio.PlayMusic("music_title");
        }

        void OnDestroy()
        {
            if (Game.Bootstrap != this) return;
            Game.Quest?.Dispose();
            Game.Bootstrap = null;
            Time.timeScale = 1f;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void EnsureBootstrap()
        {
            if (FindFirstObjectByType<GameBootstrap>() != null) return;
            Debug.Log("[dotRPG] No GameBootstrap in the scene; creating one. Open Assets/Scenes/Main.unity for the normal setup.");
            new GameObject("GameBootstrap (auto)").AddComponent<GameBootstrap>();
        }
    }
}
