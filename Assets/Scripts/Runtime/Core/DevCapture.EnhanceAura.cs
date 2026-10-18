using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

namespace DotRPG
{
    public partial class DevCapture
    {
        int auraPassed, auraFailed;
        static readonly int[] AuraGrades = { 0, 6, 7, 10, 11, 12, 13, 15, 20 };

        void AuraCheck(string label, bool ok)
        {
            if (ok) auraPassed++; else auraFailed++;
            Log("AURA " + (ok ? "PASS " : "FAIL ") + label);
        }

        static SpriteRenderer AuraLayer(SpriteRenderer source, string name) => source.transform.Find(name).GetComponent<SpriteRenderer>();

        void AuraAligned(SpriteRenderer source, SpriteRenderer effect, string label)
        {
            AuraCheck(label + " source-local transform", effect.transform.parent == source.transform && effect.transform.localPosition == Vector3.zero &&
                effect.transform.localRotation == Quaternion.identity && effect.transform.localScale == Vector3.one);
            AuraCheck(label + " same depth, mirror and mask", effect.sortingLayerID == source.sortingLayerID && effect.sortingOrder == source.sortingOrder &&
                effect.flipX == source.flipX && effect.flipY == source.flipY && effect.maskInteraction == source.maskInteraction);
            if (effect.sprite == null) { AuraCheck(label + " has sprite", false); return; }
            var pad = (effect.sprite.rect.size - source.sprite.rect.size) * .5f;
            AuraCheck(label + " source pivot and pixel density", Vector2.Distance(effect.sprite.pivot, source.sprite.pivot + pad) < .001f &&
                Mathf.Abs(effect.sprite.pixelsPerUnit - source.sprite.pixelsPerUnit) < .001f);
            AuraCheck(label + " nearest-neighbor sampling", effect.sprite.texture.filterMode == FilterMode.Point);
        }

        IEnumerator AuraGpuPixels(Texture2D texture, Action<Color32[]> result)
        {
            var request = AsyncGPUReadback.Request(texture, 0, TextureFormat.RGBA32);
            while (!request.done) yield return null;
            AuraCheck(texture.name + " GPU readback", !request.hasError);
            result(request.hasError ? null : request.GetData<Color32>().ToArray());
        }

        IEnumerator AuraLegacyPixels(Sprite source, SpriteRenderer rim, SpriteRenderer aura, string cls)
        {
            Color32[] gotRim = null, gotAura = null;
            yield return AuraGpuPixels(rim.sprite.texture, p => gotRim = p);
            yield return AuraGpuPixels(aura.sprite.texture, p => gotAura = p);
            // Independent legacy baseline: the shipped 7px Manhattan rim and blue haze.
            const int pad = 7;
            int w = (int)source.rect.width, h = (int)source.rect.height, tw = w + 14, th = h + 14;
            var src = source.texture.GetPixels32(); var mask = new bool[tw * th];
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
                if (src[((int)source.rect.y + y) * source.texture.width + (int)source.rect.x + x].a > 100) mask[(y + pad) * tw + x + pad] = true;
            var expectedRim = new Color32[tw * th]; var expectedAura = new Color32[tw * th];
            for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
            {
                int nearest = 8;
                for (int dy = -pad; dy <= pad; dy++) for (int dx = -pad; dx <= pad; dx++)
                {
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && yy >= 0 && xx < tw && yy < th && mask[yy * tw + xx]) nearest = Mathf.Min(nearest, Mathf.Abs(dx) + Mathf.Abs(dy));
                }
                bool inside = mask[y * tw + x];
                expectedRim[y * tw + x] = new Color(1, 1, 1, inside ? .28f : nearest == 1 ? .85f : nearest == 2 ? .28f : 0);
                expectedAura[y * tw + x] = new Color(1, 1, 1, inside ? .08f : Mathf.Pow(Mathf.Clamp01(1f - nearest / 7f), 2f) * .7f);
            }
            AuraCheck(cls + " <=12 legacy blue rim pixels preserved", gotRim != null && gotRim.SequenceEqual(expectedRim));
            AuraCheck(cls + " <=12 legacy blue aura pixels preserved", gotAura != null && gotAura.SequenceEqual(expectedAura));
        }

