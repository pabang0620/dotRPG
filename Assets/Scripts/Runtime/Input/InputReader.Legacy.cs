using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

#if !ENABLE_INPUT_SYSTEM
namespace DotRPG
{
    public partial class InputReader
    {
        // [I] Keyboard keys can be rebound (KeyBindScreen); gamepad buttons stay fixed.
        static KeyCode[] MobilityKeys = { KeyCode.LeftShift, KeyCode.RightShift, KeyCode.JoystickButton1 };
        static KeyCode[] AttackKeys = { KeyCode.X, KeyCode.JoystickButton2 };
        static KeyCode[] InteractKeys = { KeyCode.F, KeyCode.JoystickButton0 };
        static KeyCode[] UseItemKeys = { KeyCode.Alpha1, KeyCode.JoystickButton3 };
        static readonly KeyCode[] PauseKeys = { KeyCode.Escape, KeyCode.JoystickButton7 };
        static readonly KeyCode[] SubmitKeys = { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space, KeyCode.F, KeyCode.X, KeyCode.JoystickButton0 };
        static readonly KeyCode[] CancelKeys = { KeyCode.Escape, KeyCode.Backspace, KeyCode.JoystickButton1 };
        static KeyCode[] InventoryKeys = { KeyCode.I, KeyCode.Tab, KeyCode.JoystickButton6 };
        static KeyCode[] Skill1Keys = { KeyCode.Q, KeyCode.JoystickButton4 };
        static KeyCode[] Skill2Keys = { KeyCode.W, KeyCode.JoystickButton5 };
        static KeyCode[] Skill3Keys = { KeyCode.E };
        static KeyCode[] Skill4Keys = { KeyCode.R };
        static KeyCode[] Skill5Keys = { KeyCode.T, KeyCode.JoystickButton9 };
        static KeyCode[] ManaKeys = { KeyCode.Alpha2, KeyCode.JoystickButton8 };
        static KeyCode[] ScrollKeys = { KeyCode.Alpha3 };

        /// <summary>[I] Actions whose keyboard key the player can change, in the order the screen lists them.</summary>
        public static readonly GameAction[] Rebindable =
        {
            GameAction.Attack, GameAction.Mobility, GameAction.Interact, GameAction.Skill1, GameAction.Skill2, GameAction.Skill3,
            GameAction.Skill4, GameAction.Skill5, GameAction.UseItem, GameAction.UseMana, GameAction.TownScroll, GameAction.Inventory,
            GameAction.MoveUp, GameAction.MoveDown, GameAction.MoveLeft, GameAction.MoveRight, GameAction.Map,
            GameAction.SkillWindow, GameAction.QuestWindow, GameAction.WeekdayDungeon, GameAction.RaidWindow,
            GameAction.PartyWindow, GameAction.PartyFinder, GameAction.Auction, GameAction.Friends, GameAction.Cosmetics,
            GameAction.AutoQuest, GameAction.AutoHunt,
        };

        public static readonly GameAction[] WindowActions =
        {
            GameAction.Inventory, GameAction.Map, GameAction.SkillWindow, GameAction.QuestWindow,
            GameAction.WeekdayDungeon, GameAction.RaidWindow, GameAction.PartyWindow, GameAction.PartyFinder,
            GameAction.Auction, GameAction.Friends, GameAction.Cosmetics,
        };
        public static bool IsWindowAction(GameAction action) => System.Array.IndexOf(WindowActions, action) >= 0;
        /// <summary>[AUTO] A rebindable keyboard hotkey that is not a window (자동 진행 / 자동 사냥).</summary>
        public bool HotkeyPressed(GameAction action) => !TextInputActive && Input.GetKeyDown(KeyboardKey(action));
        public bool WindowPressed(GameAction action) => !TextInputActive && (action == GameAction.Inventory ? InventoryPressed : Input.GetKeyDown(KeyboardKey(action)));

