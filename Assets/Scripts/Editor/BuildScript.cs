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

        [MenuItem("dotRPG/Build/macOS", priority = 21)]
        public static void BuildMac() => Build(BuildTarget.StandaloneOSX, "macOS", ProjectSetup.ProductName + ".app");

        [MenuItem("dotRPG/Build/Linux (x64)", priority = 22)]
        public static void BuildLinux() => Build(BuildTarget.StandaloneLinux64, "Linux", ProjectSetup.ProductName + ".x86_64");

        static void Build(BuildTarget target, string folder, string executable)
        {
            ProjectSetup.Apply();
            string output = Path.Combine("Builds", folder, executable);
            var options = new BuildPlayerOptions
            {
                scenes = new[] { ProjectSetup.MainScene },
                locationPathName = output,
                target = target,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            var summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log($"[dotRPG] Build succeeded: {output} ({summary.totalSize / (1024 * 1024)} MB)");
            }
            else
            {
                Debug.LogError($"[dotRPG] Build {summary.result}: {summary.totalErrors} error(s).");
                if (Application.isBatchMode) EditorApplication.Exit(1);
            }
        }
    }
}
