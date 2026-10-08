using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>One synthesis rule from the server: 4 spares of a grade -> a chance at the next grade, with a fail ceiling.</summary>
    public sealed class StarSynthRule
    {
        public string from, to;
        public int count, pity, fails;
        public double rate;
    }

    /// <summary>A cosmetic collection set: own every member (or every required set) to register it for its bonus.</summary>
    public sealed class StarCollection
    {
        public string id, name;
        public readonly List<string> members = new List<string>();
        public readonly List<string> requires = new List<string>();
        public int attack, health;
        public bool registered;
    }

    public sealed class StarSynthResult
    {
        public bool success, byPity;
        public string itemId, rarity;
    }

    /// <summary>
    /// The spare side of the cash shop: a cosmetic drawn again becomes a spare (합성 재료) instead of a star refund. Spares
    /// can be synthesized (4 of a grade -> the next grade), dismantled back into 별조각 at the old refund value, and owning a
    /// whole set lets the account register a collection (permanent attack / max HP %, nothing is consumed).
    /// </summary>
    public static partial class StarShopClient
    {
        public static readonly Dictionary<string, int> Copies = new Dictionary<string, int>();
        public static readonly Dictionary<string, int> Dismantle = new Dictionary<string, int>();
        public static readonly List<StarSynthRule> Synth = new List<StarSynthRule>();
        public static readonly List<StarCollection> Collections = new List<StarCollection>();

        /// <summary>Account-wide bonuses of the registered collections (%).</summary>
        public static int CollectionAttack { get; private set; }
        public static int CollectionHealth { get; private set; }

        public static int CopiesOf(string id) => Copies.TryGetValue(id, out int n) ? n : 0;

        /// <summary>Spares of one grade, all cosmetics together (synthesis takes any 4 of them).</summary>
        public static int SparesOf(string rarity)
        {
            int n = 0;
            foreach (var kv in Copies)
            {
                var p = CosmeticCatalog.Find(kv.Key);
                if (p != null && RarityKey(p.Rarity) == rarity) n += kv.Value;
            }
            return n;
        }

        public static string RarityKey(CosmeticRarity r) =>
            r == CosmeticRarity.Unique ? "unique" : r == CosmeticRarity.Epic ? "epic" : r == CosmeticRarity.Rare ? "rare" : "common";

        static void ReadSynth(Dictionary<string, object> d)
        {
            Copies.Clear();
            foreach (var o in MiniJson.Arr(d, "items") ?? new List<object>())
            {
                int n = MiniJson.Int(o, "copies");
                if (n > 0) Copies[MiniJson.Str(o, "id", "")] = n;
            }
            Dismantle.Clear();
            var dis = MiniJson.Obj(d, "dismantle");
            if (dis != null) foreach (var kv in dis) Dismantle[kv.Key] = MiniJson.Int(dis, kv.Key);
            Synth.Clear();
            foreach (var o in MiniJson.Arr(d, "synth") ?? new List<object>())
                Synth.Add(new StarSynthRule
                {
                    from = MiniJson.Str(o, "from", ""), to = MiniJson.Str(o, "to", ""), count = MiniJson.Int(o, "count", 4),
                    rate = MiniJson.Num(o, "rate"), pity = MiniJson.Int(o, "pity"), fails = MiniJson.Int(o, "fails"),
                });
            Collections.Clear();
            CollectionAttack = CollectionHealth = 0;
            foreach (var o in MiniJson.Arr(d, "collections") ?? new List<object>())
            {
                var c = new StarCollection
                {
                    id = MiniJson.Str(o, "id", ""), name = MiniJson.Str(o, "name", ""),
                    attack = MiniJson.Int(o, "attack"), health = MiniJson.Int(o, "health"),
                    registered = Flag(o as Dictionary<string, object>, "registered"),
                };
                foreach (var m in MiniJson.Arr(o, "members") ?? new List<object>()) c.members.Add(m as string ?? "");
                foreach (var m in MiniJson.Arr(o, "requires") ?? new List<object>()) c.requires.Add(m as string ?? "");
                Collections.Add(c);
                if (c.registered) { CollectionAttack += c.attack; CollectionHealth += c.health; }
            }
        }

        /// <summary>Can this set be registered now (every member owned / every required set registered)?</summary>
        public static bool CanRegister(StarCollection c)
        {
            if (c.registered) return false;
            foreach (var m in c.members) if (!Owned.Contains(m)) return false;
            foreach (var s in c.requires)
            {
                var other = Collections.Find(x => x.id == s);
                if (other == null || !other.registered) return false;
            }
            return true;
        }

        /// <summary>Synthesizes up to <paramref name="times"/> times from spares of one grade. done(ok, message, results).</summary>
        public static void DoSynth(string rarity, int times, Action<bool, string, List<StarSynthResult>> done)
        {
            if (!Available) { done?.Invoke(false, "온라인 캐릭터로 접속해야 합니다.", null); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["rarity"] = rarity, ["times"] = times };
            Api.PostIdempotent(Base + "/synth", body, r =>
            {
                if (!r.ok) { done?.Invoke(false, string.IsNullOrEmpty(r.message) ? "합성하지 못했습니다." : r.message, null); return; }
                var list = new List<StarSynthResult>();
                foreach (var o in MiniJson.Arr(r.data, "results") ?? new List<object>())
                    list.Add(new StarSynthResult { success = Flag(o as Dictionary<string, object>, "success"), byPity = Flag(o as Dictionary<string, object>, "by_pity"), itemId = MiniJson.Str(o, "item_id", ""), rarity = MiniJson.Str(o, "rarity", "") });
                _ = RefreshAfter(() => done?.Invoke(true, "", list));
            });
        }

        /// <summary>Turns <paramref name="count"/> spares of one cosmetic into 별조각.</summary>
        public static void DoDismantle(string itemId, int count, Action<bool, string, int> done)
        {
            if (!Available) { done?.Invoke(false, "온라인 캐릭터로 접속해야 합니다.", 0); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["item_id"] = itemId, ["count"] = count };
            Api.PostIdempotent(Base + "/dismantle", body, r =>
            {
                if (!r.ok) { done?.Invoke(false, string.IsNullOrEmpty(r.message) ? "분해하지 못했습니다." : r.message, 0); return; }
                int stars = MiniJson.Int(r.data, "stars");
                _ = RefreshAfter(() => done?.Invoke(true, "", stars));
            });
        }

        /// <summary>Registers a completed collection set.</summary>
        public static void DoRegister(string setId, Action<bool, string> done)
        {
            if (!Available) { done?.Invoke(false, "온라인 캐릭터로 접속해야 합니다."); return; }
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["set_id"] = setId };
            Api.PostIdempotent(Base + "/collection", body, r =>
            {
                if (!r.ok) { done?.Invoke(false, string.IsNullOrEmpty(r.message) ? "등록하지 못했습니다." : r.message); return; }
                _ = RefreshAfter(() => done?.Invoke(true, ""));
            });
        }

        /// <summary>Reloads the shop state (spares, ownership, fails, bonuses), then answers.</summary>
        static async System.Threading.Tasks.Task RefreshAfter(Action then)
        {
            await RefreshAsync();
            _ = Game.Cosmetics?.RefreshAsync();
            then?.Invoke();
        }
    }
}
