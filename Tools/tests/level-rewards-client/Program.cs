using System;
using System.Collections.Generic;
using System.Linq;
using DotRPG;

static class Program
{
    static int changes;
    static int passed, failed;
    static IReadOnlyList<PendingRequest> Requests => ApiClient.Instance.Requests;
    static int Posts => Requests.Count(r => r.Method == "POST");
    static LevelRewardClient.Tier Free(int level = 20) => LevelRewardClient.Tiers.Find(t => t.level == level);
    static LevelRewardClient.PassTier Pass(int level = 20) => LevelRewardClient.PassTiers.Find(t => t.level == level);

    static int Main()
    {
        LevelRewardClient.Changed += () => changes++;
        Run("same-account high/low/high switches invalidate cached eligibility", HighLowSwitch);
        Run("GET reply from prior character cannot overwrite current character", () => StaleGet(false));
        Run("GET reply from prior account cannot overwrite same character ID", () => StaleGet(true));
        Run("GET reply after switch is ignored even before the next Refresh", StaleGetBeforeRefresh);
        foreach (string destination in new[] { "character", "account", "offline" })
            Run("pass POST after " + destination + " switch is ignored before the next Refresh", () => StalePostBeforeRefresh(destination));
        foreach (bool accountSwitch in new[] { false, true })
            foreach (string operation in new[] { "free", "pass", "buy" })
                Run($"stale {operation} POST after {(accountSwitch ? "account" : "character")} switch has no side effects",
                    () => StalePost(accountSwitch, operation));
        foreach (string timing in new[] { "before POST reply", "after POST reply", "after latest GET" })
            Run("old GET cannot reopen claimed tier: " + timing, () => OldGetCannotReopen(timing));
        Run("latest GET wins when refreshes complete out of order", LatestGetWins);
        Run("server claimable flags cannot override current character level", LowLevelFlags);
        Run("claimed flags and pass ownership both gate claims", ClaimedAndOwnershipFlags);
        Run("exact-level free claim uses character endpoint and serializes writes", ExactFreeClaim);
        Run("exact-level pass claim applies its delta and autosaves once", ExactPassClaim);
        Run("failed POST clears busy state and refreshes eligibility", FailedPost);
        Run("offline and no-active-character calls are null-safe and send no requests", Offline);
        Run("null data and wrong or missing character IDs never load eligibility", InvalidReadData);
        Console.WriteLine($"RESULT: {passed} passed, {failed} failed");
        return failed == 0 ? 0 : 1;
    }

    static void Run(string name, Action action)
    {
        OnlineSession.Current = null;
        LevelRewardClient.Reset();
        ApiClient.Instance.Requests.Clear();
        OnlineEconomy.Deltas.Clear();
        StarShopClient.Refreshes = Game.Flow.Autosaves = changes = 0;
        try { action(); passed++; Console.WriteLine("PASS " + name); }
        catch (Exception e) { failed++; Console.WriteLine("FAIL " + name + ": " + e); }
    }

    static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    static void Equal<T>(T expected, T actual, string message) =>
        Require(EqualityComparer<T>.Default.Equals(expected, actual), $"{message}; expected {expected}, actual {actual}");

    static PendingRequest Last(string method) => Requests.Last(r => r.Method == method);
    static void IgnoreDone(bool ok, string message) { }

    static void Enter(string character = "high") => OnlineSession.Current = new OnlineSession { ActiveCharacter = character };

    static ApiResult Snapshot(string character, int level, bool claimed = false, bool passOwned = true)
    {
        // Parse actual JSON, matching the numeric representation produced by ApiClient.
        string json = "{\"character_id\":\"" + character + "\",\"character_level\":" + level +
            ",\"stars\":300,\"pass_price\":9000,\"pass_owned\":" + passOwned.ToString().ToLowerInvariant() +
            ",\"tiers\":[{\"level\":20,\"stars\":300,\"claimable\":true,\"claimed\":" + claimed.ToString().ToLowerInvariant() +
            "},{\"level\":50,\"stars\":600,\"claimable\":true,\"claimed\":false}]," +
            "\"pass_tiers\":[{\"level\":20,\"claimable\":true,\"claimed\":" + claimed.ToString().ToLowerInvariant() +
            ",\"rewards\":[{\"item_key\":\"test_item\",\"count\":2}]},{\"level\":50,\"claimable\":true,\"claimed\":false,\"rewards\":[]}]," +
            "\"delta\":{\"test_marker\":\"reward\"}}";
        return new ApiResult { ok = true, data = (Dictionary<string, object>)MiniJson.Parse(json) };
    }

    static void Load(string character = "high", int level = 50, bool claimed = false, bool passOwned = true)
    {
        LevelRewardClient.Refresh();
        Last("GET").Reply(Snapshot(character, level, claimed, passOwned));
        Require(LevelRewardClient.Loaded, "snapshot must load");
    }

