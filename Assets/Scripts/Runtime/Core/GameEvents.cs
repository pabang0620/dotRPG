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
    }
}
