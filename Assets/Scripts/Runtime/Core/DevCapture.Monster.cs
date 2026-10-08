using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [MONSTER] <c>-dotrpgMonster &lt;folder&gt;</c>: monster and boss checks. Renders contact sheets of every
    /// monster / boss frame and the telegraph / projectile sprites, then spawns each monster next to a Lv10
    /// warrior in the forest (screenshots with telegraphs) and verifies the behaviours, super armor, the
    /// companions' telegraph dodging, the boss HP bar, groggy, every boss pattern, 해골왕's phases, totem rule
    /// and 망자의 심판, and that unblockable hits skip the block roll. Report lines "MON &lt;what&gt; PASS|FAIL"
    /// and "MON summary: N passed, M failed".
    /// </summary>
    public partial class DevCapture
    {
        bool monsterOnly;
        int monPassed, monFailed;

        static Vector2 MonSpot = new Vector2(15.5f, 25.5f);
        const int MonTankHp = 20000;
        static readonly string[] MonsterIds = { "skel_warrior", "skel_gold", "skel_miner", "skel_necro", "skel_archer", "skel_shield", "skel_knight" };
        static readonly string[] BossIds = { "boss_gold_foreman", "boss_mine_captain", "boss_lich", "boss_archer_chief", "boss_armory_warden", "boss_skeleton_king" };

        void MCheck(string what, bool ok)
        {
            if (ok) monPassed++;
            else monFailed++;
            log?.WriteLine($"MON {what} {(ok ? "PASS" : "FAIL")}");
        }

        // =============================== Helpers ===============================

        static void MonTank()
        {
            var party = Game.Party;
            if (party == null) return;
            foreach (var m in party.Members)
            {
                if (m == null || m.IsDead) continue;
                if (m.Health.Max < MonTankHp) m.Health.SetMax(MonTankHp, true);
                else if (m.Health.Current < MonTankHp / 2) m.HealFull();
            }
        }

        /// <summary>Waits (real time) keeping every member alive.</summary>
        static IEnumerator MWait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end)
            {
                MonTank();
                yield return null;
            }
        }

        /// <summary>Waits until <paramref name="done"/> or the timeout. Returns through the out-style holder.</summary>
        static IEnumerator MWaitUntil(Func<bool> done, float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (Time.realtimeSinceStartup < end && !done())
            {
                MonTank();
                yield return null;
            }
        }

        static void MonClear()
        {
            foreach (var t in Telegraph.Active.ToList()) t.Cancel();
            foreach (var p in MonsterProjectile.Active.ToList()) if (p != null) Destroy(p.gameObject);
            foreach (var e in EnemyController.Active.ToList()) if (e != null) Destroy(e.gameObject);
            CompanionBrain.PriorityTarget = null;
            CompanionBrain.DangerZones.Clear();
        }

        static Vector2 MonFree(Vector2 p)
        {
            if (Game.World == null || Game.World.IsFree(p)) return p;
            for (int ring = 1; ring <= 4; ring++)
                for (int k = 0; k < 8; k++)
                {
                    float a = k * Mathf.PI / 4f;
                    Vector2 q = p + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 0.5f * ring;
                    if (Game.World.IsFree(q)) return q;
                }
            return p;
        }

        static EnemyController MonSpawn(string id, Vector2 offset, float hpMul = 1f) =>
            MonsterDatabase.Spawn(id, MonFree(MonSpot + offset), Game.World.ObjectsRoot, hpMul);

        static int GoldPickups() => FindObjectsByType<Pickup>(FindObjectsInactive.Exclude).Count(p => p.name == "Pickup_" + ConsumableDatabase.Gold);

        static DamageInfo FromLocal(int amount, Vector2 source, float knockback = 6f) =>
            new DamageInfo(amount, source, knockback, Team.Player, Game.Player.gameObject);

        void PlaceMembers(PlayerController local, PlayerController a, PlayerController b, Vector2 aOff, Vector2 bOff)
        {
            local.Place(MonSpot, Facing.Right);
            if (a != null) a.Place(MonFree(MonSpot + aOff), Facing.Right);
            if (b != null) b.Place(MonFree(MonSpot + bOff), Facing.Right);
        }

        // =============================== Contact sheets ===============================

        static void MonSheet(string file, List<CharacterLook> looks, int cellW, int cellH, int scale, List<string> extraKeys)
        {
            Facing[] facings = { Facing.Down, Facing.DownRight, Facing.Right, Facing.UpRight, Facing.Up };
            string[] frames = { "idle0", "walk1", "attack" };
            int cols = facings.Length * frames.Length + 1;
            int rows = looks.Count + (extraKeys != null && extraKeys.Count > 0 ? 1 : 0);
            var sheet = new Texture2D(cols * cellW * scale, rows * cellH * scale, TextureFormat.RGBA32, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int y = 0; y < sheet.height; y++)
                for (int x = 0; x < sheet.width; x++)
                {
                    // Checker of two dungeon-floor greys so dark outlines and light bones both read.
                    bool alt = ((x / (cellW * scale)) + (y / (cellH * scale))) % 2 == 0;
                    fill[y * sheet.width + x] = alt ? new Color32(74, 80, 70, 255) : new Color32(92, 98, 86, 255);
                }
            sheet.SetPixels32(fill);

            void Blit(Sprite s, int col, int row, bool flip)
            {
                if (s == null) return;
                var tex = s.texture;
                var px = tex.GetPixels32();
                int w = tex.width, h = tex.height;
                int ox = col * cellW * scale + Mathf.Max(0, (cellW - w) / 2) * scale;
                int oy = (rows - 1 - row) * cellH * scale + scale;
                for (int y = 0; y < h && y < cellH; y++)
                    for (int x = 0; x < w && x < cellW; x++)
                    {
                        var c = px[y * w + (flip ? w - 1 - x : x)];
                        if (c.a == 0) continue;
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                                sheet.SetPixel(ox + x * scale + dx, oy + y * scale + dy, c);
                    }
            }

            for (int r = 0; r < looks.Count; r++)
            {
                int col = 0;
                foreach (var f in facings)
                    foreach (var fr in frames)
                        Blit(Game.Art.GetCharacter(looks[r], f.SpriteKey(), fr), col++, r, f.IsLeft());
                Blit(Game.Art.GetCharacter(looks[r], Facing.Down.SpriteKey(), "hurt"), col, r, false);
            }
            if (extraKeys != null)
                for (int i = 0; i < extraKeys.Count && i < cols; i++) Blit(Game.Art.Get(extraKeys[i]), i, looks.Count, false);
            sheet.Apply();
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            Destroy(sheet);
        }

        /// <summary>Telegraph / projectile sprites at 3x on 132px cells (they are bigger than a character cell).</summary>
        static void MonFxSheet(string file, List<string> keys)
        {
            const int cell = 132, scale = 2;
            var sheet = new Texture2D(keys.Count * cell * scale, cell * scale, TextureFormat.RGBA32, false);
            var fill = new Color32[sheet.width * sheet.height];
            for (int i = 0; i < fill.Length; i++) fill[i] = (i % sheet.width) / (cell * scale) % 2 == 0 ? new Color32(74, 80, 70, 255) : new Color32(92, 98, 86, 255);
            sheet.SetPixels32(fill);
            for (int k = 0; k < keys.Count; k++)
            {
                var tex = Game.Art.Get(keys[k]).texture;
                var px = tex.GetPixels32();
                int ox = k * cell * scale + (cell - tex.width) / 2 * scale, oy = (cell - tex.height) / 2 * scale;
                for (int y = 0; y < tex.height; y++)
                    for (int x = 0; x < tex.width; x++)
                    {
                        var c = px[y * tex.width + x];
                        if (c.a == 0) continue;
                        // Telegraph textures are white (tinted in game): show them red like in game.
                        if (keys[k].StartsWith("mon_tele")) c = new Color32(255, (byte)(60 + c.a / 6), 50, (byte)Mathf.Max(90, (int)c.a));
                        var under = sheet.GetPixel(ox + x * scale, oy + y * scale);
                        var col = Color.Lerp(under, (Color)c, c.a / 255f);
                        for (int dy = 0; dy < scale; dy++)
                            for (int dx = 0; dx < scale; dx++)
                                sheet.SetPixel(ox + x * scale + dx, oy + y * scale + dy, col);
                    }
            }
            sheet.Apply();
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            Destroy(sheet);
        }

        /// <summary>
        /// Nearest open patch (no walls / trunks in a 13x9 box, and none just below it whose canopy would cover
        /// the fight) to <paramref name="fallback"/>, so the screenshots show the monsters.
        /// </summary>
        static Vector2 FindArena(Vector2 fallback)
        {
            foreach (var size in new[] { new Vector2(13f, 10.5f), new Vector2(11f, 8.5f), new Vector2(10f, 7f) })
            {
                var a = FindArena(fallback, size);
                if (a != fallback) return a;
            }
            return fallback;
        }

        static Vector2 FindArena(Vector2 fallback, Vector2 size)
        {
            if (Game.World == null) return fallback;
            var b = Game.World.Bounds;
            Physics2D.SyncTransforms();
            Vector2 best = fallback;
            float bestDist = float.MaxValue;
            for (float y = b.yMin + 7f; y < b.yMax - 5f; y += 1f)
                for (float x = b.xMin + 5f; x < b.xMax - 9f; x += 1f)
                {
                    var c = new Vector2(Mathf.Floor(x) + 0.5f, Mathf.Floor(y) + 0.5f);
                    float d = Vector2.Distance(c, fallback);
                    if (d >= bestDist) continue;
                    // x: -4.5..+8.5, y: -7 (canopy margin) .. +3.5
                    bool blocked = false;
                    foreach (var hit in Physics2D.OverlapBoxAll(c + new Vector2(size.x * 0.5f - 4.5f, 3.5f - size.y * 0.5f), size, 0f))
                    {
                        if (hit == null || hit.isTrigger || (hit.attachedRigidbody != null && hit.attachedRigidbody.bodyType != RigidbodyType2D.Static)) continue;
                        blocked = true;
                        break;
                    }
                    if (blocked) continue;
                    best = c;
                    bestDist = d;
                }
            return best;
        }

        static int SpriteHash(Sprite s)
        {
            unchecked
            {
                int h = 17;
                foreach (var c in s.texture.GetPixels32()) h = h * 31 + c.r * 7 + c.g * 13 + c.b * 19 + c.a;
                return h;
            }
        }

        // =============================== Run ===============================

        IEnumerator MonsterRun()
        {
            monPassed = monFailed = 0;
            bool autosave = Game.Config.autosave;
            Game.Config.autosave = false;
            yield return Wait(1.5f);

            // ---------- Data + art ----------
            var defs = MonsterDatabase.All.ToList();
            var missing = MonsterIds.Concat(BossIds).Concat(new[] { MonsterDatabase.Totem }).Where(id => MonsterDatabase.Get(id) == null).ToList();
            var bossSizes = BossIds.Select(id => MonsterDatabase.Get(id)).Where(d => d != null).Select(d => d.size).ToList();
            MCheck($"database: {defs.Count} defs, missing=[{string.Join(",", missing)}] boss sizes=[{string.Join(",", bossSizes.Select(s => s.ToString("0.0")))}] patterns=[{string.Join(",", BossIds.Select(id => MonsterDatabase.Get(id)?.patterns.Count ?? 0))}]",
                missing.Count == 0 && bossSizes.All(s => s >= 2f && s <= 2.5f) && BossIds.All(id => MonsterDatabase.Get(id).patterns.Count >= 3));

            var smallLooks = MonsterIds.Select(id => MonsterDatabase.Get(id).look).Append(MonsterDatabase.Get(MonsterDatabase.Totem).look).ToList();
            var bossLooks = BossIds.Select(id => MonsterDatabase.Get(id).look).ToList();
            var fxKeys = new List<string> { "mon_arrow", "mon_bolt", "mon_bolt_big", "mon_summon", "mon_tele_ring", "mon_tele_disc", "mon_tele_rect", "mon_tele_cone_110", "mon_tele_conefill_110", "mon_tele_donut_35", "mon_tele_donutfill_35" };
            MonSheet(Path.Combine(folder, "mon_sheet_monsters.png"), smallLooks, 34, 58, 3, null); // [MONHD] HD 32x40 bodies, 32x56 totem
            MonSheet(Path.Combine(folder, "mon_sheet_bosses.png"), bossLooks, 98, 114, 2, null); // [MONHD] HD 96x112 bosses
            MonFxSheet(Path.Combine(folder, "mon_sheet_fx.png"), fxKeys);
            var idle = smallLooks.Concat(bossLooks).Select(l => Game.Art.GetCharacter(l, "down", "idle0")).ToList();
            int distinct = idle.Select(SpriteHash).Distinct().Count();
            // [MONHD] Normal monsters share the HD field skeleton's canvas + pixels-per-unit (same world size);
            // bosses are at least twice as wide at the same pixels-per-unit (artScale 2).
            var fieldSkel = Game.Art.GetCharacter(CharacterLook.Skeleton, "down", "idle0");
            var smallSprites = MonsterIds.Select(id => Game.Art.GetCharacter(MonsterDatabase.Get(id).look, "down", "idle0")).ToList();
            var bossSprites = bossLooks.Select(l => Game.Art.GetCharacter(l, "down", "idle0")).ToList();
            var bossW = bossSprites.Select(s => s.texture.width).ToList();
            bool smallMatch = smallSprites.All(s => s.texture.width == fieldSkel.texture.width && s.texture.height == fieldSkel.texture.height
                && Mathf.Approximately(s.pixelsPerUnit, fieldSkel.pixelsPerUnit));
            // [ART] Generated dot frames are 64 px like everyone else; a boss then stands bigger through artScale.
            float fieldW = fieldSkel.rect.width / fieldSkel.pixelsPerUnit;
            var bossWorld = BossIds.Select((id, i) => bossSprites[i].rect.width / bossSprites[i].pixelsPerUnit * MonsterDatabase.Get(id).artScale).ToList();
            bool bossOk = bossWorld.All(w => w >= 1.2f * fieldW);
            bool fxOk = fxKeys.All(k => Game.Art.Get(k) != null && Game.Art.Get(k).texture.width > 8 && !Game.Art.Get(k).name.StartsWith("Missing"));
            MCheck($"art: distinct idle frames {distinct}/{idle.Count} field skeleton={fieldSkel.texture.width}x{fieldSkel.texture.height}@{fieldSkel.pixelsPerUnit:0} small=[{string.Join(",", smallSprites.Select(s => $"{s.texture.width}x{s.texture.height}@{s.pixelsPerUnit:0}").Distinct())}] boss widths=[{string.Join(",", bossW)}]@{bossSprites[0].pixelsPerUnit:0} world=[{string.Join(",", bossWorld.Select(w => w.ToString("0.0")))}] vs field {fieldW:0.0} fx sprites ok={fxOk}",
                distinct == idle.Count && smallMatch && bossOk && fxOk);

            // ---------- Setup: Lv10 warrior + two mercenaries in the forest ----------
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.6f);
            Game.Session.Progression.AddXp(2600);
            Game.Session.Inventory.Add("eq_plate_1_u", 1);
            Game.Session.Equipment.Equip("eq_plate_1_u", Game.Player.Class);
            var party = Game.Party;
            party.AddCompanion("merc_bron");
            party.AddCompanion("merc_elin");
            Game.Flow.TravelTo(MapRegistry.Forest);
            yield return Wait(3.5f);
            foreach (var s in FindObjectsByType<EnemySpawner>(FindObjectsInactive.Exclude)) s.enabled = false;
            MonSpot = FindArena(MonSpot);
            Log($"arena at {MonSpot}");
            MonClear();
            var local = Game.Player;
            var bron = party.Find("merc_bron");
            var elin = party.Find("merc_elin");
            var brains = new Dictionary<PlayerController, IActorInput>();
            foreach (var m in new[] { bron, elin }) if (m != null) { brains[m] = m.Input; m.Input = new ScriptedInput(); }
            local.Input = new ScriptedInput();
            Game.Camera.SetTarget(local.transform, true);
            PlaceMembers(local, bron, elin, new Vector2(-2.5f, 1.8f), new Vector2(-2.5f, -1.8f));
            yield return MWait(0.5f);
            var cam = Game.Camera.Camera;
            float camSize = cam.orthographicSize;
            cam.orthographicSize = camSize * 0.7f;

            // Unknown ids fall back to a skeleton.
            var unknown = MonsterDatabase.Spawn("no_such_monster", MonSpot + new Vector2(20f, 20f), Game.World.ObjectsRoot);
            MCheck($"unknown id -> {unknown?.Def?.id}", unknown != null && unknown.Def != null && unknown.Def.id == "skel_warrior");
            MonClear();
            yield return null;

            // ---------- One screenshot per monster ----------
            int shotIndex = 10;
            foreach (var id in MonsterIds)
            {
                MonClear();
                PlaceMembers(local, bron, elin, new Vector2(-2.5f, 1.8f), new Vector2(-2.5f, -1.8f));
                yield return null;
                var m = MonSpawn(id, new Vector2(3.2f, 0.4f), 3f);
                float start = Time.realtimeSinceStartup;
                yield return MWaitUntil(() => Telegraph.Active.Any(t => t.Owner == m && t.Progress > 0.45f), 5f);
                if (Time.realtimeSinceStartup - start < 1.2f) yield return MWait(1.2f);
                yield return Shot($"mon_{shotIndex++}_{id}");
            }
            MonClear();
            yield return null;

            yield return ChargeChecks(local, bron, elin);
            yield return ArcherChecks(local, bron, elin);
            yield return NecroChecks(local, bron, elin);
            yield return ShieldChecks(local, bron, elin);
            yield return GoldChecks(local, bron, elin);
            yield return ArmorChecks(local, bron, elin);
            yield return DodgeChecks(local, bron, elin, brains);
            yield return UnblockableChecks(local);
            cam.orthographicSize = camSize * 0.9f; // bosses are big: a wider view
            yield return BossChecks(local, bron, elin);
            yield return KingChecks(local, bron, elin);

            cam.orthographicSize = camSize;
            MonClear();
            foreach (var pair in brains) if (pair.Key != null) pair.Key.Input = pair.Value;
            local.Input = new LocalInput();
            Game.Config.autosave = autosave;
            log?.WriteLine($"MON summary: {monPassed} passed, {monFailed} failed");
        }

    }
}
