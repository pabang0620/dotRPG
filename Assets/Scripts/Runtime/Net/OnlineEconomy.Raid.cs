using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>[SERVER] Raid rewards (Docs/server/phase13_raid_rewards.md §6.4): one card flipped per request, and the raid shop.</summary>
    public static partial class OnlineEconomy
    {
        // ---------------- raid cards (take all) ----------------

        /// <summary>One request_id per (run, card number), so pressing a card again after a lost answer repeats the same request.</summary>
        static readonly Dictionary<string, string> flipRequestIds = new Dictionary<string, string>();

        static RewardCard? CardOf(Dictionary<string, object> card) =>
            card == null ? (RewardCard?)null : new RewardCard(MiniJson.Str(card, "item_key"), MiniJson.Int(card, "count"));

        /// <summary>
        /// Flips card <paramref name="index"/> of the current raid run on the server. done(card or null). A card the server
        /// already gave (409 CARD_ALREADY_FLIPPED, e.g. by its timer) is not a failure: its content comes back too.
        /// </summary>
        public static void FlipRaidCard(int index, Action<RewardCard?> done)
        {
            if (!On || RunId == null) { done?.Invoke(null); return; }
            string key = RunId + ":" + index;
            if (!flipRequestIds.TryGetValue(key, out string requestId)) flipRequestIds[key] = requestId = ApiClient.NewRequestId();
            FlipRaid(RunId, index, requestId, false, (card, fresh) =>
            {
                if (card.HasValue) flipRequestIds.Remove(key);
                done?.Invoke(card);
            });
        }

        /// <summary>done(card, true if this request gave it). A refusal other than "already flipped" gives (null, false).</summary>
        static void FlipRaid(string run, int index, string requestId, bool quiet, Action<RewardCard?, bool> done)
        {
            var body = Body(("index", index), ("request_id", requestId));
            Api.PostIdempotent($"{Char}/dungeon-runs/{run}/cards/pick", body, r =>
            {
                if (r.ok)
                {
                    ApplyDelta(MiniJson.Obj(r.data, "delta"));
                    done?.Invoke(CardOf(MiniJson.Obj(r.data, "card")), true);
                    return;
                }
                if (r.code == "CARD_ALREADY_FLIPPED")
                {
                    var card = CardOf(MiniJson.Obj(r.errors, "card") ?? MiniJson.Obj(MiniJson.Obj(r.errors, "extra"), "card"));
                    // No delta comes with a 409: show it in the bag too (the next server answer overwrites the stack anyway).
                    if (card.HasValue && Game.Session != null && !quiet) Game.Session.Inventory.Add(card.Value.itemId, card.Value.count);
                    done?.Invoke(card, false);
                    return;
                }
                if (!quiet) GameEvents.RaiseToast(string.IsNullOrEmpty(r.message) ? "카드를 받지 못했습니다." : r.message);
                done?.Invoke(null, false);
            });
        }

        /// <summary>A raid run that ended while the player was away: takes every card still face down (0..3 in order), one summary toast.</summary>
        static void TakeRemainingRaidCards(string run, int index, int remaining, List<RewardCard> got)
        {
            if (index >= DungeonRewards.CardCount || got.Count >= remaining || !On)
            {
                if (got.Count == 0) return;
                var labels = new List<string>();
                foreach (var c in got) labels.Add(c.Label);
                GameEvents.RaiseToast($"확정된 레이드 보상: {string.Join(", ", labels)}");
                return;
            }
            FlipRaid(run, index, ApiClient.NewRequestId(), true, (card, fresh) =>
            {
                if (fresh && card.HasValue) got.Add(card.Value);
                TakeRemainingRaidCards(run, index + 1, remaining, got);
            });
        }

        // ---------------- raid shop ----------------

        public sealed class RaidShopOutcome
        {
            public string rarity, itemKey;
            public int percent;
        }

        public sealed class RaidShopOffer
        {
            public string id, name, raidId, materialKey, materialName;
            public int tierLevel, price, have;
            public bool affordable;
            public readonly List<RaidShopOutcome> outcomes = new List<RaidShopOutcome>();
        }

        /// <summary>GET raid-shop: prices, the owned material and the result table of my class. done(offers) or null.</summary>
        public static void LoadRaidShop(Action<List<RaidShopOffer>> done)
        {
            if (!On) { done?.Invoke(null); return; }
            Api.Get(Char + "/raid-shop", r =>
            {
                if (!r.ok) { done?.Invoke(null); return; }
                var list = new List<RaidShopOffer>();
                foreach (var p in MiniJson.Arr(r.data, "products") ?? new List<object>())
                {
                    var mat = MiniJson.Obj(p, "material");
                    var offer = new RaidShopOffer
                    {
                        id = MiniJson.Str(p, "id"), name = MiniJson.Str(p, "name"), raidId = MiniJson.Str(p, "raid_id"), tierLevel = MiniJson.Int(p, "tier_level"),
                        materialKey = MiniJson.Str(mat, "item_key"), materialName = MiniJson.Str(mat, "name"), price = MiniJson.Int(mat, "price"), have = MiniJson.Int(mat, "have"),
                        affordable = p is Dictionary<string, object> pd && pd.TryGetValue("affordable", out var af) && af is bool ab && ab,
                    };
                    foreach (var o in MiniJson.Arr(p, "outcomes") ?? new List<object>())
                        offer.outcomes.Add(new RaidShopOutcome { rarity = MiniJson.Str(o, "rarity"), percent = MiniJson.Int(o, "percent"), itemKey = MiniJson.Str(o, "item_key") });
                    list.Add(offer);
                }
                done?.Invoke(list);
            });
        }

        /// <summary>POST raid-shop/buy (one request_id per purchase, resent unchanged after a lost answer). done(item key, rarity) or (null, null).</summary>
        public static void BuyRaidShop(string productId, Action<string, string> done)
        {
            if (!On) { done?.Invoke(null, null); return; }
            var body = Body(("product_id", productId), ("request_id", ApiClient.NewRequestId()));
            Api.PostIdempotent(Char + "/raid-shop/buy", body, r =>
            {
                if (!r.ok)
                {
                    GameEvents.RaiseToast(string.IsNullOrEmpty(r.message) ? "구매하지 못했습니다." : r.message);
                    done?.Invoke(null, null);
                    return;
                }
                ApplyDelta(MiniJson.Obj(r.data, "delta"));
                done?.Invoke(MiniJson.Str(r.data, "item_key"), MiniJson.Str(r.data, "rarity"));
            });
        }
    }
}
