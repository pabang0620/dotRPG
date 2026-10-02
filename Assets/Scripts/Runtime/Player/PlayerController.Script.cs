using System;
using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[STORY] Cutscene control of the player: walk to a point while input waits.</summary>
    public partial class PlayerController
    {
        bool scriptWalking;

        public void StopScripted()
        {
            scriptWalking = false;
        }

        /// <summary>Walks to <paramref name="target"/> at <paramref name="speed"/> tiles/s; <paramref name="skip"/> jumps there.</summary>
        public IEnumerator ScriptWalk(Vector2 target, float speed, Func<bool> skip)
        {
            scriptWalking = true;
            Vector2 dir = target - Position;
            if (dir.sqrMagnitude > 0.0001f) Facing = FacingExtensions.FromVector(dir, Facing);
            while (scriptWalking && (skip == null || !skip()))
            {
                Vector2 pos = Position;
                if (Vector2.Distance(pos, target) < 0.03f) break;
                Vector2 next = Vector2.MoveTowards(pos, target, speed * Time.deltaTime);
                body.position = next;
                transform.position = next;
                yield return null;
            }
            scriptWalking = false;
            Place(target, Facing);
        }

        /// <summary>Animation while the game is in <see cref="GameState.Cutscene"/>.</summary>
        void UpdateScripted()
        {
            animator.Play(scriptWalking ? CharacterAnim.Walk : CharacterAnim.Idle, Facing);
        }
    }
}
