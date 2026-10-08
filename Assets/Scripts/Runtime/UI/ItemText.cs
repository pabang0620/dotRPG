using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Names, grade colours and descriptions of any item id (gear key, material, consumable, resource).
    /// Gear keys ("eq_sword_iron+12") show their own +level.
    /// </summary>
    public static class ItemText
    {
        public static string Name(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null) return $"<color={EquipmentDatabase.RarityColor(gear.rarity)}>{gear.NameAt(EquipmentDatabase.LevelOfKey(id))}</color>";
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return $"<color={EquipmentDatabase.RarityColor(mat.rarity)}>{mat.name}</color>";
            var use = ConsumableDatabase.Get(id);
            if (use != null && use.grade.HasValue) return $"<color={EquipmentDatabase.RarityColor(use.grade.Value)}>{use.name}</color>";
            return Game.Config.GetItem(id).displayName;
        }

        public static string Kind(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null)
                return $"[{EquipmentDatabase.RarityName(gear.rarity)}] {gear.CategoryName}" + (gear.classOnly.HasValue ? $" · {CharacterClassInfo.Get(gear.classOnly.Value).displayName} 전용" : "");
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return $"[{EquipmentDatabase.RarityName(mat.rarity)}] 강화 재료";
            if (ConsumableDatabase.IsTicket(id)) return $"[{EquipmentDatabase.RarityName(Grade(id) ?? ItemRarity.Common)}] 기타 · 강화 보호";
            if (ConsumableDatabase.IsUsable(id)) return "소모품";
            return "재료";
        }

        /// <summary>Grade of gear, materials and graded items such as the protection ticket (null for plain items).</summary>
        public static ItemRarity? Grade(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null) return gear.rarity;
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return mat.rarity;
            return ConsumableDatabase.Get(id)?.grade;
        }

        public static string Description(string id)
        {
            var gear = EquipmentDatabase.Get(id);
            if (gear != null) return $"{gear.StatLine(EquipmentDatabase.LevelOfKey(id))}\n{gear.description}";
            var mat = EquipmentDatabase.GetMaterial(id);
            if (mat != null) return mat.description;
            var use = ConsumableDatabase.Get(id);
            if (use != null)
            {
                string key = use.hotkey.HasValue ? $"\n<color=#ffe066>빠른 사용: [{Game.Input.GetBindingLabel(use.hotkey.Value)}]</color>" : "";
                return use.description + key;
            }
            switch (id)
            {
                case ItemIds.Wood: return "해골 숲의 나무를 베어 얻습니다. 공방 재건에 쓰입니다.";
                case ItemIds.Stone: return "해골 숲의 바위를 깨서 얻습니다. 공방 재건에 쓰입니다.";
                case ItemIds.Carrot: return "마을 밭에서 뽑은 당근. 먹으면 체력을 조금 회복합니다.";
            }
            return "";
        }

        public static Sprite Icon(string id) => Game.Art.Get(Game.Config.GetItem(id).iconKey);

        /// <summary>Grade frame colour for an icon cell.</summary>
        public static Color Frame(string id)
        {
            var grade = Grade(id);
            if (!grade.HasValue) return new Color(1f, 1f, 1f, 0.12f);
            var tint = EquipmentDatabase.RarityTint(grade.Value);
            tint.a = grade.Value == ItemRarity.Common ? 0.25f : 0.9f;
            return tint;
        }

        /// <summary>
        /// Everything in the bag in bag order, money excluded: gear keys (database order, higher +level
        /// first), consumables, resources, materials, then the protection ticket.
        /// </summary>
        public static List<string> BagOrder(Inventory bag)
        {
            var ids = EquipmentDatabase.GearKeys(bag);
            foreach (var use in ConsumableDatabase.Usable) if (bag.Count(use.id) > 0) ids.Add(use.id);
            foreach (var def in Game.Config.items) if (bag.Count(def.id) > 0 && !ids.Contains(def.id)) ids.Add(def.id);
            foreach (var mat in EquipmentDatabase.AllMaterials) if (bag.Count(mat.id) > 0) ids.Add(mat.id);
            foreach (var ticket in ConsumableDatabase.Tickets) if (bag.Count(ticket.id) > 0) ids.Add(ticket.id);
            return ids;
        }

        public static string Gold(int amount) => $"<color=#ffd84a>{amount:N0} G</color>";
    }
}
