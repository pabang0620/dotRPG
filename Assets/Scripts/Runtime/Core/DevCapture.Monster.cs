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
            bool bossOk = bossSprites.All(s => s.texture.width >= 2 * fieldSkel.texture.width && Mathf.Approximately(s.pixelsPerUnit, fieldSkel.pixelsPerUnit));
            bool fxOk = fxKeys.All(k => Game.Art.Get(k) != null && Game.Art.Get(k).texture.width > 8 && !Game.Art.Get(k).name.StartsWith("Missing"));
            MCheck($"art: distinct idle frames {distinct}/{idle.Count} field skeleton={fieldSkel.texture.width}x{fieldSkel.texture.height}@{fieldSkel.pixelsPerUnit:0} small=[{string.Join(",", smallSprites.Select(s => $"{s.texture.width}x{s.texture.height}@{s.pixelsPerUnit:0}").Distinct())}] boss widths=[{string.Join(",", bossW)}]@{bossSprites[0].pixelsPerUnit:0} fx sprites ok={fxOk}",
                distinct == idle.Count && smallMatch && bossOk && fxOk);

            // ---------- Setup: Lv10 warrior + two mercenaries in the forest ----------
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(1.6f);
            Game.Session.Progression.AddXp(2600);
            Game.Session.Inventory.Add("eq_top_leather", 1);
            Game.Session.Equipment.Equip("eq_top_leather", Game.Player.Class);
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

        // =============================== Monster checks ===============================

        IEnumerator ChargeChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            // The local player stands in the line; Bron stands well off it.
            PlaceMembers(local, bron, elin, new Vector2(2.5f, 2.4f), new Vector2(-6f, -3f));
            yield return null;
            var miner = MonSpawn("skel_miner", new Vector2(5f, 0f), 5f);
            var charger = miner.Behaviour as ChargerBehaviour;
            int localHits = 0, bronHits = 0;
            Action<DamageInfo> onLocal = i => { if (i.attacker == miner.gameObject) localHits++; };
            Action<DamageInfo> onBron = i => { if (i.attacker == miner.gameObject) bronHits++; };
            local.Health.Damaged += onLocal;
            bron.Health.Damaged += onBron;
            yield return null;
            charger.DevCharge(local.Position - miner.Position);
            yield return MWait(0.55f);
            var tele = Telegraph.Active.FirstOrDefault(t => t.Owner == miner);
            bool inLine = tele != null && tele.Contains(local.Position) && !tele.Contains(bron.Position);
            yield return Shot("mon_20_charge_telegraph");
            yield return MWaitUntil(() => !charger.Busy, 4f);
            local.Health.Damaged -= onLocal;
            bron.Health.Damaged -= onBron;
            MCheck($"miner charge: telegraph line covers local={inLine} hits local={localHits} bron(off line)={bronHits} chargeHits={charger.ChargeHits}",
                inLine && localHits >= 1 && bronHits == 0 && charger.ChargeHits >= 1);
        }

        IEnumerator ArcherChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-7f, 3f), new Vector2(-7f, -3f));
            yield return null;
            var archer = MonSpawn("skel_archer", new Vector2(1.6f, 0f), 5f);
            var ab = archer.Behaviour as ArcherBehaviour;
            float d0 = Vector2.Distance(archer.Position, local.Position);
            bool shot = false;
            float end = Time.realtimeSinceStartup + 5.5f;
            while (Time.realtimeSinceStartup < end)
            {
                MonTank();
                if (!shot && Telegraph.Active.Any(t => t.Owner == archer && t.Progress > 0.5f))
                {
                    shot = true;
                    yield return Shot("mon_21_archer_aim");
                }
                yield return null;
            }
            float d1 = Vector2.Distance(archer.Position, local.Position);
            MCheck($"archer keeps distance: {d0:0.0} -> {d1:0.0} (keep {archer.Def.keepDistance}) shots={ab.Shots}",
                d1 >= 3.5f && d1 <= 6.8f && d1 > d0 + 1f && ab.Shots >= 1);
        }

        IEnumerator NecroChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-7f, 3f), new Vector2(-7f, -3f));
            yield return null;
            var necro = MonSpawn("skel_necro", new Vector2(4.2f, 0f), 5f);
            var nb = necro.Behaviour as NecroBehaviour;
            int maxAlive = 0;
            bool killedOne = false, shotTaken = false;
            // Game time (hit-stop slows it down while the minions hit the player).
            float start = Time.time, realEnd = Time.realtimeSinceStartup + 30f;
            while (Time.time - start < 14.5f && Time.realtimeSinceStartup < realEnd)
            {
                MonTank();
                maxAlive = Mathf.Max(maxAlive, nb.AliveMinions);
                float t = Time.time - start;
                if (!shotTaken && t > 1.8f)
                {
                    shotTaken = true;
                    yield return Shot("mon_22_necro_summon");
                }
                if (!killedOne && t > 6f)
                {
                    var minion = EnemyController.Active.FirstOrDefault(e => e != null && !e.IsDead && e.Summoner == necro);
                    if (minion != null)
                    {
                        minion.TakeDamage(FromLocal(minion.Health.Current + 999, minion.Position));
                        killedOne = true;
                    }
                }
                yield return null;
            }
            MCheck($"necro summons: total={nb.Summoned} max alive={maxAlive} (cap {necro.Def.maxMinions}) killed one={killedOne} alive now={nb.AliveMinions}",
                maxAlive <= 2 && nb.Summoned >= 3 && killedOne);
        }

        IEnumerator ShieldChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-7f, 3f), new Vector2(-7f, -3f));
            yield return null;
            var shield = MonSpawn("skel_shield", new Vector2(3.5f, 0f), 5f);
            var sb = shield.Behaviour as ShieldGuardBehaviour;
            yield return MWait(1.0f);
            yield return MWaitUntil(() => sb.Guarding && !shield.IsStaggered, 3f);
            yield return Shot("mon_23_shield_guard");
            var hp = shield.Health;
            int h0 = hp.Current;
            shield.TakeDamage(FromLocal(50, shield.Position + sb.GuardDirection * 1.5f, 8f));
            int front = h0 - hp.Current;
            bool frontStagger = shield.IsStaggered;
            yield return MWait(0.3f);
            int h1 = hp.Current;
            Vector2 back = shield.Position - sb.GuardDirection * 1.5f;
            shield.TakeDamage(FromLocal(50, back, 8f));
            int behind = h1 - hp.Current;
            bool backStagger = shield.IsStaggered;
            yield return MWait(0.6f);
            yield return MWaitUntil(() => sb.Guarding && !shield.IsStaggered, 3f);
            int h2 = hp.Current;
            Vector2 side = shield.Position + new Vector2(-sb.GuardDirection.y, sb.GuardDirection.x) * 1.5f;
            shield.TakeDamage(FromLocal(50, side, 8f));
            int sideDmg = h2 - hp.Current;
            MCheck($"shield: front 50 -> {front} (stagger {frontStagger}), behind 50 -> {behind} (stagger {backStagger}), side 50 -> {sideDmg}",
                front == 10 && !frontStagger && behind == 50 && backStagger && sideDmg == 50);
        }

        IEnumerator GoldChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-7f, 3f), new Vector2(-7f, -3f));
            yield return null;
            var gold = MonSpawn("skel_gold", new Vector2(2.4f, 0f), 3f);
            float d0 = Vector2.Distance(gold.Position, local.Position);
            yield return MWait(2f);
            float d1 = Vector2.Distance(gold.Position, local.Position);
            int p0 = GoldPickups();
            gold.TakeDamage(FromLocal(5, local.Center));
            int p1 = GoldPickups();
            yield return MWait(0.2f);
            yield return Shot("mon_24_gold_spill");
            gold.TakeDamage(FromLocal(gold.Health.Current + 999, local.Center));
            int p2 = GoldPickups();
            MCheck($"gold skeleton: flees {d0:0.0} -> {d1:0.0}, hit spills {p1 - p0} pile(s), death drops {p2 - p1} pile(s)",
                d1 > d0 + 1f && p1 - p0 >= 1 && p2 - p1 >= 2);
            yield return MWait(0.5f);
        }

        IEnumerator ArmorChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-7f, 3f), new Vector2(-7f, -3f));
            yield return null;
            var knight = MonSpawn("skel_knight", new Vector2(1.4f, 0f), 10f);
            var kb = knight.Behaviour as KnightBehaviour;
            yield return null;
            kb.DevCombo(local);
            yield return MWait(0.2f);
            Vector2 before = knight.Position;
            bool armored = knight.SuperArmorActive && kb.Attacking;
            knight.TakeDamage(FromLocal(20, local.Center, 14f));
            bool staggered = knight.IsStaggered;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            float pushed = Vector2.Distance(before, knight.Position);
            yield return Shot("mon_25_knight_combo");
            // Control: a plain skeleton staggers and slides.
            var warrior = MonSpawn("skel_warrior", new Vector2(1.5f, 2.5f), 10f);
            yield return MWait(0.2f);
            warrior.TakeDamage(FromLocal(20, local.Center, 14f));
            bool warriorStagger = warrior.IsStaggered;
            MCheck($"super armor: knight attacking={armored} staggered={staggered} pushed={pushed:0.00}; plain skeleton staggered={warriorStagger}",
                armored && !staggered && pushed < 0.15f && warriorStagger);
            yield return MWaitUntil(() => !kb.Busy, 3f);
        }

        IEnumerator DodgeChecks(PlayerController local, PlayerController bron, PlayerController elin, Dictionary<PlayerController, IActorInput> brains)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-1.1f, 0.5f), new Vector2(1.1f, 0.5f));
            foreach (var pair in brains) pair.Key.Input = pair.Value;
            yield return MWait(1.2f);
            var hitList = new List<string>();
            var teles = new List<Telegraph>();
            foreach (var m in new[] { bron, elin })
            {
                var t = Telegraph.Circle(null, m.Position, 1.5f, 1.6f, 1, 0f);
                t.OnResolve += (tele, hit) => { foreach (var h in hit) hitList.Add(h.DisplayName); };
                teles.Add(t);
            }
            var startPos = new[] { bron.Position, elin.Position };
            yield return MWait(0.8f);
            yield return Shot("mon_26_companion_dodge");
            yield return MWait(1.0f);
            bool bronOut = !hitList.Contains(bron.DisplayName), elinOut = !hitList.Contains(elin.DisplayName);
            MCheck($"companions dodge telegraphs: bron escaped={bronOut} ({Vector2.Distance(startPos[0], bron.Position):0.0} moved) elin escaped={elinOut} ({Vector2.Distance(startPos[1], elin.Position):0.0} moved) hit=[{string.Join(",", hitList)}]",
                bronOut || elinOut);
            foreach (var m in new[] { bron, elin }) m.Input = new ScriptedInput();
        }

        IEnumerator UnblockableChecks(PlayerController local)
        {
            MonClear();
            yield return MWait(0.3f);
            int block = local.Data.Stats.Block;
            int seed = -1;
            for (int s = 1; s < 5000 && block > 0; s++)
            {
                UnityEngine.Random.InitState(s);
                if (UnityEngine.Random.Range(0, 100) < block) { seed = s; break; }
            }
            yield return MWaitUntil(() => !local.Health.IsInvulnerable, 3f);
            bool blocked = false, landed = false;
            if (seed > 0)
            {
                UnityEngine.Random.InitState(seed);
                blocked = !local.TakeDamage(new DamageInfo(10, local.Position + Vector2.right, 0f, Team.Enemy));
                yield return MWaitUntil(() => !local.Health.IsInvulnerable, 3f);
                UnityEngine.Random.InitState(seed);
                landed = local.TakeDamage(new DamageInfo(10, local.Position + Vector2.right, 0f, Team.Enemy) { unblockable = true });
            }
            UnityEngine.Random.InitState(Environment.TickCount);
            MCheck($"unblockable skips the block roll: block={block}% seed={seed} normal blocked={blocked} unblockable landed={landed}", blocked && landed);
            yield return MWait(0.5f);
        }

        // =============================== Bosses ===============================

        IEnumerator BossChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            var shotKinds = new HashSet<PatternKind>();
            int shotIndex = 31;
            foreach (var id in BossIds)
            {
                MonClear();
                PlaceMembers(local, bron, elin, new Vector2(-2f, 1.8f), new Vector2(-2f, -1.8f));
                yield return null;
                var boss = MonSpawn(id, new Vector2(5f, 0.3f), 20f);
                var brain = boss.Behaviour as BossBrain;
                yield return MWait(0.45f);
                var bar = BossHpBarView.Instance;
                bool bound = bar != null && bar.Current == boss && bar.DevVisible;
                if (id == BossIds[0]) yield return Shot("mon_30_boss_intro");
                // Super armor: a heavy hit neither staggers nor pushes.
                Vector2 before = boss.Position;
                boss.TakeDamage(FromLocal(10, local.Center, 20f));
                yield return new WaitForFixedUpdate();
                bool armor = !boss.IsStaggered && Vector2.Distance(before, boss.Position) < 0.3f;
                // Every pattern once.
                var results = new List<string>();
                bool allOk = true;
                foreach (var p in boss.Def.patterns)
                {
                    if (p.kind == PatternKind.Judgment) continue;
                    yield return MWaitUntil(() => !brain.Busy, 6f);
                    // Back to the open spot so every pattern screenshot shows boss and party.
                    PlaceMembers(local, bron, elin, new Vector2(-2f, 1.8f), new Vector2(-2f, -1.8f));
                    MoveEnemy(boss, MonFree(MonSpot + new Vector2(4.5f, 0.3f)));
                    yield return null;
                    int summoned0 = brain.Summoned;
                    brain.DevPlay(p.kind);
                    int maxTele = 0, maxProj = 0;
                    bool guard = false, taken = false;
                    float t0 = Time.realtimeSinceStartup;
                    while (brain.Busy && Time.realtimeSinceStartup - t0 < 8f)
                    {
                        MonTank();
                        int tele = Telegraph.Active.Count(t => t.Owner == boss);
                        maxTele = Mathf.Max(maxTele, tele);
                        maxProj = Mathf.Max(maxProj, MonsterProjectile.Active.Count(x => x != null && x.Owner == boss));
                        guard |= brain.Guarding;
                        if (!taken && !shotKinds.Contains(p.kind) && (tele > 0 && Telegraph.Active.Any(t => t.Owner == boss && t.Progress > 0.55f) || maxProj > 0 || brain.Summoned > summoned0))
                        {
                            taken = true;
                            shotKinds.Add(p.kind);
                            yield return Shot($"mon_{shotIndex++}_{id}_{p.kind}");
                            continue;
                        }
                        yield return null;
                    }
                    bool ok = maxTele > 0 || maxProj > 0 || brain.Summoned > summoned0 || guard;
                    allOk &= ok && !brain.Busy;
                    results.Add($"{p.kind}:{(ok ? "ok" : "none")}");
                }
                MCheck($"{id}: bar bound={bound} lines='{bar?.DevLinesText}' markers={bar?.DevMarkers} super armor={armor} patterns [{string.Join(" ", results)}]",
                    bound && armor && allOk);

                if (id == "boss_gold_foreman")
                {
                    // Groggy: break the gauge, then hits deal +30%.
                    yield return MWaitUntil(() => !brain.Busy, 6f);
                    float g0 = boss.Groggy;
                    boss.TakeDamage(FromLocal(Mathf.CeilToInt(boss.Groggy) + 1, local.Center, 0f));
                    bool groggy = boss.IsGroggy;
                    yield return MWait(0.25f);
                    int h0 = boss.Health.Current;
                    boss.TakeDamage(FromLocal(100, local.Center, 0f));
                    int dealt = h0 - boss.Health.Current;
                    yield return MWait(0.3f);
                    yield return Shot("mon_29_groggy");
                    string gtext = bar != null ? bar.DevGroggyText : "";
                    MCheck($"groggy: gauge {g0:0} broke={groggy} for {boss.GroggyDuration:0.0}s, 100 dmg -> {dealt}, bar='{gtext}'",
                        groggy && Mathf.Abs(boss.GroggyDuration - EnemyController.GroggySeconds) < 0.01f && dealt == 130 && gtext.StartsWith("GROGGY!"));
                    yield return MWait(0.3f);
                }
                boss.TakeDamage(FromLocal(boss.Health.Current + 99999, local.Center, 0f));
                yield return MWait(1.6f);
                if (id == BossIds[0]) MCheck($"bar unbinds after the kill: current={(bar != null && bar.Current != null ? bar.Current.name : "none")} visible={bar?.DevVisible}", bar != null && bar.Current == null && !bar.DevVisible);
            }
        }

        IEnumerator KingChecks(PlayerController local, PlayerController bron, PlayerController elin)
        {
            MonClear();
            PlaceMembers(local, bron, elin, new Vector2(-2f, 1.8f), new Vector2(-2f, -1.8f));
            yield return null;
            var king = MonSpawn("boss_skeleton_king", new Vector2(5f, 0.3f), 1f);
            var brain = king.Behaviour as BossBrain;
            var hp = king.Health;
            yield return MWait(1.5f);
            bool p1 = brain.Phase == 1;

            // ---------- Phase 2 at 70% ----------
            yield return MWaitUntil(() => !brain.Busy, 6f);
            king.TakeDamage(FromLocal(hp.Current - Mathf.FloorToInt(hp.Max * 0.69f), local.Center, 0f));
            king.EndGroggyNow();
            yield return MWait(0.3f);
            var totems = brain.Totems.ToList();
            bool priority = totems.Contains(CompanionBrain.PriorityTarget);
            MCheck($"king phase 1 -> 2 at {(float)hp.Current / hp.Max:0.00}: was p1={p1} phase={brain.Phase} totems={totems.Count} companion priority on a totem={priority}",
                p1 && brain.Phase == 2 && totems.Count == 2 && priority);
            PlaceMembers(local, bron, elin, new Vector2(-2f, 1.8f), new Vector2(-2f, -1.8f));
            MoveEnemy(king, MonFree(MonSpot + new Vector2(4.5f, 0.3f)));
            yield return MWaitUntil(() => brain.Summoned >= 2, 5f);
            yield return MWait(0.5f);
            yield return Shot("mon_50_king_phase2_totems");
            int summonedP2 = brain.Summoned;

            // ---------- Totem rule: one broken + 6 s -> it rises again ----------
            var a = totems[0];
            a.TakeDamage(FromLocal(a.Health.Current + 999, a.Position));
            yield return MWait(0.3f);
            int standing = brain.Totems.Count();
            yield return MWait(5.9f);
            var after = brain.Totems.ToList();
            MCheck($"totem rule (slow): after one broken {standing} standing; 6 s later {after.Count} standing revives={brain.TotemRevives} cleared={brain.TotemsCleared}",
                standing == 1 && after.Count == 2 && brain.TotemRevives == 1 && !brain.TotemsCleared);

            // ---------- Both within 5 s -> gone for good, summons stop ----------
            after[0].TakeDamage(FromLocal(after[0].Health.Current + 999, after[0].Position));
            yield return MWait(2f);
            after[1].TakeDamage(FromLocal(after[1].Health.Current + 999, after[1].Position));
            yield return MWait(0.4f);
            bool cleared = brain.TotemsCleared && !brain.Totems.Any() && CompanionBrain.PriorityTarget == null;
            foreach (var m in EnemyController.Active.ToList())
                if (m != null && !m.IsDead && m.Summoner == king) m.TakeDamage(FromLocal(m.Health.Current + 9999, m.Position));
            int summonedAtClear = brain.Summoned;
            yield return MWait(BossBrain.SummonEvery + 1.5f);
            MCheck($"totem rule (together): cleared={cleared} summons before={summonedP2} at clear={summonedAtClear} after {BossBrain.SummonEvery + 1.5f:0}s={brain.Summoned} revives={brain.TotemRevives}",
                cleared && summonedP2 >= 2 && brain.Summoned == summonedAtClear && brain.TotemRevives == 1);

            // ---------- Phase 3 at 35% ----------
            yield return MWaitUntil(() => !brain.Busy, 6f);
            king.TakeDamage(FromLocal(hp.Current - Mathf.FloorToInt(hp.Max * 0.34f), local.Center, 0f));
            king.EndGroggyNow();
            yield return MWait(0.3f);
            MCheck($"king phase 3 at {(float)hp.Current / hp.Max:0.00}: phase={brain.Phase} enraged={brain.Enraged}", brain.Phase == 3 && brain.Enraged);
            foreach (var m in EnemyController.Active.ToList())
                if (m != null && !m.IsDead && m.Summoner == king) m.TakeDamage(FromLocal(m.Health.Current + 9999, m.Position));

            // ---------- 망자의 심판: broken ----------
            int judgmentEnds = 0;
            bool lastBroken = false;
            Action<BossBrain, bool> onEnd = (b, broken) => { judgmentEnds++; lastBroken = broken; };
            brain.JudgmentEnded += onEnd;
            int bigHits = 0;
            int judgmentDamage = king.MonsterDamage(8f);
            var hooks = new List<(Health h, Action<DamageInfo> act)>();
            foreach (var m in MonMembers(local))
            {
                Action<DamageInfo> act = i => { if (i.attacker == king.gameObject && i.amount >= judgmentDamage) bigHits++; };
                m.Health.Damaged += act;
                hooks.Add((m.Health, act));
            }
            yield return MWaitUntil(() => !brain.Busy, 6f);
            PlaceMembers(local, bron, elin, new Vector2(-2f, 1.8f), new Vector2(-2f, -1.8f));
            MoveEnemy(king, MonFree(MonSpot + new Vector2(4.5f, 0.3f)));
            brain.DevRequestJudgment();
            yield return MWaitUntil(() => brain.CastingJudgment, 8f);
            bool casting = brain.CastingJudgment;
            yield return MWait(1.5f);
            yield return Shot("mon_51_king_judgment_cast");
            king.TakeDamage(FromLocal(Mathf.CeilToInt(king.Groggy) + 1, local.Center, 0f));
            bool groggy = king.IsGroggy;
            float groggyLen = king.GroggyDuration;
            yield return MWait(0.5f);
            yield return Shot("mon_52_king_judgment_broken");
            yield return MWait(1.0f);
            MCheck($"judgment broken: casting={casting} groggy={groggy} for {groggyLen:0.0}s ended={judgmentEnds} broken={lastBroken} big hits={bigHits}",
                casting && groggy && Mathf.Abs(groggyLen - BossBrain.JudgmentGroggy) < 0.01f && judgmentEnds == 1 && lastBroken && bigHits == 0 && !brain.CastingJudgment);
            king.EndGroggyNow();

            // ---------- 망자의 심판: resolved ----------
            yield return MWait(0.3f);
            foreach (var m in EnemyController.Active.ToList())
                if (m != null && !m.IsDead && m.Summoner == king) m.TakeDamage(FromLocal(m.Health.Current + 9999, m.Position));
            brain.DevRequestJudgment();
            yield return MWaitUntil(() => brain.CastingJudgment, 8f);
            bool casting2 = brain.CastingJudgment;
            int alive = MonMembers(local).Count(m => !m.IsDead);
            yield return MWaitUntil(() => !brain.CastingJudgment, BossBrain.JudgmentCast + 2f);
            yield return MWait(0.2f);
            yield return Shot("mon_53_king_judgment_resolved");
            MCheck($"judgment resolved: casting={casting2} ended={judgmentEnds} broken={lastBroken} hits={brain.JudgmentHits}/{alive} members big hits={bigHits} (≥{judgmentDamage})",
                casting2 && judgmentEnds == 2 && !lastBroken && brain.JudgmentHits == alive && bigHits == alive);
            brain.JudgmentEnded -= onEnd;
            foreach (var (h, act) in hooks) h.Damaged -= act;

            king.TakeDamage(FromLocal(hp.Current + 99999, local.Center, 0f));
            yield return MWait(1.6f);
            bool minionsGone = !EnemyController.Active.Any(e => e != null && !e.IsDead && e.Summoner == king);
            MCheck($"king killed: minions and totems fall with it={minionsGone} bar unbound={BossHpBarView.Instance != null && BossHpBarView.Instance.Current == null}",
                minionsGone && BossHpBarView.Instance != null && BossHpBarView.Instance.Current == null);
        }

        static List<PlayerController> MonMembers(PlayerController local) =>
            Game.Party != null ? Game.Party.Members.Where(m => m != null).ToList() : new List<PlayerController> { local };
    }
}
