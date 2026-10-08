using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class GachaScreen
    {
        protected override void Refresh()
        {
            bool online = StarShopClient.Available;
            var cur = Current();
            foreach (var c in cards)
            {
                bool on = c.id == banner;
                c.frame.color = on ? new Color(1f, .83f, .3f, 1f) : Color.clear;
                c.name.text = on ? $"<color=#ffd34a><b>{NameOf(c.id)}</b></color>" : NameOf(c.id);
                c.bg.color = on ? UiTheme.Background : UiTheme.PanelDeep;
            }
            big.sprite = Game.Art.Get("Banners/banner_gacha_" + banner);
            bigTitle.text = $"<b>{cur.name}</b>";
            // Skins released so far, and the ones this class can draw.
            var uniques = new List<string>();
            var epics = new List<string>();
            var cls = Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;
            foreach (var sk in SkinCatalog.All)
            {
                if (sk.cls != cls) continue;
                var sp = CosmeticCatalog.Find(sk.id);
                (sp != null && sp.Rarity == CosmeticRarity.Epic ? epics : uniques).Add(sk.name);
            }
            badgeBg.gameObject.SetActive(banner == "skin");
            badge.text = $"<b>유니크 {uniques.Count} · 에픽 {epics.Count}</b>";
            int pity = banner == "skin" ? StarShopClient.SkinPity : StarShopClient.Pity;
            bool hasPity = banner == "skin" || banner == "aura";
            bool showGauge = hasPity || Sealed;
            int gaugeMax = banner == "skin" ? StarShopClient.SkinGaugeMax : StarShopClient.AuraGaugeMax;
            bool full = hasPity && pity >= gaugeMax;
            gaugeBg.gameObject.SetActive(showGauge);
            gaugeText.gameObject.SetActive(showGauge);
            chooseBtn.gameObject.SetActive(full);
            if (hasPity)
            {
                gaugeFill.rectTransform.sizeDelta = new Vector2((GaugeW - 4f) * Mathf.Clamp01(pity / (float)gaugeMax), 16f);
                string goal = banner == "skin" ? "유니크 스킨" : "에픽 오라";
                gaugeText.text = full ? $"<color=#ffd34a><b>선택 게이지 가득!</b></color> {goal}{(banner == "skin" ? "을" : "를")} 직접 고를 수 있습니다"
                    : $"선택 게이지 {Mathf.Min(pity, gaugeMax)}/{gaugeMax} · 가득 차면 {goal} 직접 선택";
            }
            else if (Sealed)
            {
                // [CASH] 봉인 해제 게이지: 10 opens fill it, the next open is boosted.
                gaugeFill.rectTransform.sizeDelta = new Vector2((GaugeW - 4f) * (CashClient.NextBoosted ? 1f : Mathf.Clamp01(CashClient.Gauge / 10f)), 16f);
                gaugeText.text = CashClient.NextBoosted ? "<color=#ffd34a><b>봉인 해제!</b></color> 다음 1회는 희귀 이상 확률 2배 · 수량 2배"
                    : $"봉인 해제 게이지 {CashClient.Gauge}/10 · 가득 차면 다음 1회 부스터";
            }
            bigSub.text = $"{cur.sub}\n" +
                          (hasPity ? "" : Sealed ? "나온 아이템은 바로 가방으로\n" : "뽑은 장비는 바로 가방으로\n") +
                          (banner == "skin" ? $"<color=#ffb347>유니크 {string.Join(" · ", uniques)} (+5%)</color>\n<color=#c58cff>에픽 {string.Join(" · ", epics)} (+4%)</color>" : banner == "aura" ? "<color=#ffb347>오라 공격력 +1~3%</color>" : "");
            wallet.text = online
                ? (StarShopClient.Loaded ? $"보유 <color=#ffd34a>{StarShopClient.Stars(StarShopClient.Balance)}</color>" : "<color=#8c96a8>불러오는 중...</color>")
                : "<color=#8c96a8>온라인 캐릭터로 접속하면 이용할 수 있습니다.</color>";
            TextOf(one).text = $"1회 · 별조각 {(Sealed ? CashClient.PriceOne : StarShopClient.PriceOne):N0}";
            TextOf(ten).text = Sealed ? $"10+1회 · 별조각 {CashClient.PriceEleven:N0}" : $"{StarShopClient.TenCount - 1}+1회 · 별조각 {StarShopClient.PriceTen:N0}";
            one.interactable = ten.interactable = online && StarShopClient.Loaded && !busy;
            RefreshTierPicker(!hasPity && !Sealed && online && StarShopClient.Loaded);
            if (rateModal.gameObject.activeSelf) rateText.text = RateText();
            DrawCells();
        }

        static string NameOf(string bannerId)
        {
            foreach (var b in Banners) if (b.id == bannerId) return b.name;
            return bannerId;
        }

        List<StarRate> CurrentRates()
        {
            if (banner == "aura") return StarShopClient.Rates;
            if (banner == "skin") return StarShopClient.SkinRates;
            var byTier = TierRates();
            if (byTier != null) return byTier;
            foreach (var b in StarShopClient.Banners) if (b.id == banner) return b.rates;
            return new List<StarRate>();
        }

        string RateText()
        {
            if (Sealed) return SealedRateText();
            var list = CurrentRates();
            if (list.Count == 0) return "<color=#8c96a8>확률표를 불러오는 중입니다.</color>";
            var sb = new StringBuilder();
            sb.Append($"<size=24><b>확률 공개 · {Current().name}</b></size>\n<color=#8c96a8>확률표 {StarShopClient.RatesVersion}{(banner == "aura" ? "" : " · 내 직업 기준")}</color>\n\n");
            foreach (var r in list)
            {
                sb.Append($"<color={GradeHex(r.rarity)}><b>{r.name}  {r.rate:0.##}%</b></color>\n");
                foreach (var i in r.items)
                {
                    if (i.gear)
                    {
                        var g = EquipmentDatabase.Get(i.id);
                        sb.Append($"    {(g != null ? g.name : i.id)}   {i.rate:0.###}%\n");
                        continue;
                    }
                    var p = CosmeticCatalog.Find(i.id);
                    string mark = StarShopClient.Owned.Contains(i.id) ? "  <color=#8fe28f>보유</color>" : "";
                    string dmg = p != null && p.DamagePercent > 0 ? $"  <color=#ffb347>공격력 +{p.DamagePercent}%</color>" : "";
                    sb.Append($"    {(p != null ? p.Name : i.name)}   {i.rate:0.###}%{dmg}{mark}\n");
                }
                sb.Append('\n');
            }
            StarShopClient.Refund.TryGetValue("common", out int rc);
            StarShopClient.Refund.TryGetValue("rare", out int rr);
            StarShopClient.Refund.TryGetValue("epic", out int re);
            StarShopClient.Refund.TryGetValue("unique", out int ru);
            sb.Append("<color=#b8c4d8>");
            if (banner == "aura" || banner == "skin")
            {
                string top = banner == "skin" ? "내 직업 유니크 스킨" : "에픽 오라";
                int gm = banner == "skin" ? StarShopClient.SkinGaugeMax : StarShopClient.AuraGaugeMax;
                sb.Append($"· 선택 게이지: 1회 뽑을 때마다 1칸 찹니다. {gm}칸이 다 차면 {top} 중 원하는 것 하나를 고릅니다(고르면 {gm}칸 줄어듦, 기한 없음). 뽑기에서 자연히 나와도 게이지는 줄지 않습니다.\n");
                sb.Append("· 중복 방지: 같은 등급에서 아직 없는 것만 같은 확률로 나옵니다. 개별 확률은 아무것도 없을 때 기준입니다.\n");
                sb.Append($"· 같은 등급을 모두 가지면 이미 가진 외형이 나오고 <color=#ffd34a>여분</color>으로 쌓입니다. 여분 4개로 한 등급 위에 도전(합성)하거나 별조각으로 분해할 수 있습니다(분해: 일반 {rc}, 희귀 {rr}, 에픽 {re}, 유니크 {ru}). 캐시샵 > 합성 · 컬렉션\n");
            }
            else
            {
                sb.Append("· 등급을 먼저 정하고, 그 등급의 장비 중 하나가 같은 확률로 나옵니다. 그 뽑기에 없는 등급의 몫은 가장 낮은 등급이 가집니다.\n");
                sb.Append("· 뽑은 장비는 +0으로 바로 가방에 들어갑니다. 선택 게이지는 없습니다.\n");
            }
            sb.Append($"· 10+1회는 별조각 {StarShopClient.PriceTen:N0}로 {StarShopClient.TenCount}회를 뽑습니다(마지막 1회가 보너스). "
                + (banner == "aura" || banner == "skin" ? "보너스 1회는 희귀 이상만 나옵니다." : "보너스 1회도 위 확률 그대로입니다.") + "</color>");
            return sb.ToString();
        }
    }
}