    static void HighLowSwitch()
    {
        Enter();
        Load();
        var oldFree = Free();
        var oldPass = Pass();
        Require(LevelRewardClient.CanClaim(oldFree) && LevelRewardClient.CanClaim(oldPass), "high character can claim");
        OnlineSession.Current.ActiveCharacter = "low"; // Same session, no external Reset: guard must still work.
        Require(!LevelRewardClient.Loaded && !LevelRewardClient.AnyClaimable, "high cache immediately becomes unusable");
        Require(!LevelRewardClient.CanClaim(oldFree) && !LevelRewardClient.CanClaim(oldPass), "old tiers cannot claim");
        LevelRewardClient.Refresh();
        Equal("/characters/low/level-rewards", Last("GET").Path, "refresh uses low character");
        Equal(0, LevelRewardClient.Tiers.Count, "switch clears old free tiers");
        Equal(0, LevelRewardClient.PassTiers.Count, "switch clears old pass tiers");
        Last("GET").Reply(Snapshot("low", 1));
        Equal(1, LevelRewardClient.CharacterLevel, "cache contains low level");
        Require(!LevelRewardClient.AnyClaimable, "low character cannot inherit high eligibility");
        OnlineSession.Current.ActiveCharacter = "high";
        Load("high", 50, claimed: true);
        Require(Free().claimed && Pass().claimed, "account claim history is reflected after switching back");
        Require(!LevelRewardClient.CanClaim(Free()) && !LevelRewardClient.CanClaim(Pass()), "claimed tiers remain blocked");
        Require(LevelRewardClient.CanClaim(Free(50)), "new high snapshot restores its own eligible tier");
    }

    static void StaleGet(bool accountSwitch)
    {
        Enter();
        LevelRewardClient.Refresh();
        var old = Last("GET");
        string next = accountSwitch ? "high" : "low";
        if (accountSwitch) Enter(next); else OnlineSession.Current.ActiveCharacter = next;
        LevelRewardClient.Refresh();
        Last("GET").Reply(Snapshot(next, 1));
        int before = changes;
        old.Reply(Snapshot("high", 100));
        Equal(before, changes, "stale GET cannot publish Changed");
        Equal(1, LevelRewardClient.CharacterLevel, "stale GET cannot replace the new level");
        Require(LevelRewardClient.Loaded && !LevelRewardClient.AnyClaimable, "new low-level cache remains authoritative");
    }

    static void StaleGetBeforeRefresh()
    {
        Enter();
        LevelRewardClient.Refresh();
        OnlineSession.Current.ActiveCharacter = "low";
        int before = changes;
        Last("GET").Reply(Snapshot("high", 100));
        Equal(before, changes, "context guard ignores reply without needing another refresh");
        Require(!LevelRewardClient.Loaded && !LevelRewardClient.AnyClaimable, "stale GET cannot create usable cache");
    }

    static void StalePost(bool accountSwitch, string operation)
    {
        Enter();
        Load();
        int oldDone = 0;
        Action<bool, string> done = (ok, text) => oldDone++;
        if (operation == "free") LevelRewardClient.Claim(20, done);
        else if (operation == "pass") LevelRewardClient.ClaimPass(20, done);
        else LevelRewardClient.BuyPass(done);
        var old = Last("POST");
        Require(LevelRewardClient.Busy, "old POST starts busy");
        string next = accountSwitch ? "high" : "low";
        if (accountSwitch) Enter(next); else OnlineSession.Current.ActiveCharacter = next;
        Load(next, 20);
        LevelRewardClient.Claim(20, IgnoreDone); // A distinct new write must remain busy after the old reply.
        int before = changes, requestCount = Requests.Count;
        old.Reply(Snapshot("high", 50, claimed: true));
        Equal(0, oldDone, "stale success callback must not reach the new UI");
        Equal(0, OnlineEconomy.Deltas.Count, "stale reward must not enter the current bag");
        Equal(0, Game.Flow.Autosaves, "stale reward must not trigger autosave");
        Equal(0, StarShopClient.Refreshes, "stale write must not refresh the current wallet");
        Equal(requestCount, Requests.Count, "stale write must not issue a refresh for the new context");
        Equal(before, changes, "stale write must not publish Changed");
        Require(LevelRewardClient.Busy, "stale write must not clear the new write's busy flag");
    }

