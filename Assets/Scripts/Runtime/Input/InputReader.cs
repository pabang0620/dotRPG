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
        Inventory,
        Skill1,
        Skill2,
        Skill3,
        Skill4,
        /// <summary>Awakening (ultimate) skill.</summary>
        Skill5,
        /// <summary>Drink a mana potion.</summary>
        UseMana,
        /// <summary>Read a town return scroll.</summary>
        TownScroll,
        Mobility,
        MoveUp, MoveDown, MoveLeft, MoveRight, Map,
        SkillWindow, QuestWindow, WeekdayDungeon, RaidWindow, PartyWindow, PartyFinder, Auction, Friends, Cosmetics,
        /// <summary>[AUTO] HUD toggles: 자동 진행 / 자동 사냥 (HudView reads them).</summary>
        AutoQuest, AutoHunt,
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
    public partial class InputReader : MonoBehaviour
    {
        [Tooltip("Stick/key hold time before menu navigation starts repeating.")]
        [SerializeField] float navigateRepeatDelay = 0.4f;
        [SerializeField] float navigateRepeatInterval = 0.12f;
        [SerializeField] float stickDeadZone = 0.35f;

        public Vector2 Move { get; private set; }
        /// <summary>Automated tests only: when set, replaces the movement read from the devices.</summary>
        public Vector2? MoveOverride { get; set; }
        public bool MobilityPressed { get; private set; }
        public bool AttackPressed { get; private set; }
        public bool InteractPressed { get; private set; }
        public bool UseItemPressed { get; private set; }
        public bool PausePressed { get; private set; }
        public bool SubmitPressed { get; private set; }
        public bool CancelPressed { get; private set; }
        /// <summary>Opens/closes the item &amp; equipment window (I / Tab, gamepad Back/Select).</summary>
        public bool InventoryPressed { get; private set; }
        /// <summary>Opens / closes the big map (M by default).</summary>
        public bool MapPressed { get; private set; }
        public bool Skill1Pressed { get; private set; }
        public bool Skill2Pressed { get; private set; }
        public bool Skill3Pressed { get; private set; }
        public bool Skill4Pressed { get; private set; }
        public bool Skill5Pressed { get; private set; }
        /// <summary>2 / gamepad L3: mana potion.</summary>
        public bool UseManaPressed { get; private set; }
        /// <summary>3: town return scroll.</summary>
        public bool TownScrollPressed { get; private set; }

        /// <summary>[CHARGE] Whether the skill key of a slot (0-4) is held down now (charged skills).</summary>
        public bool SkillHeld(int slot) => slot >= 0 && slot < held.Length && held[slot];
        readonly bool[] held = new bool[5];

        /// <summary>Whether the skill key of a slot (0-4) was pressed this frame.</summary>
        public bool SkillPressed(int slot) => slot == 0 ? Skill1Pressed : slot == 1 ? Skill2Pressed : slot == 2 ? Skill3Pressed : slot == 3 ? Skill4Pressed : slot == 4 && Skill5Pressed;

        /// <summary>Discrete menu navigation step for this frame (with key repeat). (0,0) when idle.</summary>
        public Vector2Int NavigateStep { get; private set; }

        /// <summary>True when the most recent input came from a gamepad. Used to pick button prompts.</summary>
        public bool UsingGamepad { get; private set; }

        Vector2Int heldNavigate;
        float nextRepeatTime;

#if ENABLE_INPUT_SYSTEM
        InputActionMap map;
        InputAction mapAction;
        InputAction moveAction, attackAction, interactAction, useItemAction, pauseAction, submitAction, cancelAction, navigateAction, inventoryAction, skill1Action, skill2Action, skill3Action, skill4Action, skill5Action, manaAction, scrollAction, mobilityAction;
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
            ApplyTouch();
            if (TextInputActive) ClearForTyping();
            UpdateNavigateRepeat();
        }

        /// <summary>[F5] True while a chat field has focus: typed letters must not swing the sword or open the bag.</summary>
        public static bool TextInputActive;

        void ClearForTyping()
        {
            Move = Vector2.zero;
            MobilityPressed = AttackPressed = InteractPressed = UseItemPressed = SubmitPressed = InventoryPressed = MapPressed = false;
            Skill1Pressed = Skill2Pressed = Skill3Pressed = Skill4Pressed = Skill5Pressed = UseManaPressed = TownScrollPressed = false;
            PausePressed = CancelPressed = false;
            for (int i = 0; i < held.Length; i++) held[i] = false;
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
                    {
                        // [UI] Short names for the stick presses. Note: this block only compiles with the new Input System
                        // (the project runs the legacy input manager, activeInputHandler 0), so the L3 / R3 labels shown in
                        // play come from DefaultLabel below.
                        if (binding.effectivePath.EndsWith("/leftStickPress")) return "L3";
                        if (binding.effectivePath.EndsWith("/rightStickPress")) return "R3";
                        return inputAction.GetBindingDisplayString(i);
                    }
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
            if (!gamepad && action == GameAction.Move) return MovementLabel();
            if (!gamepad && System.Array.IndexOf(Rebindable, action) >= 0) return KeyLabel(KeyboardKey(action));
            switch (action)
            {
                case GameAction.Move: return gamepad ? "L스틱" : "방향키";
                case GameAction.Mobility: return gamepad ? "B" : "Shift";
                case GameAction.Attack: return gamepad ? "X" : "X";
                case GameAction.Interact: return gamepad ? "A" : "F";
                case GameAction.UseItem: return gamepad ? "Y" : "1";
                case GameAction.Pause: return gamepad ? "Start" : "ESC";
                case GameAction.Submit: return gamepad ? "A" : "Enter";
                case GameAction.Cancel: return gamepad ? "B" : "ESC";
                case GameAction.Inventory: return gamepad ? "Select" : "I";
                case GameAction.Skill1: return gamepad ? "LB" : "Q";
                case GameAction.Skill2: return gamepad ? "RB" : "W";
                case GameAction.Skill3: return gamepad ? "LT" : "E";
                case GameAction.Skill4: return gamepad ? "RT" : "R";
                case GameAction.Skill5: return gamepad ? "R3" : "T";
                case GameAction.UseMana: return gamepad ? "L3" : "2";
                case GameAction.TownScroll: return gamepad ? "-" : "3";
                default: return "?";
            }
        }

