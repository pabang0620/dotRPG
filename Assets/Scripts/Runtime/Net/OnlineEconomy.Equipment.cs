using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[SERVER] Equip / unequip reporting (see OnlineEconomy.cs).</summary>
    public static partial class OnlineEconomy
    {
        /// <summary>Worn keys right now (for <see cref="SyncWorn"/>).</summary>
        public static string[] WornSnapshot()
        {
            var eq = Game.Session.Equipment;
            var keys = new string[Equipment.SlotCount];
            for (int i = 0; i < keys.Length; i++) keys[i] = eq[(EquipSlot)i];
            return keys;
        }

        /// <summary>
        /// Sends the slot changes the local equip code just made (compared with <paramref name="before"/>) as
        /// equip / unequip requests, one after another (the server checks them in order). A successful answer's
        /// delta overwrites the local state; when one is refused, the slot that was refused and the ones after it
        /// go back to what they were (the server never moved them).
        /// </summary>
        public static void SyncWorn(string[] before)
        {
            if (!On || before == null) return;
            var now = WornSnapshot();
            var steps = new List<Action<Action>>();
            for (int i = 0; i < now.Length; i++)
            {
                if (now[i] == before[i]) continue;
                int slot = i;
                string key = now[i];
                bool ring = slot == (int)EquipSlot.Ring1 || slot == (int)EquipSlot.Ring2;
                Action<bool, Action> after = (ok, next) => { if (ok) next(); else RevertWornFrom(before, slot); };
                if (string.IsNullOrEmpty(key)) steps.Add(next => Unequip(slot, ok => after(ok, next)));
                else steps.Add(next => Equip(key, ring ? slot : (int?)null, ok => after(ok, next)));
            }
            RunInOrder(steps, 0);
        }

        /// <summary>Puts slots <paramref name="fromSlot"/> and up back to <paramref name="before"/>, moving the pieces between worn and bag.</summary>
        static void RevertWornFrom(string[] before, int fromSlot)
        {
            var s = Game.Session;
            if (s == null) return;
            for (int i = fromSlot; i < before.Length; i++)
            {
                string cur = s.Equipment[(EquipSlot)i], want = before[i];
                if ((cur ?? "") == (want ?? "")) continue;
                if (!string.IsNullOrEmpty(cur)) s.Inventory.Add(cur, 1);
                if (!string.IsNullOrEmpty(want) && s.Inventory.Count(want) > 0) s.Inventory.Remove(want, 1);
                s.Equipment.SetSlotFromServer(i, want);
            }
        }
    }
}
