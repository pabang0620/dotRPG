using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Loads, applies and saves <see cref="SettingsData"/>. UI edits <see cref="Data"/> and calls
    /// <see cref="Apply"/>/<see cref="Save"/>; no other system writes settings.
    /// </summary>
    public sealed class SettingsManager
    {
        public SettingsData Data { get; private set; } = new SettingsData();
        public event Action Applied;

        public static string FilePath => Path.Combine(Application.persistentDataPath, "settings.json");

        public void Load()
        {
            try
            {
                if (File.Exists(FilePath))
                {
                    var loaded = JsonUtility.FromJson<SettingsData>(File.ReadAllText(FilePath));
                    if (loaded != null) Data = loaded;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[dotRPG] Settings file unreadable, using defaults: {e.Message}");
                Data = new SettingsData();
            }
            Data.masterVolume = Mathf.Clamp01(Data.masterVolume);
            Data.musicVolume = Mathf.Clamp01(Data.musicVolume);
            Data.sfxVolume = Mathf.Clamp01(Data.sfxVolume);
            Data.version = SettingsData.CurrentVersion;
        }

        public void Save()
        {
            try
            {
                if (Game.Input != null) Data.bindingOverridesJson = Game.Input.SaveBindingOverrides();
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonUtility.ToJson(Data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[dotRPG] Could not save settings: {e.Message}");
            }
        }

        /// <summary>Pushes the current settings to the engine (audio, display, frame pacing).</summary>
        public void Apply()
        {
            AudioListener.volume = Data.masterVolume;
            if (Game.Audio != null) Game.Audio.SetVolumes(Data.musicVolume, Data.sfxVolume);

            QualitySettings.vSyncCount = Data.vSync ? 1 : 0;
            Application.targetFrameRate = Data.vSync ? -1 : Mathf.Max(30, Data.targetFrameRate);

#if !UNITY_EDITOR
            ApplyDisplay();
#endif
            Applied?.Invoke();
        }

        void ApplyDisplay()
        {
            var mode = ToFullScreenMode(Data.windowMode);
            int width = Data.resolutionWidth;
            int height = Data.resolutionHeight;
            if (width <= 0 || height <= 0)
            {
                width = Display.main.systemWidth;
                height = Display.main.systemHeight;
                if (mode == FullScreenMode.Windowed)
                {
                    // A native-sized window would hide behind the taskbar; default to 75%.
                    width = Mathf.RoundToInt(width * 0.75f);
                    height = Mathf.RoundToInt(height * 0.75f);
                }
            }
            Screen.SetResolution(width, height, mode);
        }

        public static FullScreenMode ToFullScreenMode(WindowMode mode)
        {
            switch (mode)
            {
                case WindowMode.ExclusiveFullscreen: return FullScreenMode.ExclusiveFullScreen;
                case WindowMode.Windowed: return FullScreenMode.Windowed;
                default: return FullScreenMode.FullScreenWindow;
            }
        }

        public static string WindowModeLabel(WindowMode mode)
        {
            switch (mode)
            {
                case WindowMode.ExclusiveFullscreen: return "전체 화면";
                case WindowMode.Windowed: return "창 모드";
                default: return "테두리 없는 창";
            }
        }

        /// <summary>
        /// Resolution choices for the settings menu. Index 0 is always "native".
        /// </summary>
        public static List<Vector2Int> GetResolutionOptions()
        {
            var list = new List<Vector2Int> { Vector2Int.zero };
            foreach (var r in Screen.resolutions)
            {
                var size = new Vector2Int(r.width, r.height);
                if (size.x < 1024 || size.y < 576 || list.Contains(size)) continue;
                list.Add(size);
            }
            foreach (var size in new[] { new Vector2Int(1280, 720), new Vector2Int(1600, 900), new Vector2Int(1920, 1080), new Vector2Int(2560, 1440) })
            {
                if (!list.Contains(size)) list.Add(size);
            }
            // "Native" (0x0) naturally sorts first because its pixel count is 0.
            list.Sort((a, b) => a.x * a.y != b.x * b.y ? (a.x * a.y).CompareTo(b.x * b.y) : a.x.CompareTo(b.x));
            return list;
        }

        public static string ResolutionLabel(Vector2Int size) => size == Vector2Int.zero ? "기본(모니터)" : $"{size.x} x {size.y}";
    }
}
