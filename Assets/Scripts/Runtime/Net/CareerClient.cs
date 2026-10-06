using System;
using System.Collections.Generic;

namespace DotRPG
{
    /// <summary>
    /// [ANTI-ABUSE] Career promotion and the awakening quest are granted by the server online (phase9 19.4, C1~C4):
    /// the local state changes only after the server answers. Offline play answers at once.
    /// done(ok, message, serverStage) - serverStage is the server's stage when it reports a mismatch (-1 = unknown).
    /// </summary>
    public static class CareerClient
    {
        static ApiClient Api => ApiClient.Instance;
        static bool Online => OnlineSession.Playing;
        static string Char => "/characters/" + OnlineSession.Current.ActiveCharacter;

        public static void Promote(Career career, Action<bool, string> done)
        {
            if (!Online) { done(true, null); return; }
            Call("/career/promote", new Dictionary<string, object> { ["career"] = (int)career }, false, (ok, msg, _) => done(ok, msg));
        }

        public static void TrialStart(Action<bool, string> done)
        {
            if (!Online) { done(true, null); return; }
            Call("/career/awakening/trial/start", new Dictionary<string, object>(), true, (ok, msg, _) => done(ok, msg));
        }

        public static void TrialFinish(bool success, Action<bool, string, int> done)
        {
            if (!Online) { done(true, null, success ? 3 : 2); return; }
            Call("/career/awakening/trial/finish", new Dictionary<string, object> { ["result"] = success ? "success" : "fail" }, true, done);
        }

        public static void Advance(int fromStage, Action<bool, string, int> done)
        {
            if (!Online) { done(true, null, fromStage + 1); return; }
            Call("/career/awakening/advance", new Dictionary<string, object> { ["from_stage"] = fromStage }, true, done);
        }

        /// <summary>Awakening steps need a fresh town presence, so one is sent first.</summary>
        static void Call(string path, Dictionary<string, object> body, bool needsPresence, Action<bool, string, int> done)
        {
            body["request_id"] = ApiClient.NewRequestId();
            string full = Char + path;
            void Send() => Api.Post(full, body, r =>
            {
                if (r.ok) { done(true, null, MiniJson.Int(r.data, "stage", -1)); return; }
                int stage = r.errors != null && r.errors.ContainsKey("stage") ? MiniJson.Int(r.errors, "stage", -1) : -1;
                done(false, string.IsNullOrEmpty(r.message) ? "서버가 요청을 받지 못했습니다." : r.message, stage);
            });
            if (needsPresence) PresenceClient.Ping(Send); else Send();
        }
    }
}
