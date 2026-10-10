using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// Only Unity/network boundaries are replaced. The runner compiles the repository's
// actual LevelRewardClient.cs and MiniJson.cs, and no request reaches a server.
namespace DotRPG
{
    public sealed class OnlineSession
    {
        public static OnlineSession Current;
        public static bool Playing => Current != null && Current.ActiveCharacter != null;
        public string ActiveCharacter;
    }

    public sealed class ApiResult
    {
        public bool ok;
        public string code, message;
        public Dictionary<string, object> data;
    }

    public sealed class PendingRequest
    {
        public string Method, Path;
        public Dictionary<string, object> Body;
        public Action<ApiResult> Callback;
        public bool Completed;

        public void Reply(ApiResult result)
        {
            if (Completed) throw new InvalidOperationException("Request already completed: " + Path);
            Completed = true;
            Callback(result);
        }
    }

    public sealed class ApiClient
    {
        public static readonly ApiClient Instance = new ApiClient();
        public readonly List<PendingRequest> Requests = new List<PendingRequest>();
        static int nextRequest;
        public static string NewRequestId() => "test-request-" + (++nextRequest);
        public void Get(string path, Action<ApiResult> done) =>
            Requests.Add(new PendingRequest { Method = "GET", Path = path, Callback = done });
        public void Post(string path, object body, Action<ApiResult> done) =>
            Requests.Add(new PendingRequest { Method = "POST", Path = path,
                Body = new Dictionary<string, object>((Dictionary<string, object>)body), Callback = done });
    }

    public static class OnlineEconomy
    {
        public static readonly List<Dictionary<string, object>> Deltas = new List<Dictionary<string, object>>();
        public static void ApplyDelta(Dictionary<string, object> delta) => Deltas.Add(delta);
    }

    public static class StarShopClient
    {
        public static int Refreshes;
        public static Task RefreshAsync() { Refreshes++; return Task.CompletedTask; }
    }

    public static class Game
    {
        public static readonly FlowStub Flow = new FlowStub();
    }

    public sealed class FlowStub
    {
        public int Autosaves;
        public void Autosave() => Autosaves++;
    }
}
