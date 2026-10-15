using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace DotRPG
{
    public sealed partial class MiniDungeonDirector
    {
        sealed class KillRequest { public string Reference, RequestId; }
        readonly Queue<KillRequest> pendingKills = new Queue<KillRequest>();
        string onlineCharacter, onlineRun;
        bool onlineReady, networkBusy;
        float onlineRemaining, nextPulse, nextKill;
        string CharacterPath(string character) => "/characters/" + character + "/mini-dungeon";

        bool SameOnline(string character, int version) => this != null && generation == version &&
            OnlineSession.Playing && OnlineSession.Current.ActiveCharacter == character;

        static float Number(Dictionary<string, object> data, string key, float fallback = 0f)
        {
            if (data == null || !data.TryGetValue(key, out var value) || value == null) return fallback;
            try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); } catch (Exception) { return fallback; }
        }

        void RefreshOnline()
        {
            string character = OnlineSession.Current.ActiveCharacter;
            if (onlineCharacter != character)
            {
                onlineCharacter = character; onlineReady = false; onlineRemaining = 0f;
                onlineRun = null; networkBusy = false; pendingKills.Clear(); generation++;
            }
            if (networkBusy || Busy) return;
            int version = generation;
            networkBusy = true;
            ApiClient.Instance.Get(CharacterPath(character), r =>
            {
                if (!SameOnline(character, version)) return;
                networkBusy = false;
                if (!r.ok) { onlineReady = false; GameEvents.RaiseToast(r.message ?? L.T("미니던전 이용 시간을 확인하지 못했습니다.")); Changed?.Invoke(); return; }
                ApplyOnlineStatus(r.data);
                onlineReady = true;
                Changed?.Invoke();
            });
        }

        void ApplyOnlineStatus(Dictionary<string, object> data)
        {
            onlineRemaining = Mathf.Clamp(Number(data, "remaining_seconds"), 0f, MiniDungeonCatalog.DailySeconds);
            OnlineEconomy.ApplyDelta(MiniJson.Obj(data, "delta"));
            var run = MiniJson.Obj(data, "active_run");
            if (run == null)
            {
                var completed = MiniJson.Obj(data, "completed_run");
                if (completed != null && MiniJson.Str(completed, "id") == onlineRun)
                {
                    Kills = MiniJson.Int(completed, "kills", Kills);
                    EarnedXp = MiniJson.Int(completed, "xp_granted", EarnedXp);
                }
                return;
            }
            onlineRun = MiniJson.Str(run, "id");
            XpPerKill = MiniJson.Int(run, "xp_per_kill", XpPerKill);
            Kills = MiniJson.Int(run, "kills", Kills);
            EarnedXp = MiniJson.Int(run, "xp_granted", EarnedXp);
            if (DateTime.TryParse(MiniJson.Str(run, "ends_at"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var end) &&
                DateTime.TryParse(MiniJson.Str(data, "server_time"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var serverNow))
                deadlineUtc = Now.AddSeconds(Math.Max(0d, Math.Min(onlineRemaining, (end.ToUniversalTime() - serverNow.ToUniversalTime()).TotalSeconds)));
            else deadlineUtc = Now.AddSeconds(onlineRemaining);
        }

        void BeginOnline()
        {
            string character = OnlineSession.Current.ActiveCharacter;
            onlineCharacter = character;
            int version = generation;
            var body = new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["tier"] = Tier };
            ApiClient.Instance.PostIdempotent(CharacterPath(character) + "/enter", body, r =>
            {
                if (!SameOnline(character, version))
                {
                    // The player can leave the title while an entry request is in flight. Close a late
                    // authorization rather than leave its timer charging a character no longer in the room.
                    var abandoned = r.ok ? MiniJson.Obj(r.data, "active_run") : null;
                    string abandonedRun = abandoned != null ? MiniJson.Str(abandoned, "id") : null;
                    if (!string.IsNullOrEmpty(abandonedRun))
                        ApiClient.Instance.PostIdempotent(CharacterPath(character) + "/runs/" + abandonedRun + "/leave",
                            new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["reason"] = "left" }, _ => { });
                    return;
                }
                if (!r.ok || MiniJson.Obj(r.data, "active_run") == null)
                {
                    Busy = false; onlineReady = false;
                    GameEvents.RaiseToast(r.message ?? L.T("미니던전에 입장하지 못했습니다."));
                    Refresh(); Changed?.Invoke(); return;
                }
                pendingKills.Clear(); networkBusy = false;
                ApplyOnlineStatus(r.data);
                onlineReady = true;
                nextPulse = Time.unscaledTime + 5f;
                nextKill = Time.unscaledTime + 2.1f;
                StartCoroutine(EnterRoom());
            });
        }

        void QueueOnlineKill(string reference)
        {
            if (pendingKills.Count >= 64) { Leave("network"); return; }
            pendingKills.Enqueue(new KillRequest { Reference = reference, RequestId = ApiClient.NewRequestId() });
        }

        void TickOnline()
        {
            if (onlineCharacter != OnlineSession.Current.ActiveCharacter) { Abort(); return; }
            if (networkBusy) return;
            if (pendingKills.Count > 0 && Time.unscaledTime >= nextKill) { SendNextKill(); return; }
            if (Time.unscaledTime < nextPulse) return;
            nextPulse = Time.unscaledTime + 5f;
            string character = onlineCharacter, run = onlineRun;
            int version = generation;
            networkBusy = true;
            ApiClient.Instance.PostIdempotent(CharacterPath(character) + "/runs/" + run + "/pulse",
                new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId() }, r =>
                {
                    if (!SameOnline(character, version)) return;
                    networkBusy = false;
                    if (!r.ok) { Leave("network"); return; }
                    ApplyOnlineStatus(r.data);
                    if (MiniJson.Obj(r.data, "active_run") == null) Leave("expired");
                    Changed?.Invoke();
                });
        }

        void SendNextKill()
        {
            if (networkBusy || pendingKills.Count == 0 || string.IsNullOrEmpty(onlineRun)) return;
            var pending = pendingKills.Peek();
            string character = onlineCharacter, run = onlineRun;
            int version = generation;
            networkBusy = true;
            ApiClient.Instance.PostIdempotent(CharacterPath(character) + "/runs/" + run + "/kills",
                new Dictionary<string, object> { ["request_id"] = pending.RequestId, ["monster_ref"] = pending.Reference }, r =>
                {
                    if (!SameOnline(character, version)) return;
                    networkBusy = false;
                    if (r.code == "MINI_KILL_RATE_LIMITED") { nextKill = Time.unscaledTime + Mathf.Max(2.05f, Number(r.errors, "retry_after_sec", 2.05f)); return; }
                    if (r.code == "PRESENCE_REQUIRED")
                    {
                        networkBusy = true;
                        PresenceClient.Ping(() => { if (!SameOnline(character, version)) return; networkBusy = false; nextKill = Time.unscaledTime + .1f; });
                        return; // same queued request and monster UUID are used on retry
                    }
                    if (!r.ok) { if (!Busy) Leave("network"); return; }
                    if (pendingKills.Count > 0 && pendingKills.Peek() == pending) pendingKills.Dequeue();
                    ApplyOnlineStatus(r.data);
                    nextKill = Time.unscaledTime + .08f; // capped server credit pool permits genuine AoE bursts
                    if (MiniJson.Obj(r.data, "active_run") == null && !Busy) Leave(Game.Session.Progression.Level >= Progression.MaxLevel ? "max_level" : "expired");
                    Changed?.Invoke();
                });
        }

        void LeaveOnline(string reason) => StartCoroutine(DrainAndLeave(reason));

        IEnumerator DrainAndLeave(string reason)
        {
            ClearEnemies();
            float until = Time.unscaledTime + (reason == "network" ? 0f : 8f);
            while (pendingKills.Count > 0 && Time.unscaledTime < until)
            {
                if (!networkBusy && Time.unscaledTime >= nextKill) SendNextKill();
                yield return null;
            }
            string character = onlineCharacter, run = onlineRun;
            int version = generation;
            bool done = false;
            if (!string.IsNullOrEmpty(run))
                ApiClient.Instance.PostIdempotent(CharacterPath(character) + "/runs/" + run + "/leave",
                    new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["reason"] = reason == "death" ? "death" : reason == "network" ? "disconnected" : "left" }, r =>
                    {
                        done = true;
                        if (!SameOnline(character, version)) return;
                        if (r.ok) { ApplyOnlineStatus(r.data); onlineReady = true; }
                        else onlineReady = false;
                    });
            else done = true;
            float leaveUntil = Time.unscaledTime + 3f;
            while (!done && Time.unscaledTime < leaveUntil) yield return null;
            pendingKills.Clear();
            onlineRun = null;
            yield return ReturnRoom(reason);
        }

        void AbortOnline()
        {
            string character = onlineCharacter, run = onlineRun;
            if (!string.IsNullOrEmpty(character) && !string.IsNullOrEmpty(run))
                ApiClient.Instance.PostIdempotent(CharacterPath(character) + "/runs/" + run + "/leave",
                    new Dictionary<string, object> { ["request_id"] = ApiClient.NewRequestId(), ["reason"] = "disconnected" }, _ => { });
            onlineRun = null; onlineReady = false; networkBusy = false; pendingKills.Clear();
        }
    }
}