        static readonly System.Collections.Generic.Dictionary<GameAction, KeyCode> keyOverrides = new System.Collections.Generic.Dictionary<GameAction, KeyCode>();

        public static KeyCode DefaultKey(GameAction a)
        {
            switch (a)
            {
                case GameAction.Attack: return KeyCode.X;
                case GameAction.Mobility: return KeyCode.LeftShift;
                case GameAction.Interact: return KeyCode.F;
                case GameAction.Skill1: return KeyCode.Q;
                case GameAction.Skill2: return KeyCode.W;
                case GameAction.Skill3: return KeyCode.E;
                case GameAction.Skill4: return KeyCode.R;
                case GameAction.Skill5: return KeyCode.T;
                case GameAction.UseItem: return KeyCode.Alpha1;
                case GameAction.UseMana: return KeyCode.Alpha2;
                case GameAction.TownScroll: return KeyCode.Alpha3;
                case GameAction.Inventory: return KeyCode.I;
                case GameAction.MoveUp: return KeyCode.UpArrow;
                case GameAction.MoveDown: return KeyCode.DownArrow;
                case GameAction.MoveLeft: return KeyCode.LeftArrow;
                case GameAction.MoveRight: return KeyCode.RightArrow;
                case GameAction.Map: return KeyCode.M;
                case GameAction.SkillWindow: return KeyCode.K;
                case GameAction.QuestWindow: return KeyCode.J;
                case GameAction.WeekdayDungeon: return KeyCode.G;
                case GameAction.RaidWindow: return KeyCode.H;
                case GameAction.PartyWindow: return KeyCode.P;
                case GameAction.PartyFinder: return KeyCode.O;
                case GameAction.Auction: return KeyCode.U;
                case GameAction.Friends: return KeyCode.L;
                case GameAction.Cosmetics: return KeyCode.C;
                case GameAction.AutoQuest: return KeyCode.F6;
                case GameAction.AutoHunt: return KeyCode.F7;
                default: return KeyCode.None;
            }
        }

        public static KeyCode KeyboardKey(GameAction a) => keyOverrides.TryGetValue(a, out var k) ? k : DefaultKey(a);

        /// <summary>Binds <paramref name="key"/> to <paramref name="action"/>; an action already on that key takes the old key (swap).</summary>
        public static void SetKeyboardKey(GameAction action, KeyCode key)
        {
            if (System.Array.IndexOf(Rebindable, action) < 0 || !CanBindKeyboardKey(key)) return;
            KeyCode old = KeyboardKey(action);
            foreach (var other in Rebindable)
                if (other != action && KeyboardKey(other) == key) keyOverrides[other] = old;
            keyOverrides[action] = key;
            RebuildKeys();
        }

        public static bool CanBindKeyboardKey(KeyCode key) => System.Enum.IsDefined(typeof(KeyCode), key) && key > KeyCode.None && key < KeyCode.Mouse0
            && key != KeyCode.Escape && key != KeyCode.Return && key != KeyCode.KeypadEnter && key != KeyCode.Backspace
            && key != KeyCode.LeftWindows && key != KeyCode.RightWindows && key != KeyCode.LeftCommand && key != KeyCode.RightCommand
            && key != KeyCode.CapsLock && key != KeyCode.Numlock && key != KeyCode.ScrollLock && key != KeyCode.Print && key != KeyCode.Pause;

        public static void ResetKeyboardKeys()
        {
            keyOverrides.Clear();
            RebuildKeys();
        }

        /// <summary>"Attack=X;Skill1=A" for the settings file.</summary>
        public static string SaveKeyOverrides()
        {
            var parts = new System.Collections.Generic.List<string>();
            foreach (var action in Rebindable) if (KeyboardKey(action) != DefaultKey(action)) parts.Add(action + "=" + KeyboardKey(action));
            return string.Join(";", parts);
        }

