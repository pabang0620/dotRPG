using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Main story quests can't be abandoned; side quests can be repeated or dropped.</summary>
    public enum QuestKind
    {
        Main,
        Sub,
    }

    /// <summary>
    /// Life of one quest: locked until its requirements are met, then offered by its giver (or accepted
    /// at once when it has none), active while steps run, ready to turn in when the last step only waits
    /// for the report talk, completed after the reward.
    /// </summary>
    public enum QuestStatus
    {
        Locked = 0,
        Available = 1,
        Active = 2,
        ReadyToTurnIn = 3,
        Completed = 4,
    }

    /// <summary>
    /// What an objective waits for. Every type listens to one gameplay signal (see <see cref="QuestManager"/>):
    /// talk = finishing a conversation with <c>target</c> (npc id), kill = <c>target</c> enemy id ("*" any),
    /// collect = holding <c>count</c> of item <c>target</c>, reach = entering map <c>target</c>,
    /// interact = a world object raising id <c>target</c>, dungeon / raid = clearing dungeon <c>target</c> ("*" any),
    /// flag = story flag <c>target</c> set, level = player level &gt;= count, cutscene = playing cutscene <c>target</c>.
    /// </summary>
    public static class ObjectiveTypes
    {
        public const string Talk = "talk";
        public const string Kill = "kill";
        public const string Collect = "collect";
        public const string Reach = "reach";
        public const string Interact = "interact";
        public const string Dungeon = "dungeon";
        public const string Raid = "raid";
        public const string Flag = "flag";
        public const string Level = "level";
        public const string Cutscene = "cutscene";
        /// <summary>Finish <c>count</c> of the quests listed in <c>target</c> ("id1,id2,..."): the 0/5 errand boards.</summary>
        public const string Quests = "quests";
    }

    [Serializable]
    public class ObjectiveDef
    {
        public string type = ObjectiveTypes.Talk;
        public string target = "";
        public int count = 1;
        /// <summary>Tracker line. Tokens: {n} = progress, {count} = needed.</summary>
        public string text = "";
        /// <summary>talk: the conversation played instead of the npc's normal one.</summary>
        public string dialogue = "";
        /// <summary>collect: the items are taken when the step completes.</summary>
        public bool consume;
        /// <summary>Map the objective happens on (for the tracker's "where" hint and map markers).</summary>
        public string map = "";
    }

    [Serializable]
    public class QuestStepDef
    {
        /// <summary>Journal line for this step.</summary>
        public string text = "";
        public List<ObjectiveDef> objectives = new List<ObjectiveDef>();
        /// <summary>Cutscene played when every objective of the step is done (before the next step starts).</summary>
        public string cutscene = "";
        /// <summary>Story flags set when the step completes.</summary>
        public List<string> setFlags = new List<string>();
    }

    [Serializable]
    public class QuestRewardDef
    {
        public int xp;
        public int gold;
        public List<ItemStack> items = new List<ItemStack>();
        /// <summary>Extra base max HP (2 = one heart).</summary>
        public int maxHealth;
        public List<string> setFlags = new List<string>();
    }

    [Serializable]
    public class QuestDef
    {
        public string id = "";
        public int chapter = 1;
        /// <summary>"main" or "sub".</summary>
        public string kind = "main";
        /// <summary>Short code shown before the title ("1-3").</summary>
        public string code = "";
        public string title = "";
        public string summary = "";
        /// <summary>Quest ids that must be completed first.</summary>
        public List<string> requires = new List<string>();
        /// <summary>Story flags that must be set first.</summary>
        public List<string> requiresFlags = new List<string>();
        public int minLevel;
        /// <summary>Npc id offering the quest ("" = accepted automatically once unlocked).</summary>
        public string giver = "";
        /// <summary>Conversation that offers the quest; finishing it accepts.</summary>
        public string offerDialogue = "";
        /// <summary>Npc id to report to after the last step ("" = completes on its own).</summary>
        public string turnIn = "";
        public string turnInDialogue = "";
        /// <summary>Played when the quest is accepted.</summary>
        public string startCutscene = "";
        public List<QuestStepDef> steps = new List<QuestStepDef>();
        public QuestRewardDef reward = new QuestRewardDef();

        public QuestKind Kind => kind == "sub" ? QuestKind.Sub : QuestKind.Main;
        public string DisplayTitle => string.IsNullOrEmpty(code) ? title : $"{code} {title}";
    }

    [Serializable]
    public class ChapterDef
    {
        public int number;
        public string title = "";
        public string summary = "";
    }

    [Serializable]
    class QuestFile
    {
        public List<ChapterDef> chapters = new List<ChapterDef>();
        public List<QuestDef> quests = new List<QuestDef>();
    }

    /// <summary>Saved state of one quest (see <see cref="QuestStatus"/>).</summary>
    [Serializable]
    public class QuestSave
    {
        public string id;
        public int status;
        public int step;
        /// <summary>Progress per objective of the current step.</summary>
        public List<int> counts = new List<int>();
    }

    /// <summary>All quests and chapters, read from Resources/Data/Quests.json (order in the file = order in the journal).</summary>
    public sealed class QuestDatabase
    {
        public const string ResourcePath = "Data/Quests";

        readonly List<QuestDef> quests = new List<QuestDef>();
        readonly List<ChapterDef> chapters = new List<ChapterDef>();
        readonly Dictionary<string, QuestDef> byId = new Dictionary<string, QuestDef>();

        public IReadOnlyList<QuestDef> All => quests;
        public IReadOnlyList<ChapterDef> Chapters => chapters;

        public QuestDatabase(TextAsset source)
        {
            if (source == null)
            {
                Debug.LogWarning("[dotRPG] Quests.json missing: no quests.");
                return;
            }
            QuestFile file;
            try { file = JsonUtility.FromJson<QuestFile>(source.text); }
            catch (Exception e)
            {
                Debug.LogError("[dotRPG] Quests.json could not be read: " + e.Message);
                return;
            }
            if (file == null) return;
            chapters.AddRange(file.chapters);
            foreach (var q in file.quests)
            {
                if (string.IsNullOrEmpty(q.id) || byId.ContainsKey(q.id))
                {
                    Debug.LogWarning($"[dotRPG] Quest id empty or duplicated: '{q.id}'");
                    continue;
                }
                quests.Add(q);
                byId[q.id] = q;
            }
        }

        public QuestDef Get(string id) => id != null && byId.TryGetValue(id, out var q) ? q : null;

        public ChapterDef Chapter(int number)
        {
            foreach (var c in chapters) if (c.number == number) return c;
            return null;
        }
    }
}
