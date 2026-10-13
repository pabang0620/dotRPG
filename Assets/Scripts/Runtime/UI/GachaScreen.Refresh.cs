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
                (sp != null && sp.Rarity == CosmeticRarity.Epic ? epics : uniques).Add(L.T(sk.name));
            }
            badgeBg.gameObject.SetActive(banner == "skin");
            badge.text = L.F("<b>유니크 {0} · 에픽 {1}</b>", uniques.Count, epics.Count);
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
                string goal = banner == "skin" ? L.T("유니크 스킨") : L.T("에픽 오라");
                gaugeText.text = full ? (banner == "skin" ? L.T("<color=#ffd34a><b>선택 게이지 가득!</b></color> 유니크 스킨을 직접 고를 수 있습니다") : L.T("<color=#ffd34a><b>선택 게이지 가득!</b></color> 에픽 오라를 직접 고를 수 있습니다"))
                    : L.F("선택 게이지 {0}/{1} · 가득 차면 {2} 직접 선택", Mathf.Min(pity, gaugeMax), gaugeMax, goal);
            }
            else if (Sealed)
            {
                // [CASH] 봉인 해제 게이지: 10 opens fill it, the next open is boosted.
                gaugeFill.rectTransform.sizeDelta = new Vector2((GaugeW - 4f) * (CashClient.NextBoosted ? 1f : Mathf.Clamp01(CashClient.Gauge / 10f)), 16f);
                gaugeText.text = CashClient.NextBoosted ? L.T("<color=#ffd34a><b>봉인 해제!</b></color> 다음 1회는 희귀 이상 확률 2배 · 수량 2배")
                    : L.F("봉인 해제 게이지 {0}/10 · 가득 차면 다음 1회 부스터", CashClient.Gauge);
            }
            bigSub.text = $"{cur.sub}\n" +
                          (hasPity ? "" : Sealed ? L.T("나온 아이템은 바로 가방으로") + "\n" : L.T("뽑은 장비는 바로 가방으로") + "\n") +
                          (banner == "skin" ? L.F("<color=#ffb347>유니크 {0} (+5%)</color>\n<color=#c58cff>에픽 {1} (+4%)</color>", string.Join(" · ", uniques), string.Join(" · ", epics)) : banner == "aura" ? L.T("<color=#ffb347>오라 공격력 +1~3%</color>") : "");
            wallet.text = online
                ? (StarShopClient.Loaded ? L.F("보유 <color=#ffd34a>{0}</color>", StarShopClient.Stars(StarShopClient.Balance)) : L.T("<color=#8c96a8>불러오는 중...</color>"))
                : L.T("<color=#8c96a8>온라인 캐릭터로 접속하면 이용할 수 있습니다.</color>");
            TextOf(one).text = L.F("1회 · 별조각 {0:N0}", Sealed ? CashClient.PriceOne : StarShopClient.PriceOne);
            TextOf(ten).text = Sealed ? L.F("10+1회 · 별조각 {0:N0}", CashClient.PriceEleven) : L.F("{0}+1회 · 별조각 {1:N0}", StarShopClient.TenCount - 1, StarShopClient.PriceTen);
            one.interactable = ten.interactable = online && StarShopClient.Loaded && !busy;
            RefreshTierPicker(!hasPity && !Sealed && online && StarShopClient.Loaded);
            RefreshCeiling(online);
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
            if (list.Count == 0) return L.T("<color=#8c96a8>확률표를 불러오는 중입니다.</color>");
            var sb = new StringBuilder();
            sb.Append(L.F("<size=24><b>확률 공개 · {0}</b></size>", Current().name) + "\n" + L.F("<color=#8c96a8>확률표 {0}{1}</color>", StarShopClient.RatesVersion, banner == "aura" ? "" : L.T(" · 내 직업 기준")) + "\n\n");
            foreach (var r in list)
            {
                sb.Append($"<color={GradeHex(r.rarity)}><b>{L.T(r.name)}  {r.rate:0.##}%</b></color>\n");
                foreach (var i in r.items)
                {
                    if (i.gear)
                    {
                        var g = EquipmentDatabase.Get(i.id);
                        sb.Append($"    {L.T(g != null ? g.name : i.id)}   {i.rate:0.###}%\n");
                        continue;
                    }
                    var p = CosmeticCatalog.Find(i.id);
                    string mark = StarShopClient.Owned.Contains(i.id) ? L.T("  <color=#8fe28f>보유</color>") : "";
                    string dmg = p != null && p.DamagePercent > 0 ? L.F("  <color=#ffb347>공격력 +{0}%</color>", p.DamagePercent) : "";
                    sb.Append($"    {L.T(p != null ? p.Name : i.name)}   {i.rate:0.###}%{dmg}{mark}\n");
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
                string top = banner == "skin" ? L.T("내 직업 유니크 스킨") : L.T("에픽 오라");
                int gm = banner == "skin" ? StarShopClient.SkinGaugeMax : StarShopClient.AuraGaugeMax;
                sb.Append(L.F("· 선택 게이지: 1회 뽑을 때마다 1칸 찹니다. {0}칸이 다 차면 {1} 중 원하는 것 하나를 고릅니다(고르면 {0}칸 줄어듦, 기한 없음). 뽑기에서 자연히 나와도 게이지는 줄지 않습니다.", gm, top) + "\n");
                sb.Append(L.T("· 중복 방지: 같은 등급에서 아직 없는 것만 같은 확률로 나옵니다. 개별 확률은 아무것도 없을 때 기준입니다.") + "\n");
                sb.Append(L.F("· 같은 등급을 모두 가지면 이미 가진 외형이 나오고 <color=#ffd34a>여분</color>으로 쌓입니다. 여분 4개로 한 등급 위에 도전(합성)하거나 별조각으로 분해할 수 있습니다(분해: 일반 {0}, 희귀 {1}, 에픽 {2}, 유니크 {3}). 캐시샵 > 합성 · 컬렉션", rc, rr, re, ru) + "\n");
            }
            else
            {
                sb.Append(L.T("· 등급을 먼저 정하고, 그 등급의 장비 중 하나가 같은 확률로 나옵니다. 그 뽑기에 없는 등급의 몫은 가장 낮은 등급이 가집니다.") + "\n");
                sb.Append(L.T("· 뽑은 장비는 +0으로 바로 가방에 들어갑니다.") + "\n");
                sb.Append(L.F("· 천장: 장비 뽑기(무기·방어구·장신구 합산) 1회마다 1칸 찹니다. {0}칸이 다 차면 고른 뽑기·레벨 단계의 유니크 장비 중 하나를 고릅니다(그 단계에 유니크가 없으면 가장 높은 등급). 고르면 {0}칸 줄고 넘친 칸은 남습니다. 뽑기에서 자연히 나와도 게이지는 줄지 않습니다.", StarShopClient.GearCeilingMax) + "\n");
            }
            sb.Append(L.F("· 10+1회는 별조각 {0:N0}로 {1}회를 뽑습니다(마지막 1회가 보너스). ", StarShopClient.PriceTen, StarShopClient.TenCount)
                + (banner == "aura" || banner == "skin" ? L.T("보너스 1회는 희귀 이상만 나옵니다.") : L.T("보너스 1회도 위 확률 그대로입니다.")) + "</color>");
            return sb.ToString();
        }
    }
}