        public static void LoadKeyOverrides(string text)
        {
            keyOverrides.Clear();
            if (!string.IsNullOrEmpty(text))
                foreach (var part in text.Split(';'))
                {
                    var kv = part.Split('=');
                    if (kv.Length == 2 && System.Enum.TryParse(kv[0], out GameAction a) && System.Enum.TryParse(kv[1], out KeyCode k) && System.Array.IndexOf(Rebindable, a) >= 0 && CanBindKeyboardKey(k)) SetKeyboardKey(a, k);
                }
            RebuildKeys();
        }

        static void RebuildKeys()
        {
            KeyCode K(GameAction a) => KeyboardKey(a);
            bool UsedByOther(KeyCode key, GameAction action) => System.Array.Exists(Rebindable, a => a != action && K(a) == key);
            bool shiftDefault = K(GameAction.Mobility) == KeyCode.LeftShift && !UsedByOther(KeyCode.RightShift, GameAction.Mobility);
            MobilityKeys = shiftDefault ? new[] { KeyCode.LeftShift, KeyCode.RightShift, KeyCode.JoystickButton1 } : new[] { K(GameAction.Mobility), KeyCode.JoystickButton1 };
            AttackKeys = new[] { K(GameAction.Attack), KeyCode.JoystickButton2 };
            InteractKeys = new[] { K(GameAction.Interact), KeyCode.JoystickButton0 };
            UseItemKeys = new[] { K(GameAction.UseItem), KeyCode.JoystickButton3 };
            InventoryKeys = K(GameAction.Inventory) == KeyCode.I && !UsedByOther(KeyCode.Tab, GameAction.Inventory) ? new[] { KeyCode.I, KeyCode.Tab, KeyCode.JoystickButton6 } : new[] { K(GameAction.Inventory), KeyCode.JoystickButton6 };
            Skill1Keys = new[] { K(GameAction.Skill1), KeyCode.JoystickButton4 };
            Skill2Keys = new[] { K(GameAction.Skill2), KeyCode.JoystickButton5 };
            Skill3Keys = new[] { K(GameAction.Skill3) };
            Skill4Keys = new[] { K(GameAction.Skill4) };
            Skill5Keys = new[] { K(GameAction.Skill5), KeyCode.JoystickButton9 };
            ManaKeys = new[] { K(GameAction.UseMana), KeyCode.JoystickButton8 };
            ScrollKeys = new[] { K(GameAction.TownScroll) };
        }

        public static string MovementLabel() => KeyboardKey(GameAction.MoveUp) == KeyCode.UpArrow && KeyboardKey(GameAction.MoveDown) == KeyCode.DownArrow
            && KeyboardKey(GameAction.MoveLeft) == KeyCode.LeftArrow && KeyboardKey(GameAction.MoveRight) == KeyCode.RightArrow ? "↑↓←→"
            : KeyLabel(KeyboardKey(GameAction.MoveUp)) + "/" + KeyLabel(KeyboardKey(GameAction.MoveDown)) + "/" + KeyLabel(KeyboardKey(GameAction.MoveLeft)) + "/" + KeyLabel(KeyboardKey(GameAction.MoveRight));

        static readonly System.Collections.Generic.Dictionary<KeyCode, string> keyLabels = new System.Collections.Generic.Dictionary<KeyCode, string>();

        /// <summary>Short label of a key ("Q", "Shift", "1"). Cached: the HUD asks every frame.</summary>
        public static string KeyLabel(KeyCode k)
        {
            if (!keyLabels.TryGetValue(k, out var label)) keyLabels[k] = label = MakeKeyLabel(k);
            return label;
        }

