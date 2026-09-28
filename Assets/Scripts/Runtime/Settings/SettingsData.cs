using System;

namespace DotRPG
{
    public enum WindowMode
    {
        /// <summary>Borderless window covering the screen (recommended default on PC).</summary>
        FullscreenWindow = 0,
        ExclusiveFullscreen = 1,
        Windowed = 2,
    }

    /// <summary>
    /// Player preferences, saved separately from game progress (settings.json).
    /// Add new options as fields with a default value; old files simply keep the default.
    /// </summary>
    [Serializable]
    public class SettingsData
    {
        public const int CurrentVersion = 1;

        public int version = CurrentVersion;

        // Audio (0..1)
        public float masterVolume = 0.8f;
        public float musicVolume = 0.6f;
        public float sfxVolume = 0.8f;

        // Display. 0x0 means "use the monitor's native resolution".
        public int resolutionWidth;
        public int resolutionHeight;
        public WindowMode windowMode = WindowMode.FullscreenWindow;
        public bool vSync = true;
        public int targetFrameRate = 60;

        // Gameplay / accessibility
        public bool screenShake = true;
        public string language = "ko";

        // Input System binding overrides (JSON produced by InputActionMap.SaveBindingOverridesAsJson).
        public string bindingOverridesJson = "";
    }
}
