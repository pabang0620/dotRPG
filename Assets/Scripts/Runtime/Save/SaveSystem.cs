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
        /// <summary>[I] Three save slots; the game reads and writes <see cref="ActiveSlot"/>.</summary>
        public static int SlotCount { get; internal set; } = 3;
        public static int ActiveSlot = DefaultSlot;
        /// <summary>[I] Set by the last <see cref="Read"/>: why it used the backup or failed (null = clean read).</summary>
        public static string LastReadNotice;

        static int Resolve(int slot) => slot < 0 ? ActiveSlot : slot;

        /// <summary>Set by automated test runs so they save into their own folder and never touch the player's progress.</summary>
        public static string DirectoryOverride;

        public static string SaveDirectory => DirectoryOverride ?? Path.Combine(Application.persistentDataPath, "Saves");

        public static string SlotPath(int slot) => Path.Combine(SaveDirectory, $"slot_{slot}.json");

        public bool HasSave(int slot = -1) => File.Exists(SlotPath(Resolve(slot))) || File.Exists(SlotPath(Resolve(slot)) + ".bak");

        public bool Write(SaveData data, int slot = -1)
        {
            // [SERVER] An online character is saved on the server, never in a local slot (PLAN_ONLINE O1).
            if (OnlineSession.Playing)
            {
                OnlineSession.Current.UploadState(data);
                return true;
            }
            string path = SlotPath(Resolve(slot));
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

        public SaveData Read(int slot = -1)
        {
            string path = SlotPath(Resolve(slot));
            LastReadNotice = null;
            tooNew = false;
            var data = TryRead(path);
            if (data != null) return data;
            if (tooNew)
            {
                // Written by a newer build: do not load it (or its older backup), a save from this build would overwrite it.
                LastReadNotice = "더 새로운 버전의 게임으로 저장된 파일입니다. 게임을 최신 버전으로 업데이트해 주세요.";
                return null;
            }
            data = TryRead(path + ".bak");
            if (data != null && File.Exists(path)) LastReadNotice = "저장 파일이 손상되어 직전 백업에서 불러왔습니다.";
            else if (data == null && (File.Exists(path) || File.Exists(path + ".bak"))) LastReadNotice = "저장 파일이 손상되어 불러올 수 없습니다. 다른 슬롯을 사용해 주세요.";
            return data;
        }

        public void Delete(int slot = -1)
        {
            string path = SlotPath(Resolve(slot));
            foreach (var p in new[] { path, path + ".bak", path + ".tmp" })
            {
                try { if (File.Exists(p)) File.Delete(p); }
                catch (Exception e) { Debug.LogWarning($"[dotRPG] Could not delete {p}: {e.Message}"); }
            }
        }

        static bool tooNew;

        static SaveData TryRead(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                var data = JsonUtility.FromJson<SaveData>(File.ReadAllText(path));
                if (data != null && data.version > SaveData.CurrentVersion) { tooNew = true; return null; }
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
            if (data.version < 5) MigrateStory(data);
            if (data.quests == null) data.quests = new List<QuestSave>();
            if (data.storyFlags == null) data.storyFlags = new List<string>();
            // v6 added SaveData.career; older saves must reach Progression.RestoreCareer as null (legacy path), never as a blank CareerSave.
            if (data.version < 6) data.career = null;
            data.version = SaveData.CurrentVersion;
            return data;
        }

        /// <summary>
        /// v5 ([STORY]): quests became data-driven. Older saves already live after the attack on the village,
        /// so the prologue counts as played; the workshop quest keeps its stage (1-6 마을 복구).
        /// </summary>
        public static void MigrateStory(SaveData data)
        {
            data.quests = new List<QuestSave>();
            foreach (var id in StoryIds.Prologue) data.quests.Add(new QuestSave { id = id, status = (int)QuestStatus.Completed });
            data.storyFlags = new List<string>(StoryIds.PrologueFlags);
            var q = data.quest ?? new QuestProgress();
            var workshop = new QuestSave { id = QuestManager.WorkshopQuest };
            switch ((QuestStage)q.stage)
            {
                case QuestStage.Active:
                    workshop.status = (int)QuestStatus.Active;
                    workshop.counts.Add(q.workshopBuilt ? 1 : 0);
                    workshop.counts.Add(q.skeletonsDefeated);
                    break;
                case QuestStage.ReadyToReport:
                    workshop.status = (int)QuestStatus.ReadyToTurnIn;
                    workshop.step = 1;
                    break;
                case QuestStage.Completed:
                    workshop.status = (int)QuestStatus.Completed;
                    break;
            }
            if (workshop.status != 0) data.quests.Add(workshop);
            if (q.workshopBuilt) data.storyFlags.Add(QuestManager.WorkshopBuiltFlag);
        }

        /// <summary>
        /// v4: the +level moved from the item kind (every copy shared it) onto each piece (its key).
        /// Each kind at +L becomes key "{kind}+L" on every worn copy; when none is worn, one copy in the bag
        /// is converted, else one copy in storage. Other copies stay at +0. The new growth formula is weaker
        /// than v3's, so every converted enhanced kind grants one protection ticket (at most
        /// <see cref="MaxEnhanceCompensation"/>), reported in <see cref="SaveData.enhanceCompensation"/>.
        /// </summary>
        public static void MigrateEnhanceLevels(SaveData data)
        {
            data.enhanceCompensation = 0;
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
                bool converted = worn || ConvertOne(data.inventory, kind, key) || ConvertOne(data.storage, kind, key);
                if (converted && key != kind) data.enhanceCompensation++;
            }
            data.enhanceLevels.Clear();
            data.enhanceCompensation = Math.Min(MaxEnhanceCompensation, data.enhanceCompensation);
            if (data.enhanceCompensation > 0) data.inventory.Add(new ItemStack(ConsumableDatabase.ProtectTicket, data.enhanceCompensation));
        }

        /// <summary>Most protection tickets the v4 migration hands out.</summary>
        public const int MaxEnhanceCompensation = 3;

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
