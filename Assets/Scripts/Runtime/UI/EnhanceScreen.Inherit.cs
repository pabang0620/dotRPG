using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] The blacksmith's 계승 button: carries a Lv.40 unique / legendary into the same piece at Lv.50 (PromoteRules.InheritCost),
    /// keeping its +level, protection history and worn slot. Online only, like 승급: the server checks the chapter gate,
    /// takes the cost and swaps the item in one transaction (POST /characters/:uuid/promote/inherit).
    /// </summary>
    public partial class EnhanceScreen
    {
        void OnInheritPressed()
        {
            if (busy || ticketPending || entries.Count == 0 || !IsTop) return;
            if (!Chapter3.Enabled) { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast(L.T("아직 열리지 않은 콘텐츠입니다.")); return; }
            var e = entries[selected];
            var gear = EquipmentDatabase.Get(e.key);
            var target = PromoteRules.InheritTargetOf(gear);
            var cost = gear != null ? PromoteRules.InheritCost(gear.rarity) : null;
            if (target == null || cost == null) return;
            var c = cost.Value;
            if (!OnlineEconomy.On) { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast(L.T("계승은 온라인에서만 할 수 있습니다.")); return; }
            var inv = Game.Session.Inventory;
            int cores = inv.Count(PromoteRules.CoreItem), shards = inv.Count(RaidRewards.RaidMatXibak), seals = inv.Count(RaidRewards.RaidMatKamatz);
            if (cores < c.cores || shards < c.shards || seals < c.seals || Game.Session.Gold < c.gold)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast(L.F("고대의 핵 {0}/{1}, 침식 결정 {2}/{3}, 잿빛 인장 {4}/{5}, 골드 {6:N0}/{7:N0}", cores, c.cores, shards, c.shards, seals, c.seals, Game.Session.Gold, c.gold));
                return;
            }
            int level = EquipmentDatabase.LevelOfKey(e.key);
            string from = EquipmentDatabase.RichName(e.key);
            string to = EquipmentDatabase.RichName(EquipmentDatabase.KeyFor(target.id, level));
            Game.Audio.PlaySfx("select");
            Game.UI.Confirm(L.F("<b>장비 계승</b>\n{0}  →  {1}\n<color=#b8c4d8>강화 수치 +{2} 그대로 · 고대의 핵 {3} · 침식 결정 {4} · 잿빛 인장 {5} · 골드 {6:N0}</color>\n계승할까요?",
                from, to, level, c.cores, c.shards, c.seals, c.gold), () =>
            {
                if (busy || ticketPending) return;
                busy = true;
                resultText.text = L.T("<color=#b8c4d8>계승하는 중…</color>");
                OnlineEconomy.Inherit(e.slot.HasValue ? (int)e.slot.Value : (int?)null, e.slot.HasValue ? null : e.key, d =>
                {
                    busy = false;
                    if (d == null) { resultText.text = ""; Refresh(); return; }
                    string newKey = MiniJson.Str(d, "new_key");
                    Game.Audio.PlaySfx("quest");
                    EnhanceFx.Sparks(bigIcon.rectTransform, 28, new Color32(255, 214, 90, 255));
                    resultText.text = L.F("<color=#ffd34a><b>계승 완료!</b></color>  {0}", EquipmentDatabase.RichName(newKey));
                    BuildEntries();
                    for (int i = 0; i < entries.Count; i++)
                        if (entries[i].key == newKey && entries[i].slot == e.slot) { selected = i; break; }
                    Game.Flow.Autosave();
                    Refresh();
                });
            }, true);
        }
    }
}