    static void StalePostBeforeRefresh(string destination)
    {
        Enter();
        Load();
        int doneCount = 0;
        LevelRewardClient.ClaimPass(20, (ok, text) => doneCount++);
        var old = Last("POST");
        if (destination == "character") OnlineSession.Current.ActiveCharacter = "low";
        else if (destination == "account") Enter("high");
        else OnlineSession.Current = null;
        int before = changes, requestCount = Requests.Count;
        old.Reply(Snapshot("high", 50, claimed: true));
        Equal(0, doneCount, "old completion is not reported in a changed context");
        Equal(0, OnlineEconomy.Deltas.Count, "context guard blocks stale inventory delta");
        Equal(0, Game.Flow.Autosaves, "context guard blocks stale autosave");
        Equal(0, StarShopClient.Refreshes, "context guard blocks stale wallet refresh");
        Equal(before, changes, "context guard blocks stale change event");
        Equal(requestCount, Requests.Count, "context guard blocks stale follow-up request");
        Require(!LevelRewardClient.Loaded && !LevelRewardClient.AnyClaimable, "old receipt cannot unlock new context");
    }

    static void OldGetCannotReopen(string timing)
    {
        Enter();
        Load();
        LevelRewardClient.Refresh();
        var old = Last("GET");
        LevelRewardClient.ClaimPass(20, IgnoreDone);
        Require(!LevelRewardClient.AnyClaimable, "claim disables other claim buttons while pending");
        if (timing == "before POST reply")
        {
            old.Reply(Snapshot("high", 50));
            Require(!LevelRewardClient.Loaded, "old GET cannot restore loaded state during write");
        }
        Last("POST").Reply(Snapshot("high", 50, claimed: true));
        if (timing == "after POST reply")
        {
            old.Reply(Snapshot("high", 50));
            Require(Pass().claimed && Free().claimed, "old GET cannot reopen tier even before fresh GET completes");
            Require(!LevelRewardClient.CanClaim(Pass()) && !LevelRewardClient.CanClaim(Free()), "claim stays disabled between replies");
        }
        Last("GET").Reply(Snapshot("high", 50, claimed: true));
        if (timing == "after latest GET") old.Reply(Snapshot("high", 50));
        Require(Pass().claimed && !LevelRewardClient.CanClaim(Pass()), "old GET must never reopen awarded pass tier");
        Require(Free().claimed && !LevelRewardClient.CanClaim(Free()), "old GET must never reopen awarded free tier");
        Equal(1, OnlineEconomy.Deltas.Count, "accepted pass reward applies once");
    }

    static void LatestGetWins()
    {
        Enter();
        LevelRewardClient.Refresh();
        var older = Last("GET");
        LevelRewardClient.Refresh();
        Last("GET").Reply(Snapshot("high", 20, claimed: true));
        older.Reply(Snapshot("high", 100));
        Equal(20, LevelRewardClient.CharacterLevel, "newer response owns the level");
        Require(Free().claimed && Pass().claimed, "newer response owns claim history");
    }

    static void LowLevelFlags()
    {
        Enter("low");
        Load("low", 19);
        Require(Free().claimable && Pass().claimable, "fixture intentionally has inconsistent server flags");
        Require(!LevelRewardClient.CanClaim(Free()) && !LevelRewardClient.CanClaim(Pass()), "character level independently gates both kinds");
        int rejected = 0;
        Action<bool, string> done = (ok, text) => { Require(!ok, "under-level claim must fail locally"); rejected++; };
        LevelRewardClient.Claim(20, done);
        LevelRewardClient.ClaimPass(20, done);
        Equal(2, rejected, "both callers get a failure");
        Equal(0, Posts, "no under-level claim is posted");
    }

    static void ClaimedAndOwnershipFlags()
    {
        Enter();
        Load(claimed: true);
        Require(!LevelRewardClient.CanClaim(Free()) && !LevelRewardClient.CanClaim(Pass()), "claimed takes precedence over claimable");
        LevelRewardClient.Claim(20, IgnoreDone);
        LevelRewardClient.ClaimPass(20, IgnoreDone);
        Equal(0, Posts, "already claimed tiers are rejected locally");
        Load(passOwned: false);
        Require(!LevelRewardClient.CanClaim(Pass()) && LevelRewardClient.CanClaim(Free()), "pass ownership only gates pass rewards");
        LevelRewardClient.ClaimPass(20, IgnoreDone);
        Equal(0, Posts, "unowned pass cannot be claimed");
    }

    static void AssertPost(string path, int level)
    {
        Equal(path, Last("POST").Path, "character-scoped claim endpoint");
        Equal(level, Convert.ToInt32(Last("POST").Body["level"]), "exact requested level in body");
        Require(Last("POST").Body["request_id"] is string id && !string.IsNullOrEmpty(id), "write includes request ID");
        Require(LevelRewardClient.Busy && !LevelRewardClient.AnyClaimable, "write locks claim buttons");
    }

