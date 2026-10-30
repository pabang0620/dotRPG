using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [CEILING] 천장 (Docs/PLAN_SIM_IMPROVEMENTS_2026-10-12.md A) under the banner's own gauge: the 봉인된 상자 one
    /// (every open from the shop or the bag, full = one +12 강화권) and the gear one (every gear draw, full = one 유니크
    /// piece the player picks from the chosen banner and level tier). Natural top drops never empty it; only the
    /// exchange does, and what is over the size stays.
    /// </summary>
    public partial class GachaScreen
    {
        Image ceilingBg, ceilingFill;
        Text ceilingText, ceilingPct;
        Button ceilingBtn;
        /// <summary>Gear pieces shown in the choice window while picking with the gear ceiling (null = cosmetic choice).</summary>
        List<string> gearPick;

        bool GearBanner => banner != "aura" && banner != "skin" && !Sealed;

        void BuildCeiling(Transform box)
        {
            var tl = new Vector2(0f, 1f);
            ceilingBg = UIFactory.Image(box, "Ceiling", Game.Art.Get("ui_white"), new Color32(10, 12, 20, 220));
            ceilingBg.preserveAspect = false; ceilingBg.raycastTarget = false;
            UIFactory.Place(ceilingBg.rectTransform, tl, tl, new Vector2(30f, -302f), new Vector2(GaugeW, 16f));
            ceilingFill = UIFactory.Image(ceilingBg.transform, "Fill", Game.Art.Get("ui_white"), new Color32(255, 179, 71, 255));
            ceilingFill.preserveAspect = false; ceilingFill.raycastTarget = false;
            UIFactory.Place(ceilingFill.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(2f, 0f), new Vector2(0f, 10f));
            ceilingPct = BarPercent(ceilingBg); // [UI] bar + percentage
            // [UI] the text stops at the bar's right end (the button sits right of it); the button fits fr / id at 14 px
            ceilingText = Label(box, "CeilingText", "", 17, tl, tl, new Vector2(30f, -324f), new Vector2(GaugeW, 26f), TextAnchor.MiddleLeft);
            ceilingBtn = Button(box, "CeilingBtn", L.T("천장 받기"), "ui_btn", tl, tl, new Vector2(30f + GaugeW + 14f, -296f), new Vector2(SideBtnW, 40f), UseCeiling, 18);
        }

        void RefreshCeiling(bool online)
        {
            bool show = online && (Sealed ? CashClient.Loaded : GearBanner && StarShopClient.Loaded);
            ceilingBg.gameObject.SetActive(show);
            ceilingText.gameObject.SetActive(show);
            if (!show) { ceilingBtn.gameObject.SetActive(false); return; }
            int gauge = Sealed ? CashClient.Ceiling : StarShopClient.GearCeiling;
            int max = Mathf.Max(1, Sealed ? CashClient.CeilingMax : StarShopClient.GearCeilingMax);
            bool ready = gauge >= max;
            ceilingFill.rectTransform.sizeDelta = new Vector2((GaugeW - 4f) * Mathf.Clamp01(gauge / (float)max), 10f);
            ceilingPct.text = Pct(gauge, max);
            string reward = Sealed
                ? $"{L.T(DungeonDatabase.ItemName(CashClient.CeilingReward))}{(CashClient.CeilingRewardCount > 1 ? $" x{CashClient.CeilingRewardCount}" : "")}"
                : L.T("유니크 장비 1개 선택");
            ceilingText.text = ready
                ? (Sealed ? L.F("<color=#ffb347><b>천장 {0}/{1} 가득!</b></color> {2}와 바꿀 수 있습니다", gauge, max, reward) : L.F("<color=#ffb347><b>천장 {0}/{1} 가득!</b></color> {2}를 바꿀 수 있습니다", gauge, max, reward))
                : (Sealed ? L.F("천장 {0}/{1} · 상자를 열 때마다 1칸, 가득 차면 {2}", gauge, max, reward) : L.F("천장 {0}/{1} · 장비를 뽑을 때마다 1칸, 가득 차면 {2}", gauge, max, reward));
            ceilingBtn.gameObject.SetActive(ready);
            ceilingBtn.interactable = !busy;
        }

        void UseCeiling()
        {
            if (busy || Revealing) return;
            if (Sealed) { AskSealedCeiling(); return; }
            OpenGearChoices();
        }

        void AskSealedCeiling()
        {
            string reward = L.T(DungeonDatabase.ItemName(CashClient.CeilingReward));
            Game.UI.Confirm(L.F("봉인된 상자 천장\n게이지 {0}칸을 써서 {1}을(를) 받습니다.", CashClient.CeilingMax, reward + (CashClient.CeilingRewardCount > 1 ? $" x{CashClient.CeilingRewardCount}" : "")), () =>
            {
                busy = true;
                Refresh();
                CashClient.ClaimCeiling((ok, msg) =>
                {
                    busy = false;
                    if (!ok) { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast(msg); Refresh(); return; }
                    shown = new List<StarPullResult>
                    {
                        new StarPullResult { itemId = CashClient.CeilingReward, rarity = RarityOf(CashClient.CeilingReward), cash = true, count = CashClient.CeilingRewardCount, byPity = true },
                    };
                    revealed = 0; waiting = -1; lastCount = 1;
                    revealAt = Time.unscaledTime + 0.3f;
                    OpenResults();
                    Refresh();
                });
            }, true);
        }

        void OpenGearChoices()
        {
            var ids = StarShopClient.GearCeilingChoices(CurrentRates());
            if (ids.Count == 0) { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast(L.T("이 단계에서 고를 수 있는 장비가 없습니다.")); return; }
            gearPick = ids;
            for (int i = 0; i < choiceRows.Count; i++)
            {
                var row = choiceRows[i];
                bool on = i < ids.Count;
                row.btn.gameObject.SetActive(on);
                if (!on) continue;
                var g = EquipmentDatabase.Get(ids[i]);
                row.icon.sprite = Game.Art.Get(DungeonDatabase.ItemIcon(ids[i]));
                row.icon.color = Color.white;
                string grade = g != null ? L.T(EquipmentDatabase.RarityName(g.rarity)) : "";
                row.label.text = $"<color=#ffb347>{L.T(g != null ? g.name : ids[i])}</color>  <size=15>{grade} · Lv.{SelectedTierLevel()}</size>";
                row.btn.interactable = true;
            }
            choiceModal.gameObject.SetActive(true);
            choiceModal.SetAsLastSibling();
            Game.Audio.PlaySfx("select");
        }

        void ChooseGear(int i)
        {
            if (gearPick == null || i >= gearPick.Count) return;
            string id = gearPick[i];
            string bannerId = banner;
            int tier = SelectedTier();
            var g = EquipmentDatabase.Get(id);
            Game.UI.Confirm(L.T(g != null ? g.name : id) + "\n" + L.F("천장 게이지 {0}칸을 써서 이 장비를 받습니다.", StarShopClient.GearCeilingMax), () =>
            {
                busy = true;
                StarShopClient.ClaimGearCeiling(bannerId, tier, id, (ok, msg) =>
                {
                    busy = false;
                    choiceModal.gameObject.SetActive(false);
                    gearPick = null;
                    if (!ok) { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast(msg); Refresh(); return; }
                    shown = new List<StarPullResult> { new StarPullResult { itemId = id, rarity = g != null ? g.rarity.ToString().ToLowerInvariant() : "unique", gear = true, byPity = true } };
                    revealed = 0; waiting = -1; lastCount = 1;
                    revealAt = Time.unscaledTime + 0.3f;
                    OpenResults();
                    Refresh();
                });
            }, true);
        }
    }
}
