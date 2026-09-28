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
    }

    /// <summary>
    /// Top-level game state. Owns <see cref="Time.timeScale"/> so that pausing, dialogue and menus
    /// freeze the world consistently. UI and gameplay subscribe to <see cref="Changed"/>.
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

        public void Set(GameState next)
        {
            if (next == Current) return;
            Previous = Current;
            Current = next;
            LastChangeFrame = Time.frameCount;
            Time.timeScale = next is GameState.Playing or GameState.Title ? 1f : 0f;
            Changed?.Invoke(Previous, next);
        }
    }
}