#if ENABLE_INPUT_SYSTEM
        InputAction GetAction(GameAction action)
        {
            switch (action)
            {
                case GameAction.Move: return moveAction;
                case GameAction.Mobility: return mobilityAction;
                case GameAction.Attack: return attackAction;
                case GameAction.Interact: return interactAction;
                case GameAction.UseItem: return useItemAction;
                case GameAction.Pause: return pauseAction;
                case GameAction.Submit: return submitAction;
                case GameAction.Cancel: return cancelAction;
                case GameAction.Inventory: return inventoryAction;
                case GameAction.Skill1: return skill1Action;
                case GameAction.Skill2: return skill2Action;
                case GameAction.Skill3: return skill3Action;
                case GameAction.Skill4: return skill4Action;
                case GameAction.Skill5: return skill5Action;
                case GameAction.UseMana: return manaAction;
                case GameAction.TownScroll: return scrollAction;
                default: return null;
            }
        }

        void CreateActions()
        {
            map = new InputActionMap("Game");

            moveAction = map.AddAction("Move", InputActionType.Value);
            AddArrows(moveAction);
            moveAction.AddBinding("<Gamepad>/leftStick");
            moveAction.AddBinding("<Gamepad>/dpad");

            navigateAction = map.AddAction("Navigate", InputActionType.Value);
            AddArrows(navigateAction);
            navigateAction.AddBinding("<Gamepad>/leftStick");
            navigateAction.AddBinding("<Gamepad>/dpad");

            mobilityAction = map.AddAction("Mobility", InputActionType.Button);
            mobilityAction.AddBinding("<Keyboard>/leftShift");
            mobilityAction.AddBinding("<Keyboard>/rightShift");
            mobilityAction.AddBinding("<Gamepad>/buttonEast");

            attackAction = map.AddAction("Attack", InputActionType.Button);
            attackAction.AddBinding("<Keyboard>/x");
            attackAction.AddBinding("<Gamepad>/buttonWest");

            interactAction = map.AddAction("Interact", InputActionType.Button);
            interactAction.AddBinding("<Keyboard>/f");
            interactAction.AddBinding("<Gamepad>/buttonSouth");

            useItemAction = map.AddAction("UseItem", InputActionType.Button);
            useItemAction.AddBinding("<Keyboard>/1");
            useItemAction.AddBinding("<Gamepad>/buttonNorth");

            pauseAction = map.AddAction("Pause", InputActionType.Button);
            pauseAction.AddBinding("<Keyboard>/escape");
            pauseAction.AddBinding("<Gamepad>/start");

            submitAction = map.AddAction("Submit", InputActionType.Button);
            submitAction.AddBinding("<Keyboard>/enter");
            submitAction.AddBinding("<Keyboard>/space");
            submitAction.AddBinding("<Keyboard>/f");
            submitAction.AddBinding("<Keyboard>/x");
            submitAction.AddBinding("<Gamepad>/buttonSouth");

            cancelAction = map.AddAction("Cancel", InputActionType.Button);
            cancelAction.AddBinding("<Keyboard>/escape");
            cancelAction.AddBinding("<Keyboard>/backspace");
            cancelAction.AddBinding("<Gamepad>/buttonEast");

            mapAction = map.AddAction("Map", InputActionType.Button);
            mapAction.AddBinding("<Keyboard>/m");
            inventoryAction = map.AddAction("Inventory", InputActionType.Button);
            inventoryAction.AddBinding("<Keyboard>/i");
            inventoryAction.AddBinding("<Keyboard>/tab");
            inventoryAction.AddBinding("<Gamepad>/select");

            skill1Action = map.AddAction("Skill1", InputActionType.Button);
            skill1Action.AddBinding("<Keyboard>/q");
            skill1Action.AddBinding("<Gamepad>/leftShoulder");
            skill2Action = map.AddAction("Skill2", InputActionType.Button);
            skill2Action.AddBinding("<Keyboard>/w");
            skill2Action.AddBinding("<Gamepad>/rightShoulder");
            skill3Action = map.AddAction("Skill3", InputActionType.Button);
            skill3Action.AddBinding("<Keyboard>/e");
            skill3Action.AddBinding("<Gamepad>/leftTrigger");
            skill4Action = map.AddAction("Skill4", InputActionType.Button);
            skill4Action.AddBinding("<Keyboard>/r");
            skill4Action.AddBinding("<Gamepad>/rightTrigger");
            skill5Action = map.AddAction("Skill5", InputActionType.Button);
            skill5Action.AddBinding("<Keyboard>/t");
            skill5Action.AddBinding("<Gamepad>/rightStickPress");

            manaAction = map.AddAction("UseMana", InputActionType.Button);
            manaAction.AddBinding("<Keyboard>/2");
            manaAction.AddBinding("<Gamepad>/leftStickPress");
            scrollAction = map.AddAction("TownScroll", InputActionType.Button);
            scrollAction.AddBinding("<Keyboard>/3");
        }

        static void AddArrows(InputAction action)
        {
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
            Move = MoveOverride ?? Vector2.ClampMagnitude(move, 1f);

            MobilityPressed = mobilityAction.WasPressedThisFrame();
            TrackDevice(mobilityAction, MobilityPressed);
            AttackPressed = attackAction.WasPressedThisFrame();
            InteractPressed = interactAction.WasPressedThisFrame();
            UseItemPressed = useItemAction.WasPressedThisFrame();
            PausePressed = pauseAction.WasPressedThisFrame();
            SubmitPressed = submitAction.WasPressedThisFrame();
            CancelPressed = cancelAction.WasPressedThisFrame();
            InventoryPressed = inventoryAction.WasPressedThisFrame();
            MapPressed = mapAction.WasPressedThisFrame();
            Skill1Pressed = skill1Action.WasPressedThisFrame();
            Skill2Pressed = skill2Action.WasPressedThisFrame();
            Skill3Pressed = skill3Action.WasPressedThisFrame();
            Skill4Pressed = skill4Action.WasPressedThisFrame();
            Skill5Pressed = skill5Action.WasPressedThisFrame();
            held[0] = skill1Action.IsPressed(); held[1] = skill2Action.IsPressed(); held[2] = skill3Action.IsPressed();
            held[3] = skill4Action.IsPressed(); held[4] = skill5Action.IsPressed();
            UseManaPressed = manaAction.WasPressedThisFrame();
            TownScrollPressed = scrollAction.WasPressedThisFrame();

            TrackDevice(moveAction, Move != Vector2.zero);
            TrackDevice(attackAction, AttackPressed);
            TrackDevice(interactAction, InteractPressed);
            TrackDevice(submitAction, SubmitPressed);
            TrackDevice(pauseAction, PausePressed);
            TrackDevice(skill1Action, Skill1Pressed);
            TrackDevice(skill2Action, Skill2Pressed);
            TrackDevice(skill3Action, Skill3Pressed);
            TrackDevice(skill4Action, Skill4Pressed);
            TrackDevice(skill5Action, Skill5Pressed);
            TrackDevice(useItemAction, UseItemPressed);
            TrackDevice(manaAction, UseManaPressed);
            TrackDevice(scrollAction, TownScrollPressed);
        }

        void TrackDevice(InputAction action, bool active)
        {
            if (!active) return;
            var control = action.activeControl;
            if (control != null) UsingGamepad = control.device is Gamepad;
        }

        Vector2 ReadNavigateVector() => navigateAction != null ? navigateAction.ReadValue<Vector2>() : Vector2.zero;
#else
#endif
    }
}
