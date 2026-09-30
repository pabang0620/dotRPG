using System;
using System.Collections.Generic;
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

        /// <summary>Set by automated test runs so they save into their own folder and never touch the player's progress.</summary>
        public static string DirectoryOverride;

        public static string SaveDirectory => DirectoryOverride ?? Path.Combine(Application.persistentDataPath, "Saves");

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

        /// <summary>
        /// Upgrades older save formats (every save read goes through here). Add a case per version bump.
        /// Public so automated checks can run hand-built old saves through the real migration.
        /// </summary>
        public static SaveData Migrate(SaveData data)
        {
            if (data.version > SaveData.CurrentVersion)
                Debug.LogWarning("[dotRPG] Save was written by a newer version of the game.");
            if (data.inventory == null) data.inventory = new List<ItemStack>();
            if (data.quest == null) data.quest = new QuestProgress();
            if (string.IsNullOrEmpty(data.mapId)) data.mapId = MapRegistry.Village;
            if (string.IsNullOrEmpty(data.playerClass)) data.playerClass = "warrior";
            if (data.version < 2)
            {
                // v2 switched from half-heart units to HP points (×10).
                data.playerHealth *= 10;
                data.playerMaxHealth *= 10;
                if (data.level < 1) data.level = 1;
            }
            if (data.storage == null) data.storage = new List<ItemStack>();
            if (data.version < 3)
            {
                // v3 added gold, potions and the return scroll: hand out the starter pack once.
                foreach (var (id, count) in ConsumableDatabase.StarterPack)
                    data.inventory.Add(new ItemStack(id, count));
                // The village was rebuilt, so an old position there may now be inside a building.
                if (data.mapId == MapRegistry.Village) data.playerX = data.playerY = -1f;
            }
            if (data.equipped == null) data.equipped = new List<string>();
            if (data.enhancePity == null) data.enhancePity = new List<ItemStack>();
            if (data.version < 4) MigrateEnhanceLevels(data);
            data.version = SaveData.CurrentVersion;
            return data;
        }

        /// <summary>
        /// v4: the +level moved from the item kind (every copy shared it) onto each piece (its key).
        /// Each kind at +L becomes key "{kind}+L" on every worn copy; when none is worn, one copy in the bag
        /// is converted, else one copy in storage. Other copies stay at +0.
        /// </summary>
        public static void MigrateEnhanceLevels(SaveData data)
        {
            if (data.equipped == null) data.equipped = new List<string>();
            if (data.enhanceLevels == null)
            {
                data.enhanceLevels = new List<ItemStack>();
                return;
            }
            foreach (var entry in data.enhanceLevels)
            {
                if (entry == null || entry.count <= 0 || !EquipmentDatabase.IsEquipment(entry.id)) continue;
                string kind = entry.id;
                if (EquipmentDatabase.BaseId(kind) != kind) continue; // v3 only ever stored bare ids
                string key = EquipmentDatabase.KeyFor(kind, Math.Min(entry.count, EquipmentDatabase.MaxEnhance));
                bool worn = false;
                for (int i = 0; i < data.equipped.Count; i++)
                {
                    if (data.equipped[i] != kind) continue;
                    data.equipped[i] = key;
                    worn = true;
                }
                if (!worn && !ConvertOne(data.inventory, kind, key)) ConvertOne(data.storage, kind, key);
            }
            data.enhanceLevels.Clear();
        }

        /// <summary>Turns one unit of <paramref name="id"/> in a stack list into <paramref name="key"/>. False when there is none.</summary>
        static bool ConvertOne(List<ItemStack> stacks, string id, string key)
        {
            if (stacks == null) return false;
            ItemStack from = null, to = null;
            foreach (var s in stacks)
            {
                if (s == null) continue;
                if (from == null && s.id == id && s.count > 0) from = s;
                if (to == null && s.id == key) to = s;
            }
            if (from == null) return false;
            from.count--;
            if (from.count <= 0) stacks.Remove(from);
            if (to != null) to.count++;
            else stacks.Add(new ItemStack(key, 1));
            return true;
        }
    }
}
