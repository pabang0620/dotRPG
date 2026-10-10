using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DotRPG
{
    /// <summary>
    /// [LOG G8] Sends this client's exceptions and errors to the server (POST /client-errors, Docs/server/phase15_load_and_logging.md)
    /// so problems on phones and friends' PCs can be traced. Each message (first line) once, at most 20 a session and 5 a minute,
    /// only while logged in; up to 5 errors from before login wait and go out once a token exists. Failures are never reported.
    /// </summary>
    public static class ClientErrorReporter
    {
        const int PerSession = 20, PerMinute = 5, MaxPending = 5, MessageChars = 500, StackChars = 1500, SceneChars = 64, VersionChars = 32;
        const float PendingCheckSeconds = 10f, PendingGiveUpSeconds = 600f;

        static readonly HashSet<string> seen = new HashSet<string>();
        static readonly Queue<float> sentTimes = new Queue<float>();
        static readonly List<Dictionary<string, object>> pending = new List<Dictionary<string, object>>();
        static int sentCount;
        static bool sending, waiting, quitting;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            Application.logMessageReceived -= OnLog;
            Application.logMessageReceived += OnLog;
            // Logs while the app shuts down must not create the ApiClient object again.
            Application.quitting += () => quitting = true;
        }

        static void OnLog(string message, string stack, LogType type)
        {
            if (sending || quitting || (type != LogType.Exception && type != LogType.Error) || string.IsNullOrEmpty(message)) return;
            if (sentCount + pending.Count >= PerSession) return;
            string first = message.Split('\n')[0].Trim();
            if (!seen.Add(first)) return;
            var body = new Dictionary<string, object>
            {
                ["version"] = Cut(string.IsNullOrEmpty(Application.version) ? "0" : Application.version, VersionChars),
                ["platform"] = Application.platform.ToString(),
                ["message"] = Cut(message, MessageChars),
                ["stack"] = Cut(stack ?? "", StackChars),
                ["scene"] = Cut((Game.Session != null ? Game.Session.MapId : SceneManager.GetActiveScene().name) ?? "", SceneChars),
            };
            if (LoggedIn) Send(body);
            else if (pending.Count < MaxPending)
            {
                pending.Add(body);
                if (!waiting) ApiClient.Instance.StartCoroutine(FlushWhenLoggedIn());
            }
        }

        static bool LoggedIn => !string.IsNullOrEmpty(ApiClient.Instance.AccessToken);

        // The server takes at most 4KB per report (UTF-8): the stack is cut shorter than its 2000-char field so Korean text still fits.
        static string Cut(string s, int max) => s.Length <= max ? s : s.Substring(0, max);

        static IEnumerator FlushWhenLoggedIn()
        {
            waiting = true;
            for (float waited = 0f; waited < PendingGiveUpSeconds && pending.Count > 0; waited += PendingCheckSeconds)
            {
                yield return new WaitForSecondsRealtime(PendingCheckSeconds);
                if (!LoggedIn) continue;
                var batch = new List<Dictionary<string, object>>(pending);
                pending.Clear();
                foreach (var body in batch) Send(body);
            }
            pending.Clear();
            waiting = false;
        }

        static void Send(Dictionary<string, object> body)
        {
            float now = Time.unscaledTime;
            while (sentTimes.Count > 0 && now - sentTimes.Peek() > 60f) sentTimes.Dequeue();
            if (sentTimes.Count >= PerMinute || sentCount >= PerSession) return;
            sentTimes.Enqueue(now);
            sentCount++;
            // Anything logged while the request is being made is ours (or about ours): never report it back.
            sending = true;
            try { ApiClient.Instance.Post("/client-errors", body, _ => { }); }
            finally { sending = false; }
        }
    }
}
