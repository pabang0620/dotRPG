using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// One command of a cutscene timeline (Resources/Data/Cutscenes.json). Fields are shared; each op reads
    /// the ones it needs. Positions are map-text cells: <c>col</c> = column, <c>row</c> = row counted from
    /// the top line of the map file, so a scene is written by reading coordinates off the map text.
    /// </summary>
    [Serializable]
    public class CutsceneCmd
    {
        /// <summary>
        /// line (speaker, text) · dialogue (id) · wait (t) · fadeOut / fadeIn (t) · tint (color, t) ·
        /// spawn (actor = story npc id, col, row, dir) · despawn (actor) · move (actor, col, row, speed, wait) ·
        /// face (actor, dir) · place (actor, col, row, dir) · emote (actor, text) · shake (t, power) ·
        /// sfx (id) · music (id) · flag (id) · unflag (id) · camera (actor or col/row, t) · cameraReset ·
        /// travel (id = map, col, row, dir) · heal · fire (col, row, count) · enemies (id = enemy, col, row, count) ·
        /// letterbox (on: count = 1, off: 0) · title (text, t) · join / leave (actor = story companion).
        /// </summary>
        public string op = "";
        public string actor = "";
        public string id = "";
        public string speaker = "";
        public string text = "";
        public float col = -1f;
        public float row = -1f;
        public float t;
        public float speed = 2f;
        public float power = 0.2f;
        public int count;
        public string dir = "";
        /// <summary>Hex "#rrggbbaa" for tint.</summary>
        public string color = "";
        /// <summary>move: wait for arrival before the next command (default) or walk while the scene goes on.</summary>
        public bool noWait;
    }

    [Serializable]
    public class CutsceneDef
    {
        public string id = "";
        /// <summary>Esc skips the rest (state-changing commands still run instantly).</summary>
        public bool skippable = true;
        public List<CutsceneCmd> cmds = new List<CutsceneCmd>();
    }

    [Serializable]
    class CutsceneFile
    {
        public List<CutsceneDef> cutscenes = new List<CutsceneDef>();
    }

    public sealed class CutsceneDatabase
    {
        public const string ResourcePath = "Data/Cutscenes";
        readonly Dictionary<string, CutsceneDef> byId = new Dictionary<string, CutsceneDef>();

        public CutsceneDatabase(TextAsset source)
        {
            if (source == null) return;
            try
            {
                var file = JsonUtility.FromJson<CutsceneFile>(source.text);
                if (file != null) foreach (var c in file.cutscenes) if (!string.IsNullOrEmpty(c.id)) byId[c.id] = c;
            }
            catch (Exception e) { Debug.LogError("[dotRPG] Cutscenes.json could not be read: " + e.Message); }
        }

        public CutsceneDef Get(string id) => id != null && byId.TryGetValue(id, out var c) ? c : null;
        public IEnumerable<string> Ids => byId.Keys;
    }
}
