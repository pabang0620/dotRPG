using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace DotRPG.EditorTools
{
    /// <summary>Small deterministic checks; no real payment, network or gameplay loop.</summary>
    public static class CosmeticStoreChecks
    {
        const string SelectionKey = "dotRPG.cosmetic.aura";

        [MenuItem("DotRPG/Checks/Cosmetic Store")]
        public static void Run()
        {
            bool hadSelection = PlayerPrefs.HasKey(SelectionKey);
            string previous = PlayerPrefs.GetString(SelectionKey);
            try
            {
                PlayerPrefs.DeleteKey(SelectionKey);
                var offline = new CosmeticStore(new UnavailableCommerceProvider());
                Require(offline.Equipped.Id == "aura_none", "Existing players retain the default appearance.");
                Require(offline.Equip("aura_sky"), "Free aura can be equipped.");
                Require(!offline.Equip("aura_sunset") && !offline.Equip("unknown"), "Unowned and unknown items cannot be equipped.");
                Require(new CosmeticStore(new UnavailableCommerceProvider()).Equipped.Id == "aura_sky", "Selection survives store recreation.");
                PlayerPrefs.SetString(SelectionKey, "aura_sunset");
                Require(new CosmeticStore(new UnavailableCommerceProvider()).Equipped.Id == "aura_none", "Preferences cannot grant paid ownership.");

                var provider = new TestProvider();
                var store = new CosmeticStore(provider);
                store.RefreshAsync().GetAwaiter().GetResult();
                var offer = store.OfferFor("aura_sunset");
                Require(offer != null, "Verified offer is available.");
                var purchase = store.PurchaseAsync(offer);
                store.PurchaseAsync(offer).GetAwaiter().GetResult();
                Require(provider.Purchases == 1 && store.IsBusy && !store.Owns("aura_sunset"), "Concurrent clicks cannot duplicate or prematurely grant a purchase.");
                provider.Owned = true;
                provider.Pending.SetResult(true);
                purchase.GetAwaiter().GetResult();
                Require(store.Owns("aura_sunset") && store.Equip("aura_sunset"), "Verified ownership enables equipment.");
                store.PurchaseAsync(offer).GetAwaiter().GetResult();
                Require(provider.Purchases == 1, "Owned product cannot be repurchased.");
                provider.Owned = false;
                store.RestoreAsync().GetAwaiter().GetResult();
                Require(!store.Owns("aura_sunset") && store.Equipped.Id == "aura_none", "Refunded ownership is revoked on synchronization.");
                store.PurchaseAsync(offer).GetAwaiter().GetResult();
                Require(provider.Purchases == 1, "Stale confirmation cannot buy a replacement quote.");
                provider.Pending = new TaskCompletionSource<bool>();
                offer = store.OfferFor("aura_sunset");
                purchase = store.PurchaseAsync(offer);
                provider.Pending.SetCanceled();
                purchase.GetAwaiter().GetResult();
                Require(!store.Owns("aura_sunset") && !store.IsBusy && store.OfferFor("aura_sunset") == null, "Cancellation clears busy state and consumes the quote without a grant.");
                CheckScreen();
                Debug.Log("[CosmeticStoreChecks] PASS: 12 state checks and screen creation; no network or payments.");
            }
            finally
            {
                if (hadSelection) PlayerPrefs.SetString(SelectionKey, previous);
                else PlayerPrefs.DeleteKey(SelectionKey);
                PlayerPrefs.Save();
            }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        static void CheckScreen()
        {
            var previousArt = Game.Art;
            var previousStore = Game.Cosmetics;
            var previousPlayer = Game.Player;
            var canvas = new GameObject("CosmeticChecksCanvas", typeof(RectTransform));
            try
            {
                Game.Art = new SpriteLibrary(16);
                Game.Cosmetics = new CosmeticStore(new UnavailableCommerceProvider());
                Game.Player = null;
                var icon = Resources.Load<Sprite>("Art/menuicon_cosmetics");
                var stars = Resources.Load<Sprite>("Art/fx_cosmetic_stars");
                Require(icon != null && stars != null, "Generated assets are imported as sprites.");
                Require(icon.texture.width <= 128 && stars.texture.width <= 256, "Runtime textures stay within their size budgets.");
                Require(stars.bounds.size.x >= 0.8f && stars.bounds.size.x <= 1.2f, "Aura world size matches the original one-unit footprint.");
                Require(CosmeticAura.ForProduct(CosmeticCatalog.Find("aura_sunset")) == stars, "Paid aura resolves the generated art.");
                Require(CosmeticAura.ForProduct(CosmeticCatalog.Find("aura_sky")) != stars, "Free aura keeps the original art.");
                var screen = CosmeticShopScreen.Create(canvas.transform);
                screen.Show();
                Require(screen.gameObject.activeSelf, "Screen opens with the unavailable provider.");
                var button = screen.transform.Find("Content/Layout/Preview/PurchaseOrEquip").GetComponent<UnityEngine.UI.Button>();
                screen.transform.Find("Content/Layout/Catalog/Product_aura_sunset").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Require(!button.interactable, "Paid purchases are disabled before integration.");
                Require(screen.transform.Find("Content/Layout/Preview/Aura").GetComponent<UnityEngine.UI.Image>().sprite == stars,
                    "Preview uses the same paid artwork as the world renderer.");
                screen.transform.Find("Content/Layout/Catalog/Product_aura_sky").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Require(button.interactable, "Free appearance remains available.");
                screen.Hide();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
                Game.Art = previousArt;
                Game.Cosmetics = previousStore;
                Game.Player = previousPlayer;
            }
        }

        sealed class TestProvider : ICommerceProvider
        {
            public bool IsAvailable => true;
            public bool Owned;
            public int Purchases;
            public TaskCompletionSource<bool> Pending = new TaskCompletionSource<bool>();
            public Task<CommerceSnapshot> FetchAsync() => Task.FromResult(new CommerceSnapshot
            {
                OwnedProductIds = Owned ? new[] { "aura_sunset" } : Array.Empty<string>(),
                Offers = new[] { new CosmeticOffer("aura_sunset", "테스트 가격", Guid.NewGuid().ToString()) },
            });
            public Task<CommerceSnapshot> RestoreAsync() => FetchAsync();
            public Task PurchaseAsync(CosmeticOffer offer)
            {
                Purchases++;
                return Pending.Task;
            }
        }
    }
}
