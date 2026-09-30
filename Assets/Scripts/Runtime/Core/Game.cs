namespace DotRPG
{
    /// <summary>
    /// Global access point to the running game's services.
    /// Everything here is created and wired by <see cref="GameBootstrap"/>; gameplay code reads
    /// from it instead of searching the scene, which keeps dependencies explicit and easy to swap.
    /// </summary>
    public static class Game
    {
        public static GameBootstrap Bootstrap;
        public static GameConfig Config;
        public static GameStateMachine State;
        public static SettingsManager Settings;
        public static InputReader Input;
        public static AudioManager Audio;
        public static SaveSystem Saves;
        public static GameSession Session;
        public static SpriteLibrary Art;
        public static DialogueDatabase Dialogues;
        public static DialogueManager Dialogue;
        public static QuestManager Quest;
        public static WorldBuilder World;
        public static PlayerController Player;
        public static CameraFollow Camera;
        public static UIRoot UI;
        public static GameFlow Flow;
        // [PARTY] Party members (Local = Player, AI companions). Created right after the player.
        public static PartyManager Party;
        // [DUNGEON] Dungeon runs (rooms, revive, clear, result). Created right after the party.
        public static DungeonDirector Dungeon;

        /// <summary>True while the player has control of the character.</summary>
        public static bool IsPlaying => State != null && State.Current == GameState.Playing;
    }
}
