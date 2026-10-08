using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        /// <summary>Whole map at 32 px per tile, then close-ups (1280x720 at 64 px per tile) around the given points.</summary>
        IEnumerator RenderMap(string prefix, (string name, float x, float y)[] closeups)
        {
            SetPlayerVisible(false);
            SetExitMarkersVisible(false);
            SetCharactersVisible(false);
            yield return new WaitForEndOfFrame();
            RenderRegion(Path.Combine(folder, prefix + "_overview.png"), Game.World.Bounds, 32);
            float hw = 10f, hh = 5.625f;
            foreach (var (name, x, y) in closeups)
                RenderRegion(Path.Combine(folder, $"{prefix}_{name}.png"), new Rect(x - hw, y - hh, hw * 2f, hh * 2f), 64);
            SetCharactersVisible(true);
            SetExitMarkersVisible(true);
            SetPlayerVisible(true);
            Log($"rendered {prefix}: overview + {closeups.Length} close-ups");
        }

        static void SetCharactersVisible(bool visible)
        {
            foreach (var npc in Game.World.ObjectsRoot.GetComponentsInChildren<NpcController>(true))
                foreach (var r in npc.GetComponentsInChildren<SpriteRenderer>(true)) r.forceRenderingOff = !visible;
            foreach (var e in Game.World.ObjectsRoot.GetComponentsInChildren<EnemyController>(true))
                foreach (var r in e.GetComponentsInChildren<Renderer>(true)) r.forceRenderingOff = !visible;
        }

        static void SaveTexture(Texture2D tex, string file)
        {
            if (tex == null) return;
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(tex, rt);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var copy = new Texture2D(tex.width, tex.height, TextureFormat.RGB24, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(file, copy.EncodeToPNG());
            Destroy(copy);
        }

        /// <summary>
        /// Winter map: renders the whole map at 2x (32px per tile) and some close-ups at 3x straight
        /// from the camera into textures (no HUD, player hidden), then a couple of in-game screenshots.
        /// </summary>
        IEnumerator MapShowcase()
        {
            yield return Wait(1f);
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.2f);
            // Walk into the canyon's north portal (top edge, x 28-31) to reach the winter village.
            Game.Flow.TravelTo(MapRegistry.Canyon);
            yield return Wait(2f);
            yield return Teleport(new Vector2(29.5f, 58.3f));
            Game.Player.Spawn(new Vector2(29.5f, 59.4f), Facing.Up, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            yield return Wait(3f);
            Log($"via canyon portal: map={Game.World.MapId} player={Game.Player.Position}");
            if (Game.World.MapId != MapRegistry.Winter)
            {
                Game.Flow.TravelTo(MapRegistry.Winter);
                yield return Wait(3f);
            }
            var b = Game.World.Bounds;
            Log($"map={Game.World.MapId} size={b.width}x{b.height} spawn={Game.World.PlayerSpawn} objects={Game.World.ObjectsRoot.childCount}");
            SetPlayerVisible(false);
            SetExitMarkersVisible(false);
            yield return new WaitForEndOfFrame();
            RenderRegion(Path.Combine(folder, "winter_overview.png"), b, 32);
            float hw = 13.35f, hh = 7.5f; // 1280x720 at 48px per tile
            foreach (var (name, x, y) in new[]
            {
                ("winter_plaza", 23.5f, 25.5f), ("winter_house", 13.4f, 23.5f), ("winter_camp", 35.5f, 25.5f), ("winter_farm", 14f, 7.5f),
                ("winter_bridge", 38.6f, 23.5f), ("winter_north", 19.5f, 38.5f), ("winter_lookout", 38.6f, 38.5f), ("winter_chest", 38.6f, 31.5f),
            })
                RenderRegion(Path.Combine(folder, name + ".png"), new Rect(x - hw, y - hh, hw * 2f, hh * 2f), 48);
            SetExitMarkersVisible(true);
            SetPlayerVisible(true);
            yield return Wait(0.3f);
            yield return Shot("winter_ingame_start");
            yield return Teleport(new Vector2(23.5f, 21.5f));
            yield return Wait(0.3f);
            yield return Shot("winter_ingame_plaza");
            Log($"after walk: player={Game.Player.Position}");
        }

        static void SetPlayerVisible(bool visible)
        {
            foreach (var r in Game.Player.GetComponentsInChildren<SpriteRenderer>(true)) r.forceRenderingOff = !visible;
        }

        /// <summary>The glowing exit arrows are a gameplay hint, so the clean map renders leave them out.</summary>
        static void SetExitMarkersVisible(bool visible)
        {
            foreach (var r in Game.World.ObjectsRoot.GetComponentsInChildren<SpriteRenderer>(true))
                if (r.gameObject.name.StartsWith("arrow_")) r.forceRenderingOff = !visible;
        }

        /// <summary>Renders a world rectangle to a PNG at a fixed number of pixels per tile (crisp pixel art).</summary>
        static void RenderRegion(string file, Rect region, int pixelsPerTile)
        {
            var cam = Game.Camera.Camera;
            int w = Mathf.RoundToInt(region.width * pixelsPerTile), h = Mathf.RoundToInt(region.height * pixelsPerTile);
            var rt = new RenderTexture(w, h, 24) { filterMode = FilterMode.Point };
            var prevTarget = cam.targetTexture;
            float prevSize = cam.orthographicSize;
            var prevPos = cam.transform.position;
            cam.targetTexture = rt;
            cam.aspect = region.width / region.height;
            cam.orthographicSize = region.height * 0.5f;
            cam.transform.position = new Vector3(region.center.x, region.center.y, prevPos.z);
            var prevActive = RenderTexture.active;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = prevActive;
            cam.targetTexture = prevTarget;
            cam.ResetAspect();
            cam.orthographicSize = prevSize;
            cam.transform.position = prevPos;
            File.WriteAllBytes(file, tex.EncodeToPNG());
            Destroy(tex);
            rt.Release();
            Destroy(rt);
        }

        IEnumerator Teleport(Vector2 position)
        {
            Game.Player.Spawn(position, Facing.Down, Game.Session.PlayerHealth, Game.Session.PlayerMaxHealth);
            Game.Camera.SetTarget(Game.Player.transform, true);
            yield return Wait(0.4f);
        }

        /// <summary>
        /// Writes a contact sheet: one row per look (with and without gear), columns = the 8 facings
        /// (idle and a walk frame each), then the held weapons. Pixel art scaled 4x.
        /// </summary>
        static void SaveCharacterSheet(string file)
        {
            var looks = new List<CharacterLook>
            {
                CharacterLook.Player,
                CharacterLook.WithGear(CharacterLook.Player, "eq_plate_1_c", "eq_greaves_1_c"),
                CharacterLook.WithGear(CharacterLook.Player, "eq_plate_1_u", "eq_greaves_1_u"),
                CharacterLook.WithGear(CharacterLook.Player, "eq_plate_10_e", "eq_greaves_1_u"),
                CharacterLook.Mage,
                CharacterLook.WithGear(CharacterLook.Mage, "eq_robe_10_e", "eq_skirt_1_c"),
                CharacterLook.Skeleton,
                CharacterLook.Chief,
                CharacterLook.Farmer,
            };
            Facing[] facings = { Facing.Down, Facing.DownRight, Facing.Right, Facing.UpRight, Facing.Up, Facing.UpLeft, Facing.Left, Facing.DownLeft };
            string[] frames = { "idle0", "walk0" };
            // Cells fit the largest frame (16px, 32px or 64px art alike); the scale keeps the sheet readable.
            int maxW = 0, maxH = 0;
            foreach (var look in looks)
                foreach (var f in facings)
                {
                    var s = Game.Art.GetCharacter(look, f.SpriteKey(), "idle0");
                    if (s != null) { maxW = Mathf.Max(maxW, (int)s.rect.width); maxH = Mathf.Max(maxH, (int)s.rect.height); }
                }
            for (int t = 0; t < 4; t++)
                foreach (var key in new[] { $"wpn_sword_{t}", $"wpn_staff_{t}" })
                {
                    var s = Game.Art.Get(key);
                    if (s != null) { maxW = Mathf.Max(maxW, (int)s.rect.width); maxH = Mathf.Max(maxH, (int)s.rect.height); }
                }
            int cellW = maxW + 2, cellH = maxH + 7, scale = Mathf.Clamp(108 / Mathf.Max(1, cellH), 1, 4);
            int cols = facings.Length * frames.Length, rows = looks.Count + 1;
            var sheet = new Texture2D(cols * cellW * scale, rows * cellH * scale, TextureFormat.RGBA32, false);
            int sheetW = sheet.width, sheetH = sheet.height;
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = new Color32(58, 74, 92, 255);

            void Blit(Sprite s, int col, int row, bool flip)
            {
                if (s == null) return;
                var tex = s.texture;
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                int ox = col * cellW * scale + (cellW - w) / 2 * scale, oy = (rows - 1 - row) * cellH * scale + scale;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var c = px[y * w + (flip ? w - 1 - x : x)];
                        if (c.a == 0) continue;
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                            {
                                int sx = ox + x * scale + dx, sy = oy + y * scale + dy;
                                if (sx >= 0 && sy >= 0 && sx < sheetW && sy < sheetH) fill[sy * sheetW + sx] = c;
                            }
                    }
            }

            for (int r = 0; r < looks.Count; r++)
                for (int f = 0; f < facings.Length; f++)
                    for (int k = 0; k < frames.Length; k++)
                        Blit(Game.Art.GetCharacter(looks[r], facings[f].SpriteKey(), frames[k]), f * frames.Length + k, r, facings[f].IsLeft());
            for (int t = 0; t < 4; t++)
            {
                Blit(Game.Art.Get($"wpn_sword_{t}"), t, looks.Count, false);
                Blit(Game.Art.Get($"wpn_staff_{t}"), 4 + t, looks.Count, false);
            }
            sheet.SetPixels32(fill);
            sheet.Apply();
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            Destroy(sheet);
        }

        /// <summary>
        /// Warrior then mage: level up, socket both skills, stand next to a canyon skeleton, cast each
        /// skill and grab frames mid-effect. Also checks that Frost Nova freezes and encases monsters.
        /// </summary>
        IEnumerator FxShowcase()
        {
            yield return Wait(1f);
            SaveCharacterSheet(Path.Combine(folder, "sheet_characters.png"));
            Log("character sheet saved");
            foreach (var cls in new[] { CharacterClass.Warrior, CharacterClass.Mage })
            {
                Game.Flow.NewGame(cls);
                yield return Wait(1.2f);
                var prog = Game.Session.Progression;
                prog.AddXp(2600); // Lv10: all five slots open
                bool mage = cls == CharacterClass.Mage;
                prog.SetGem(0, 1, mage ? "sup_chain" : "sup_aoe");
                prog.SetGem(1, 1, "sup_aoe");
                Game.Flow.TravelTo(MapRegistry.Forest);
                yield return Wait(4f); // let the level-up toasts and the map banner clear
                Log($"fx class={Game.Player.Class} Lv{prog.Level} map={Game.World.MapId} monsters={EnemyController.Active.Count}");
                for (int slot = 0; slot <= SkillGems.Slots; slot++)
                {
                    yield return StageSkeletons(new Vector2(15.5f, 25.5f));
                    // Slots 0-4 are the skills (4 = awakening); the last round is the class's basic attack.
                    bool basic = slot == SkillGems.Slots;
                    string id = !basic ? prog.Active(slot)?.id ?? "none" : mage ? "bolt" : "sword";
                    EnemyController target = null;
                    foreach (var e in EnemyController.Active)
                        if (e != null && !e.IsDead && e.isActiveAndEnabled) { target = e; break; }
                    if (target == null) { Log($"fx {id}: no monster found"); continue; }
                    // Gather a few more monsters around the target so chains and area hits show.
                    var offsets = new[] { new Vector2(1.5f, 0.9f), new Vector2(2.3f, -0.5f), new Vector2(0.9f, -1.3f) };
                    int placed = 0;
                    foreach (var e in EnemyController.Active)
                    {
                        if (e == target || e == null || e.IsDead || placed >= offsets.Length) continue;
                        Vector2 p = target.Position + offsets[placed++];
                        var rb = e.GetComponent<Rigidbody2D>();
                        if (rb != null) rb.position = p;
                        e.transform.position = p;
                    }
                    Game.Player.Spawn(target.Position + Vector2.left * 1.4f, Facing.Right, int.MaxValue, 0);
                    Game.Camera.SetTarget(Game.Player.transform, true);
                    Game.Session.PlayerMana = CharacterStats.MaxMp;
                    yield return Wait(0.35f);
                    Game.Player.FaceTowards(target.Position);
                    int kills = CountDead();
                    if (!basic) Game.Player.Skills.TryCast(slot);
                    else Game.Player.GetComponent<PlayerCombat>().TryAttack();
                    float t0 = Time.realtimeSinceStartup;
                    bool ult = slot == SkillGems.UltimateSlot;
                    foreach (float at in ult ? new[] { 0.2f, 0.62f, 1.05f } : new[] { 0.05f, 0.14f, 0.3f })
                    {
                        while (Time.realtimeSinceStartup - t0 < at) yield return null;
                        yield return Shot($"fx_{id}_{Mathf.RoundToInt(at * 100):00}");
                    }
                    int frozen = 0, encased = 0;
                    foreach (var e in EnemyController.Active)
                    {
                        if (e.IsFrozen) frozen++;
                        if (e.GetComponent<IceEncase>() != null) encased++;
                    }
                    Log($"fx {id}: frozen={frozen} encased={encased} liveFx={(Fx.Root != null ? Fx.Root.childCount : -1)}");
                    yield return Wait(1.3f);
                    Log($"fx {id}: kills +{CountDead() - kills} liveFx after={(Fx.Root != null ? Fx.Root.childCount : -1)}");
                }
                // Worn gear shows on the character: put on a full set, then close-ups facing three ways.
                var bag = Game.Session.Inventory;
                var worn = Game.Session.Equipment;
                foreach (var gid in mage ? new[] { "eq_staff_20_l", "eq_robe_1_c", "eq_skirt_1_u" } : new[] { "eq_sword_20_l", "eq_plate_10_e", "eq_greaves_1_u" })
                {
                    bag.Add(gid, 1);
                    Log($"fx equip {gid}: {worn.Equip(gid, cls)}");
                }
                var cam = Game.Camera.Camera;
                float camSize = cam.orthographicSize;
                cam.orthographicSize = camSize * 0.45f;
                foreach (var f in new[] { Facing.Down, Facing.DownRight, Facing.UpLeft })
                {
                    Game.Player.FaceTowards(Game.Player.Position + f.ToVector());
                    yield return Wait(0.25f);
                    yield return Shot($"fx_look_{(mage ? "mage" : "warrior")}_{f}");
                }
                cam.orthographicSize = camSize;
            }
        }

        static IEnumerator Wait(float seconds) => new WaitForSecondsRealtime(seconds);

        static int CountDead()
        {
            return killed;
        }

        static int killed;

        void OnEnable() => GameEvents.EnemyKilled += OnKilled;

        static void OnKilled(string id) => killed++;

        IEnumerator Shot(string name)
        {
            yield return new WaitForEndOfFrame();
            // ScreenCapture lives in an optional module this project does not include; read the back buffer instead.
            var tex = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
            tex.Apply();
            File.WriteAllBytes(Path.Combine(folder, name + ".png"), tex.EncodeToPNG());
            Destroy(tex);
            yield return null;
            Log("shot " + name);
        }

        void Log(string message) => log?.WriteLine($"[{Time.realtimeSinceStartup:0.00}] {message}");

        void OnLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Warning)
                Log($"{type}: {condition}");
        }

        void OnDestroy()
        {
            Application.logMessageReceived -= OnLog;
            GameEvents.EnemyKilled -= OnKilled;
        }
    }
}
