using System;
using System.Threading.Tasks;

namespace DotRPG
{
    public sealed class CosmeticOffer
    {
        public string ProductId { get; }
        public string LocalizedPrice { get; }
        public string QuoteId { get; }

        public CosmeticOffer(string productId, string localizedPrice, string quoteId)
        {
            ProductId = productId;
            LocalizedPrice = localizedPrice;
            QuoteId = quoteId;
        }
    }

    public sealed class CommerceSnapshot
    {
        public string[] OwnedProductIds = Array.Empty<string>();
        public CosmeticOffer[] Offers = Array.Empty<CosmeticOffer>();
    }

    /// <summary>
    /// A platform adapter must authenticate the account and fetch verified server entitlements.
    /// PurchaseAsync finishes only after receipt/order verification, or throws on cancel/failure.
    /// Quote IDs bind the displayed price and product; expired or changed quotes must be rejected.
    /// RestoreAsync invokes platform restoration and returns the full current entitlement snapshot.
    /// Never implement grants from local preferences or a payment-overlay close callback.
    /// </summary>
    public interface ICommerceProvider
    {
        bool IsAvailable { get; }
        Task<CommerceSnapshot> FetchAsync();
        Task PurchaseAsync(CosmeticOffer offer);
        Task<CommerceSnapshot> RestoreAsync();
    }

    public sealed class UnavailableCommerceProvider : ICommerceProvider
    {
        public bool IsAvailable => false;
        public Task<CommerceSnapshot> FetchAsync() => Task.FromResult(new CommerceSnapshot());
        public Task<CommerceSnapshot> RestoreAsync() => Task.FromResult(new CommerceSnapshot());
        public Task PurchaseAsync(CosmeticOffer offer) =>
            Task.FromException(new InvalidOperationException("Payment provider is not configured."));
    }
}
