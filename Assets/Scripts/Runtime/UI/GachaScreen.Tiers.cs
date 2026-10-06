using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Gear banners: pick the level tier to draw from (Lv.1, 10, 15 ... up to my level). Only my class's gear comes out,
    /// the rate table and the draw follow the chosen tier, and the default is the tier of my level.
    /// </summary>
    public partial class GachaScreen
    {
        const int MaxTiers = 8;
        readonly List<Button> tierButtons = new List<Button>();
        Text tierHint;
        /// <summary>Chosen tier per gear banner (-1 = my tier).</summary>
        readonly Dictionary<string, int> chosenTier = new Dictionary<string, int>();

        void BuildTierPicker(Transform box)
        {
            var tl = new Vector2(0f, 1f);
            tierHint = Label(box, "TierHint", "", 17, tl, tl, new Vector2(30f, -214f), new Vector2(560f, 26f), TextAnchor.MiddleLeft);
            for (int i = 0; i < MaxTiers; i++)
            {
                int index = i;
                var b = Button(box, "Tier" + i, "", "ui_btngray", tl, tl, new Vector2(30f + i * 72f, -244f), new Vector2(66f, 40f), () => ChooseTier(index), 16);
                // [UI] Tiers above my level carry a lock.
                var lockImg = UIFactory.Image(b.transform, "Lock", Game.Art.Get("ui_lock"), Color.white);
                lockImg.raycastTarget = false;
                UIFactory.Place(lockImg.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(4f, 6f), new Vector2(18f, 18f));
                tierButtons.Add(b);
            }
        }

        List<(int tier, int level, List<StarRate> rates)> TiersOf(string bannerId) =>
            StarShopClient.GearTiers.TryGetValue(bannerId, out var list) ? list : null;

        int SelectedTier()
        {
            if (banner == "aura" || banner == "skin") return -1;
            var tiers = TiersOf(banner);
            if (tiers == null || tiers.Count == 0) return -1;
            int t = chosenTier.TryGetValue(banner, out int c) && c >= 0 ? c : StarShopClient.MyTier;
            return Mathf.Clamp(t, 0, tiers.Count - 1);
        }

        int SelectedTierLevel()
        {
            var tiers = TiersOf(banner);
            int t = SelectedTier();
            return tiers != null && t >= 0 && t < tiers.Count ? tiers[t].level : 1;
        }

        List<StarRate> TierRates()
        {
            var tiers = TiersOf(banner);
            int t = SelectedTier();
            return tiers != null && t >= 0 && t < tiers.Count ? tiers[t].rates : null;
        }

        void ChooseTier(int index)
        {
            if (busy || Revealing) return;
            if (index > StarShopClient.MyTier) { Game.Audio.PlaySfx("cancel"); GameEvents.RaiseToast("내 레벨 단계까지만 고를 수 있습니다."); return; }
            chosenTier[banner] = index;
            Game.Audio.PlaySfx("select");
            Refresh();
        }

        void RefreshTierPicker(bool show)
        {
            var tiers = show ? TiersOf(banner) : null;
            bool on = tiers != null && tiers.Count > 0;
            tierHint.gameObject.SetActive(on);
            int selected = SelectedTier();
            for (int i = 0; i < tierButtons.Count; i++)
            {
                var b = tierButtons[i];
                bool has = on && i < tiers.Count;
                b.gameObject.SetActive(has);
                if (!has) continue;
                bool sel = i == selected, locked = i > StarShopClient.MyTier, mine = i == StarShopClient.MyTier;
                b.image.sprite = Game.Art.Get(sel ? "ui_btn" : "ui_btngray");
                b.image.color = locked ? new Color(1f, 1f, 1f, 0.45f) : Color.white;
                string lv = $"Lv.{tiers[i].level}";
                TextOf(b).text = sel ? $"<b>{lv}</b>" : mine ? $"<color=#ffd34a>{lv}</color>" : locked ? $"<color=#8c96a8>{lv}</color>" : lv;
                b.transform.Find("Lock").gameObject.SetActive(locked);
                b.interactable = !busy;
            }
            if (on) tierHint.text = $"<color=#ffd34a>레벨 단계</color> 선택 · 내 직업 Lv.{SelectedTierLevel()} 장비가 나옵니다 <color=#8c96a8>(노란 단계가 내 레벨, 그 위는 잠김)</color>";
        }
    }
}
