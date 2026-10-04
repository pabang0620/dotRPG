using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>
    /// Finds sprite keys the game asks for that have no art (SpriteLibrary falls back to a magenta square).
    /// Keys come from the runtime sources (Art.Get("..."), icon fields, menu icons), every skill gem, item and gear icon.
    /// Batch: -executeMethod DotRPG.EditorTools.ArtKeyCheck.Run  -> Logs/artcheck.txt
    /// </summary>
    public static class ArtKeyCheck
    {
        [MenuItem("dotRPG/Check Missing Art")]
        public static void Run()
        {
            var keys = new SortedSet<string>();
            var src = string.Join("\n", Directory.GetFiles("Assets/Scripts/Runtime", "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));
            foreach (Match m in Regex.Matches(src, @"Art\.Get\(""([a-z0-9_]+)""\)")) keys.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(src, @"""(menuicon_[a-z0-9_]+)""")) keys.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(src, @"(?:icon|iconKey) = ""([a-z0-9_]+)""")) keys.Add(m.Groups[1].Value);
            foreach (var g in SkillGems.All) if (!string.IsNullOrEmpty(g.icon)) keys.Add(g.icon);
            foreach (var e in EquipmentDatabase.All) if (!string.IsNullOrEmpty(e.iconKey)) keys.Add(e.iconKey);
            foreach (var m in EquipmentDatabase.AllMaterials) if (!string.IsNullOrEmpty(m.iconKey)) keys.Add(m.iconKey);
            var config = Resources.Load<GameConfig>("Data/GameConfig");
            if (config != null) foreach (var i in config.items) if (!string.IsNullOrEmpty(i.iconKey)) keys.Add(i.iconKey);

            var missing = new List<string>();
            void OnLog(string message, string stack, LogType type)
            {
                var m = Regex.Match(message, @"No sprite for key '([^']+)'");
                if (m.Success) missing.Add(m.Groups[1].Value);
            }
            Application.logMessageReceived += OnLog;
            var lib = new SpriteLibrary(16);
            foreach (var k in keys)
            {
                try { lib.Get(k); }
                catch (System.Exception ex) { missing.Add(k + " (error: " + ex.GetType().Name + ")"); }
            }
            Application.logMessageReceived -= OnLog;

            var report = $"checked {keys.Count} keys\nmissing {missing.Count}:\n" + string.Join("\n", missing);
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/artcheck.txt", report);
            Debug.Log("[ArtKeyCheck] " + report);
        }
    }
}
