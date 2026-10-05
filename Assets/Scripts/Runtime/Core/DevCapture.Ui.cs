using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class DevCapture
    {
        /// <summary>-dotrpgUi: screenshots and checks of the 32px windows, HUD and icons.</summary>
        IEnumerator UiShowcase()
        {
            // The standalone player keeps the desktop resolution in windowed mode, so honour the
            // requested -screen-width/-screen-height ourselves — the UI is resolution-dependent and the
            // brief wants a 1280x720 pass and a 1920x1080 pass.
            ApplyRequestedResolution();
            yield return Wait(1.2f);

            // ---- Title + character select ----
            yield return Shot("01_title");
            Game.UI.Push(Game.UI.CharacterSelect);
            yield return Wait(0.6f);
            yield return Shot("02_character_select");

            // ---- New game as mage, fill the bag/wallet so every window has content ----
            Game.Flow.NewGame(CharacterClass.Mage);
            yield return Wait(1.5f);
            var eq = Game.Session.Equipment;
            var bag = Game.Session.Inventory;
            var prog = Game.Session.Progression;
            bag.Add(ConsumableDatabase.Gold, 99999);
            bag.Add(ConsumableDatabase.HpPotion, 12); bag.Add(ConsumableDatabase.MpPotion, 8); bag.Add(ConsumableDatabase.TownScroll, 4);
            bag.Add("mat_bone", 60); bag.Add("mat_ore", 30); bag.Add("mat_essence", 9);
            foreach (var id in new[] { "eq_staff_10_u", "eq_neck_1_c", "eq_ring_10_r", "eq_ring_1_c", "eq_robe_1_u", "eq_skirt_1_c" })
                bag.Add(id, 1);
            eq.AutoEquip(Game.Player.Class);
            prog.AddXp(4000);
            yield return Wait(0.4f);
            Log($"setup: Lv={prog.Level} points={prog.PointsLeft} gold={bag.Count(ConsumableDatabase.Gold)} weapon={eq[EquipSlot.Weapon]}");

            // ---- HUD (status bars, skill bar, quick items, minimap, side-menu button) ----
            yield return Shot("03_hud");

            // ---- Bag / equipment window (frames, slots, the character + weapon preview) ----
            Game.Flow.OpenInventory();
            yield return Wait(0.5f);
            yield return Shot("04_bag_equipment");
            // Hover the worn weapon to raise a tooltip.
            var worn = GameObject.Find("Worn_Weapon");
            if (worn != null)
                ExecuteEvents.Execute(worn, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);
            yield return Wait(0.3f);
            var tip = GameObject.Find("Tooltip");
            Log($"tooltip: wornFound={worn != null} visible={(tip != null && tip.activeInHierarchy)}");
            yield return Shot("05_tooltip");
            // "기타" tab shows the material icons.
            var etcTab = GameObject.Find("Tab5");
            if (etcTab != null) ExecuteEvents.Execute(etcTab, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            yield return Wait(0.3f);
            yield return Shot("06_bag_materials");
            Game.Flow.CloseInventory();
            yield return Wait(0.3f);

            // ---- Skill window: passive tree + gem sockets ----
            var menuBtn = GameObject.Find("Btn_메뉴");
            foreach (var id in new[] { "Lt0", "Lt1", "Ld1", "LD", "Lc1", "LC", "Rt0", "Rt1", "UM" })
                prog.Allocate(PassiveTree.Get(id));
            prog.SetGem(0, 1, "sup_dmg"); prog.SetGem(0, 2, "sup_chain");
            prog.SetGem(1, 1, "sup_aoe"); prog.SetGem(1, 2, "sup_multi");
            yield return OpenMenu(menuBtn, "스킬", "07_skill_tree");
            var gemTab = GameObject.Find("Tab1");
            if (gemTab != null) ExecuteEvents.Execute(gemTab, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler);
            yield return Wait(0.3f);
            yield return Shot("08_skill_gems");
            Game.Flow.CloseInventory();
            yield return Wait(0.3f);

            // ---- Map / quest / dungeon / raid windows ----
            yield return OpenMenu(menuBtn, "지도", "09_map");
            Game.Flow.CloseInventory(); yield return Wait(0.2f);
            yield return OpenMenu(menuBtn, "퀘스트", "10_quest");
            Game.Flow.CloseInventory(); yield return Wait(0.2f);
            yield return OpenMenu(menuBtn, "미니던전", "11_dungeon");
            Game.Flow.CloseInventory(); yield return Wait(0.2f);
            yield return OpenMenu(menuBtn, "레이드", "12_raid");
            Game.Flow.CloseInventory(); yield return Wait(0.2f);

            // ---- The side menu itself, expanded ----
            menuBtn?.GetComponent<Button>()?.onClick.Invoke();
            yield return Wait(0.4f);
            yield return Shot("13_side_menu");
            menuBtn?.GetComponent<Button>()?.onClick.Invoke();
            yield return Wait(0.3f);

            // ---- Town service windows: shop, storage, blacksmith enhance ----
            Game.UI.Shop.SetKeeper("잡화 상인", "무엇을 사시겠어요?");
            Game.Flow.OpenWindow(Game.UI.Shop);
            yield return Wait(0.5f);
            yield return Shot("14_shop");
            Game.Flow.CloseInventory(); yield return Wait(0.2f);

            Game.UI.Storage.SetKeeper("창고지기", "물건을 맡기시겠어요?");
            Game.Flow.OpenWindow(Game.UI.Storage);
            yield return Wait(0.5f);
            yield return Shot("15_storage");
            Game.Flow.CloseInventory(); yield return Wait(0.2f);

            Game.UI.Enhance.SetKeeper("대장장이", "강화를 도와드리죠.");
            Game.Flow.OpenWindow(Game.UI.Enhance);
            yield return Wait(0.5f);
            yield return Shot("16_enhance");
            Game.Flow.CloseInventory(); yield return Wait(0.3f);

            // ---- Dialogue box ----
            Game.Dialogue.Play("chief_intro", null, "촌장");
            yield return Wait(0.6f);
            var dlg = GameObject.Find("DialogueBox");
            Log($"dialogue: open={Game.Dialogue.IsOpen} boxVisible={(dlg != null && dlg.activeInHierarchy)} speaker={Game.Dialogue.Speaker}");
            yield return Shot("17_dialogue");
            Game.State.Set(GameState.Playing);
            yield return Wait(0.2f);

            // ---- Screen fader ----
            Game.UI.Fader.SetAlpha(0.6f);
            yield return Wait(0.15f);
            yield return Shot("18_fader");
            Game.UI.Fader.SetAlpha(0f);
            yield return Wait(0.2f);

            // ---- Icon sheet + 2x crops of every frame / slot / button ----
            int w = Screen.width, h = Screen.height;
            Log($"screen={w}x{h}");
            SaveIconSheet(Path.Combine(folder, $"icon_sheet_{w}x{h}.png"));
            Log("icon sheet saved");
            SaveFrameCrops(Path.Combine(folder, $"frame_crops_{w}x{h}.png"));
            Log("frame/slot/button crops saved");
        }

        /// <summary>
        /// Applies the -screen-width / -screen-height values from the command line via
        /// Screen.SetResolution (windowed), so a UI capture actually runs at the requested size.
        /// </summary>
        static void ApplyRequestedResolution()
        {
            var args = System.Environment.GetCommandLineArgs();
            int w = 0, h = 0;
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "-screen-width") int.TryParse(args[i + 1], out w);
                else if (args[i] == "-screen-height") int.TryParse(args[i + 1], out h);
            }
            if (w > 0 && h > 0 && (Screen.width != w || Screen.height != h))
                Screen.SetResolution(w, h, false);
        }

        /// <summary>Opens the side menu (if needed), clicks a labelled window button and screenshots it.</summary>
        IEnumerator OpenMenu(GameObject menuBtn, string label, string shot)
        {
            if (!GameObject.Find("Column")) menuBtn?.GetComponent<Button>()?.onClick.Invoke();
            yield return Wait(0.2f);
            var b = GameObject.Find("Btn_" + label);
            b?.GetComponent<Button>()?.onClick.Invoke();
            yield return Wait(0.45f);
            Log($"window {label}: button={(b != null)} top={(Game.UI.Top != null ? Game.UI.Top.name : "-")}");
            yield return Shot(shot);
        }

        // ---- Icon / gem / glyph sheet ------------------------------------------------------

        static readonly string[] IconKeys =
        {
            "icon_wood", "icon_stone", "icon_carrot",
            "icon_gold", "icon_potion_hp", "icon_potion_mp", "icon_scroll", "icon_chest", "icon_anvil",
            "maticon_bone", "maticon_ore", "maticon_essence",
            "menuicon_menu", "menuicon_bag", "menuicon_skill", "menuicon_map", "menuicon_quest", "menuicon_dungeon", "menuicon_raid",
        };

        static readonly string[] EqIconKeys =
        {
            "eqicon_sword_0", "eqicon_sword_1", "eqicon_sword_2", "eqicon_sword_3",
            "eqicon_staff_0", "eqicon_staff_1", "eqicon_staff_2", "eqicon_staff_3",
            "eqicon_neck_0", "eqicon_neck_1", "eqicon_neck_2",
            "eqicon_ring_0", "eqicon_ring_1", "eqicon_ring_2",
            "eqicon_top_0", "eqicon_top_1", "eqicon_top_2",
            "eqicon_bot_0", "eqicon_bot_1", "eqicon_bot_2",
        };

        static readonly string[] GemKeys =
        {
            "gem_whirl", "gem_slam", "gem_wave", "gem_cry", "gem_blades",
            "gem_arc", "gem_nova", "gem_frostorb", "gem_thunder", "gem_meteor",
            "gem_sup_dmg", "gem_sup_aoe", "gem_sup_multi", "gem_sup_eff", "gem_sup_leech", "gem_sup_chain",
        };

        static readonly string[] NodeKeys =
        {
            "node_start", "node_mastery", "node_sun", "node_area", "node_cd", "node_dmg",
            "node_shield", "node_burst", "node_drop",
        };

        static readonly string[] HeartKeys = { "heart_full", "heart_half", "heart_empty" };

        void SaveIconSheet(string file)
        {
            var groups = new List<string[]> { IconKeys, EqIconKeys, GemKeys, NodeKeys, HeartKeys };
            const int scale = 2, cell = 52, pad = 6, cols = 10;
            int rows = 0;
            foreach (var g in groups) rows += (g.Length + cols - 1) / cols;
            int sw = cols * cell + pad, sh = rows * cell + pad;
            var sheet = new Texture2D(sw, sh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            // Checkerboard so transparent edges are visible.
            var bg = new Color32[sw * sh];
            for (int y = 0; y < sh; y++)
                for (int x = 0; x < sw; x++)
                    bg[y * sw + x] = ((x / 8 + y / 8) % 2 == 0) ? new Color32(46, 50, 60, 255) : new Color32(38, 42, 52, 255);
            sheet.SetPixels32(bg);

            int row = 0;
            foreach (var g in groups)
            {
                for (int i = 0; i < g.Length; i++)
                {
                    int col = i % cols;
                    if (i > 0 && col == 0) row++;
                    BlitSpriteCentered(sheet, Game.Art.Get(g[i]), col * cell + pad, (rows - 1 - row) * cell + pad, cell - pad, scale);
                }
                row++;
            }
            sheet.Apply();
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            Destroy(sheet);
        }

        /// <summary>2x crops of the 9-slice frames, slots and buttons drawn at a legible size.</summary>
        void SaveFrameCrops(string file)
        {
            string[] keys = { "ui_panel", "ui_dark", "ui_slot", "ui_slotblue", "ui_frame", "ui_btn", "ui_btngray", "ui_tooltip", "ui_select", "ui_ring", "ui_dot", "ui_corner" };
            const int scale = 4, cell = 40 * scale, pad = 8, cols = 4;
            int rows = (keys.Length + cols - 1) / cols;
            int sw = cols * (cell + pad) + pad, sh = rows * (cell + pad) + pad;
            var sheet = new Texture2D(sw, sh, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            var bg = new Color32[sw * sh];
            for (int i = 0; i < bg.Length; i++) bg[i] = new Color32(30, 33, 40, 255);
            sheet.SetPixels32(bg);
            for (int i = 0; i < keys.Length; i++)
            {
                int col = i % cols, r = i / cols;
                BlitSpriteScaled(sheet, Game.Art.Get(keys[i]), pad + col * (cell + pad), sh - (pad + (r + 1) * (cell + pad)) + pad, cell, cell);
            }
            sheet.Apply();
            File.WriteAllBytes(file, sheet.EncodeToPNG());
            Destroy(sheet);
        }

        /// <summary>Nearest-neighbour blit of a sprite, centred inside a box, at an integer scale.</summary>
        static void BlitSpriteCentered(Texture2D sheet, Sprite s, int boxX, int boxY, int box, int scale)
        {
            if (s == null || s.texture == null) return;
            var r = s.textureRect;
            int sx = Mathf.RoundToInt(r.x), sy = Mathf.RoundToInt(r.y), w = Mathf.RoundToInt(r.width), hh = Mathf.RoundToInt(r.height);
            var px = s.texture.GetPixels(sx, sy, w, hh);
            int drawScale = Mathf.Max(1, Mathf.Min(scale, box / Mathf.Max(w, hh)));
            int ox = boxX + (box - w * drawScale) / 2, oy = boxY + (box - hh * drawScale) / 2;
            for (int y = 0; y < hh; y++)
                for (int x = 0; x < w; x++)
                {
                    var c = px[y * w + x];
                    if (c.a <= 0f) continue;
                    for (int dy = 0; dy < drawScale; dy++)
                        for (int dx = 0; dx < drawScale; dx++)
                            sheet.SetPixel(ox + x * drawScale + dx, oy + y * drawScale + dy, c);
                }
        }

        /// <summary>Nearest-neighbour blit of a sprite stretched to fill a WxH box (for 9-slice frames).</summary>
        static void BlitSpriteScaled(Texture2D sheet, Sprite s, int boxX, int boxY, int bw, int bh)
        {
            if (s == null || s.texture == null) return;
            var r = s.textureRect;
            int sx = Mathf.RoundToInt(r.x), sy = Mathf.RoundToInt(r.y), w = Mathf.RoundToInt(r.width), hh = Mathf.RoundToInt(r.height);
            var px = s.texture.GetPixels(sx, sy, w, hh);
            for (int y = 0; y < bh; y++)
                for (int x = 0; x < bw; x++)
                {
                    int srcX = Mathf.Clamp(x * w / bw, 0, w - 1);
                    int srcY = Mathf.Clamp(y * hh / bh, 0, hh - 1);
                    var c = px[srcY * w + srcX];
                    if (c.a <= 0f) continue;
                    sheet.SetPixel(boxX + x, boxY + y, c);
                }
        }
    }
}
