using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// The blacksmith's 승급 button: turns the selected piece into the next grade of the same slot (PromoteRules),
    /// keeping its +level, for 고대의 핵 (raid rewards only) and gold. The server checks and applies it.
    /// </summary>
    public partial class EnhanceScreen
    {
        Button promoteButton;
        Text promoteLabel;

        void BuildPromote(Transform right)
        {
            promoteButton = Button(right, "Promote", "승급", "ui_btngray", new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-16f, 12f), new Vector2(140f, 64f), OnPromotePressed, 22);
            promoteLabel = promoteButton.GetComponentInChildren<Text>();
        }

        void RefreshPromote(string key)
        {
            if (promoteButton == null) return;
            var next = PromoteRules.NextOf(EquipmentDatabase.Get(key));
            promoteButton.gameObject.SetActive(next != null);
            if (next == null) return;
            var (cores, _) = PromoteRules.CostInto(next.rarity);
            int have = Game.Session.Inventory.Count(PromoteRules.CoreItem);
            promoteButton.interactable = !busy;
            string head = $"승급 <size=17>({Game.Input.GetBindingLabel(GameAction.UseItem)})</size>";
            promoteLabel.text = have >= cores ? head : $"{head}\n<size=15>핵 {have}/{cores}</size>";
        }

        void OnPromotePressed()
        {
            if (busy || ticketPending || entries.Count == 0 || !IsTop) return;
            var e = entries[selected];
            var gear = EquipmentDatabase.Get(e.key);
            var next = PromoteRules.NextOf(gear);
            if (next == null) return;
            var (cores, gold) = PromoteRules.CostInto(next.rarity);
            int have = Game.Session.Inventory.Count(PromoteRules.CoreItem);
            int level = EquipmentDatabase.LevelOfKey(e.key);
            if (!OnlineEconomy.On) { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast("승급은 온라인에서만 할 수 있습니다."); return; }
            if (have < cores || Game.Session.Gold < gold)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast($"고대의 핵 {have}/{cores}, 골드 {Game.Session.Gold:N0}/{gold:N0}. 고대의 핵은 레이드 클리어 보상으로 얻습니다.");
                return;
            }
            string from = EquipmentDatabase.RichName(e.key);
            string to = EquipmentDatabase.RichName(EquipmentDatabase.KeyFor(next.id, level));
            Game.Audio.PlaySfx("select");
            Game.UI.Confirm($"<b>장비 승급</b>\n{from}  →  {to}\n<color=#b8c4d8>강화 수치 +{level} 그대로 · 고대의 핵 {cores}개 · 골드 {gold:N0}</color>\n승급할까요?", () =>
            {
                if (busy || ticketPending) return;
                busy = true;
                resultText.text = "<color=#b8c4d8>승급하는 중…</color>";
                OnlineEconomy.Promote(e.slot.HasValue ? (int)e.slot.Value : (int?)null, e.slot.HasValue ? null : e.key, d =>
                {
                    busy = false;
                    if (d == null) { resultText.text = ""; Refresh(); return; }
                    string newKey = MiniJson.Str(d, "new_key");
                    Game.Audio.PlaySfx("quest");
                    EnhanceFx.Sparks(bigIcon.rectTransform, 24, new Color32(255, 214, 90, 255));
                    resultText.text = $"<color=#ffd34a><b>승급 성공!</b></color>  {EquipmentDatabase.RichName(newKey)}";
                    // Keep the cursor on the promoted piece.
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
