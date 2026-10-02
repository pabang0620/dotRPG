using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// Saved story state: every quest's <see cref="QuestSave"/>, the story flags (world changes such as
    /// "the stable burned"), the side quest pinned to the tracker and the hero's name. Owned by
    /// <see cref="GameSession"/>; rules live in <see cref="QuestManager"/>.
    /// </summary>
    public sealed class QuestJournal
    {
        public const string DefaultName = "아린";

        readonly Dictionary<string, QuestSave> states = new Dictionary<string, QuestSave>();
        public readonly HashSet<string> Flags = new HashSet<string>();
        /// <summary>Side quest pinned under the main quest on the HUD ("" = none).</summary>
        public string Tracked = "";
        public string PlayerName = DefaultName;

        public QuestSave State(string id)
        {
            if (!states.TryGetValue(id, out var s))
            {
                s = new QuestSave { id = id };
                states[id] = s;
            }
            return s;
        }

        public QuestStatus Status(string id) => states.TryGetValue(id, out var s) ? (QuestStatus)s.status : QuestStatus.Locked;
        public bool IsCompleted(string id) => Status(id) == QuestStatus.Completed;
        public bool HasFlag(string flag) => !string.IsNullOrEmpty(flag) && Flags.Contains(flag);

        public void Clear()
        {
            states.Clear();
            Flags.Clear();
            Tracked = "";
            PlayerName = DefaultName;
        }

        public void Capture(SaveData data)
        {
            data.quests = new List<QuestSave>();
            foreach (var s in states.Values)
                data.quests.Add(new QuestSave { id = s.id, status = s.status, step = s.step, counts = new List<int>(s.counts) });
            data.storyFlags = new List<string>(Flags);
            data.trackedQuest = Tracked ?? "";
            data.playerName = PlayerName ?? DefaultName;
        }

        public void Restore(SaveData data)
        {
            Clear();
            if (data.quests != null)
                foreach (var s in data.quests)
                    if (!string.IsNullOrEmpty(s.id))
                        states[s.id] = new QuestSave { id = s.id, status = s.status, step = s.step, counts = s.counts ?? new List<int>() };
            if (data.storyFlags != null) Flags.UnionWith(data.storyFlags);
            Tracked = data.trackedQuest ?? "";
            PlayerName = string.IsNullOrWhiteSpace(data.playerName) ? DefaultName : data.playerName;
        }
    }
}
