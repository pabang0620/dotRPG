using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [CASH] 강화권 사용 at the forge: raises the chosen gear straight to +N (no failure). Only tickets above the gear's
    /// level are offered; left click uses the shown one (after a confirm), right click switches to the next one.
    /// </summary>
    public partial class EnhanceScreen
    {
        static readonly int[] TicketLevels = { 10, 12, 13, 15 };
        Button ticketButton;
        Text ticketLabel;
        int ticketPick = -1;

        void BuildTicket(Transform right)
        {
            ticketButton = Button(right, "Ticket", "", "ui_btngray", new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(16f, 17f), new Vector2(158f, 54f), UseTicket, 15); // left of the centred 강화 button
            ticketLabel = ticketButton.GetComponentInChildren<Text>();
            var relay = ticketButton.gameObject.AddComponent<PointerRelay>();
            relay.onClick = b => { if (b == PointerEventData.InputButton.Right) { ticketPick++; Refresh(); } };
            ticketButton.gameObject.SetActive(false);
        }

        /// <summary>Owned tickets that would raise this gear.</summary>
        List<int> TicketsFor(int level)
        {
            var list = new List<int>();
            var bag = Game.Session.Inventory;
            foreach (int t in TicketLevels) if (t > level && bag.Count(ConsumableDatabase.EnhanceTicket(t)) > 0) list.Add(t);
            return list;
        }

        void RefreshTicket(string key)
        {
            if (ticketButton == null) return;
            var options = string.IsNullOrEmpty(key) ? new List<int>() : TicketsFor(EquipmentDatabase.LevelOfKey(key));
            ticketButton.gameObject.SetActive(options.Count > 0);
            if (options.Count == 0) { ticketPick = -1; return; }
            if (ticketPick < 0 || ticketPick >= options.Count) ticketPick = 0;
            int t = options[ticketPick];
            ticketLabel.text = $"<b>+{t} 강화권 사용</b>\n<size=13>{Game.Session.Inventory.Count(ConsumableDatabase.EnhanceTicket(t))}장{(options.Count > 1 ? " · 우클릭: 다른 강화권" : "")}</size>";
        }

        void UseTicket()
        {
            if (entries.Count == 0 || selected >= entries.Count) return;
            var e = entries[selected];
            var options = TicketsFor(EquipmentDatabase.LevelOfKey(e.key));
            if (options.Count == 0) return;
            int t = options[Mathf.Clamp(ticketPick, 0, options.Count - 1)];
            string key = e.key;
            Game.UI.Confirm($"<b>{EquipmentDatabase.RichName(key)}</b>\n+{t} 강화권을 써서 바로 +{t}로 올립니다.\n실패하지 않습니다. 사용할까요?", () =>
                CashClient.UseTicket(ConsumableDatabase.EnhanceTicket(t), key, (ok, msg) =>
                {
                    if (!ok) { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast(msg); return; }
                    Game.Audio.PlaySfx("rank_reveal");
                    GameEvents.RaiseToast($"+{t} 강화 성공! (강화권)");
                    Refresh();
                }), true);
        }
    }
}
