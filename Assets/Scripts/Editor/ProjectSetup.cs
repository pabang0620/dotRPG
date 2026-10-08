using System.IO;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// Applies the project's PC player settings (product name, window defaults, build scene list).
    /// Runs automatically the first time the project is opened, and from the menu at any time.
    /// ProjectSettings.asset is intentionally not hand-written in the repository; Unity generates it
    /// and this script sets the values that matter.
    /// </summary>
    [InitializeOnLoad]
    public static class ProjectSetup
    {
        public const string ProductName = "dotRPG";
        public const string CompanyName = "dotRPG Team";
        public const string Version = "0.1.0";
        public const string MainScene = "Assets/Scenes/Main.unity";

        static ProjectSetup()
        {
            EditorApplication.delayCall += () =>
            {
                if (PlayerSettings.productName != ProductName) Apply();
            };
        }

        [MenuItem("dotRPG/Apply Project Settings", priority = 0)]
        public static void Apply()
        {
            PlayerSettings.productName = ProductName;
            PlayerSettings.companyName = CompanyName;
            PlayerSettings.bundleVersion = Version;

            // PC window defaults (players can change these in the in-game settings menu).
            PlayerSettings.fullScreenMode = FullScreenMode.FullScreenWindow;
            PlayerSettings.defaultIsNativeResolution = true;
            PlayerSettings.defaultScreenWidth = 1920;
            PlayerSettings.defaultScreenHeight = 1080;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
            PlayerSettings.visibleInBackground = true;
            PlayerSettings.allowFullscreenSwitch = true;
            PlayerSettings.usePlayerLog = true;
            ApplyAppIcon();

            if (File.Exists(MainScene))
            {
                EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(MainScene, true) };
            }

            AssetDatabase.SaveAssets();
            Debug.Log("[dotRPG] Project settings applied (product name, PC window defaults, icon, build scenes).");
        }

        /// <summary>Game icon for the exe, taskbar and window title bar (instead of the Unity logo).</summary>
        public const string AppIcon = "Assets/Art/AppIcon/app_icon_1024.png";

        static void ApplyAppIcon()
        {
            if (!File.Exists(AppIcon)) return;
            AssetDatabase.ImportAsset(AppIcon);
            if (AssetImporter.GetAtPath(AppIcon) is TextureImporter importer
                && (importer.textureCompression != TextureImporterCompression.Uncompressed || importer.mipmapEnabled))
            {
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = false;
                importer.SaveAndReimport();
            }
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(AppIcon);
            if (icon != null) PlayerSettings.SetIcons(UnityEditor.Build.NamedBuildTarget.Unknown, new[] { icon }, IconKind.Any);
        }
    }
}
