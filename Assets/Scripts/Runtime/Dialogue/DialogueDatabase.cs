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

        /// <summary>[STORY] Story conversations live in their own file next to Dialogues.json.</summary>
        public const string StoryResourcePath = "Data/StoryDialogues";

        public DialogueDatabase(TextAsset source)
        {
            Merge(source);
            Merge(Resources.Load<TextAsset>(StoryResourcePath));
        }

        /// <summary>Adds the conversations of another dialogue file (later files win on equal ids).</summary>
        public void Merge(TextAsset source)
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
                {
                    if (string.IsNullOrEmpty(d.id)) continue;
                    // [I18N] Lines in the player's language; the speaker stays raw (portraits match it) and is
                    // translated where it is shown.
                    foreach (var line in d.lines) if (line != null) line.text = L.T(line.text);
                    byId[d.id] = d;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[dotRPG] Dialogue file is not valid JSON: {e.Message}");
            }
        }

        /// <summary>[ROGUE] A rogue hears "&lt;id&gt;_rogue" when the story has a rogue version of the conversation (1-5 training).</summary>
        public bool TryGet(string id, out DialogueData data)
        {
            if (!string.IsNullOrEmpty(id) && Game.Session != null && Game.Session.PlayerClass == CharacterClass.Rogue
                && byId.TryGetValue(id + "_rogue", out data)) return true;
            return byId.TryGetValue(id ?? "", out data);
        }

        public DialogueData Get(string id)
        {
            if (TryGet(id, out var data)) return data;
            Debug.LogWarning($"[dotRPG] Dialogue '{id}' not found.");
            return new DialogueData
            {
                id = id,
                lines = new List<DialogueLine> { new DialogueLine { speaker = "", text = L.F("(대사 '{0}' 없음)", id) } },
            };
        }
    }
}