        IEnumerator AuraCrownPixels(Sprite source, SpriteRenderer crown, SpriteRenderer flow, string cls)
        {
            Color32[] actual = null;
            yield return AuraGpuPixels(crown.sprite.texture, p => actual = p);
            if (actual == null) yield break;
            int tw = (int)crown.sprite.rect.width, th = (int)crown.sprite.rect.height;
            int pad = (tw - (int)source.rect.width) / 2, atlasWidth = crown.sprite.texture.width;
            var src = source.texture.GetPixels32(); bool transparent = true;
            var hashes = new HashSet<int>();
            for (int frame = 0; frame < WeaponEnhanceVfx.CrownFrameCount; frame++)
            {
                int hash = 17;
                for (int y = 0; y < th; y++) for (int x = 0; x < tw; x++)
                {
                    var shell = actual[y * atlasWidth + frame * tw + x];
                    var streak = actual[(y + th) * atlasWidth + frame * tw + x];
                    unchecked { hash = hash * 31 + shell.GetHashCode(); hash = hash * 31 + streak.GetHashCode(); }
                    int sx = x - pad, sy = y - pad;
                    if (sx >= 0 && sx < source.rect.width && sy > source.pivot.y + 3 && sy < source.rect.height &&
                        src[((int)source.rect.y + sy) * source.texture.width + (int)source.rect.x + sx].a > 100)
                        transparent &= shell.a == 0 && streak.a == 0;
                }
                hashes.Add(hash);
            }
            AuraCheck(cls + " new aura does not paint over blade pixels", transparent);
            AuraCheck(cls + " eight distinct moving aura drawings", hashes.Count == 8);
            AuraCheck(cls + " two effects share one cached atlas", crown.sprite.texture == flow.sprite.texture);
        }

