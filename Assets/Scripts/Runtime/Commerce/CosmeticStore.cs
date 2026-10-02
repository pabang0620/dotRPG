using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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

        public event Action Changed;
        public bool IsBusy { get; private set; }
        public bool IsAvailable => provider.IsAvailable;
        public string Status { get; private set; } = "";
        public CosmeticProduct Equipped => Owns(selectedId) ? CosmeticCatalog.Find(selectedId) : CosmeticCatalog.All[0];

        public CosmeticStore(ICommerceProvider provider)
        {
            this.provider = provider ?? throw new ArgumentNullException(nameof(provider));
            selectedId = PlayerPrefs.GetString(SelectionKey, CosmeticCatalog.All[0].Id);
        }

        public bool Owns(string id) => CosmeticCatalog.Find(id) is CosmeticProduct product && (product.IsFree || owned.Contains(id));
        public CosmeticOffer OfferFor(string id) => id != null && offers.TryGetValue(id, out var offer) ? offer : null;

        public bool Equip(string id)
        {
            if (!Owns(id)) return false;
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
                Status = "상점 준비 중 · 무료 오라를 먼저 착용해 보세요.";
                Changed?.Invoke();
                return;
            }
            Begin("보유 상품을 확인하고 있습니다.");
            try
            {
                Apply(await (restore ? provider.RestoreAsync() : provider.FetchAsync()));
                Status = restore ? "구매 내역을 복원했습니다." : "보유 상품을 확인했습니다.";
            }
            catch (Exception exception)
            {
                // Do not retain stale purchase quotes after a failed synchronization.
                offers.Clear();
                Status = "상점에 연결하지 못했습니다. 잠시 후 다시 시도해 주세요.";
                Debug.LogWarning($"[dotRPG] Cosmetic store synchronization failed: {exception.GetType().Name}");
            }
            finally { End(); }
        }

        public async Task PurchaseAsync(CosmeticOffer confirmedOffer)
        {
            if (IsBusy || !IsAvailable || confirmedOffer == null) return;
            string id = confirmedOffer.ProductId;
            if (Owns(id) || !ReferenceEquals(OfferFor(id), confirmedOffer)) return;
            Begin("결제를 확인하고 있습니다. 완료될 때까지 기다려 주세요.");
            try
            {
                await provider.PurchaseAsync(confirmedOffer);
                Apply(await provider.FetchAsync());
                Status = Owns(id) ? "구매한 외형을 옷장에서 착용할 수 있습니다." : "구매 확인 중입니다. 구매 내역 복원을 눌러 주세요.";
            }
            catch (OperationCanceledException)
            {
                Status = "결제가 취소되었습니다.";
            }
            catch (Exception exception)
            {
                Status = "구매 상태를 확인하지 못했습니다. 재구매 전에 구매 내역을 복원해 주세요.";
                Debug.LogWarning($"[dotRPG] Cosmetic purchase failed: {exception.GetType().Name}");
            }
            finally
            {
                // A quote is single-use even when the result is uncertain.
                offers.Remove(id);
                End();
            }
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
