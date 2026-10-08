using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Plays cutscene timelines (<see cref="CutsceneDef"/>): talk, walk, fade, tint, shake, spawn story
    /// actors, set flags and travel, while the game sits in <see cref="GameState.Cutscene"/> (world time runs,
    /// enemies and player input wait). Esc skips: waits and walks finish at once, lines are dropped, and
    /// commands that change the world (flags, spawns, travel) still run so skipping never breaks the story.
    /// </summary>
    public class CutscenePlayer : MonoBehaviour
    {
        CutsceneDatabase db;
        Canvas canvas;
        Image tint, barTop, barBottom;
        Text title;
        CanvasGroup titleGroup;
        bool skipping;
        float letterbox;
        float letterboxTarget;
        CutsceneDef current;
        readonly Dictionary<string, NpcController> spawned = new Dictionary<string, NpcController>();
        readonly List<GameObject> props = new List<GameObject>();

        public bool IsPlaying => current != null;
        /// <summary>Esc ends this scene (some story beats cannot be skipped).</summary>
        public bool CanSkip => current != null && current.skippable && !skipping;

        /// <summary>Automated checks: skip the scene that is playing (same as Esc).</summary>
        public void DevSkip()
        {
            if (current == null) return;
            skipping = true;
            if (Game.Dialogue.IsOpen) Game.Dialogue.Close();
        }
        public string CurrentId => current != null ? current.id : "";
        public CutsceneDef Get(string id) => db != null ? db.Get(id) : null;
        public CutsceneDatabase Database => db;

        public static CutscenePlayer Create(Transform parent, CutsceneDatabase database)
        {
            var go = new GameObject("Cutscenes");
            go.transform.SetParent(parent, false);
            var p = go.AddComponent<CutscenePlayer>();
            p.db = database;
            p.BuildOverlay();
            return p;
        }

        void BuildOverlay()
        {
            var cgo = new GameObject("CutsceneCanvas", typeof(RectTransform));
            cgo.transform.SetParent(transform, false);
            canvas = cgo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 90; // under the UI canvas (100): the dialogue box stays on top
            var scaler = cgo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280f, 720f);
            scaler.matchWidthOrHeight = 1f;
            if (TouchUi.Enabled) scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand; // [TOUCH] same rule as the UI canvas; PC keeps match-height
            var root = (RectTransform)cgo.transform;

            tint = MakeImage(root, "Tint", new Color(0f, 0f, 0f, 0f));
            UIFactory.Stretch(tint.rectTransform);
            barTop = MakeImage(root, "BarTop", Color.black);
            barBottom = MakeImage(root, "BarBottom", Color.black);
            foreach (var bar in new[] { barTop, barBottom })
            {
                var rt = bar.rectTransform;
                bool top = bar == barTop;
                rt.anchorMin = new Vector2(0f, top ? 1f : 0f);
                rt.anchorMax = new Vector2(1f, top ? 1f : 0f);
                rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
                rt.sizeDelta = new Vector2(0f, 0f);
                rt.anchoredPosition = Vector2.zero;
            }
            title = UIFactory.Text(root, "Title", "", 40, new Color32(246, 231, 200, 255), TextAnchor.MiddleCenter, true);
            UIFactory.Place(title.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 40f), new Vector2(1100f, 160f));
            titleGroup = title.gameObject.AddComponent<CanvasGroup>();
            titleGroup.alpha = 0f;
            canvas.gameObject.SetActive(true);
        }

        static Image MakeImage(Transform parent, string name, Color c)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var img = go.AddComponent<Image>();
            img.color = c;
            img.raycastTarget = false;
            return img;
        }

        // =============================== Public ===============================

        public void Play(string id, Action done)
        {
            var def = Get(id);
            if (def == null || current != null)
            {
                if (current != null) Debug.LogWarning($"[dotRPG] Cutscene '{id}' asked while '{current.id}' plays.");
                done?.Invoke();
                return;
            }
            StartCoroutine(Run(def, done));
        }

        /// <summary>Story actors and props of the current scene leave with the map (world rebuild).</summary>
        public void OnWorldRebuilt()
        {
            spawned.Clear();
            props.Clear();
        }

        // =============================== Timeline ===============================

        IEnumerator Run(CutsceneDef def, Action done)
        {
            current = def;
            skipping = false;
            Game.State.Set(GameState.Cutscene);
            Game.Player?.StopScripted();
            foreach (var cmd in def.cmds)
            {
                if (cmd == null) continue;
                yield return Exec(cmd);
            }
            // A scene never leaves the screen black, the bars down or the camera elsewhere.
            letterboxTarget = 0f;
            if (Game.UI != null && Game.UI.Fader != null) Game.UI.Fader.SetAlpha(0f);
            Game.Camera.SetTarget(Game.Player.transform, false);
            titleGroup.alpha = 0f;
            Game.Player?.StopScripted();
            current = null;
            skipping = false;
            if (Game.State.Current == GameState.Cutscene || Game.State.Current == GameState.Dialogue) Game.State.Set(GameState.Playing);
            done?.Invoke();
        }

        void Update()
        {
            letterbox = Mathf.MoveTowards(letterbox, letterboxTarget, Time.unscaledDeltaTime * 3f);
            float h = letterbox * 90f;
            barTop.rectTransform.sizeDelta = new Vector2(0f, h);
            barBottom.rectTransform.sizeDelta = new Vector2(0f, h);

            if (current == null || skipping || !current.skippable) return;
            if (Game.State.Current != GameState.Cutscene && Game.State.Current != GameState.Dialogue) return;
            if (Game.Input != null && Game.Input.PausePressed && !Game.State.ChangedThisFrame)
            {
                skipping = true;
                if (Game.Dialogue.IsOpen) Game.Dialogue.Close();
            }
        }

        IEnumerator Exec(CutsceneCmd c)
        {
            switch (c.op)
            {
                case "line":
                    if (skipping) break;
                    yield return Talk(new DialogueData { id = "", lines = new List<DialogueLine> { new DialogueLine { speaker = c.speaker, text = c.text } } });
                    break;
                case "dialogue":
                    if (skipping) break;
                    yield return Talk(Game.Dialogues.Get(c.id));
                    break;
                case "wait":
                    yield return Wait(c.t);
                    break;
                case "fadeOut":
                    if (skipping) Game.UI.Fader.SetAlpha(1f);
                    else yield return Game.UI.Fader.Fade(1f, c.t > 0f ? c.t : 0.6f);
                    break;
                case "fadeIn":
                    if (skipping) Game.UI.Fader.SetAlpha(0f);
                    else yield return Game.UI.Fader.Fade(0f, c.t > 0f ? c.t : 0.6f);
                    break;
                case "tint":
                    yield return Tint(ParseColor(c.color), skipping ? 0f : c.t);
                    break;
                case "letterbox":
                    letterboxTarget = c.count > 0 ? 1f : 0f;
                    break;
                case "title":
                    if (skipping) break;
                    yield return Title(c.text, c.t > 0f ? c.t : 2.5f);
                    break;
                case "spawn":
                    Spawn(c.actor, Cell(c), ParseFacing(c.dir, Facing.Down));
                    break;
                case "despawn":
                    Despawn(c.actor);
                    break;
                case "place":
                    Place(c.actor, Cell(c), ParseFacing(c.dir, Facing.Down));
                    break;
                case "move":
                {
                    var routine = Move(c.actor, Cell(c), c.speed > 0f ? c.speed : 2f);
                    if (skipping || !c.noWait) yield return routine;
                    else StartCoroutine(routine);
                    break;
                }
                case "face":
                    Face(c.actor, c.dir);
                    break;
                case "emote":
                    if (!skipping) Emote(c.actor, string.IsNullOrEmpty(c.text) ? "!" : c.text);
                    break;
                case "shake":
                    if (!skipping) Game.Camera.Shake(c.power > 0f ? c.power : 0.2f, c.t > 0f ? c.t : 0.4f);
                    break;
                case "sfx":
                    if (!skipping) Game.Audio.PlaySfx(c.id);
                    break;
                case "music":
                    Game.Audio.PlayMusic(c.id);
                    break;
                case "flag":
                    Game.Quest.SetFlag(c.id);
                    break;
                case "unflag":
                    Game.Quest.ClearFlag(c.id);
                    break;
                case "camera":
                {
                    var target = Actor(c.actor);
                    if (target != null) Game.Camera.SetTarget(target.transform, skipping);
                    else if (c.col >= 0f) Game.Camera.SetTarget(Anchor(Cell(c)), skipping);
                    yield return Wait(c.t);
                    break;
                }
                case "cameraReset":
                    Game.Camera.SetTarget(Game.Player.transform, skipping);
                    break;
                case "travel":
                    yield return Travel(c);
                    break;
                case "heal":
                    Game.Player.HealFull();
                    break;
                case "fire":
                    SpawnFire(Cell(c), Mathf.Max(1, c.count));
                    break;
                case "clearProps":
                    foreach (var p in props) if (p != null) Destroy(p);
                    props.Clear();
                    break;
                case "enemies":
                    SpawnEnemies(c.id, Cell(c), Mathf.Max(1, c.count));
                    break;
                case "join":
                    StoryCompanions.Join(c.actor);
                    break;
                case "leave":
                    StoryCompanions.Leave(c.actor);
                    break;
                default:
                    Debug.LogWarning($"[dotRPG] Cutscene '{current?.id}': unknown op '{c.op}'.");
                    break;
            }
        }

        IEnumerator Talk(DialogueData data)
        {
            if (data == null || data.lines.Count == 0) yield break;
            bool open = true;
            Game.Dialogue.Play(data, () => open = false);
            while (open && !skipping) yield return null;
            if (open) Game.Dialogue.Close();
            if (Game.State.Current != GameState.Cutscene) Game.State.Set(GameState.Cutscene);
        }

        IEnumerator Wait(float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end && !skipping) yield return null;
        }

        IEnumerator Tint(Color to, float seconds)
        {
            Color from = tint.color;
            float t = 0f;
            while (t < seconds && !skipping)
            {
                t += Time.unscaledDeltaTime;
                tint.color = Color.Lerp(from, to, t / seconds);
                yield return null;
            }
            tint.color = to;
        }

        IEnumerator Title(string text, float seconds)
        {
            title.text = Game.Quest.FormatTokens(text ?? "");
            for (float a = 0f; a < 1f && !skipping; a += Time.unscaledDeltaTime * 1.5f) { titleGroup.alpha = a; yield return null; }
            titleGroup.alpha = 1f;
            yield return Wait(seconds);
            for (float a = 1f; a > 0f && !skipping; a -= Time.unscaledDeltaTime * 1.5f) { titleGroup.alpha = a; yield return null; }
            titleGroup.alpha = 0f;
        }

        IEnumerator Travel(CutsceneCmd c)
        {
            string map = c.id;
            if (!MapRegistry.Exists(map)) yield break;
            Game.Session.MapId = map;
            Game.World.Load(map);
            OnWorldRebuilt();
            var pos = c.col >= 0f ? Cell(c) : Game.World.PlayerSpawn;
            Game.Player.Spawn(pos, ParseFacing(c.dir, Facing.Down), Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            Game.Camera.SetTarget(Game.Player.transform, true);
            Game.Audio.PlayMusic(Game.World.Map.music);
            Game.UI.Hud.RefreshAll();
            GameEvents.RaiseMapEntered(map);
            yield return null;
        }

        // =============================== Actors ===============================

        /// <summary>World position of a map-text cell (column, row from the top line).</summary>
        public static Vector2 CellToWorld(float col, float row) => CellToWorld(Game.World.Bounds, col, row);

        public static Vector2 CellToWorld(Rect bounds, float col, float row) => new Vector2(bounds.xMin + col + 0.5f, bounds.yMax - row - 0.5f);

        static Vector2 Cell(CutsceneCmd c) => c.col >= 0f && c.row >= 0f ? CellToWorld(c.col, c.row) : (Vector2)Game.Player.transform.position;

        Transform anchor;

        Transform Anchor(Vector2 pos)
        {
            if (anchor == null) anchor = new GameObject("CameraAnchor").transform;
            anchor.SetParent(transform, false);
            anchor.position = pos;
            return anchor;
        }

        Component Actor(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            if (id == "player") return Game.Player;
            if (spawned.TryGetValue(id, out var npc) && npc != null) return npc;
            if (Game.World != null && Game.World.ObjectsRoot != null)
                foreach (var n in Game.World.ObjectsRoot.GetComponentsInChildren<NpcController>())
                    if (n.Definition != null && n.Definition.npcId == id) return n;
            return null;
        }

        void Spawn(string id, Vector2 pos, Facing facing)
        {
            var existing = Actor(id);
            if (existing is NpcController n)
            {
                n.ScriptPlace(pos, facing);
                return;
            }
            var def = StoryCast.Find(id);
            if (def == null)
            {
                Debug.LogWarning($"[dotRPG] Cutscene spawn: no story actor '{id}'.");
                return;
            }
            var npc = NpcController.Create(def, pos, Game.World.ObjectsRoot);
            npc.ScriptPlace(pos, facing);
            spawned[id] = npc;
        }

        void Despawn(string id)
        {
            var a = Actor(id);
            if (a is NpcController n)
            {
                spawned.Remove(id);
                Destroy(n.gameObject);
            }
        }

        void Place(string id, Vector2 pos, Facing facing)
        {
            var a = Actor(id);
            if (a is NpcController n) n.ScriptPlace(pos, facing);
            else if (a is PlayerController p) p.Place(pos, facing);
        }

        IEnumerator Move(string id, Vector2 target, float speed)
        {
            var a = Actor(id);
            if (a is NpcController n)
            {
                if (skipping) { n.ScriptPlace(target, n.CurrentFacing); yield break; }
                yield return n.ScriptWalk(target, speed, () => skipping);
            }
            else if (a is PlayerController p)
            {
                if (skipping) { p.Place(target, p.Facing); yield break; }
                yield return p.ScriptWalk(target, speed, () => skipping);
            }
        }

        void Face(string id, string dir)
        {
            var a = Actor(id);
            if (a is NpcController n) n.ScriptFace(ParseFacing(dir, n.CurrentFacing));
            else if (a is PlayerController p) p.Place(p.Position, ParseFacing(dir, p.Facing));
        }

        void Emote(string id, string text)
        {
            var a = Actor(id);
            if (a == null) return;
            var go = new GameObject("Emote");
            go.transform.SetParent(a.transform, false);
            go.transform.localPosition = new Vector3(0f, 1.9f, 0f);
            var tm = go.AddComponent<TextMesh>();
            tm.text = text;
            tm.anchor = TextAnchor.LowerCenter;
            tm.characterSize = 0.12f;
            tm.fontSize = 48;
            tm.color = new Color32(255, 230, 102, 255);
            var font = UIFont.Get();
            if (font != null)
            {
                tm.font = font;
                go.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
            go.GetComponent<MeshRenderer>().sortingOrder = 30000;
            Destroy(go, 1.4f);
        }

        void SpawnFire(Vector2 pos, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var offset = count > 1 ? UnityEngine.Random.insideUnitCircle * 1.4f : Vector2.zero;
                var go = new GameObject("StoryFire");
                go.transform.SetParent(Game.World.ObjectsRoot, false);
                go.transform.position = pos + offset;
                go.AddComponent<StoryFire>();
                props.Add(go);
            }
        }

        void SpawnEnemies(string enemyId, Vector2 pos, int count)
        {
            for (int i = 0; i < count; i++)
            {
                var p = pos + UnityEngine.Random.insideUnitCircle * 1.2f;
                MonsterDatabase.Spawn(enemyId, p, Game.World.ObjectsRoot);
            }
        }

        // =============================== Parsing ===============================

        public static Facing ParseFacing(string dir, Facing fallback)
        {
            switch (dir)
            {
                case "down": return Facing.Down;
                case "up": return Facing.Up;
                case "left": return Facing.Left;
                case "right": return Facing.Right;
                default: return fallback;
            }
        }

        static Color ParseColor(string hex)
        {
            if (!string.IsNullOrEmpty(hex) && ColorUtility.TryParseHtmlString(hex, out var c)) return c;
            return new Color(0f, 0f, 0f, 0f);
        }
    }

}
