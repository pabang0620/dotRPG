using System;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// JSON save slots under Application.persistentDataPath/Saves.
    /// Writes go to a temp file first and the previous save is kept as .bak, so a crash or power
    /// loss mid-write never destroys progress. This folder is also what Steam Auto-Cloud should sync.
    /// </summary>
    public sealed class SaveSystem
    {
        public const int DefaultSlot = 0;

        public static string SaveDirectory => Path.Combine(Application.persistentDataPath, "Saves");

        public static string SlotPath(int slot) => Path.Combine(SaveDirectory, $"slot_{slot}.json");

        public bool HasSave(int slot = DefaultSlot) => File.Exists(SlotPath(slot)) || File.Exists(SlotPath(slot) + ".bak");

        public bool Write(SaveData data, int slot = DefaultSlot)
        {
            string path = SlotPath(slot);
            string temp = path + ".tmp";
            string backup = path + ".bak";
            try
            {
                Directory.CreateDirectory(SaveDirectory);
                data.version = SaveData.CurrentVersion;
                data.savedAtUtc = DateTime.UtcNow.ToString("o");
                File.WriteAllText(temp, JsonUtility.ToJson(data, true));
                if (File.Exists(path))
                {
                    if (File.Exists(backup)) File.Delete(backup);
                    File.Move(path, backup);
                }
                File.Move(temp, path);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[dotRPG] Save failed: {e}");
                return false;
            }
        }

        public SaveData Read(int slot = DefaultSlot)
        {
            string path = SlotPath(slot);
            return TryRead(path) ?? TryRead(path + ".bak");
        }

        public void Delete(int slot = DefaultSlot)
        {
            string path = SlotPath(slot);
            foreach (var p in new[] { path, path + ".bak", path + ".tmp" })
            {
                try { if (File.Exists(p)) File.Delete(p); }
                catch (Exception e) { Debug.LogWarning($"[dotRPG] Could not delete {p}: {e.Message}"); }
            }
        }

        static SaveData TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                return data != null ? Migrate(data) : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[dotRPG] Save file {path} unreadable: {e.Message}");
                return null;
            }
        }

        /// <summary>Upgrades older save formats. Add a case per version bump.</summary>
        static SaveData Migrate(SaveData data)
        {
            if (data.version > SaveData.CurrentVersion)
                Debug.LogWarning("[dotRPG] Save was written by a newer version of the game.");
            if (data.inventory == null) data.inventory = new System.Collections.Generic.List<ItemStack>();
            if (data.quest == null) data.quest = new QuestProgress();
            data.version = SaveData.CurrentVersion;
            return data;
        }
    }
}
