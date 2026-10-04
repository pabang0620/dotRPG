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

        // [I] Accessibility and keys.
        /// <summary>UI size: 0 = 작게 (90%), 1 = 보통, 2 = 크게 (115%), 3 = 아주 크게 (130%).</summary>
        public int uiScale = 1;
        /// <summary>Colour-blind friendly danger zones and bars.</summary>
        public bool colorBlind;
        /// <summary>Loot filter: dropped items of these kinds are left on the ground (Pickup.Skipped).</summary>
        public bool skipCommonGear, skipUncommonGear, skipConsumables, skipMaterials;
        /// <summary>Rebound keyboard keys ("Attack=Z;Skill1=A"), see InputReader.SaveKeyOverrides.</summary>
        public string keyOverrides = "";

        /// <summary>[F5] Comma-separated names (sample friends until an account server exists).</summary>
        public string chatFriends = "검은뿔,달빛마녀";
        public string chatBlocked = "";
    }
}
