using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace DotRPG
{
    public enum GameAction
    {
        Move,
        Attack,
        Interact,
        UseItem,
        Pause,
        Submit,
        Cancel,
    }

    /// <summary>
    /// Single place that turns devices into game actions. Gameplay and UI only ask this class
    /// "was Attack pressed?" and never touch keys directly, so key rebinding, gamepad support and
    /// Steam Input can be added here without changing gameplay code.
    ///
    /// Uses the Input System package when it is the active input backend (ENABLE_INPUT_SYSTEM) and
    /// falls back to the legacy Input Manager otherwise, so the project runs with either setting.
    /// </summary>
    [DefaultExecutionOrder(-900)]
    public class InputReader : MonoBehaviour
    {
        [Tooltip("Stick/key hold time before menu navigation starts repeating.")]
        [SerializeField] float navigateRepeatDelay = 0.4f;
        [SerializeField] float navigateRepeatInterval = 0.12f;
        [SerializeField] float stickDeadZone = 0.35f;

        public Vector2 Move { get; private set; }
        public bool AttackPressed { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool UseItemPressed { get; private set; }
        public bool PausePressed { get; private set; }
        public bool SubmitPressed { get; private set; }
        public bool CancelPressed { get; private set; }

        /// <summary>Discrete menu navigation step for this frame (with key repeat). (0,0) when idle.</summary>
        public Vector2Int NavigateStep { get; private set; }

        /// <summary>True when the most recent input came from a gamepad. Used to pick button prompts.</summary>
        public bool UsingGamepad { get; private set; }

        Vector2Int heldNavigate;
        float nextRepeatTime;

#if ENABLE_INPUT_SYSTEM
        InputActionMap map;
        InputAction moveAction, attackAction, interactAction, useItemAction, pauseAction, submitAction, cancelAction, navigateAction;
#else
        bool legacyAxesAvailable = true;
#endif

        public void Initialize(string bindingOverridesJson)
        {
#if ENABLE_INPUT_SYSTEM
            CreateActions();
            if (!string.IsNullOrEmpty(bindingOverridesJson))
            {
                try { map.LoadBindingOverridesFromJson(bindingOverridesJson); }
                catch (System.Exception e) { Debug.LogWarning($"[dotRPG] Could not load key bindings: {e.Message}"); }
            }
            map.Enable();
#endif
        }

        /// <summary>Serialized binding overrides for SettingsData (empty when nothing was rebound).</summary>
        public string SaveBindingOverrides()
        {
#if ENABLE_INPUT_SYSTEM
            return map != null ? map.SaveBindingOverridesAsJson() : "";
#else
            return "";
#endif
        }

        public void ResetBindings()
        {
#if ENABLE_INPUT_SYSTEM
            map?.RemoveAllBindingOverrides();
#endif
        }

        void OnDestroy()
        {
#if ENABLE_INPUT_SYSTEM
            map?.Disable();
            map?.Dispose();
#endif
        }

        void Update()
        {
#if ENABLE_INPUT_SYSTEM
            ReadInputSystem();
#else
            ReadLegacy();
#endif
            UpdateNavigateRepeat();
        }

        void UpdateNavigateRepeat()
        {
            Vector2 raw = ReadNavigateVector();
            var dir = Vector2Int.zero;
            if (Mathf.Abs(raw.y) >= stickDeadZone && Mathf.Abs(raw.y) >= Mathf.Abs(raw.x)) dir.y = raw.y > 0 ? 1 : -1;
            else if (Mathf.Abs(raw.x) >= stickDeadZone) dir.x = raw.x > 0 ? 1 : -1;

            float now = Time.unscaledTime;
            if (dir == Vector2Int.zero)
            {
                heldNavigate = Vector2Int.zero;
                NavigateStep = Vector2Int.zero;
            }
            else if (dir != heldNavigate)
            {
                heldNavigate = dir;
                NavigateStep = dir;
                nextRepeatTime = now + navigateRepeatDelay;
            }
            else if (now >= nextRepeatTime)
            {
                NavigateStep = dir;
                nextRepeatTime = now + navigateRepeatInterval;
            }
            else
            {
                NavigateStep = Vector2Int.zero;
            }
        }

        /// <summary>Human readable binding for button prompts, e.g. "E" or "A".</summary>
        public string GetBindingLabel(GameAction action)
        {
#if ENABLE_INPUT_SYSTEM
            var inputAction = GetAction(action);
            if (inputAction != null)
            {
                string wanted = UsingGamepad ? "<Gamepad>" : "<Keyboard>";
                for (int i = 0; i < inputAction.bindings.Count; i++)
                {
                    var binding = inputAction.bindings[i];
                    if (binding.isComposite || binding.isPartOfComposite) continue;
                    if (binding.effectivePath != null && binding.effectivePath.StartsWith(wanted))
                        return inputAction.GetBindingDisplayString(i);
                }
            }
#endif
            return DefaultLabel(action, UsingGamepad);
        }

        /// <summary>Both keyboard and gamepad labels, for the controls screen.</summary>
        public string GetBindingLabel(GameAction action, bool gamepad)
        {
            bool previous = UsingGamepad;
            UsingGamepad = gamepad;
            string label = GetBindingLabel(action);
            UsingGamepad = previous;
            return label;
        }

        static string DefaultLabel(GameAction action, bool gamepad)
        {
            switch (action)
            {
                case GameAction.Move: return gamepad ? "L스틱" : "WASD";
                case GameAction.Attack: return gamepad ? "X" : "J";
                case GameAction.Interact: return gamepad ? "A" : "E";
                case GameAction.UseItem: return gamepad ? "Y" : "Q";
                case GameAction.Pause: return gamepad ? "Start" : "Esc";
                case GameAction.Submit: return gamepad ? "A" : "Enter";
                case GameAction.Cancel: return gamepad ? "B" : "Esc";
                default: return "?";
            }
        }

#if ENABLE_INPUT_SYSTEM
        InputAction GetAction(GameAction action)
        {
            switch (action)
            {
                case GameAction.Move: return moveAction;
                case GameAction.Attack: return attackAction;
                case GameAction.Interact: return interactAction;
                case GameAction.UseItem: return useItemAction;
                case GameAction.Pause: return pauseAction;
                case GameAction.Submit: return submitAction;
                case GameAction.Cancel: return cancelAction;
                default: return null;
            }
        }

        void CreateActions()
        {
            map = new InputActionMap("Game");

            moveAction = map.AddAction("Move", InputActionType.Value);
            AddWasd(moveAction);
            moveAction.AddBinding("<Gamepad>/leftStick");
            moveAction.AddBinding("<Gamepad>/dpad");

            navigateAction = map.AddAction("Navigate", InputActionType.Value);
            AddWasd(navigateAction);
            navigateAction.AddBinding("<Gamepad>/leftStick");
            navigateAction.AddBinding("<Gamepad>/dpad");

            attackAction = map.AddAction("Attack", InputActionType.Button);
            attackAction.AddBinding("<Keyboard>/j");
            attackAction.AddBinding("<Keyboard>/space");
            attackAction.AddBinding("<Gamepad>/buttonWest");

            interactAction = map.AddAction("Interact", InputActionType.Button);
            interactAction.AddBinding("<Keyboard>/e");
            interactAction.AddBinding("<Gamepad>/buttonSouth");

            useItemAction = map.AddAction("UseItem", InputActionType.Button);
            useItemAction.AddBinding("<Keyboard>/q");
            useItemAction.AddBinding("<Gamepad>/buttonNorth");

            pauseAction = map.AddAction("Pause", InputActionType.Button);
            pauseAction.AddBinding("<Keyboard>/escape");
            pauseAction.AddBinding("<Gamepad>/start");

            submitAction = map.AddAction("Submit", InputActionType.Button);
            submitAction.AddBinding("<Keyboard>/enter");
            submitAction.AddBinding("<Keyboard>/space");
            submitAction.AddBinding("<Keyboard>/e");
            submitAction.AddBinding("<Keyboard>/j");
            submitAction.AddBinding("<Gamepad>/buttonSouth");

            cancelAction = map.AddAction("Cancel", InputActionType.Button);
            cancelAction.AddBinding("<Keyboard>/escape");
            cancelAction.AddBinding("<Keyboard>/backspace");
            cancelAction.AddBinding("<Gamepad>/buttonEast");
        }

        static void AddWasd(InputAction action)
        {
            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/w")
                .With("Down", "<Keyboard>/s")
                .With("Left", "<Keyboard>/a")
                .With("Right", "<Keyboard>/d");
            action.AddCompositeBinding("2DVector")
                .With("Up", "<Keyboard>/upArrow")
                .With("Down", "<Keyboard>/downArrow")
                .With("Left", "<Keyboard>/leftArrow")
                .With("Right", "<Keyboard>/rightArrow");
        }

        void ReadInputSystem()
        {
            Vector2 move = moveAction.ReadValue<Vector2>();
            if (move.magnitude < stickDeadZone * 0.5f) move = Vector2.zero;
            Move = Vector2.ClampMagnitude(move, 1f);

            AttackPressed = attackAction.WasPressedThisFrame();
            InteractPressed = interactAction.WasPressedThisFrame();
            UseItemPressed = useItemAction.WasPressedThisFrame();
            PausePressed = pauseAction.WasPressedThisFrame();
            SubmitPressed = submitAction.WasPressedThisFrame();
            CancelPressed = cancelAction.WasPressedThisFrame();

            TrackDevice(moveAction, Move != Vector2.zero);
            TrackDevice(attackAction, AttackPressed);
            TrackDevice(interactAction, InteractPressed);
            TrackDevice(submitAction, SubmitPressed);
            TrackDevice(pauseAction, PausePressed);
        }

        void TrackDevice(InputAction action, bool active)
        {
            if (!active) return;
            var control = action.activeControl;
            if (control != null) UsingGamepad = control.device is Gamepad;
        }

        Vector2 ReadNavigateVector() => navigateAction != null ? navigateAction.ReadValue<Vector2>() : Vector2.zero;
#else
        static readonly KeyCode[] AttackKeys = { KeyCode.J, KeyCode.Space, KeyCode.JoystickButton2 };
        static readonly KeyCode[] InteractKeys = { KeyCode.E, KeyCode.JoystickButton0 };
        static readonly KeyCode[] UseItemKeys = { KeyCode.Q, KeyCode.JoystickButton3 };
        static readonly KeyCode[] PauseKeys = { KeyCode.Escape, KeyCode.JoystickButton7 };
        static readonly KeyCode[] SubmitKeys = { KeyCode.Return, KeyCode.KeypadEnter, KeyCode.Space, KeyCode.E, KeyCode.J, KeyCode.JoystickButton0 };
        static readonly KeyCode[] CancelKeys = { KeyCode.Escape, KeyCode.Backspace, KeyCode.JoystickButton1 };

        void ReadLegacy()
        {
            Vector2 keys = Vector2.zero;
            if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) keys.x -= 1;
            if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) keys.x += 1;
            if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) keys.y -= 1;
            if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) keys.y += 1;

            Vector2 stick = Vector2.zero;
            if (legacyAxesAvailable)
            {
                try { stick = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")); }
                catch (System.ArgumentException) { legacyAxesAvailable = false; }
            }
            if (stick.magnitude < stickDeadZone) stick = Vector2.zero;

            Vector2 move = keys != Vector2.zero ? keys : stick;
            Move = Vector2.ClampMagnitude(move, 1f);

            AttackPressed = AnyDown(AttackKeys);
            InteractPressed = AnyDown(InteractKeys);
            UseItemPressed = AnyDown(UseItemKeys);
            PausePressed = AnyDown(PauseKeys);
            SubmitPressed = AnyDown(SubmitKeys);
            CancelPressed = AnyDown(CancelKeys);

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

        Vector2 ReadNavigateVector() => Move;
#endif
    }
}
