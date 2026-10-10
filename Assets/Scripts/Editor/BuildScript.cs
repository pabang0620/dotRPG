using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// One-click / command-line PC builds. Command line example:
    ///   Unity -batchmode -quit -projectPath . -executeMethod DotRPG.EditorTools.BuildScript.BuildWindows
    /// Output goes to Builds/{platform}/ (ignored by git). Steam depots can later point at these folders.
    /// </summary>
    public static class BuildScript
    {
        [MenuItem("dotRPG/Build/Windows (x64)", priority = 20)]
        public static void BuildWindows() => Build(BuildTarget.StandaloneWindows64, "Windows", ProjectSetup.ProductName + ".exe");

        /// <summary>
        /// [PARTY 8] Steam test build: Valve's test app 480 (steam_appid.txt next to the exe) so two PCs with
        /// Steam can play over Steam P2P before the game has its own app id. Never ship this one.
        /// </summary>
        [MenuItem("dotRPG/Build/Windows Steam test (app 480)", priority = 23)]
        public static void BuildWindowsSteamTest() => Build(BuildTarget.StandaloneWindows64, "WindowsSteamTest", ProjectSetup.ProductName + ".exe", steamTest: true);

        /// <summary>
        /// [RELEASE] The build that ships: define DOTRPG_RELEASE (no capture / dev network modes, server address fixed to
        /// ApiClient.ReleaseServer). Refuses to build while that address is not https.
        /// </summary>
        [MenuItem("dotRPG/Build/Windows release (ships)", priority = 19)]
        public static void BuildWindowsRelease() => Build(BuildTarget.StandaloneWindows64, "WindowsRelease", ProjectSetup.ProductName + ".exe", release: true);

        [MenuItem("dotRPG/Build/macOS", priority = 21)]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "macOS", ProjectSetup.ProductName + ".app");

        [MenuItem("dotRPG/Build/Linux (x64)", priority = 22)]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Linux", ProjectSetup.ProductName + ".x86_64");

        /// <summary>[ANDROID] Development apk (Development build, http servers allowed) for testing on a phone over USB.</summary>
        [MenuItem("dotRPG/Build/Android dev apk", priority = 24)]
        public static void BuildAndroidDev() => BuildAndroid(release: false);

        /// <summary>[ANDROID] Play Store bundle (aab): DOTRPG_RELEASE, https server only, needs a keystore in Publishing Settings.</summary>
        [MenuItem("dotRPG/Build/Android release (aab)", priority = 25)]
        public static void BuildAndroidRelease() => BuildAndroid(release: true);

        static void BuildAndroid(bool release)
        {
            if (release && !ApiClient.ReleaseServer.StartsWith("https://", StringComparison.Ordinal))
            {
                FailAndroid("Release build needs the live server address: set ApiClient.ReleaseServer (https://...).");
                return;
            }
            if (release && (string.IsNullOrEmpty(PlayerSettings.Android.keystoreName) || !File.Exists(PlayerSettings.Android.keystoreName)
                || string.IsNullOrEmpty(PlayerSettings.Android.keyaliasName)))
            {
                FailAndroid("Release build needs a keystore: Project Settings > Player > Android > Publishing Settings (custom keystore file, alias and passwords). See Docs/ANDROID.md.");
                return;
            }
            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
            {
                FailAndroid("Could not switch to Android: install Android Build Support (with OpenJDK, SDK and NDK) in Unity Hub. See Docs/ANDROID.md.");
                return;
            }
            ProjectSetup.Apply();
            var restore = ProjectSetup.ApplyAndroid(development: !release);
            try
            {
                string output = Path.Combine("Builds", "Android", release ? ProjectSetup.ProductName + ".aab" : ProjectSetup.ProductName + "-dev.apk");
                var options = new BuildPlayerOptions
                {
                    scenes = new[] { ProjectSetup.MainScene },
                    locationPathName = output,
                    target = BuildTarget.Android,
                    options = (release ? BuildOptions.None : BuildOptions.Development) | CleanFlag(),
                    extraScriptingDefines = release ? new[] { "DOTRPG_RELEASE" } : null,
                };
                var summary = BuildPipeline.BuildPlayer(options).summary;
                if (summary.result == BuildResult.Succeeded) Debug.Log($"[dotRPG] Build succeeded: {output} ({summary.totalSize / (1024 * 1024)} MB)");
                else FailAndroid($"Build {summary.result}: {summary.totalErrors} error(s).");
            }
            finally { restore(); }
        }

        /// <summary>
        /// -dotrpgCleanBuild on the command line: rebuild without the build cache. Gradle patches the previous apk in place,
        /// so after large assets are removed the file keeps their space as dead bytes (2026-10-11: 869 MB file, 375 MB of entries).
        /// </summary>
        static BuildOptions CleanFlag() => Array.IndexOf(Environment.GetCommandLineArgs(), "-dotrpgCleanBuild") >= 0 ? BuildOptions.CleanBuildCache : BuildOptions.None;

        static void FailAndroid(string message)
        {
            Debug.LogError("[dotRPG] " + message);
            if (Application.isBatchMode) EditorApplication.Exit(1);
        }

        static void Build(BuildTarget target, string folder, string executable, bool steamTest = false, bool release = false)
        {
            if (release && !ApiClient.ReleaseServer.StartsWith("https://", StringComparison.Ordinal))
            {
                Debug.LogError("[dotRPG] Release build needs the live server address: set ApiClient.ReleaseServer (https://...).");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                return;
            }
            ProjectSetup.Apply();
            string output = Path.Combine("Builds", folder, executable);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.MainScene },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
                extraScriptingDefines = release ? new[] { "DOTRPG_RELEASE" } : null,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[dotRPG] Build succeeded: {output} ({summary.totalSize / (1024 * 1024)} MB)");
                // [PARTY 8] Test app id only in the test build; a release build must not carry it (Steam decides the id).
                string appIdFile = Path.Combine("Builds", folder, "steam_appid.txt");
                if (steamTest) File.WriteAllText(appIdFile, "480");
                else if (File.Exists(appIdFile))
                {
                    Debug.LogError("[dotRPG] steam_appid.txt must not ship in a release build: " + appIdFile);
                    if (Application.isBatchMode) EditorApplication.Exit(1);
                }
            }
            else
            {
                Debug.LogError($"[dotRPG] Build {summary.result}: {summary.totalErrors} error(s).");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }
}
