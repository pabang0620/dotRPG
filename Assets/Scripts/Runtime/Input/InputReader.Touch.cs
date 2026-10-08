using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Whether the on-screen touch controls are used: always on a phone / tablet, and on a PC when the game is started
    /// with the command line flag -touchUI (to try the touch layout with a mouse).
    /// </summary>
    public static class TouchUi
    {
        static int state; // 0 = not asked yet, 1 = off, 2 = on

        public static bool Enabled
        {
            get
            {
                if (state == 0)
                    state = Application.isMobilePlatform || System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-touchUI") >= 0 ? 2 : 1;
                return state == 2;
            }
        }
    }

    /// <summary>
    /// Touch input source: the touch controls (UI/TouchControls*.cs) report here, and <see cref="ApplyTouch"/> merges the
    /// values with the keyboard / gamepad values every frame (OR), so ActorCommand and the rest of the game are untouched.
    /// </summary>
    public partial class InputReader
    {
        const int TouchActionCount = (int)GameAction.AutoHunt + 1;

        Vector2 touchMove;
        readonly bool[] touchDown = new bool[TouchActionCount];
        readonly bool[] touchHeld = new bool[TouchActionCount];

        /// <summary>Virtual stick value (each axis -1..1); zero when the stick is released.</summary>
        public void TouchSetMove(Vector2 value) => touchMove = Vector2.ClampMagnitude(value, 1f);

        /// <summary>A finger went down (<paramref name="down"/>) or up on a button; counts as a press on the way down and as held in between.</summary>
        public void TouchHold(GameAction action, bool down)
        {
            int i = (int)action;
            if (i < 0 || i >= TouchActionCount) return;
            if (down) touchDown[i] = true;
            touchHeld[i] = down;
        }

        /// <summary>One press without holding (menu taps, dialogue taps).</summary>
        public void TouchTap(GameAction action)
        {
            int i = (int)action;
            if (i >= 0 && i < TouchActionCount) touchDown[i] = true;
        }

        /// <summary>Drops every touch value (controls hidden or a finger was lost).</summary>
        public void TouchReset()
        {
            touchMove = Vector2.zero;
            System.Array.Clear(touchDown, 0, touchDown.Length);
            System.Array.Clear(touchHeld, 0, touchHeld.Length);
        }

        bool TakeTouch(GameAction action)
        {
            int i = (int)action;
            bool v = touchDown[i];
            touchDown[i] = false;
            return v;
        }

        void ApplyTouch()
        {
            if (!TouchUi.Enabled) return;
            bool used = false;
            if (touchMove.sqrMagnitude > 0.0001f && Move == Vector2.zero && !MoveOverride.HasValue) { Move = touchMove; used = true; }
            if (TakeTouch(GameAction.Mobility)) { MobilityPressed = true; used = true; }
            if (TakeTouch(GameAction.Attack)) { AttackPressed = true; used = true; }
            if (TakeTouch(GameAction.Interact)) { InteractPressed = true; used = true; }
            if (TakeTouch(GameAction.UseItem)) { UseItemPressed = true; used = true; }
            if (TakeTouch(GameAction.UseMana)) { UseManaPressed = true; used = true; }
            if (TakeTouch(GameAction.TownScroll)) { TownScrollPressed = true; used = true; }
            if (TakeTouch(GameAction.Submit)) { SubmitPressed = true; used = true; }
            if (TakeTouch(GameAction.Pause)) { PausePressed = true; used = true; }
            if (TakeTouch(GameAction.Cancel)) { CancelPressed = true; used = true; }
            if (TakeTouch(GameAction.Skill1)) { Skill1Pressed = true; used = true; }
            if (TakeTouch(GameAction.Skill2)) { Skill2Pressed = true; used = true; }
            if (TakeTouch(GameAction.Skill3)) { Skill3Pressed = true; used = true; }
            if (TakeTouch(GameAction.Skill4)) { Skill4Pressed = true; used = true; }
            if (TakeTouch(GameAction.Skill5)) { Skill5Pressed = true; used = true; }
            for (int s = 0; s < held.Length; s++)
                if (touchHeld[(int)GameAction.Skill1 + s]) { held[s] = true; used = true; }
            if (used) UsingGamepad = false;
        }
    }
}
