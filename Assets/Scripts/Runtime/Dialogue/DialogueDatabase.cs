using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    [Serializable]
    public class DialogueLine
    {
        [Tooltip("Empty = use the NPC's display name.")]
        public string speaker;
        [TextArea] public string text;
    }

    [Serializable]
    public class DialogueData
    {
        public string id;
        public List<DialogueLine> lines = new List<DialogueLine>();
    }

    [Serializable]
    class DialogueFile
    {
        public List<DialogueData> dialogues = new List<DialogueData>();
    }

    /// <summary>
    /// All dialogue text, loaded from Resources/Data/Dialogues.json. Keeping text out of code makes
    /// writing, proofreading and localisation (one JSON per language) straightforward.
    /// Text may contain tokens such as {wood_required}; see QuestManager.FormatTokens.
    /// </summary>
    public sealed class DialogueDatabase
    {
        readonly Dictionary<string, DialogueData> byId = new Dictionary<string, DialogueData>();

        public DialogueDatabase(TextAsset source)
        {
            if (source == null)
            {
                Debug.LogWarning("[dotRPG] Dialogue file missing.");
                return;
            }
            try
            {
                var file = JsonUtility.FromJson<DialogueFile>(source.text);
                foreach (var d in file.dialogues)
                    if (!string.IsNullOrEmpty(d.id)) byId[d.id] = d;
            }
            catch (Exception e)
            {
                Debug.LogError($"[dotRPG] Dialogue file is not valid JSON: {e.Message}");
            }
        }

        public bool TryGet(string id, out DialogueData data) => byId.TryGetValue(id ?? "", out data);

        public DialogueData Get(string id)
        {
            if (TryGet(id, out var data)) return data;
            Debug.LogWarning($"[dotRPG] Dialogue '{id}' not found.");
            return new DialogueData
            {
                id = id,
                lines = new List<DialogueLine> { new DialogueLine { speaker = "", text = $"(대사 '{id}' 없음)" } },
            };
        }
    }
}
