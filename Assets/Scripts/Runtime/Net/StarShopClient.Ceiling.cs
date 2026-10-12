using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [CEILING] 장비 뽑기 천장 (Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md A). Every gear draw (weapon, armor, accessory
    /// together) fills one account gauge; when it is full the player picks one 유니크 piece of a chosen banner and level
    /// tier (the top grade there when that tier has no 유니크). The server checks the choice against its own table.
    /// </summary>
    public static partial class StarShopClient
    {
        public static int GearCeiling { get; private set; }
        public static int GearCeilingMax { get; private set; } = 300;
        public static bool GearCeilingReady => GearCeiling >= GearCeilingMax;

        static void ReadGearCeiling(Dictionary<string, object> c)
        {
            if (c == null) return;
            GearCeiling = MiniJson.Int(c, "gauge", GearCeiling);
            GearCeilingMax = MiniJson.Int(c, "max", GearCeilingMax);
        }

        /// <summary>The pieces the gear ceiling can give for this table: its 유니크 row, else its top row.</summary>
        public static List<string> GearCeilingChoices(List<StarRate> table)
        {
            var list = new List<string>();
            if (table == null || table.Count == 0) return list;
            StarRate row = null;
            foreach (var r in table) if (r.rarity == "unique") { row = r; break; }
            row ??= table[0]; // the server sends the table highest grade first
            foreach (var i in row.items) list.Add(i.id);
            return list;
        }

        /// <summary>The gauge is full: take the chosen piece (bag, account bound like any draw).</summary>
        public static void ClaimGearCeiling(string banner, int tier, string itemId, Action<bool, string> done)
        {
            if (!Available) { done?.Invoke(false, "온라인 캐릭터로 접속해야 합니다."); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["banner"] = banner, ["item_id"] = itemId };
            if (tier >= 0) body["tier"] = tier;
            Api.PostIdempotent(Base + "/gear-ceiling", body, r =>
            {
                if (!r.ok) { done?.Invoke(false, string.IsNullOrEmpty(r.message) ? "받지 못했습니다." : r.message); return; }
                ReadGearCeiling(MiniJson.Obj(r.data, "gear_ceiling"));
                OnlineEconomy.ApplyDelta(MiniJson.Obj(r.data, "delta"));
                Game.Flow.Autosave();
                Changed?.Invoke();
                done?.Invoke(true, "");
            });
        }
    }
}