    static void ExactFreeClaim()
    {
        Enter("exact");
        Load("exact", 20);
        Require(LevelRewardClient.CanClaim(Free()), "exact threshold is eligible");
        int accepted = 0, denied = 0;
        LevelRewardClient.Claim(20, (ok, text) => { Require(ok, "successful receipt"); accepted++; });
        AssertPost("/characters/exact/level-rewards/claim", 20);
        LevelRewardClient.Claim(20, (ok, text) => { Require(!ok, "concurrent write denied"); denied++; });
        Equal(1, Posts, "only one write is sent");
        Equal(1, denied, "concurrent caller is notified");
        Last("POST").Reply(Snapshot("exact", 20, claimed: true));
        Equal(1, accepted, "accepted completion invoked once");
        Equal(1, StarShopClient.Refreshes, "free claim refreshes wallet");
        Equal(0, OnlineEconomy.Deltas.Count, "free wallet reward does not apply an inventory delta");
        Require(!LevelRewardClient.Busy && Free().claimed, "successful receipt marks claim as taken");
    }

    static void ExactPassClaim()
    {
        Enter("exact");
        Load("exact", 20);
        int accepted = 0;
        LevelRewardClient.ClaimPass(20, (ok, text) => { Require(ok, "successful pass receipt"); accepted++; });
        AssertPost("/characters/exact/level-rewards/pass/claim", 20);
        Last("POST").Reply(Snapshot("exact", 20, claimed: true));
        Equal(1, accepted, "pass callback invoked once");
        Equal(1, OnlineEconomy.Deltas.Count, "pass delta applied once");
        Equal("reward", MiniJson.Str(OnlineEconomy.Deltas[0], "test_marker"), "delta came from accepted response");
        Equal(1, Game.Flow.Autosaves, "inventory change autosaved once");
        Equal(1, StarShopClient.Refreshes, "pass claim refreshes wallet once");
        Require(!LevelRewardClient.Busy && Pass().claimed, "pass receipt closes awarded tier");
    }

    static void FailedPost()
    {
        Enter();
        Load();
        int denied = 0;
        LevelRewardClient.ClaimPass(20, (ok, text) => { Require(!ok, "error reported to caller"); denied++; });
        int before = Requests.Count;
        Last("POST").Reply(new ApiResult { ok = false, code = "LEVEL_NOT_REACHED" });
        Equal(1, denied, "one failure callback");
        Require(!LevelRewardClient.Busy && !LevelRewardClient.Loaded, "failed claim requires fresh eligibility");
        Equal(before + 1, Requests.Count, "failed write fetches eligibility again");
        Equal(0, OnlineEconomy.Deltas.Count, "failed claim has no inventory delta");
        Last("GET").Reply(Snapshot("high", 1));
        Require(LevelRewardClient.Loaded && !LevelRewardClient.AnyClaimable, "refresh resolves server rejection to blocked eligibility");
    }

    static void Offline()
    {
        for (int variant = 0; variant < 2; variant++)
        {
            OnlineSession.Current = variant == 0 ? null : new OnlineSession();
            LevelRewardClient.Refresh();
            Require(!LevelRewardClient.Loaded && !LevelRewardClient.AnyClaimable, "offline cache is disabled");
            Require(!LevelRewardClient.CanClaim((LevelRewardClient.Tier)null), "null free tier is safe");
            Require(!LevelRewardClient.CanClaim((LevelRewardClient.PassTier)null), "null pass tier is safe");
            int denied = 0;
            Action<bool, string> done = (ok, text) => { Require(!ok, "offline action rejected"); denied++; };
            LevelRewardClient.Claim(20, done);
            LevelRewardClient.ClaimPass(20, done);
            LevelRewardClient.BuyPass(done);
            Equal(3, denied, "all offline actions notify caller");
            Equal(0, Requests.Count, "offline actions issue no request");
        }
        Enter();
        Load();
        Require(!LevelRewardClient.CanClaim((LevelRewardClient.Tier)null), "null free tier is safe while loaded");
        Require(!LevelRewardClient.CanClaim((LevelRewardClient.PassTier)null), "null pass tier is safe while loaded");
    }

    static void InvalidReadData()
    {
        Enter();
        LevelRewardClient.Refresh();
        Last("GET").Reply(new ApiResult { ok = true, data = null });
        Require(!LevelRewardClient.Loaded, "null data is not usable eligibility");
        LevelRewardClient.Refresh();
        Last("GET").Reply(Snapshot("other", 100));
        Require(!LevelRewardClient.Loaded, "another character's response is rejected");
        LevelRewardClient.Refresh();
        var legacy = Snapshot("high", 100);
        legacy.data.Remove("character_id");
        Last("GET").Reply(legacy);
        Require(!LevelRewardClient.Loaded && !LevelRewardClient.AnyClaimable, "legacy account-wide response cannot unlock rewards");
        Equal(0, LevelRewardClient.Tiers.Count, "invalid responses create no free tiers");
        Equal(0, LevelRewardClient.PassTiers.Count, "invalid responses create no pass tiers");
    }
}
