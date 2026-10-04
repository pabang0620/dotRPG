using System;
using UnityEngine;

namespace DotRPG
{
    public enum GameState
    {
        Boot,
        Title,
        Playing,
        Dialogue,
        Paused,
        GameOver,
        Ending,
        /// <summary>Item / equipment window open (offline world frozen).</summary>
        Inventory,
        /// <summary>A story cutscene runs: world time flows, player input and enemies wait.</summary>
        Cutscene,
    }

    /// <summary>
    /// Top-level game state. Owns <see cref="Time.timeScale"/> so that pausing, dialogue and menus
    /// freeze the offline world consistently. Online menus only suspend local input. UI and gameplay subscribe to <see cref="Changed"/>.
    /// </summary>
    public sealed class GameStateMachine
    {
        public GameState Current { get; private set; } = GameState.Boot;
        public GameState Previous { get; private set; } = GameState.Boot;
        public int LastChangeFrame { get; private set; } = -1;

        /// <summary>(previous, next)</summary>
        public event Action<GameState, GameState> Changed;

        /// <summary>
        /// True on the frame a state change happened. Input consumers use this to avoid a single
        /// key press both opening and immediately confirming/closing a menu.
        /// </summary>
        public bool ChangedThisFrame => LastChangeFrame == Time.frameCount;

        /// <summary>Re-evaluate when an online session starts or ends while a menu is open.</summary>
        public void RefreshTimeScale()
        {
            bool onlineMenu = Game.IsOnlineWorld &&
                (Current is GameState.Paused or GameState.Inventory or GameState.Dialogue);
            Time.timeScale = onlineMenu || (Current is GameState.Playing or GameState.Title or GameState.Cutscene) ? 1f : 0f;
        }

        public void Set(GameState next)
        {
            if (next == Current) return;
            Previous = Current;
            Current = next;
            LastChangeFrame = Time.frameCount;
            RefreshTimeScale();
            Changed?.Invoke(Previous, next);
        }
    }
}