        static string MakeKeyLabel(KeyCode k)
        {
            if (k >= KeyCode.Alpha0 && k <= KeyCode.Alpha9) return ((int)(k - KeyCode.Alpha0)).ToString();
            if (k >= KeyCode.Keypad0 && k <= KeyCode.Keypad9) return "Num" + (int)(k - KeyCode.Keypad0);
            switch (k)
            {
                case KeyCode.LeftShift: case KeyCode.RightShift: return "Shift";
                case KeyCode.LeftControl: case KeyCode.RightControl: return "Ctrl";
                case KeyCode.LeftAlt: case KeyCode.RightAlt: return "Alt";
                case KeyCode.Space: return "Space";
                case KeyCode.Return: return "Enter";
                case KeyCode.UpArrow: return "↑"; case KeyCode.DownArrow: return "↓";
                case KeyCode.LeftArrow: return "←"; case KeyCode.RightArrow: return "→";
                default: return k.ToString();
            }
        }

        public static Vector2 KeyboardMove(System.Func<KeyCode, bool> held)
        {
            Vector2 keys = Vector2.zero;
            if (held(KeyboardKey(GameAction.MoveLeft))) keys.x -= 1;
            if (held(KeyboardKey(GameAction.MoveRight))) keys.x += 1;
            if (held(KeyboardKey(GameAction.MoveDown))) keys.y -= 1;
            if (held(KeyboardKey(GameAction.MoveUp))) keys.y += 1;
            return Vector2.ClampMagnitude(keys, 1f);
        }

        void ReadLegacy()
        {
            Vector2 keys = KeyboardMove(Input.GetKey);

            Vector2 stick = Vector2.zero;
            if (legacyAxesAvailable)
            {
                try { stick = new Vector2(Input.GetAxisRaw("GamepadHorizontal"), Input.GetAxisRaw("GamepadVertical")); }
                catch (System.ArgumentException) { legacyAxesAvailable = false; }
            }
            if (stick.magnitude < stickDeadZone) stick = Vector2.zero;

            Vector2 move = keys != Vector2.zero ? keys : stick;
            Move = MoveOverride ?? Vector2.ClampMagnitude(move, 1f);

            MobilityPressed = AnyDown(MobilityKeys);
            AttackPressed = AnyDown(AttackKeys);
            InteractPressed = AnyDown(InteractKeys);
            UseItemPressed = AnyDown(UseItemKeys);
            PausePressed = AnyDown(PauseKeys);
            SubmitPressed = AnyDown(SubmitKeys);
            CancelPressed = AnyDown(CancelKeys);
            InventoryPressed = AnyDown(InventoryKeys);
            MapPressed = Input.GetKeyDown(KeyboardKey(GameAction.Map));
            Skill1Pressed = AnyDown(Skill1Keys);
            Skill2Pressed = AnyDown(Skill2Keys);
            Skill3Pressed = AnyDown(Skill3Keys);
            Skill4Pressed = AnyDown(Skill4Keys);
            Skill5Pressed = AnyDown(Skill5Keys);
            held[0] = AnyHeld(Skill1Keys); held[1] = AnyHeld(Skill2Keys); held[2] = AnyHeld(Skill3Keys);
            held[3] = AnyHeld(Skill4Keys); held[4] = AnyHeld(Skill5Keys);
            UseManaPressed = AnyDown(ManaKeys);
            TownScrollPressed = AnyDown(ScrollKeys);

            if (keys != Vector2.zero || Input.anyKeyDown && !JoystickButtonDown()) UsingGamepad = false;
            if (JoystickButtonDown() || (keys == Vector2.zero && stick != Vector2.zero)) UsingGamepad = true;
        }

        static bool JoystickButtonDown()
        {
            for (int i = 0; i < 10; i++)
                if (Input.GetKeyDown(KeyCode.JoystickButton0 + i)) return true;
            return false;
        }

        static bool AnyDown(KeyCode[] codes)
        {
            foreach (var code in codes)
                if (Input.GetKeyDown(code)) return true;
            return false;
        }

        static bool AnyHeld(KeyCode[] codes)
        {
            foreach (var code in codes)
                if (Input.GetKey(code)) return true;
            return false;
        }

        Vector2 ReadNavigateVector() => Move;
    }
}
#endif
