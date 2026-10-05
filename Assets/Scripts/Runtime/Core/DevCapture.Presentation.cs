using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class DevCapture
    {
        IEnumerator PresentationShot(string name)
        {
            yield return null;
            var canvas = Game.UI.GetComponent<Canvas>();
            var camera = Game.Camera.Camera;
            var oldTarget = camera.targetTexture; var oldActive = RenderTexture.active;
            var oldMode = canvas.renderMode; var oldCamera = canvas.worldCamera; float oldPlane = canvas.planeDistance;
            int width = 1280, height = 720; var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-screen-width") int.TryParse(args[i + 1], out width);
                if (args[i] == "-screen-height") int.TryParse(args[i + 1], out height);
            }
            var scaler = Game.UI.GetComponent<CanvasScaler>(); var oldScaleMode = scaler.uiScaleMode; float oldScale = scaler.scaleFactor;
            var rt = new RenderTexture(width, height, 24);
            var texture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = rt;
                canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize; scaler.scaleFactor = Mathf.Min(width / 1280f, height / 720f);
                Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = rt;
                texture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); texture.Apply();
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), texture.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = oldTarget; canvas.renderMode = oldMode; canvas.worldCamera = oldCamera; canvas.planeDistance = oldPlane;
                scaler.uiScaleMode = oldScaleMode; scaler.scaleFactor = oldScale;
                RenderTexture.active = oldActive; rt.Release(); Destroy(rt); Destroy(texture); Canvas.ForceUpdateCanvases();
            }
        }

        IEnumerator PresentationRun()
        {
            dgnPassed = dgnFailed = 0; ApplyRequestedResolution();
            yield return Wait(1); Game.Config.autosave = false;
            Game.Flow.NewGame(CharacterClass.Warrior); yield return Wait(1.5f);
            Game.Player.Input = new ScriptedInput(); Game.Player.Health.SetInvulnerable(600);
            var bag = Game.Session.Inventory; var gear = Game.Session.Equipment;
            Game.Session.Progression.AddXp(25000);
            foreach (var id in new[] { "Lt0", "Lt1", "Ld1", "LD", "La1", "LA", "Lx1", "LM", "Rt0", "Rt1", "Rd1", "RD" }) Game.Session.Progression.Allocate(PassiveTree.Get(id));
            Game.Flow.OpenWindow(Game.UI.Skills); yield return Wait(.3f);
            DCheck("skill nodes built", Game.UI.Skills.GetComponentsInChildren<Image>().Count(i => i.name.StartsWith("Node_")) == PassiveTree.All.Count());
            yield return PresentationShot("01_skill_tree");
            Game.Flow.OpenWindow(PartyFinderScreen.Instance); yield return null;
            var finder = PartyFinderScreen.Instance;
            Button FinderButton(string name) => finder.GetComponentsInChildren<Button>(true).First(b => b.name == name);
            FinderButton("TabCreate").onClick.Invoke();
            yield return PresentationShot("01b_party_registration");
            for (int i = 0; i < 3; i++) FinderButton("C1").onClick.Invoke();
            for (int i = 0; i < 3; i++) FinderButton("C4").onClick.Invoke();
            yield return PresentationShot("01c_party_registration_long");
            Game.Flow.CloseInventory(); Game.Flow.Pause(); Game.UI.Push(Game.UI.KeyBind); yield return Wait(.2f);
            var kb = Game.UI.KeyBind;
            DCheck("physical keyboard rendered", kb.GetComponentsInChildren<Image>().Count(i => i.name.StartsWith("Key_")) >= 95);
            DCheck("no duplicate default keys", InputReader.Rebindable.Select(InputReader.KeyboardKey).Distinct().Count() == InputReader.Rebindable.Length);
            yield return PresentationShot("02_keyboard");
            // Use the exact same pointer relay as a mouse click on an action row and keycap.
            void Click(string name)
            {
                var target = kb.GetComponentsInChildren<Transform>().First(t => t.name == name).gameObject;
                ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            }
            Click("Binding_Attack"); Click("Key_Q");
            DCheck("click swaps conflicting attack and skill keys", InputReader.KeyboardKey(GameAction.Attack) == KeyCode.Q && InputReader.KeyboardKey(GameAction.Skill1) == KeyCode.X);
            kb.BeginBinding(GameAction.MoveUp); kb.AssignKey(KeyCode.W);
            DCheck("movement rebind swaps occupied skill key", InputReader.KeyboardKey(GameAction.MoveUp) == KeyCode.W && InputReader.KeyboardKey(GameAction.Skill2) == KeyCode.UpArrow);
            DCheck("old movement key no longer moves", InputReader.KeyboardMove(k => k == KeyCode.UpArrow) == Vector2.zero);
            DCheck("new movement key drives character", InputReader.KeyboardMove(k => k == KeyCode.W) == Vector2.up);
            DCheck("diagonal movement remains normalized", Mathf.Abs(InputReader.KeyboardMove(k => k == KeyCode.W || k == KeyCode.RightArrow).magnitude - 1f) < .001f);
            kb.BeginBinding(GameAction.Map); kb.AssignKey(KeyCode.Escape);
            DCheck("reserved key rejected", InputReader.KeyboardKey(GameAction.Map) == KeyCode.M && kb.IsListening);
            kb.AssignKey(KeyCode.F8); kb.ApplyChanges();
            var settings = new SettingsManager(); settings.Load();
            DCheck("keys persisted to settings", settings.Data.keyOverrides == InputReader.SaveKeyOverrides() && SettingsManager.FilePath.StartsWith(folder));
            string savedKeys = InputReader.SaveKeyOverrides();
            InputReader.ResetKeyboardKeys(); InputReader.LoadKeyOverrides(settings.Data.keyOverrides);
            DCheck("bindings restored after reload", InputReader.SaveKeyOverrides() == savedKeys);
            kb.BeginBinding(GameAction.Attack); kb.AssignKey(KeyCode.Z); Game.UI.Pop();
            DCheck("close discards unsaved edit", InputReader.KeyboardKey(GameAction.Attack) == KeyCode.Q);
            Game.UI.Push(kb); kb.ResetDraft();
            DCheck("reset restores defaults", InputReader.Rebindable.All(a => InputReader.KeyboardKey(a) == InputReader.DefaultKey(a)));
            kb.ApplyChanges();
            kb.SelectTab(true);
            DCheck("all HUD windows listed on window tab", kb.GetComponentsInChildren<Image>().Count(i => i.name.StartsWith("Binding_")) == InputReader.WindowActions.Length);
            yield return PresentationShot("02b_keyboard_windows");
            // Exercise each new binding through the same dispatcher used by GameFlow.Update.
            foreach (var action in InputReader.WindowActions)
            {
                var oldKey = InputReader.KeyboardKey(action);
                kb.BeginBinding(action); kb.AssignKey(KeyCode.F9); kb.ApplyChanges();
                var persisted = new SettingsManager(); persisted.Load(); InputReader.LoadKeyOverrides(persisted.Data.keyOverrides);
                DCheck(action + " window key survives save reload", InputReader.KeyboardKey(action) == KeyCode.F9);
                Game.UI.Pop(); Game.Flow.Resume(); yield return null;
                DCheck(action + " old key no longer opens window", !Game.Flow.ProcessWindowShortcuts(a => InputReader.KeyboardKey(a) == oldKey));
                DCheck(action + " rebound key opens expected window", Game.Flow.ProcessWindowShortcuts(a => InputReader.KeyboardKey(a) == KeyCode.F9) && Game.UI.Top == Game.Flow.WindowFor(action));
                if (action == GameAction.WeekdayDungeon || action == GameAction.RaidWindow)
                    DCheck(action + " correct dungeon tab", Game.UI.Dungeon.IsRaidTab == (action == GameAction.RaidWindow));
                yield return null;
                DCheck(action + " same shortcut closes", Game.Flow.ProcessWindowShortcuts(a => InputReader.KeyboardKey(a) == KeyCode.F9) && Game.State.Current == GameState.Playing);
                yield return null;
                Game.Flow.Pause(); Game.UI.Push(kb); kb.ResetDraft(); kb.ApplyChanges();
            }
            Game.UI.Pop(); Game.Flow.Resume(); yield return null;
            InputReader.TextInputActive = true;
            DCheck("typing suppresses window shortcuts", !Game.Flow.ProcessWindowShortcuts(a => a == GameAction.SkillWindow));
            InputReader.TextInputActive = false;
            Game.Flow.ProcessWindowShortcuts(a => a == GameAction.WeekdayDungeon); yield return null;
            Game.Flow.ProcessWindowShortcuts(a => a == GameAction.RaidWindow);
            DCheck("weekday shortcut switches to raid tab", Game.UI.Top == Game.UI.Dungeon && Game.UI.Dungeon.IsRaidTab);
            Game.UI.Confirm("Test", () => { }, overlay: true); yield return null;
            DCheck("confirmation cannot be bypassed by shortcut", !Game.Flow.ProcessWindowShortcuts(a => a == GameAction.Map) && Game.UI.Top == Game.UI.ConfirmDialog);
            Game.UI.Pop(); Game.Flow.CloseInventory();
            foreach (var cls in new[] { CharacterClass.Warrior, CharacterClass.Mage })
            {
                Game.Player.SetClass(cls);
                foreach (int level in new[] { 0, 1, 6, 7, 10, 11, 12, 13 })
                {
                    string key = EquipmentDatabase.KeyFor(cls == CharacterClass.Warrior ? "eq_sword_10_u" : "eq_staff_10_u", level);
                    bag.Add(key, 1); gear.Equip(key, cls); yield return Wait(.08f);
                    var fx = Game.Player.GetComponent<WeaponEnhanceVfx>();
                    DCheck(cls + " enhancement +" + level, fx.Tier == WeaponEnhanceVfx.TierFor(level) && fx.LightVisible == (level >= 7) && fx.AuraVisible == (level >= 11));
                    if (level == 6 || level == 7 || level == 11)
                    {
                        Game.Player.Place(Game.Player.Position, Facing.Down); yield return Wait(.1f);
                        RenderRegion(Path.Combine(folder, cls + "_plus" + level + ".png"), new Rect(Game.Player.Position.x - 2.4f, Game.Player.Position.y - 1.5f, 4.8f, 4.8f), 128);
                    }
                }
                var combat = Game.Player.GetComponent<PlayerCombat>();
                foreach (Facing facing in Enum.GetValues(typeof(Facing)))
                {
                    Game.Player.Place(Game.Player.Position, facing); yield return Wait(.1f);
                    combat.TryAttack(); yield return Wait(.12f);
                    var weapon = combat.WeaponRenderer;
                    var rim = weapon.transform.Find("WeaponBlueLight").GetComponent<SpriteRenderer>();
                    var aura = weapon.transform.Find("WeaponBlueAura").GetComponent<SpriteRenderer>();
                    DCheck(cls + " effect alignment " + facing, rim.enabled && aura.enabled && rim.sortingOrder == weapon.sortingOrder && rim.flipX == weapon.flipX && rim.transform.localPosition == Vector3.zero && rim.transform.localRotation == Quaternion.identity);
                    combat.Cancel();
                }
                gear.Unequip(EquipSlot.Weapon); yield return Wait(.1f);
                DCheck(cls + " unequip clears effect", !Game.Player.GetComponent<WeaponEnhanceVfx>().LightVisible && !Game.Player.GetComponent<WeaponEnhanceVfx>().AuraVisible);
            }
            Game.Player.SetClass(CharacterClass.Warrior);
            string targetKey = EquipmentDatabase.KeyFor("eq_sword_10_u", 10); bag.Add(targetKey, 1); gear.Equip(targetKey, CharacterClass.Warrior);
            bag.Add(ConsumableDatabase.Gold, 999999); bag.Add("mat_bone", 9999); bag.Add("mat_ore", 9999); bag.Add("mat_essence", 9999);
            Game.Flow.OpenWindow(Game.UI.Enhance); yield return Wait(.25f);
            Game.UI.Enhance.DevSelectSlot(EquipSlot.Weapon); Game.UI.Enhance.DevForceRoll(0); Game.UI.Enhance.DevPress();
            if (Game.UI.Top == Game.UI.ConfirmDialog) Game.UI.ConfirmDialog.DevAnswer(true);
            yield return Wait(.45f); yield return PresentationShot("03_forge_charge");
            while (Game.UI.Enhance.DevBusy) yield return null;
            yield return Wait(.1f); yield return PresentationShot("04_forge_success");
            DCheck("successful worn enhancement immediately enables aura", EquipmentDatabase.LevelOfKey(gear[EquipSlot.Weapon]) == 11 && Game.Player.GetComponent<WeaponEnhanceVfx>().AuraVisible);
            yield return Wait(1.3f);
            DCheck("forge effects cleaned after result", GameObject.Find("ForgeParticles") == null);
            var icon = Game.UI.Enhance.GetComponentsInChildren<Image>().First(i => i.name == "Icon" && i.transform.parent.name == "IconBg").rectTransform;
            var origin = icon.anchoredPosition;
            EnhanceFx.Fail(icon, true); yield return Wait(.1f); Game.Flow.CloseInventory(); yield return Wait(.15f);
            DCheck("closing forge clears particles and restores position", icon.anchoredPosition == origin && !Game.UI.Enhance.GetComponentsInChildren<RectTransform>(true).Any(t => t.name == "ForgeParticles"));
            Game.World.Load(MapRegistry.Winter); Game.Player.Place(Game.World.PlayerSpawn, Facing.Down); yield return Wait(.2f);
            DCheck("snow fences use connected carrot-plot pieces", Game.World.ObjectsRoot.GetComponentsInChildren<SpriteRenderer>().Any(sr => sr.sprite != null && sr.sprite.name.StartsWith("town_fence_")));
            RenderRegion(Path.Combine(folder, "05_winter_fences.png"), Game.World.Bounds, 32);
            yield return AtlasChecks();
            Log($"PRESENTATION summary: {dgnPassed} passed, {dgnFailed} failed");
        }
    }
}
