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
            // Skins this class can draw now (retired and exchange-only ones left out), by grade.
            int legends = 0, uniques = 0, epics = 0, hiddens = 0;
            var cls = Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;
            foreach (var sk in SkinCatalog.All)
            {
                if (sk.cls != cls) continue;
                var sp = CosmeticCatalog.Find(sk.id);
                if (sp == null || sp.Retired || sp.ExchangeOnly) continue;
                if (sp.Hidden) hiddens++;
                else if (sp.Rarity == CosmeticRarity.Legend) legends++;
                else if (sp.Rarity == CosmeticRarity.Unique) uniques++;
                else if (sp.Rarity == CosmeticRarity.Epic) epics++;
            }
            badgeBg.gameObject.SetActive(banner == "skin");
            badge.text = legends > 0 ? L.F("<b>전설 {0} · 유니크 {1} · 히든 {2}</b>", legends, uniques, hiddens) : L.F("<b>유니크 {0} · 에픽 {1}</b>", uniques, epics);
            string gaugeId = banner == "skin" ? "skin" : "aura";
            int pity = StarShopClient.GaugeValue(gaugeId);
            bool hasPity = banner == "skin" || banner == "aura";
            bool showGauge = hasPity || Sealed;
            // The bar runs to the dearest goal; the button shows once the cheapest one is paid for.
            int gaugeMin = Mathf.Max(1, StarShopClient.GaugeMin(gaugeId) > 0 ? StarShopClient.GaugeMin(gaugeId) : banner == "skin" ? StarShopClient.SkinGaugeMax : StarShopClient.AuraGaugeMax);
            int gaugeMax = Mathf.Max(gaugeMin, StarShopClient.GaugeMax(gaugeId));
            bool full = hasPity && pity >= gaugeMin;
            gaugeBg.gameObject.SetActive(showGauge);
            gaugeText.gameObject.SetActive(showGauge);
            chooseBtn.gameObject.SetActive(full);
            if (hasPity)
            {
                gaugeFill.rectTransform.sizeDelta = new Vector2((GaugeW - 4f) * Mathf.Clamp01(pity / (float)gaugeMax), 16f);
                gaugeText.text = (full ? L.T("<color=#ffd34a><b>선택 가능!</b></color> ") : "") + L.F("선택 게이지 {0} · {1}", pity, GoalText(gaugeId));
            }
            else if (Sealed)
            {
                // [CASH] 봉인 해제 게이지: 10 opens fill it, the next open is boosted.
                gaugeFill.rectTransform.sizeDelta = new Vector2((GaugeW - 4f) * (CashClient.NextBoosted ? 1f : Mathf.Clamp01(CashClient.Gauge / 10f)), 16f);
                gaugeText.text = CashClient.NextBoosted ? L.T("<color=#ffd34a><b>봉인 해제!</b></color> 다음 1회는 희귀 이상 확률 2배 · 수량 2배")
                    : L.F("봉인 해제 게이지 {0}/10 · 가득 차면 다음 1회 부스터", CashClient.Gauge);
            }
            RefreshHiddenGauge(online);
            bigSub.text = $"{cur.sub}\n" +
                          (hasPity ? "" : Sealed ? L.T("나온 아이템은 바로 가방으로") + "\n" : L.T("뽑은 장비는 바로 가방으로") + "\n") +
                          (banner == "skin"
                              ? (legends > 0 ? L.F("<color=#ff6b5e>전설 {0}종 (+6%)</color>\n", legends) : "")
                                + L.F("<color=#ffb347>유니크 {0}종 (+5%)</color>\n<color=#c58cff>에픽 {1}종 (+4%)</color>", uniques, epics)
                                + (hiddens > 0 ? "\n" + L.F("<color=#ffe27a>히든 {0}종 (한 단계 위 공격력)</color>", hiddens) : "")
                              : banner == "aura" ? L.T("<color=#ffb347>오라 공격력 +1~3%</color>") : "");
            wallet.text = online
                ? (StarShopClient.Loaded ? L.F("보유 <color=#ffd34a>{0}</color>", StarShopClient.Stars(StarShopClient.Balance)) : L.T("<color=#8c96a8>불러오는 중...</color>"))
                : L.T("<color=#8c96a8>온라인 캐릭터로 접속하면 이용할 수 있습니다.</color>");
            TextOf(one).text = L.F("1회 · 별조각 {0:N0}", Sealed ? CashClient.PriceOne : StarShopClient.PriceOne);
            TextOf(ten).text = Sealed ? L.F("10+1회 · 별조각 {0:N0}", CashClient.PriceEleven) : L.F("{0}+1회 · 별조각 {1:N0}", StarShopClient.TenCount - 1, StarShopClient.PriceTen);
            one.interactable = ten.interactable = online && StarShopClient.Loaded && !busy;
            RefreshTierPicker(!hasPity && !Sealed && online && StarShopClient.Loaded);
            RefreshCeiling(online);
            if (rateModal.gameObject.activeSelf)
            {
                rateText.text = RateText();
                if (rateTopPending && StarShopClient.Loaded)
                {
                    rateTopPending = false;
                    StartCoroutine(ResetRateTop(rateOpenVersion));
                }
            }
            DrawCells();
        }

        /// <summary>The Hidden gauge row (skin banner): every skin draw adds one; Hidden skins are picked with it.</summary>
        void RefreshHiddenGauge(bool online)
        {
            bool show = banner == "skin" && online && StarShopClient.Loaded && StarShopClient.GaugeTargets("hidden").Count > 0;
            hiddenBg.gameObject.SetActive(show);
            hiddenText.gameObject.SetActive(show);
            int have = StarShopClient.HiddenPity, min = StarShopClient.GaugeMin("hidden"), max = Mathf.Max(1, StarShopClient.GaugeMax("hidden"));
            hiddenBtn.gameObject.SetActive(show && min > 0 && have >= min);
            hiddenBtn.interactable = !busy;
            if (!show) return;
            hiddenFill.rectTransform.sizeDelta = new Vector2((GaugeW - 4f) * Mathf.Clamp01(have / (float)max), 10f);
            hiddenText.text = (have >= min ? L.T("<color=#ffe27a><b>히든 선택 가능!</b></color> ") : "") + L.F("히든 게이지 {0} · {1}", have, GoalText("hidden"));
        }

        /// <summary>"유니크 100칸 · 전설 1,000칸": each goal of a gauge (achieved ones in gold).</summary>
        static string GoalText(string gauge)
        {
            int have = StarShopClient.GaugeValue(gauge);
            var parts = new List<string>();
            foreach (var g in StarShopClient.GaugeTargets(gauge))
            {
                string grade = GoalGrade(g);
                string text = L.F("{0} {1:N0}칸", grade, g.cost);
                parts.Add(have >= g.cost ? "<color=#ffd34a>" + text + "</color>" : text);
            }
            return string.Join(" · ", parts);
        }

        static string GoalGrade(StarGaugeTarget g)
        {
            var r = RarityOfKey(g.rarity);
            return g.hidden ? L.F("{0} 히든", CosmeticCatalog.RarityName(r)) : CosmeticCatalog.RarityName(r);
        }

        static CosmeticRarity RarityOfKey(string r) =>
            r == "legend" ? CosmeticRarity.Legend : r == "unique" ? CosmeticRarity.Unique : r == "epic" ? CosmeticRarity.Epic : r == "rare" ? CosmeticRarity.Rare : CosmeticRarity.Common;

        /// <summary>Grade colour of a cosmetic (a Hidden one in the Hidden gold).</summary>
        static string GradeColor(CosmeticProduct p) => p.Hidden ? CosmeticCatalog.HiddenHex : CosmeticCatalog.RarityHex(p.Rarity);

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
            bool cosmetic = banner == "aura" || banner == "skin";
            foreach (var r in list)
            {
                string rowName = cosmetic ? (r.hidden ? L.F("{0} 히든", CosmeticCatalog.RarityName(RarityOfKey(r.rarity))) : CosmeticCatalog.RarityName(RarityOfKey(r.rarity))) : L.T(r.name);
                string bonus = cosmetic ? L.F("  <size=14>(보너스 1회 {0:0.###}%)</size>", r.bonusRate) : "";
                sb.Append($"<color={(r.hidden ? CosmeticCatalog.HiddenHex : GradeHex(r.rarity))}><b>{rowName}  {r.rate:0.###}%</b></color>{bonus}\n");
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
            sb.Append("<color=#b8c4d8>");
            if (cosmetic)
            {
                string gauge = banner == "skin" ? "skin" : "aura";
                sb.Append(L.F("· 선택 게이지: 1회 뽑을 때마다 1칸 찹니다. 목표 칸({0})이 차면 그 등급의 내 직업 외형 중 하나를 고르고 그만큼 칸이 줄며, 남은 칸은 이어집니다(기한 없음). 뽑기에서 자연히 나와도 게이지는 줄지 않습니다.", GoalText(gauge)) + "\n");
                if (banner == "skin" && StarShopClient.GaugeTargets("hidden").Count > 0)
                    sb.Append(L.F("· 히든 게이지: 스킨 뽑기 1회마다 1칸 찹니다. 목표 칸({0})이 차면 그 등급 히든 하나를 고릅니다.", GoalText("hidden")) + "\n");
                if (banner == "skin")
                    sb.Append(L.T("· 히든: 같은 등급 안의 특별 디자인입니다. 히든 1종의 확률은 같은 등급 보통 1종의 1/5이고, 공격력 보너스는 한 단계 위입니다.") + "\n");
                sb.Append(L.T("· 중복 방지: 같은 칸에서 아직 없는 것만 같은 확률로 나옵니다. 개별 확률은 아무것도 없을 때 기준입니다.") + "\n");
                sb.Append(L.T("· 같은 칸을 모두 가지면 이미 가진 외형이 나오고 <color=#ffd34a>여분</color>으로 쌓입니다. 여분 4개로 한 등급 위에 도전(합성, 히든 제외)하거나 별조각·별의 결정으로 분해할 수 있습니다. 캐시샵 > 합성 · 컬렉션") + "\n");
            }
            else
            {
                sb.Append(L.T("· 등급을 먼저 정하고, 그 등급의 장비 중 하나가 같은 확률로 나옵니다. 그 뽑기에 없는 등급의 몫은 가장 낮은 등급이 가집니다.") + "\n");
                sb.Append(L.T("· 뽑은 장비는 +0으로 바로 가방에 들어갑니다.") + "\n");
                sb.Append(L.F("· 천장: 장비 뽑기(무기·방어구·장신구 합산) 1회마다 1칸 찹니다. {0}칸이 다 차면 고른 뽑기·레벨 단계의 유니크 장비 중 하나를 고릅니다(그 단계에 유니크가 없으면 가장 높은 등급). 고르면 {0}칸 줄고 넘친 칸은 남습니다. 뽑기에서 자연히 나와도 게이지는 줄지 않습니다.", StarShopClient.GearCeilingMax) + "\n");
            }
            sb.Append(L.F("· 10+1회는 별조각 {0:N0}로 {1}회를 뽑습니다(마지막 1회가 보너스). ", StarShopClient.PriceTen, StarShopClient.TenCount)
                + (cosmetic ? L.T("보너스 1회는 일반(보통)이 나오지 않고 나머지가 같은 비율로 커집니다(괄호 안 확률).") : L.T("보너스 1회도 위 확률 그대로입니다.")) + "</color>");
            return sb.ToString();
        }
    }
}
