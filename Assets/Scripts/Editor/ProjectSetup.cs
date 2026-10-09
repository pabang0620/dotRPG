using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build;
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

        public const string AndroidApplicationId = "com.dotrpg.game";

        /// <summary>
        /// [ANDROID] Applied right before an Android build (BuildScript), after <see cref="Apply"/> so the PC window values set
        /// there are never touched. Android-only settings (id, scripting backend, ARM64, SDK levels) stay. Settings that every
        /// platform shares (screen orientation list, insecureHttpOption, AAB switch) are changed for this build only: the
        /// returned action puts them back, so the PC builds keep the values they had.
        /// </summary>
        public static Action ApplyAndroid(bool development)
        {
            var orientation = PlayerSettings.defaultInterfaceOrientation;
            bool left = PlayerSettings.allowedAutorotateToLandscapeLeft, right = PlayerSettings.allowedAutorotateToLandscapeRight;
            bool portrait = PlayerSettings.allowedAutorotateToPortrait, upsideDown = PlayerSettings.allowedAutorotateToPortraitUpsideDown;
            var http = PlayerSettings.insecureHttpOption;
            bool bundle = EditorUserBuildSettings.buildAppBundle;

            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, AndroidApplicationId);
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            // Unity 6000.5 ships Android platform 36. Pinning 35 makes Gradle try to install it into
            // Unity's read-only SDK under Program Files, which fails before packaging the APK.
            PlayerSettings.Android.targetSdkVersion = (AndroidSdkVersions)36;

            // Landscape only (left or right, whichever way the phone is held). Standalone players ignore these.
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortrait = false;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;

            // http:// servers (a PC on the LAN) only in the development apk; the store build must use https.
            PlayerSettings.insecureHttpOption = development ? InsecureHttpOption.AlwaysAllowed : InsecureHttpOption.NotAllowed;
            EditorUserBuildSettings.buildAppBundle = !development;

            ArtImportSettings.ApplyAndroidToExisting();
            AssetDatabase.SaveAssets();
            return () =>
            {
                PlayerSettings.defaultInterfaceOrientation = orientation;
                PlayerSettings.allowedAutorotateToLandscapeLeft = left;
                PlayerSettings.allowedAutorotateToLandscapeRight = right;
                PlayerSettings.allowedAutorotateToPortrait = portrait;
                PlayerSettings.allowedAutorotateToPortraitUpsideDown = upsideDown;
                PlayerSettings.insecureHttpOption = http;
                EditorUserBuildSettings.buildAppBundle = bundle;
                AssetDatabase.SaveAssets();
            };
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
