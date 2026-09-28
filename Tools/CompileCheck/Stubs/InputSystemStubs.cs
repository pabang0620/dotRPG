// Compile-check stubs for the com.unity.inputsystem package (only the members dotRPG uses).
// Signatures mirror Input System 1.x; bodies are empty. Never included in the Unity project.
using System;

namespace UnityEngine.InputSystem.Utilities
{
    public struct ReadOnlyArray<TValue>
    {
        public int Count => 0;
        public TValue this[int index] => default;
    }
}

namespace UnityEngine.InputSystem
{
    using UnityEngine.InputSystem.Utilities;

    public enum InputActionType { Value, Button, PassThrough }

    public abstract class InputControl
    {
        public InputDevice device => null;
    }

    public class InputDevice : InputControl { }

    public class Gamepad : InputDevice { }

    public class Keyboard : InputDevice { }

    public struct InputBinding
    {
        [Flags]
        public enum DisplayStringOptions { DontUseShortDisplayNames = 1, DontOmitDevice = 2, DontIncludeInteractions = 4, IgnoreBindingOverrides = 8 }
        public bool isComposite => false;
        public bool isPartOfComposite => false;
        public string effectivePath => null;
    }

    public interface IInputActionCollection2 { }

    public sealed class InputAction
    {
        public ReadOnlyArray<InputBinding> bindings => default;
        public InputControl activeControl => null;
        public TValue ReadValue<TValue>() where TValue : struct => default;
        public bool WasPressedThisFrame() => false;
    }

    public sealed class InputActionMap : IInputActionCollection2, IDisposable
    {
        public InputActionMap(string name = null) { }
        public void Enable() { }
        public void Disable() { }
        public void Dispose() { }
    }

    public static class InputActionSetupExtensions
    {
        public static InputAction AddAction(this InputActionMap map, string name, InputActionType type = default, string binding = null,
            string interactions = null, string processors = null, string groups = null, string expectedControlLayout = null) => null;

        public static BindingSyntax AddBinding(this InputAction action, string path, string interactions = null, string processors = null,
            string groups = null) => default;

        public static CompositeSyntax AddCompositeBinding(this InputAction action, string composite, string interactions = null,
            string processors = null) => default;

        public struct BindingSyntax { }

        public struct CompositeSyntax
        {
            public CompositeSyntax With(string name, string binding, string groups = null, string processors = null) => this;
        }
    }

    public static class InputActionRebindingExtensions
    {
        public static string GetBindingDisplayString(this InputAction action, int bindingIndex,
            InputBinding.DisplayStringOptions options = default) => null;

        public static void LoadBindingOverridesFromJson(this IInputActionCollection2 actions, string json, bool removeExisting = true) { }
        public static string SaveBindingOverridesAsJson(this IInputActionCollection2 actions) => null;
        public static void RemoveAllBindingOverrides(this IInputActionCollection2 actions) { }
    }
}

namespace UnityEngine.InputSystem.UI
{
    public class InputSystemUIInputModule : UnityEngine.EventSystems.BaseInputModule
    {
        public void AssignDefaultActions() { }
    }
}
