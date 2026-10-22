using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] The chapter 3 story bundle: quests, cutscenes and dialogue of chapter 3 kept out of Quests.json,
    /// Cutscenes.json and StoryDialogues.json (Docs/ch3/LIVE_CONTENT.md, spoiler rule). The bundle is one JSON document
    /// { chapter, quests, cutscenes, dialogues } with the same field shapes as those files.
    /// Development reads it from Resources (<see cref="MergeFromResources"/>); stage 6 hands the server's
    /// GET /content/chapter3 text to <see cref="Merge"/>, the same entry point.
    /// </summary>
    public static class Chapter3Content
    {
        public const string ResourcePath = "Data/chapter3";

        [Serializable]
        public class Bundle
        {
            public ChapterDef chapter = new ChapterDef();
            public List<QuestDef> quests = new List<QuestDef>();
            public List<CutsceneDef> cutscenes = new List<CutsceneDef>();
            public List<DialogueData> dialogues = new List<DialogueData>();
        }

        /// <summary>True once a bundle went into the quest, cutscene and dialogue databases.</summary>
        public static bool Merged { get; private set; }

        /// <summary>Reads a bundle (null when the text is empty or not a bundle). Used by the merge and the data export.</summary>
        public static Bundle Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            try { return JsonUtility.FromJson<Bundle>(json); }
            catch (Exception e)
            {
                Debug.LogError("[dotRPG] chapter3 bundle could not be read: " + e.Message);
                return null;
            }
        }

        /// <summary>The bundle shipped in Resources (development and the data export), or null.</summary>
        public static Bundle FromResources()
        {
            var text = Resources.Load<TextAsset>(ResourcePath);
            return text != null ? Parse(text.text) : null;
        }

        /// <summary>Development path: merge the bundle from Resources when it is there.</summary>
        public static bool MergeFromResources()
        {
            var text = Resources.Load<TextAsset>(ResourcePath);
            return text != null && Merge(text.text);
        }

        /// <summary>Puts a bundle's quests, scenes and lines into the running databases. Safe to call twice.</summary>
        public static bool Merge(string json)
        {
            var b = Parse(json);
            if (b == null) return false;
            Game.Quest?.Database?.Add(b.chapter, b.quests);
            Game.Cutscenes?.Database?.Add(b.cutscenes);
            Game.Dialogues?.Add(b.dialogues);
            Merged = true;
            return true;
        }
    }
}
