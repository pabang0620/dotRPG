using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] The chapter 3 story bundle: quests, cutscenes and dialogue of chapter 3 kept out of Quests.json,
    /// Cutscenes.json and StoryDialogues.json (Docs/ch3/LIVE_CONTENT.md 7, spoiler rule). The bundle is one JSON document
    /// { chapter, quests, cutscenes, dialogues } with the same field shapes as those files.
    /// The bundle is not in the build: it lives in Assets/Chapter3Bundle (outside Resources, BuildScript refuses a build
    /// that still has it in Resources). Online, the server's GET /content/chapter3 hands it over once chapter 3 is open
    /// (<see cref="Download"/>); the editor reads the local copy (<see cref="MergeLocal"/>). A release build offline has
    /// no bundle, so chapter 3 stays closed offline (chapters 1 and 2 only).
    /// </summary>
    public static class Chapter3Content
    {
        /// <summary>Folder of the bundle and its translations (Lang/&lt;code&gt;/chapter3.json), relative to Assets.</summary>
        public const string LocalFolder = "Chapter3Bundle";
        public const string FileName = "chapter3.json";

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

        /// <summary>Data version of the bundle the server sent (null = none downloaded this run).</summary>
        public static string DownloadedVersion { get; private set; }

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

        /// <summary>Path of a local bundle file (lang null = the Korean bundle). Only meaningful in the editor.</summary>
        public static string LocalPath(string lang = null) =>
            lang == null
                ? System.IO.Path.Combine(Application.dataPath, LocalFolder, FileName)
                : System.IO.Path.Combine(Application.dataPath, LocalFolder, "Lang", lang, FileName);

        /// <summary>Text of a local bundle file, or null (always null outside the editor: the build has no bundle).</summary>
        public static string LocalText(string lang = null)
        {
#if UNITY_EDITOR
            var path = LocalPath(lang);
            return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
#else
            return null;
#endif
        }

        /// <summary>The local bundle (editor and data export), or null.</summary>
        public static Bundle FromLocal() => Parse(LocalText());

        /// <summary>Editor path: merge the local bundle and its translation for the current language. False in a build.</summary>
        public static bool MergeLocal()
        {
            var text = LocalText();
            if (text == null) return false;
            if (!L.Ko && MiniJson.Parse(LocalText(L.Lang) ?? "") is Dictionary<string, object> table) L.AddEntries(table);
            return Merge(text);
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

        /// <summary>
        /// Online path (after login): when the server says chapter 3 is open, fetch GET /content/chapter3 once per data
        /// version and merge it (translation first, the quest merge translates as it adds). Never blocks the login: a failed
        /// download is logged and retried at the next login, and chapter 3 quests simply do not show meanwhile.
        /// </summary>
        public static void Download(ApiClient api, Action done)
        {
            if (!Chapter3.ServerOpen || api == null || DownloadedVersion == ApiClient.DataVersion) { done?.Invoke(); return; }
            api.Get("/content/chapter3", r =>
            {
                if (r.ok)
                {
                    if (!L.Ko && MiniJson.Obj(MiniJson.Obj(r.data, "lang"), L.Lang) is Dictionary<string, object> table) L.AddEntries(table);
                    if (Merge(MiniJson.Str(r.data, "bundle"))) DownloadedVersion = MiniJson.Str(r.data, "data_version", ApiClient.DataVersion);
                }
                else Debug.LogWarning("[dotRPG] chapter3 content download failed: " + r.code);
                done?.Invoke();
            });
        }
    }
}
