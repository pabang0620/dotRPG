using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [TEST TOOLS] Test-server shortcuts (Docs/PLAN_TEST_TOOLS_2026-10-29.md): finish the next main quest (or every main quest of
    /// the open chapters) and raise the level by one (or to the cap). The server decides everything and only answers on a test
    /// server for a dev-login account; /meta's test_tools tells the client whether to show the buttons at all.
    /// </summary>
    public static class TestTools
    {
        static bool serverSays;

        /// <summary>Reads test_tools from a /meta answer (called before every login, like <see cref="Chapter3.ReadMeta"/>).</summary>
        public static void ReadMeta(Dictionary<string, object> meta) =>
            serverSays = meta != null && meta.TryGetValue("test_tools", out var on) && on is bool b && b;

        /// <summary>Show the test tools entry: an online character on a server that says it is a test server.</summary>
        public static bool Visible => OnlineSession.Playing && (serverSays || ApiClient.Instance.BaseUrl.TrimEnd('/') == ApiClient.MobileTestServer); // the test server address also counts: the server still answers 404 to these calls off the test stage
    }

    public static partial class OnlineEconomy
    {
        /// <summary>[TEST TOOLS] Completes the next main quest (chapter = every one left in the open chapters). done(ok).</summary>
        public static void TestPushMainQuest(bool chapter, Action<bool> done)
        {
            Post("/test-tools/main-quest", Body(("mode", chapter ? "chapter" : "one")), r =>
            {
                if (!r.ok) { done?.Invoke(false); return; }
                var completed = MiniJson.Arr(r.data, "completed") ?? new List<object>();
                foreach (var o in completed)
                {
                    string id = o as string;
                    if (string.IsNullOrEmpty(id)) continue;
                    OnlineSession.ClaimedQuests.Add(id); // paid by the server: never claimed again
                    Game.Quest?.CompleteFromServer(id);
                }
                if (Game.Quest != null)
                {
                    foreach (var f in MiniJson.Arr(r.data, "story_flags") ?? new List<object>())
                        if (f is string flag && !string.IsNullOrEmpty(flag)) Game.Quest.Journal.Flags.Add(flag);
                    Game.Quest.Refresh();
                }
                Game.Flow.Autosave();
                GameEvents.RaiseToast(L.F("메인 퀘스트 {0}개를 완료했습니다.", completed.Count));
                if (MiniJson.Str(MiniJson.Obj(r.data, "stopped"), "code") == "TEST_NEEDS_CAREER")
                    GameEvents.RaiseToast(L.T("전직을 먼저 마쳐야 다음 메인 퀘스트를 진행할 수 있습니다."));
                done?.Invoke(true);
            });
        }

        /// <summary>[TEST TOOLS] One level up (max = up to the level cap). The answer's delta raises the level through the usual level-up path.</summary>
        public static void TestPushLevel(bool max, Action<bool> done) =>
            Post("/test-tools/level", Body(("mode", max ? "max" : "one")), r =>
            {
                if (r.ok) Game.Flow.Autosave();
                done?.Invoke(r.ok);
            });
    }
}