        IEnumerator EnhanceAuraRun()
        {
            auraPassed = auraFailed = 0;
            bool deathOnly = Array.IndexOf(Environment.GetCommandLineArgs(), "-enhanceDeathOnly") >= 0;
            yield return Wait(1f);
            bool autosave = Game.Config.autosave;
            try
            {
                Game.Config.autosave = false;
                Game.Flow.NewGame(CharacterClass.Warrior, "13강 무기 오라 검증"); yield return Wait(1f);
                ApplyRequestedResolution(); yield return Wait(.2f);
                AudioListener.volume = 0; Game.Audio?.SetVolumes(0, 0);
                Game.Session.Progression.SetFromServer(40, 0);
                Game.Player.Health.SetInvulnerable(240); Game.UI.Hud.ClearToasts();
                var player = Game.Player; var combat = player.GetComponent<PlayerCombat>(); var animator = player.GetComponent<CharacterAnimator>();
                var fx = player.GetComponent<WeaponEnhanceVfx>(); var bag = Game.Session.Inventory; var gear = Game.Session.Equipment;
                var input = new ScriptedInput(); player.Input = input; var origin = MobilityTestOrigin();
                foreach (var cls in deathOnly ? Array.Empty<CharacterClass>() : new[] { CharacterClass.Warrior, CharacterClass.Mage })
                {
                    player.SetClass(cls); player.Place(origin, Facing.Down);
                    var item = EquipmentDatabase.All.First(e => e.category == EquipCategory.Weapon && e.UsableBy(cls) && e.Tier == 1 && !e.starter);
                    var sourcePixels = new Dictionary<int, Color32[]>();
                    foreach (int grade in AuraGrades)
                    {
                        string key = item.KeyAt(grade); bag.Add(key, 1);
                        AuraCheck(cls + " equips +" + grade, gear.Equip(key, cls));
                        int power = player.Data.Stats.Power(cls); yield return Wait(.05f); yield return new WaitForEndOfFrame();
                        var source = combat.WeaponRenderer; var rim = AuraLayer(source, "WeaponBlueLight"); var blue = AuraLayer(source, "WeaponBlueAura");
                        var crown = AuraLayer(source, "WeaponAscendantAura"); var flow = AuraLayer(source, "WeaponAscendantFlow");
                        int expected = grade < 7 ? 0 : grade < 11 ? 1 : grade < 13 ? 2 : 3;
                        AuraCheck(cls + " +" + grade + " threshold and layers", fx.Tier == expected && fx.LightVisible == (grade >= 7) && fx.AuraVisible == (grade >= 11) &&
                            fx.CrownVisible == (grade >= 13) && flow.enabled == (grade >= 13) && (grade >= 13 ? fx.AuraFrame >= 0 && fx.AuraFrame < 8 : fx.AuraFrame == -1));
                        AuraCheck(cls + " +" + grade + " visual effect leaves combat power alone", power == player.Data.Stats.Power(cls));
                        sourcePixels[grade] = source.sprite.texture.GetPixels32();
                        if (grade >= 13)
                        {
                            AuraAligned(source, crown, cls + " +" + grade + " crown"); AuraAligned(source, flow, cls + " +" + grade + " flow");
                            AuraCheck(cls + " +" + grade + " larger crown bounds", crown.sprite.rect.width > blue.sprite.rect.width && crown.sprite.rect.height > blue.sprite.rect.height);
                        }
                        if (grade == 12) yield return AuraLegacyPixels(source.sprite, rim, blue, cls.ToString());
                        if (grade == 13) yield return AuraCrownPixels(source.sprite, crown, flow, cls.ToString());
                        if (grade == 12 || grade == 13 || grade == 20)
                        {
                            player.Place(origin, Facing.Down); yield return Wait(.1f); Game.UI.Hud.ClearToasts();
                            RenderRegion(Path.Combine(folder, cls + "-plus" + grade + ".png"), new Rect(origin.x - 2.6f, origin.y - 1.3f, 5.2f, 5.2f), 144);
                            yield return Shot(cls + "-plus" + grade + "-play");
                        }
                    }
                    AuraCheck(cls + " weapon source artwork identical at every grade", sourcePixels.Values.All(p => p.SequenceEqual(sourcePixels[0])));
                    gear.Equip(item.KeyAt(13), cls); yield return Wait(.1f);
                    var weapon = combat.WeaponRenderer; var shell = AuraLayer(weapon, "WeaponAscendantAura"); var stream = AuraLayer(weapon, "WeaponAscendantFlow");
                    var frames = new HashSet<int>(); var sprites = new HashSet<Sprite>();
                    int stablePower = player.Data.Stats.Power(cls), warmCache = WeaponEnhanceVfx.CachedWeaponCount;
                    float until = Time.realtimeSinceStartup + 1.3f;
                    while (Time.realtimeSinceStartup < until)
                    {
                        yield return new WaitForEndOfFrame(); frames.Add(fx.AuraFrame); sprites.Add(shell.sprite);
                    }
                    AuraCheck(cls + " animation cycles through every frame", frames.Count == 8 && sprites.Count == 8);
                    AuraCheck(cls + " cycling allocates no new weapon cache entries", WeaponEnhanceVfx.CachedWeaponCount == warmCache);
                    AuraCheck(cls + " animation does not modify damage power", player.Data.Stats.Power(cls) == stablePower);
                    foreach (Facing facing in Enum.GetValues(typeof(Facing)))
                    {
                        player.Place(origin, facing); input.Move = facing.ToVector(); yield return Wait(.09f); yield return new WaitForEndOfFrame();
                        AuraCheck(cls + " " + facing + " natural walk", animator.Current == CharacterAnim.Walk && player.Facing == facing);
                        AuraAligned(weapon, shell, cls + " " + facing + " walk crown"); AuraAligned(weapon, stream, cls + " " + facing + " walk flow");
                        if (cls == CharacterClass.Warrior) AuraCheck(facing + " warrior grip preserved", Vector2.Distance(weapon.transform.localPosition, SilverWarriorArt.Hand(SilverWarriorArt.ViewKey(facing), animator.FrameKey)) < .001f);
                        input.Move = Vector2.zero; combat.TryAttack(); yield return Wait(.09f); yield return new WaitForEndOfFrame();
                        AuraCheck(cls + " " + facing + " actual attack", combat.IsAttacking);
                        AuraAligned(weapon, shell, cls + " " + facing + " attack crown"); AuraAligned(weapon, stream, cls + " " + facing + " attack flow");
                        if (facing == Facing.UpRight || facing == Facing.UpLeft)
                            RenderRegion(Path.Combine(folder, cls + "-plus13-attack-" + facing + ".png"), new Rect(player.Position.x - 2.6f, player.Position.y - 1.3f, 5.2f, 5.2f), 144);
                        combat.Cancel(); yield return Wait(.6f);
                    }
                    // Stress only already-seen keys. This detects accidental atlas regeneration during re-equip.
                    warmCache = WeaponEnhanceVfx.CachedWeaponCount;
                    for (int cycle = 0; cycle < 2; cycle++) foreach (int grade in AuraGrades) { gear.Equip(item.KeyAt(grade), cls); yield return null; }
                    AuraCheck(cls + " repeated equip reuses cached masks and atlases", WeaponEnhanceVfx.CachedWeaponCount == warmCache);
                    gear.Equip(item.KeyAt(12), cls); yield return Wait(.1f);
                    AuraCheck(cls + " 13 to 12 removes new crown only", !fx.CrownVisible && !shell.enabled && !stream.enabled && fx.AuraVisible && fx.LightVisible && fx.AuraFrame == -1);
                    gear.Equip(item.KeyAt(13), cls); yield return Wait(.1f); fx.enabled = false; yield return null;
                    AuraCheck(cls + " disabling component clears all layers", !fx.CrownVisible && !fx.AuraVisible && !fx.LightVisible && !stream.enabled && fx.AuraFrame == -1);
                    fx.enabled = true; yield return Wait(.1f);
                    AuraCheck(cls + " re-enable restores the equipped crown", fx.CrownVisible && stream.enabled);
                    gear.Unequip(EquipSlot.Weapon); yield return Wait(.1f);
                    AuraCheck(cls + " unequip clears every effect", !fx.CrownVisible && !fx.AuraVisible && !fx.LightVisible && !stream.enabled && fx.AuraFrame == -1);
                    string previousMap = Game.World.MapId;
                    Game.World.Load("hollow_depths"); player.Place(Game.World.PlayerSpawn, Facing.Down);
                    player.Health.SetInvulnerable(240); Game.Camera.SetTarget(player.transform, true);
                    yield return Wait(.25f);
                    foreach (int grade in new[] { 12, 13 })
                    {
                        gear.Equip(item.KeyAt(grade), cls); yield return Wait(.1f); Game.UI.Hud.ClearToasts();
                        var at = player.Position;
                        RenderRegion(Path.Combine(folder, cls + "-plus" + grade + "-dark.png"), new Rect(at.x - 2.6f, at.y - 1.3f, 5.2f, 5.2f), 144);
                        yield return Shot(cls + "-plus" + grade + "-dark-play");
                    }
                    Game.World.Load(previousMap); player.Place(origin, Facing.Down); Game.Camera.SetTarget(player.transform, true);
                    yield return Wait(.2f);
                }
                player.SetClass(CharacterClass.Mage);
                var mageWeapon = EquipmentDatabase.All.First(e => e.category == EquipCategory.Weapon && e.UsableBy(CharacterClass.Mage) && e.Tier == 1 && !e.starter);
                bag.Add(mageWeapon.KeyAt(13), 1);
                gear.Equip(mageWeapon.KeyAt(13), CharacterClass.Mage); yield return Wait(.1f);
                player.Health.Init(player.Health.Max, player.Health.Max, 0);
                var lethal = new DamageInfo(player.Health.Max * 10, player.Position, 0, Team.Enemy, null) { unblockable = true };
                AuraCheck("lethal hit follows the actual player damage pipeline", player.TakeDamage(lethal)); yield return Wait(.1f);
                AuraCheck("character death clears all enhancement layers", player.IsDead && !fx.CrownVisible && !fx.AuraVisible && !fx.LightVisible && fx.AuraFrame == -1);
                float deathUiUntil = Time.realtimeSinceStartup + 2.5f;
                while (Time.realtimeSinceStartup < deathUiUntil &&
                    (Game.State.Current != GameState.GameOver || Game.UI.Top != Game.UI.GameOver || !Game.UI.GameOver.gameObject.activeSelf))
                    yield return null;
                yield return Wait(.1f);
                bool deathVisible = Game.State.Current == GameState.GameOver && Game.UI.Top == Game.UI.GameOver && Game.UI.GameOver.gameObject.activeSelf;
                var deathMenu = Game.UI.GameOver.GetComponentInChildren<MenuList>();
                var deathLabels = deathMenu == null ? Array.Empty<string>() : deathMenu.GetComponentsInChildren<UnityEngine.UI.Text>()
                    .Where(t => t.name == "Label").Select(t => t.text).ToArray();
                AuraCheck("actual death menu is visible after death transition", deathVisible);
                AuraCheck("death menu contains only three current actions", deathVisible && deathMenu != null && deathMenu.Count == 3);
                // MenuList.Refresh adds a visible selection arrow and same-width invisible arrows.
                AuraCheck("death menu exact action labels", deathLabels.SequenceEqual(new[] {
                    "▶ " + ReviveCoins.FieldLabel,
                    "<color=#00000000>▶</color> " + L.T("마을에서 다시 일어나기"),
                    "<color=#00000000>▶</color> " + L.T("타이틀로") }));
                if (deathMenu != null)
                {
                    deathMenu.SetSelectedSilently(3);
                    AuraCheck("death navigation has no hidden fourth action", deathMenu.Selected == 2);
                    deathMenu.SetSelectedSilently(0);
                }
                AuraCheck("removed last-save action is absent", !Game.UI.GameOver.GetComponentsInChildren<UnityEngine.UI.Text>(true).Any(t => t.text.Contains(L.T("마지막 저장 불러오기"))));
                Game.UI.Hud.ClearToasts(); yield return Shot("death-screen-current-actions");
                AuraCheck("verification stays silent", AudioListener.volume == 0);
                File.WriteAllText(Path.Combine(folder, "verification.json"), MiniJson.Write(HRow(("mode", deathOnly ? "focused-death-menu" : "full-enhancement-aura"),
                    ("grades", deathOnly ? new[] { 13 } : AuraGrades), ("classes", deathOnly ? new[] { "Mage" } : new[] { "Warrior", "Mage" }),
                    ("auraFrames", 8), ("directions", deathOnly ? 1 : 8), ("audioVolume", 0), ("cacheEntries", WeaponEnhanceVfx.CachedWeaponCount),
                    ("deathMenuVisible", deathVisible), ("deathMenuLabels", deathLabels), ("passed", auraPassed), ("failed", auraFailed))));
            }
            finally
            {
                if (Game.Player != null) Game.Player.Input = new LocalInput();
                Game.Config.autosave = autosave;
                Log($"AURA RESULTS: {auraPassed} passed, {auraFailed} failed");
            }
        }
    }
}
