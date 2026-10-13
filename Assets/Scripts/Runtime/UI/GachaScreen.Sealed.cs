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
                GameEvents.RaiseToast(L.F("별조각이 모자랍니다. (필요 {0:N0}, 보유 {1:N0})", price, StarShopClient.Balance));
                return;
            }
            string what = count == 10 ? L.T("10회 + 보너스 1회") : L.T("1회");
            Game.UI.Confirm(L.F("봉인된 상자\n{0}로 {1} 엽니다.\n열겠습니까?", StarShopClient.Stars(price), what) + "\n" + PolicyClient.RefundNotice, () => PullSealed(count), true);
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
            if (CashClient.Table.Count == 0) return L.T("<color=#8c96a8>확률표를 불러오는 중입니다.</color>");
            var sb = new StringBuilder();
            sb.Append(L.T("<size=24><b>확률 공개 · 봉인된 상자</b></size>") + "\n" + L.F("<color=#8c96a8>1회 별조각 {0:N0} · 10+1회 {1:N0}</color>", CashClient.PriceOne, CashClient.PriceEleven) + "\n\n");
            Table(sb, L.T("기본"), CashClient.Table);
            if (CashClient.BoostedTable.Count > 0) { sb.Append('\n'); Table(sb, L.T("부스터 (10회마다 다음 1회, 수량 x2)"), CashClient.BoostedTable); }
            sb.Append("\n" + L.F("<color=#b8c4d8>확률 상자 \"+N 강화권 상자 (P%)\"는 열면 P% 확률로 +N 강화권, 아니면 마력 정수 20개가 나옵니다.\n천장: 봉인된 상자를 열 때마다(캐시샵·가방 상자 모두, 11연은 11칸) 1칸 찹니다. {0}칸이 다 차면 {1}과(와) 바꿀 수 있습니다. 바꾸면 {0}칸 줄고 넘친 칸은 남습니다. 확률로 나와도 게이지는 줄지 않습니다.</color>", CashClient.CeilingMax, L.T(DungeonDatabase.ItemName(CashClient.CeilingReward)) + (CashClient.CeilingRewardCount > 1 ? " x" + CashClient.CeilingRewardCount : "")));
            return sb.ToString();
        }

        static void Table(StringBuilder sb, string title, List<CashClient.Rate> rows)
        {
            sb.Append($"<b>{title}</b>\n");
            foreach (var r in rows)
                sb.Append($"<color={GradeHex(RarityOf(r.itemKey))}>{L.T(DungeonDatabase.ItemName(r.itemKey))}{(r.count > 1 ? $" x{r.count}" : "")}</color>  {r.rate:0.###}%\n");
        }
    }
}
