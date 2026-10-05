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
                CheckRecovery();
                Debug.Log("[CosmeticStoreChecks] PASS: original state/art checks, timeout recovery, single-owner input and responsive layout; no network or payments.");
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

        static void CheckRecovery()
        {
            var provider = new TestProvider();
            // Advance the timeout immediately; tests never wait for production timers.
            var store = new CosmeticStore(provider, (duration, cancellation) => Task.CompletedTask);
            store.RefreshAsync().GetAwaiter().GetResult();
            store.PurchaseAsync(store.OfferFor("aura_sunset")).GetAwaiter().GetResult();
            Require(!store.IsBusy && store.NeedsPurchaseRecovery, "Timeout releases the UI but retains purchase recovery.");
            Require(store.Equip("aura_sky"), "Free equipment remains usable during recovery.");
            store.RestoreAsync().GetAwaiter().GetResult();
            store.PurchaseAsync(store.OfferFor("aura_sunset")).GetAwaiter().GetResult();
            Require(provider.Purchases == 1 && store.NeedsPurchaseRecovery, "Restoration cannot reopen checkout while the original payment is running.");
            provider.Owned = true;
            provider.Pending.SetResult(true);
            Require(!store.Owns("aura_sunset"), "Late completion alone does not grant ownership.");
            store.RestoreAsync().GetAwaiter().GetResult();
            Require(store.Owns("aura_sunset") && !store.NeedsPurchaseRecovery, "Authoritative restoration recovers a late payment.");

            provider = new TestProvider();
            var oldFetch = new TaskCompletionSource<CommerceSnapshot>();
            provider.FetchOverride = oldFetch.Task;
            store = new CosmeticStore(provider, (duration, cancellation) => Task.CompletedTask);
            store.RefreshAsync().GetAwaiter().GetResult();
            Require(!store.IsBusy, "A stalled fetch does not lock the UI.");
            oldFetch.SetResult(new CommerceSnapshot { OwnedProductIds = new[] { "aura_sunset" } });
            Require(!store.Owns("aura_sunset"), "Late stale fetch results are ignored.");
            provider.FetchOverride = null;
            store.RefreshAsync().GetAwaiter().GetResult();
            Require(store.OfferFor("aura_sunset") != null, "Fetch can be retried after timeout.");

            provider.Pending.SetResult(true);
            provider.FetchOverride = Task.FromCanceled<CommerceSnapshot>(new System.Threading.CancellationToken(true));
            store.PurchaseAsync(store.OfferFor("aura_sunset")).GetAwaiter().GetResult();
            Require(store.NeedsPurchaseRecovery && !store.Status.Contains("결제가 취소"), "A cancelled post-payment fetch must not claim the payment was cancelled.");
        }

        static void CheckScreen()
        {
            var previousArt = Game.Art;
            var previousStore = Game.Cosmetics;
            var previousPlayer = Game.Player;
            var previousUi = Game.UI;
            var canvas = new GameObject("CosmeticChecksCanvas", typeof(RectTransform));
            try
            {
                Game.Art = new SpriteLibrary(16);
                Game.Cosmetics = new CosmeticStore(new UnavailableCommerceProvider());
                Game.Player = null;
                Game.UI = null;
                ((RectTransform)canvas.transform).sizeDelta = new Vector2(1280f, 720f);
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
                var layout = screen.transform.Find("Content/Viewport/Layout");
                var button = layout.Find("Preview/PurchaseOrEquip").GetComponent<UnityEngine.UI.Button>();
                layout.Find("Catalog/List/Items/Product_aura_sunset").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Require(!button.interactable, "Paid purchases are disabled before integration.");
                Require(layout.Find("Preview/Stage/Aura").GetComponent<UnityEngine.UI.Image>().sprite == stars,
                    "Preview uses the same paid artwork as the world renderer.");
                layout.Find("Catalog/List/Items/Product_aura_sky").GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
                Require(button.interactable, "Free appearance remains available.");
                foreach (float scale in UiTheme.UiScales)
                {
                    ((RectTransform)canvas.transform).sizeDelta = new Vector2(1280f, 720f) / scale;
                    screen.Show();
                    Require(layout.localScale == Vector3.one, "UI size settings are never cancelled by shrinking the content.");
                    var list = layout.Find("Catalog").GetComponent<RectTransform>();
                    var detail = layout.Find("Preview").GetComponent<RectTransform>();
                    Require(((RectTransform)layout).rect.width - detail.sizeDelta.x >= list.sizeDelta.x,
                        "Catalog and the fixed preview sit side by side at every supported UI scale.");
                }
                screen.Hide();
                UnityEngine.Object.DestroyImmediate(screen.gameObject);
                var provider = new TestProvider();
                Game.Cosmetics = new CosmeticStore(provider);
                screen = CosmeticShopScreen.Create(canvas.transform);
                screen.Show();
                screen.Navigate(Vector2Int.right);
                screen.SubmitFocused();
                Require(provider.Restores == 1, "Keyboard/gamepad can activate restore.");
                layout = screen.transform.Find("Content/Viewport/Layout");
                var restore = layout.Find("Preview/Restore").GetComponent<CosmeticShopButton>();
                restore.OnSubmit(null);
                Require(provider.Restores == 1, "Unity's native Submit does not duplicate the screen command.");
                screen.Navigate(Vector2Int.left);
                Require(!layout.Find("Preview/Restore/Text").GetComponent<UnityEngine.UI.Text>().text.StartsWith("▶"), "Left returns focus to products.");
                screen.Hide();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(canvas);
                Game.Art = previousArt;
                Game.Cosmetics = previousStore;
                Game.Player = previousPlayer;
                Game.UI = previousUi;
            }
        }

        sealed class TestProvider : ICommerceProvider
        {
            public bool IsAvailable => true;
            public bool Owned;
            public int Purchases;
            public int Restores;
            public Task<CommerceSnapshot> FetchOverride;
            public TaskCompletionSource<bool> Pending = new TaskCompletionSource<bool>();
            public Task<CommerceSnapshot> FetchAsync() => FetchOverride ?? Task.FromResult(new CommerceSnapshot
            {
                OwnedProductIds = Owned ? new[] { "aura_sunset" } : Array.Empty<string>(),
                Offers = new[] { new CosmeticOffer("aura_sunset", "테스트 가격", Guid.NewGuid().ToString()) },
            });
            public Task<CommerceSnapshot> RestoreAsync() { Restores++; return FetchAsync(); }
            public Task PurchaseAsync(CosmeticOffer offer)
            {
                Purchases++;
                return Pending.Task;
            }
        }
    }
}
