using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        /// <summary>
        /// -dotrpgChars: the 32px characters, NPCs and monsters. Writes a full character contact sheet
        /// (every look x view x frame, 2x+), then in-game shots: NPCs in the village, the player walking
        /// and attacking with each weapon tier (warrior and mage), the mage casting, and skeletons
        /// walking / attacking / hurt / dying with the hit flash, damage numbers and HP bars.
        /// </summary>
        IEnumerator CharsShowcase()
        {
            yield return Wait(1f);

            // ---- 1. Contact sheets straight from the art (no game world needed) ----
            SaveCharacterSheet(Path.Combine(folder, "sheet_characters.png"));
            Log("character sheet saved (looks x 8 views x 2 frames + weapon tiers)");
            SaveCharsShowcaseSheet(Path.Combine(folder, "sheet_chars_full.png"));
            Log("full showcase sheet saved (player/mage/skeleton all 8 frames + NPCs + tools + digits)");

            // Report a few density facts so a reader can confirm the conversion without opening a PNG.
            var down0 = Game.Art.GetCharacter(CharacterLook.Player, "down", "idle0");
            var sword0 = Game.Art.Get("wpn_sword_0");
            var shadow = Game.Art.Get("shadow");
            var hpbg = Game.Art.Get("hpbar_bg");
            var num5 = Game.Art.Get("num_5");
            Log($"density check: player frame {down0.rect.width}x{down0.rect.height} ppu={down0.pixelsPerUnit} " +
                $"world={down0.rect.width / down0.pixelsPerUnit:0.###}x{down0.rect.height / down0.pixelsPerUnit:0.###}");
            Log($"density check: wpn_sword_0 ppu={sword0.pixelsPerUnit} shadow ppu={shadow.pixelsPerUnit} hpbar_bg {hpbg.rect.width}x{hpbg.rect.height} ppu={hpbg.pixelsPerUnit} num_5 ppu={num5.pixelsPerUnit}");
            Log($"sharp material present: {(FxMaterials.Sharp != null)}");

            // ---- 2. Village: NPCs and services ----
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.2f);
            var bounds = Game.World.Bounds;
            Log($"village: map={Game.World.MapId} size={bounds.width}x{bounds.height} npcs={CountNpcs()}");
            yield return CheckNpcMaterials();

            // A close look at the service NPCs (merchant / blacksmith / storage keeper).
            var cam = Game.Camera.Camera;
            float baseSize = cam.orthographicSize;
            foreach (var (id, shotName) in new[] { ("merchant", "npc_merchant"), ("smith", "npc_smith"), ("keeper", "npc_keeper"), ("farmer", "npc_farmer"), ("carrier", "npc_carrier") })
            {
                var npc = FindNpc(id);
                if (npc == null) { Log($"npc {id}: not found"); continue; }
                cam.orthographicSize = baseSize * 0.4f;
                yield return Teleport((Vector2)npc.transform.position + Vector2.down * 1.3f);
                Game.Player.FaceTowards(npc.transform.position);
                yield return Wait(0.4f);
                yield return Shot(shotName);
                cam.orthographicSize = baseSize;
            }

            // Honest depth-sort check vs a village lamp / tree.
            yield return DepthSortCheck();

            // Player walking and attacking with each weapon tier, warrior then mage.
            yield return WeaponTierShots();

            // ---- 3. Forest: skeletons, hit flash, damage numbers, HP bars ----
            // Fresh warrior so the in-frame shots show a sword, not a leftover mage staff.
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1f);
            Game.Flow.TravelTo(MapRegistry.Forest);
            yield return Wait(3f);
            Log($"forest: map={Game.World.MapId} monsters={EnemyController.Active.Count}");
            yield return SkeletonShots();

            // Mage casting a bolt at a skeleton.
            yield return MageCastShot();

            Log("chars showcase done");
        }

        static int CountNpcs()
        {
            int n = 0;
            foreach (var npc in FindObjectsByType<NpcController>(FindObjectsInactive.Exclude)) if (npc != null) n++;
            return n;
        }

        /// <summary>Confirms the runtime character/weapon/tool renderers actually carry the sharp HD material.</summary>
        IEnumerator CheckNpcMaterials()
        {
            yield return Wait(0.2f);
            int hd = 0, total = 0, tools = 0;
            foreach (var npc in FindObjectsByType<NpcController>(FindObjectsInactive.Exclude))
            {
                var body = npc.transform.Find("Visual/Body")?.GetComponent<SpriteRenderer>();
                if (body != null)
                {
                    total++;
                    if (body.sharedMaterial != null && body.sharedMaterial.name == "SpriteSharp") hd++;
                }
                var tool = npc.transform.Find("Visual/Tool")?.GetComponent<SpriteRenderer>();
                if (tool != null && tool.sharedMaterial != null && tool.sharedMaterial.name == "SpriteSharp") tools++;
            }
            var pbody = Game.Player.transform.Find("Visual/Body")?.GetComponent<SpriteRenderer>();
            bool playerSharp = pbody != null && pbody.sharedMaterial != null && pbody.sharedMaterial.name == "SpriteSharp";
            Log($"HD material: player body sharp={playerSharp}; npc bodies sharp={hd}/{total}; npc tools sharp={tools}");
        }

        /// <summary>Warrior and mage: equip each weapon tier, take an idle-hold shot and an attack shot.</summary>
        IEnumerator WeaponTierShots()
        {
            var cam = Game.Camera.Camera;
            float baseSize = cam.orthographicSize;
            var warriorWeapons = new[] { "eq_sword_wood", "eq_sword_iron", "eq_sword_bone", "eq_sword_dragon" };
            var mageWeapons = new[] { "eq_staff_oak", "eq_staff_crystal", "eq_staff_moon", "eq_staff_star" };

            foreach (var cls in new[] { CharacterClass.Warrior, CharacterClass.Mage })
            {
                Game.Flow.NewGame(cls);
                yield return Wait(1f);
                yield return Teleport(new Vector2(28.5f, 17.5f));
                cam.orthographicSize = baseSize * 0.42f;
                bool mage = cls == CharacterClass.Mage;
                var weps = mage ? mageWeapons : warriorWeapons;
                var bag = Game.Session.Inventory;
                var worn = Game.Session.Equipment;
                for (int t = 0; t < weps.Length; t++)
                {
                    bag.Add(weps[t], 1);
                    worn.Equip(weps[t], cls);
                    yield return Wait(0.25f);
                    Game.Player.FaceTowards(Game.Player.Position + Vector2.down);
                    yield return Wait(0.15f);
                    yield return Shot($"{(mage ? "mage" : "warrior")}_hold_t{t}");
                    // Attack (side view reads the swing / cast best).
                    Game.Player.FaceTowards(Game.Player.Position + Vector2.right);
                    yield return Wait(0.1f);
                    Game.Player.GetComponent<PlayerCombat>().TryAttack();
                    yield return Wait(0.12f);
                    yield return Shot($"{(mage ? "mage" : "warrior")}_attack_t{t}");
                    yield return Wait(0.4f);
                }
                // A walk-cycle shot (side).
                Game.Player.FaceTowards(Game.Player.Position + Vector2.right);
                yield return Shot($"{(mage ? "mage" : "warrior")}_walk");
                cam.orthographicSize = baseSize;
            }
        }

        /// <summary>Skeletons walking, attacking, hurt (flash + damage number), dying, with HP bars.</summary>
        IEnumerator SkeletonShots()
        {
            var cam = Game.Camera.Camera;
            float baseSize = cam.orthographicSize;
            yield return StageSkeletons(new Vector2(15.5f, 25.5f));
            cam.orthographicSize = baseSize * 0.5f;

            // Monster-only shots: hide the player entirely so no held weapon floats in frame.
            SetPlayerVisible(false);
            yield return Wait(0.6f);
            yield return Shot("skeletons_idle");
            yield return Wait(0.5f);
            yield return Shot("skeletons_walk");
            SetPlayerVisible(true);

            EnemyController first = null;
            foreach (var e in EnemyController.Active) if (e != null && !e.IsDead) { first = e; break; }
            if (first != null)
            {
                // Player IN frame (warrior, sword) beside the skeleton: flash, damage number, HP bar.
                Game.Player.Spawn(first.Position + Vector2.left * 1.2f, Facing.Right, int.MaxValue, 0);
                Game.Camera.SetTarget(Game.Player.transform, true);
                yield return Wait(0.3f);
                first.TakeDamage(new DamageInfo(1, Game.Player.Position, 1f, Team.Player));
                yield return Wait(0.05f);
                yield return Shot("skeleton_hitflash");   // white silhouette + "10" pop
                yield return Wait(0.25f);
                yield return Shot("skeleton_hpbar");       // bar drained a chunk (player visible)
                yield return Wait(0.8f);
                yield return Shot("skeleton_attack");
                // Kill it: death fade.
                first.TakeDamage(new DamageInfo(999, Game.Player.Position, 0f, Team.Player));
                yield return Wait(0.2f);
                yield return Shot("skeleton_dying");
                yield return Wait(0.6f);
            }
            cam.orthographicSize = baseSize;
        }

        /// <summary>
        /// Honest depth-sort check against a LAMP (the prop the review flagged). Y-sort rule:
        /// sortingOrder = -round(y*32), so a lower y (nearer the camera) draws in front. Standing the
        /// player NORTH of the lamp (higher y) must put the player BEHIND it; standing SOUTH (lower y)
        /// must put the player IN FRONT. We read the player body's and the lamp post's real orders.
        /// </summary>
        IEnumerator DepthSortCheck()
        {
            Transform lamp = null;
            foreach (var t in Game.World.ObjectsRoot.GetComponentsInChildren<Transform>())
                if (t.name == "Lamp") { lamp = t; break; }
            if (lamp == null)
            {
                // Fall back to any tall prop.
                foreach (var t in Game.World.ObjectsRoot.GetComponentsInChildren<Transform>())
                    if (t.name == "Tree" || t.name == "Well" || t.name == "Sign") { lamp = t; break; }
            }
            if (lamp == null) { Log("depth check: no lamp/prop found in village"); yield break; }

            // The lamp POST renderer (not the additive glow child) is the one that y-sorts with the player.
            SpriteRenderer post = lamp.GetComponent<SpriteRenderer>();
            if (post == null) post = lamp.GetComponentInChildren<SpriteRenderer>();
            Vector2 p = lamp.position;

            yield return Teleport(p + new Vector2(0.6f, 0.9f));   // NORTH (higher y) and a touch aside
            yield return Wait(0.3f);
            var pbody = Game.Player.transform.Find("Visual/Body").GetComponent<SpriteRenderer>();
            Log($"depth NORTH of '{lamp.name}': playerY={Game.Player.Position.y:0.00} lampY={p.y:0.00} " +
                $"playerOrder={pbody.sortingOrder} postOrder={post.sortingOrder} playerBehind={pbody.sortingOrder < post.sortingOrder} (expect true)");
            yield return Shot("depth_player_north_of_lamp");

            yield return Teleport(p + new Vector2(0.6f, -0.9f));  // SOUTH (lower y)
            yield return Wait(0.3f);
            pbody = Game.Player.transform.Find("Visual/Body").GetComponent<SpriteRenderer>();
            Log($"depth SOUTH of '{lamp.name}': playerY={Game.Player.Position.y:0.00} lampY={p.y:0.00} " +
                $"playerOrder={pbody.sortingOrder} postOrder={post.sortingOrder} playerInFront={pbody.sortingOrder > post.sortingOrder} (expect true)");
            yield return Shot("depth_player_south_of_lamp");
        }

        IEnumerator MageCastShot()
        {
            Game.Flow.NewGame(CharacterClass.Mage);
            yield return Wait(1f);
            Game.Flow.TravelTo(MapRegistry.Forest);
            yield return Wait(3f);
            var cam = Game.Camera.Camera;
            float baseSize = cam.orthographicSize;
            yield return StageSkeletons(new Vector2(15.5f, 25.5f));
            EnemyController target = null;
            foreach (var e in EnemyController.Active) if (e != null && !e.IsDead) { target = e; break; }
            if (target == null) { Log("mage cast: no skeleton"); yield break; }
            cam.orthographicSize = baseSize * 0.5f;
            Game.Player.Spawn(target.Position + Vector2.left * 2f, Facing.Right, int.MaxValue, 0);
            Game.Camera.SetTarget(Game.Player.transform, true);
            Game.Session.PlayerMana = CharacterStats.MaxMp;
            yield return Wait(0.4f);
            Game.Player.FaceTowards(target.Position);
            Game.Player.GetComponent<PlayerCombat>().TryAttack();
            yield return Wait(0.14f);
            yield return Shot("mage_cast_00");
            yield return Wait(0.18f);
            yield return Shot("mage_cast_01");
            cam.orthographicSize = baseSize;
        }

        /// <summary>
        /// Contact sheet sized from the REAL sprite rects (max width/height over every frame, plus a
        /// margin) so nothing is cropped — the wizard-hat frames are the tallest. Every look
        /// (player + 3 gear looks, mage + gear, skeleton, every NPC incl. 미르/무쇠/보람) is a block of
        /// 5 views (down, downside, side, upside, up) x 8 frames (idle0,idle1,walk0-3,attack,hurt).
        /// </summary>
        static void SaveCharsShowcaseSheet(string file)
        {
            var looks = new List<CharacterLook>
            {
                CharacterLook.Player,
                CharacterLook.WithGear(CharacterLook.Player, "eq_top_cloth", "eq_bot_cloth"),
                CharacterLook.WithGear(CharacterLook.Player, "eq_top_leather", "eq_bot_leather"),
                CharacterLook.WithGear(CharacterLook.Player, "eq_top_iron", "eq_bot_leather"),
                CharacterLook.Mage,
                CharacterLook.WithGear(CharacterLook.Mage, "eq_top_iron", "eq_bot_cloth"),
                CharacterLook.Skeleton,
                CharacterLook.Chief, CharacterLook.Farmer, CharacterLook.Fisher, CharacterLook.Builder,
                CharacterLook.Lumberjack, CharacterLook.Miner, CharacterLook.Carrier,
            };
            // Service NPCs (merchant 미르, blacksmith 무쇠, keeper 보람) come from the config.
            foreach (var id in new[] { "merchant", "smith", "keeper" })
            {
                var def = FindNpcDef(id);
                if (def != null) looks.Add(def.look);
            }
            string[] dirs = { "down", "downside", "side", "upside", "up" };
            string[] frames = ProceduralArt.CharacterFrames; // 8

            // Measure the largest frame so no cell clips (wizard hat is tallest / widest brim).
            int maxW = 0, maxH = 0;
            foreach (var look in looks)
                foreach (var d in dirs)
                    foreach (var f in frames)
                    {
                        var s = Game.Art.GetCharacter(look, d, f);
                        if (s == null) continue;
                        maxW = Mathf.Max(maxW, (int)s.rect.width);
                        maxH = Mathf.Max(maxH, (int)s.rect.height);
                    }
            // 3x for the 32px art; 2x once frames are 64x80 so the sheet stays a sensible size.
            int scale = Mathf.Clamp(200 / Mathf.Max(1, maxH), 1, 3);
            const int margin = 3;
            int cellW = maxW + margin * 2, cellH = maxH + margin * 2;
            int cols = dirs.Length * frames.Length;   // 40
            int rows = looks.Count;
            var sheet = new Texture2D(cols * cellW * scale, rows * cellH * scale, TextureFormat.RGBA32, false);
            int sheetW = sheet.width;
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++)
            {
                int cx = (i % sheet.width) / (cellW * scale);
                fill[i] = (cx / frames.Length) % 2 == 0 ? new Color32(50, 60, 76, 255) : new Color32(44, 54, 68, 255);
            }

            void Blit(Sprite s, int col, int row, bool flip)
            {
                if (s == null) return;
                var tex = s.texture;
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                // Centre horizontally, sit the feet a fixed margin above the cell bottom (pivot-aware).
                int ox = col * cellW * scale + (cellW - w) / 2 * scale;
                int oy = (rows - 1 - row) * cellH * scale + margin * scale;
                for (int y = 0; y < h; y++)
                    for (int x = 0; x < w; x++)
                    {
                        var cpx = px[y * w + (flip ? w - 1 - x : x)];
                        if (cpx.a == 0) continue;
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                                fill[(oy + y * scale + dy) * sheetW + ox + x * scale + dx] = cpx;
                    }
            }

            for (int r = 0; r < looks.Count; r++)
            {
                int col = 0;
                foreach (var d in dirs)
                {
                    // Left views are the right-facing art flipped, matching the renderer.
                    bool flip = false;
                    foreach (var f in frames)
                        Blit(Game.Art.GetCharacter(looks[r], d, f), col++, r, flip);
                }
            }
            sheet.SetPixels32(fill);
            sheet.Apply();
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            Destroy(sheet);
        }

        static NpcDefinition FindNpcDef(string id)
        {
            foreach (var def in Game.Config.npcs)
                if (def != null && def.npcId == id) return def;
            return null;
        }
    }
}
