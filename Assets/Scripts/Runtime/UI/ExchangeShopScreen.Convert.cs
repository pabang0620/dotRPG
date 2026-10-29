using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// 별빛 교환소 goods paging, class tags and the 별의 결정 -> 별조각 page.
    /// Goods pages: the 2 x GoodRows rows show one page of the list, so every good the server sends stays reachable
    /// however many there are (the 교환 품목 and 별조각 상품 tabs both use it). Class skins carry their class, own class first.
    /// Conversion: Star Crystals become FREE Star Shards at the server's rate (StarShopClient.CrystalsPerStar), no limit.
    /// </summary>
    public partial class ExchangeShopScreen
    {
        static readonly int[] ConvSteps = { -100, -10, -1, 1, 10, 100 };

        RectTransform goodsBar, convPage;
        Button convertBtn, convOk;
        Text convInfo, convAmount, convHave;
        (Button prev, Text label, Button next) goodsPager;
        int goodsPage, convStars;

        bool ConvertOpen => convPage != null && convPage.gameObject.activeSelf;
        static CharacterClass MyClass => Game.Player != null ? Game.Player.Class : Game.Session.PlayerClass;

        /// <summary>Built in Create after the pick page: the goods bar (convert button + pager) and the convert page on top.</summary>
        void BuildGoodsExtras()
        {
            var tl = new Vector2(0f, 1f);
            goodsBar = UIFactory.Place(UIFactory.Rect(content, "GoodsBar"), tl, tl, new Vector2(0f, -62f), new Vector2(414f, 44f));
            convertBtn = Button(goodsBar, "Convert", L.T("별조각으로 바꾸기"), "ui_btngray", tl, tl, Vector2.zero, new Vector2(200f, 44f), OpenConvert, 16);
            goodsPager = Pager(goodsBar, new Vector2(0f, -6f), d => { goodsPage += d; Refresh(); });

            var page = Panel(content, "ConvertPage", new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 360f), new Color32(10, 14, 24, 250));
            page.raycastTarget = true;
            convPage = page.rectTransform;
            Label(page.transform, "Title", L.T("별의 결정 → 별조각"), 22, tl, tl, new Vector2(20f, -14f), new Vector2(720f, 34f), TextAnchor.MiddleLeft);
            convInfo = Label(page.transform, "Info", "", 16, tl, tl, new Vector2(20f, -54f), new Vector2(720f, 48f), TextAnchor.UpperLeft);
            convHave = Label(page.transform, "Have", "", 17, tl, tl, new Vector2(20f, -104f), new Vector2(720f, 30f), TextAnchor.MiddleLeft);
            convAmount = Label(page.transform, "Amount", "", 22, tl, tl, new Vector2(20f, -142f), new Vector2(720f, 40f), TextAnchor.MiddleCenter);
            for (int i = 0; i < ConvSteps.Length; i++)
            {
                int step = ConvSteps[i];
                Button(page.transform, "ConvStep" + i, step > 0 ? "+" + step : step.ToString(), "ui_btngray", tl, tl, new Vector2(20f + i * 92f, -198f), new Vector2(86f, 48f), () => StepConvert(step), 18);
            }
            Button(page.transform, "ConvMax", L.T("최대"), "ui_btngray", tl, tl, new Vector2(572f, -198f), new Vector2(168f, 48f), () => { convStars = ConvertMax(); RefreshConvert(); }, 18);
            convOk = Button(page.transform, "ConvOk", L.T("바꾸기"), "ui_btn", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(-110f, 16f), new Vector2(200f, 48f), DoConvert, 18);
            Button(page.transform, "ConvCancel", L.T("취소"), "ui_btngray", new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(110f, 16f), new Vector2(200f, 48f), CloseConvert, 18);
            convPage.gameObject.SetActive(false);
        }

        // ---------------- goods pages ----------------

        /// <summary>First list index of the shown page; also sets the pager and the bar.</summary>
        int GoodsPageStart(int count)
        {
            int pages = Mathf.Max(1, (count + goods.Count - 1) / goods.Count);
            goodsPage = Mathf.Clamp(goodsPage, 0, pages - 1);
            SetPager(goodsPager, goodsPage, pages);
            convertBtn.interactable = !busy && !Closed && StarShopClient.ExchangeLoaded && StarShopClient.Crystals >= StarShopClient.CrystalsPerStar;
            RefreshConvert();
            return goodsPage * goods.Count;
        }

        /// <summary>Class of a class-only skin good (null = not a class skin).</summary>
        static CharacterClass? ClassOf(ExchangeGood g)
        {
            if (g.kind != "cosmetic" || string.IsNullOrEmpty(g.itemId)) return null;
            var skin = CosmeticCatalog.Find(g.itemId)?.Skin;
            if (skin != null) return skin.cls;
            return g.itemId.StartsWith("skin_m_") ? CharacterClass.Mage : g.itemId.StartsWith("skin_r_") ? CharacterClass.Rogue
                : g.itemId.StartsWith("skin_w_") ? CharacterClass.Warrior : (CharacterClass?)null;
        }

        /// <summary>The goods in server order, except class skins: my class first, then the other classes (stable).</summary>
        static List<ExchangeGood> ShownGoods()
        {
            var mine = MyClass;
            return StarShopClient.Goods.OrderBy(g => { var c = ClassOf(g); return c == null ? 0 : c == mine ? 1 : 2; }).ToList();
        }

        static string ClassTag(ExchangeGood g)
        {
            var c = ClassOf(g);
            if (c == null) return "";
            string name = CharacterClassInfo.NameOf(c.Value);
            return c == MyClass
                ? "  <size=15><color=#8fe28f>" + L.F("{0} 전용 (내 직업)", name) + "</color></size>"
                : "  <size=15><color=#b8c4d8>" + L.F("{0} 전용", name) + "</color></size>";
        }

        /// <summary>Pick goods list skins of every class: my class first, then auras and the other classes (stable).</summary>
        static void SortChoices(ExchangeGood g)
        {
            string mine = MyClass == CharacterClass.Mage ? "mage" : MyClass == CharacterClass.Rogue ? "rogue" : "warrior";
            var sorted = g.choices.OrderBy(c => c.skin == mine ? 0 : string.IsNullOrEmpty(c.skin) ? 1 : 2).ToList();
            g.choices.Clear();
            g.choices.AddRange(sorted);
        }

        // ---------------- 별의 결정 -> 별조각 ----------------

        static int ConvertMax() => (int)System.Math.Min(500_000_000L, StarShopClient.Crystals / System.Math.Max(1, StarShopClient.CrystalsPerStar));

        void OpenConvert()
        {
            if (busy || !StarShopClient.ExchangeLoaded) return;
            ClosePick();
            convStars = Mathf.Min(1, ConvertMax());
            convPage.gameObject.SetActive(true);
            convPage.SetAsLastSibling();
            RefreshConvert();
        }

        void CloseConvert()
        {
            if (convPage != null) convPage.gameObject.SetActive(false);
        }

        void StepConvert(int step)
        {
            convStars = Mathf.Clamp(convStars + step, 0, ConvertMax());
            RefreshConvert();
        }

        void RefreshConvert()
        {
            if (!ConvertOpen) return;
            int per = StarShopClient.CrystalsPerStar;
            convStars = Mathf.Clamp(convStars, 0, ConvertMax());
            convInfo.text = L.F("<color=#b8c4d8>별의 결정 {0}개 = 별조각 1개. 받은 별조각은 무료 별조각이며, 바꾼 결정은 되돌릴 수 없습니다.</color>", per);
            convHave.text = L.F("보유 · <color=#d8b8ff>{0}</color> · <color=#ffd76a>{1}</color>", StarShopClient.CrystalText(StarShopClient.Crystals), StarShopClient.Stars(StarShopClient.Balance));
            convAmount.text = L.F("별의 결정 {0:N0}개 → <color=#ffd76a>별조각 {1:N0}개</color>", (long)convStars * per, convStars);
            convOk.interactable = !busy && !Closed && convStars > 0;
        }

        void DoConvert()
        {
            if (busy || convStars <= 0) return;
            long crystals = (long)convStars * StarShopClient.CrystalsPerStar;
            busy = true;
            RefreshConvert();
            Refresh();
            StarShopClient.ConvertCrystals(crystals, (ok, msg, got) =>
            {
                busy = false;
                if (!ok) { SetStatus(msg, false); Game.Audio.PlaySfx("cancel"); }
                else
                {
                    Game.Audio.PlaySfx("quest");
                    CloseConvert();
                    SetStatus(L.F("바꾸기 완료 · 별조각 +{0:N0}", got));
                    GameEvents.RaiseToast(L.F("<color=#d8b8ff>별의 결정</color> → <color=#ffd76a>별조각 +{0:N0}</color>", got));
                }
                RefreshConvert();
                Refresh();
            });
        }
    }
}
