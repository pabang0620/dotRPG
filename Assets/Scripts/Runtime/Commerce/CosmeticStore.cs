using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Account entitlements stay in memory; only the preferred appearance is stored locally.</summary>
    public sealed class CosmeticStore
    {
        const string SelectionKey = "dotRPG.cosmetic.aura";
        readonly ICommerceProvider provider;
        readonly HashSet<string> owned = new HashSet<string>();
        readonly Dictionary<string, CosmeticOffer> offers = new Dictionary<string, CosmeticOffer>();
        string selectedId;
        readonly Func<TimeSpan, CancellationToken, Task> delay;
        Task pendingPurchase;
        string pendingProductId;

        public event Action Changed;
        public bool IsBusy { get; private set; }
        public bool IsAvailable => provider.IsAvailable;
        public bool NeedsPurchaseRecovery => pendingProductId != null;
        public string Status { get; private set; } = "";
        public CosmeticProduct Equipped => Owns(selectedId) && !CosmeticCatalog.Find(selectedId).IsSkin ? CosmeticCatalog.Find(selectedId) : CosmeticCatalog.All[0];

        static string SkinKey(CharacterClass cls) => "dotRPG.cosmetic.skin." + cls;

        /// <summary>The costume skin worn by this class (null = the class's own look). Only owned skins count.</summary>
        public string SkinFor(CharacterClass cls)
        {
            string id = PlayerPrefs.GetString(SkinKey(cls), "");
            var p = CosmeticCatalog.Find(id);
            return p != null && p.IsSkin && p.Skin.cls == cls && Owns(id) ? id : null;
        }

        /// <summary>Takes the skin of a class off (back to its own look).</summary>
        public void RemoveSkin(CharacterClass cls)
        {
            PlayerPrefs.SetString(SkinKey(cls), "");
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        public CosmeticStore(ICommerceProvider provider, Func<TimeSpan, CancellationToken, Task> delay = null)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            this.delay = delay ?? Task.Delay;
            selectedId = PlayerPrefs.GetString(SelectionKey, CosmeticCatalog.All[0].Id);
        }

        public bool Owns(string id) => CosmeticCatalog.Find(id) is CosmeticProduct product && (product.IsFree || owned.Contains(id));
        public CosmeticOffer OfferFor(string id) => id != null && offers.TryGetValue(id, out var offer) ? offer : null;

        public bool Equip(string id)
        {
            if (!Owns(id)) return false;
            var skin = CosmeticCatalog.Find(id).Skin;
            if (skin != null)
            {
                PlayerPrefs.SetString(SkinKey(skin.cls), id);
                PlayerPrefs.Save();
                Changed?.Invoke();
                return true;
            }
            selectedId = id;
            PlayerPrefs.SetString(SelectionKey, id);
            PlayerPrefs.Save();
            Changed?.Invoke();
            return true;
        }

        public Task RefreshAsync() => SynchronizeAsync(false);
        public Task RestoreAsync() => SynchronizeAsync(true);

        async Task SynchronizeAsync(bool restore)
        {
            if (IsBusy) return;
            if (!IsAvailable)
            {
                // Paid auras are the online account's; offline only the free ones show.
                owned.Clear();
                offers.Clear();
                Status = "온라인 캐릭터로 접속하면 별조각 외형을 쓸 수 있습니다.";
                Changed?.Invoke();
                return;
            }
            Begin("보유 외형을 확인하고 있습니다.");
            try
            {
                var request = restore ? provider.RestoreAsync() : provider.FetchAsync();
                await WaitAsync(request, TimeSpan.FromSeconds(30));
                Apply(await request);
                // A timed-out payment may still finish. Do not reopen checkout while it is running.
                if (NeedsPurchaseRecovery && (Owns(pendingProductId) || pendingPurchase == null || pendingPurchase.IsCompleted))
                    pendingProductId = null;
                Status = NeedsPurchaseRecovery ? "이전 결제 확인 중입니다. 잠시 후 구매 내역을 다시 복원해 주세요."
                    : restore ? "보유 외형을 다시 불러왔습니다." : "보유 외형을 확인했습니다.";
            }
            catch (Exception exception)
            {
                // Do not retain stale purchase quotes after a failed synchronization.
                offers.Clear();
                Status = "캐시샵에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요.";
                Debug.LogWarning($"[dotRPG] Cosmetic store synchronization failed: {exception.GetType().Name}");
            }
            finally { End(); }
        }

        public async Task PurchaseAsync(CosmeticOffer confirmedOffer)
        {
            if (IsBusy || NeedsPurchaseRecovery || !IsAvailable || confirmedOffer == null) return;
            string id = confirmedOffer.ProductId;
            if (Owns(id) || !ReferenceEquals(OfferFor(id), confirmedOffer)) return;
            Begin("교환을 확인하고 있습니다. 완료될 때까지 기다려 주세요.");
            pendingProductId = id;
            bool paymentVerified = false;
            try
            {
                pendingPurchase = provider.PurchaseAsync(confirmedOffer);
                await WaitAsync(pendingPurchase, TimeSpan.FromMinutes(2));
                paymentVerified = true;
                var request = provider.FetchAsync();
                await WaitAsync(request, TimeSpan.FromSeconds(30));
                Apply(await request);
                if (Owns(id)) pendingProductId = null;
                Status = Owns(id) ? "교환한 외형을 바로 착용할 수 있습니다." : "교환 확인 중입니다. 보유 외형 다시 불러오기를 눌러 주세요.";
            }
            catch (OperationCanceledException)
            {
                if (!paymentVerified) pendingProductId = null;
                Status = paymentVerified ? "결제 후 보유 내역 확인이 중단되었습니다. 구매 내역을 복원해 주세요." : "결제가 취소되었습니다.";
            }
            catch (Exception exception)
            {
                Status = "교환 상태를 확인하지 못했습니다. 보유 외형 다시 불러오기를 눌러 주세요.";
                Debug.LogWarning($"[dotRPG] Cosmetic purchase failed: {exception.GetType().Name}");
            }
            finally
            {
                // A quote is single-use even when the result is uncertain.
                offers.Remove(id);
                End();
            }
        }

        async Task WaitAsync(Task request, TimeSpan timeout)
        {
            using (var cancellation = new CancellationTokenSource())
            {
                try
                {
                    await Task.WhenAny(request, delay(timeout, cancellation.Token));
                    if (!request.IsCompleted)
                    {
                        // Observe a late failure without applying stale results or cancelling a real charge.
                        _ = ObserveLateCompletionAsync(request);
                        throw new TimeoutException();
                    }
                    await request;
                }
                finally { cancellation.Cancel(); }
            }
        }

        static async Task ObserveLateCompletionAsync(Task request)
        {
            try { await request; }
            catch (Exception) { /* The recovery request reports the authoritative outcome. */ }
        }

        void Apply(CommerceSnapshot snapshot)
        {
            if (snapshot == null || snapshot.OwnedProductIds == null || snapshot.Offers == null)
                throw new InvalidOperationException("Incomplete entitlement snapshot.");
            owned.Clear();
            foreach (string id in snapshot.OwnedProductIds)
                if (CosmeticCatalog.Find(id) != null) owned.Add(id);
            offers.Clear();
            foreach (var offer in snapshot.Offers)
            {
                var product = offer == null ? null : CosmeticCatalog.Find(offer.ProductId);
                if (product == null || product.IsFree || string.IsNullOrWhiteSpace(offer.LocalizedPrice)
                    || string.IsNullOrWhiteSpace(offer.QuoteId)) continue;
                offers[product.Id] = offer;
            }
        }

        void Begin(string message)
        {
            IsBusy = true;
            Status = message;
            Changed?.Invoke();
        }

        void End()
        {
            IsBusy = false;
            Changed?.Invoke();
        }
    }
}
