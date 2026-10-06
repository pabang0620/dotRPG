using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        /// <summary>
        /// [DEMO] Monster region trial (-dotrpgMonsterDemo &lt;folder&gt;): four local slots, each a character at the level
        /// and gear of one of the new hunting regions, standing in that region's branch hunting ground. The world map
        /// can jump to any region in this mode (GameFlow.DemoSlots). Never touches the normal save slots.
        /// </summary>
        IEnumerator MonsterDemoRun()
        {
            ApplyRequestedResolution();
            yield return Wait(1);
            Game.Config.autosave = false;
            var slots = new[]
            {
                (zone: "forest_depths", level: 12, cls: CharacterClass.Warrior, career: Career.None, name: "숲 사냥터 체험"),
                (zone: "canyon_ridge", level: 23, cls: CharacterClass.Mage, career: Career.Arcanist, name: "협곡 사냥터 체험"),
                (zone: "winter_peak", level: 37, cls: CharacterClass.Warrior, career: Career.Fighter, name: "설원 사냥터 체험"),
                (zone: "sanctum_hall", level: 40, cls: CharacterClass.Warrior, career: Career.Guardian, name: "성소 사냥터 체험"),
            };
            for (int slot = 0; slot < slots.Length; slot++)
            {
                var d = slots[slot];
                if (Game.Saves.HasSave(slot)) { Log("kept monster demo slot " + slot); continue; }
                SaveSystem.ActiveSlot = slot;
                Game.Flow.NewGame(d.cls, d.name);
                yield return Wait(.1f);
                while (Game.Flow.IsTransitioning) yield return null;
                var p = Game.Session.Progression;
                p.SetFromServer(d.level, 0);
                if (d.career != Career.None)
                {
                    p.Promote(d.career);
                    var skills = CareerCatalog.For(d.career);
                    foreach (var s in skills) if (s.kind != CareerSkillKind.Awakening && s.level <= d.level) p.Learn(s.id);
                    int key = 0;
                    foreach (var s in skills)
                        if (key < 4 && s.kind == CareerSkillKind.Active && p.Rank(s.id) > 0 && p.EquipSkill(key, s.id)) key++;
                }
                // Gear of the region's tier, epic, weapon +7 (strong enough to fight the region comfortably).
                int tier = 1;
                foreach (int t in new[] { 1, 10, 15, 20, 25, 30, 35, 40 }) if (t <= d.level) tier = t;
                bool warrior = d.cls == CharacterClass.Warrior;
                var bag = Game.Session.Inventory;
                foreach (var gear in new[]
                {
                    (warrior ? "eq_sword_" : "eq_staff_") + tier + "_e+7",
                    (warrior ? "eq_plate_" : "eq_robe_") + tier + "_e",
                    (warrior ? "eq_greaves_" : "eq_skirt_") + tier + "_e",
                    "eq_neck_" + tier + "_e", "eq_ring_" + tier + "_e",
                })
                {
                    bag.Add(gear, 1);
                    if (!Game.Session.Equipment.Equip(gear, d.cls)) Log("demo gear not worn: " + gear);
                }
                bag.Add(ConsumableDatabase.Gold, 100000);
                bag.Add(ConsumableDatabase.HpPotion, 100);
                bag.Add(ConsumableDatabase.MpPotion, 100);
                bag.Add(ConsumableDatabase.TownScroll, 20);
                Game.Flow.TravelTo(d.zone, true);
                yield return Wait(.2f);
                while (Game.Flow.IsTransitioning) yield return null;
                Game.Player.HealFull();
                var save = Game.Session.Capture(Game.Player.Position, Game.Player.Facing);
                if (!Game.Saves.Write(save, slot)) throw new IOException("Could not save monster demo slot " + slot);
                Log($"created slot {slot}: {d.zone} Lv.{d.level} {d.cls} {d.career}");
            }
            Game.Config.autosave = true;
            Game.Flow.ReturnToTitle(); yield return Wait(.1f);
            while (Game.Flow.IsTransitioning) yield return null;
            SaveSystem.ActiveSlot = 0;
            Game.UI.Slots.Open(true);
            yield return Wait(.2f);
        }
    }
}
