using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CASH] The 봉인된 상자 banner (Docs/PLAN_CASH_BOX_PASS.md): 1 or 10+1 opens from the server, shown on the same
    /// result page as the other banners; its gauge is the 봉인 해제 booster and its rate page shows both tables.
    /// </summary>
    public partial class GachaScreen
    {
        void AskSealed(int count)
        {
            int price = count == 10 ? CashClient.PriceEleven : CashClient.PriceOne;
            if (StarShopClient.Balance < price)
            {
                Game.Audio.PlaySfx("cancel");
                GameEvents.RaiseToast($"별조각이 모자랍니다. (필요 {price:N0}, 보유 {StarShopClient.Balance:N0})");
                return;
            }
            string what = count == 10 ? "10회 + 보너스 1회" : "1회";
            Game.UI.Confirm($"봉인된 상자\n{StarShopClient.Stars(price)}로 {what} 엽니다.\n열겠습니까?", () => PullSealed(count), true);
        }

        void PullSealed(int count)
        {
            busy = true;
            Refresh();
            CashClient.Pull(count == 10 ? 11 : 1, (ok, msg, rewards) =>
            {
                busy = false;
                if (!ok)
                {
                    Game.Audio.PlaySfx("cancel");
                    GameEvents.RaiseToast(msg);
                    Refresh();
                    return;
                }
                var list = new List<StarPullResult>();
                foreach (var r in rewards)
                    list.Add(new StarPullResult { itemId = r.itemKey, rarity = RarityOf(r.itemKey), cash = true, boosted = r.boosted, count = r.count });
                shown = list;
                revealed = 0;
                waiting = -1;
                lastCount = count;
                revealAt = Time.unscaledTime + 0.45f;
                OpenResults();
                Refresh();
            });
        }

        /// <summary>The item's own grade drives the card colour (plain items show as common).</summary>
        static string RarityOf(string itemKey)
        {
            var item = ConsumableDatabase.Get(itemKey);
            if (item == null || item.grade == null) return "common";
            return item.grade.Value switch
            {
                ItemRarity.Legendary => "legendary",
                ItemRarity.Unique => "unique",
                ItemRarity.Epic => "epic",
                ItemRarity.Rare => "rare",
                ItemRarity.Uncommon => "uncommon",
                _ => "common",
            };
        }

        string SealedRateText()
        {
            if (CashClient.Table.Count == 0) return "<color=#8c96a8>확률표를 불러오는 중입니다.</color>";
            var sb = new StringBuilder();
            sb.Append("<size=24><b>확률 공개 · 봉인된 상자</b></size>\n<color=#8c96a8>1회 별조각 " + CashClient.PriceOne.ToString("N0") + " · 10+1회 " + CashClient.PriceEleven.ToString("N0") + "</color>\n\n");
            Table(sb, "기본", CashClient.Table);
            if (CashClient.BoostedTable.Count > 0) { sb.Append('\n'); Table(sb, "부스터 (10회마다 다음 1회, 수량 x2)", CashClient.BoostedTable); }
            sb.Append("\n<color=#b8c4d8>확률 상자 \"+N 강화권 상자 (P%)\"는 열면 P% 확률로 +N 강화권, 아니면 마력 정수 20개가 나옵니다.\n확정 지급(천장)은 없습니다.</color>");
            return sb.ToString();
        }

        static void Table(StringBuilder sb, string title, List<CashClient.Rate> rows)
        {
            sb.Append($"<b>{title}</b>\n");
            foreach (var r in rows)
                sb.Append($"<color={GradeHex(RarityOf(r.itemKey))}>{DungeonDatabase.ItemName(r.itemKey)}{(r.count > 1 ? $" x{r.count}" : "")}</color>  {r.rate:0.###}%\n");
        }
    }
}
