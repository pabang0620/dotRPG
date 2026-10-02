using System;

namespace DotRPG
{
    /// <summary>
    /// Lightweight global event bus for cross-system notifications
    /// (gameplay → UI, gameplay → quest). Systems that own state expose their own events instead.
    /// </summary>
    public static class GameEvents
    {
        /// <summary>Short on-screen message ("+2 목재", "저장했습니다").</summary>
        public static event Action<string> Toast;

        /// <summary>An enemy was defeated. Argument is the enemy kind id (e.g. "skeleton").</summary>
        public static event Action<string> EnemyKilled;

        /// <summary>The player's health or max health changed.</summary>
        public static event Action<int, int> PlayerHealthChanged;

        public static void RaiseToast(string message) => Toast?.Invoke(message);
        public static void RaiseEnemyKilled(string enemyId) => EnemyKilled?.Invoke(enemyId);
        public static void RaisePlayerHealthChanged(int current, int max) => PlayerHealthChanged?.Invoke(current, max);

        /// <summary>A world object with a quest id was used (graves, hair ribbon, story props). Argument is that id.</summary>
        public static event Action<string> Interacted;
        public static void RaiseInteracted(string id) => Interacted?.Invoke(id);

        /// <summary>The world finished building a map (new game, continue, travel). Argument is the map id.</summary>
        public static event Action<string> MapEntered;
        public static void RaiseMapEntered(string mapId) => MapEntered?.Invoke(mapId);

        /// <summary>A quest was completed and rewarded. Argument is the quest id.</summary>
        public static event Action<string> QuestCompleted;
        public static void RaiseQuestCompleted(string questId) => QuestCompleted?.Invoke(questId);

        /// <summary>An awakening (ultimate) skill was cast: skill name and theme colour for the cut-in banner.</summary>
        public static event Action<string, UnityEngine.Color> Awakening;
        public static void RaiseAwakening(string skillName, UnityEngine.Color color) => Awakening?.Invoke(skillName, color);
    }
}
