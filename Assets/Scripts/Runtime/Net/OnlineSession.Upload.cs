using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [SERVER] Character state upload (PUT /characters/{id}/state): one request at a time, the newest snapshot per character wins,
    /// a snapshot that failed on the network is sent again later, and a waiting snapshot is flushed before the character
    /// changes and (within a short limit) before the game quits.
    /// </summary>
    public sealed partial class OnlineSession
    {
        // The state version belongs to the character: a snapshot left over from the previous character must not use the next one's.
        readonly Dictionary<string, int> stateVersions = new Dictionary<string, int>();
        bool uploading;
        bool abandoned;
        SaveData pendingUpload;
        string pendingId;
        // A waiting snapshot of another character that a newer save pushed aside (sent after the current one).
        readonly Dictionary<string, SaveData> parked = new Dictionary<string, SaveData>();
        // [J3] Network failures: the newest snapshot stays queued and goes out again after a growing pause.
        int networkFailures;
        bool retryScheduled;
        static readonly float[] UploadRetryDelays = { 5f, 15f, 30f, 60f };
        /// <summary>Longest wait for the previous character's upload before the next one is entered.</summary>
        const float SwitchWaitSeconds = 3f;

        bool HasUnsent => pendingUpload != null || uploading || parked.Count > 0;

        /// <summary>
        /// Called by SaveSystem.Write while <see cref="Playing"/>. Uploads run one at a time; a save that comes in
        /// meanwhile replaces the waiting one of the same character (only the newest snapshot matters).
        /// </summary>
        public void UploadState(SaveData data)
        {
            if (abandoned || ActiveCharacter == null) return;
            if (pendingUpload != null && pendingId != ActiveCharacter) parked[pendingId] = pendingUpload;
            pendingUpload = data;
            pendingId = ActiveCharacter;
            if (!uploading) SendNext(retriedConflict: false);
        }

        /// <summary>[J3] Sends a snapshot left over from a network failure right away (title / logout / character change / quit).</summary>
        void FlushPending()
        {
            if ((pendingUpload != null || parked.Count > 0) && !uploading) SendNext(retriedConflict: false);
        }

        /// <summary>The account is gone (withdrawal): nothing more can be saved to the server.</summary>
        void Abandon()
        {
            abandoned = true;
            pendingUpload = null;
            parked.Clear();
        }

        /// <summary>Flushes what waits, then runs <paramref name="then"/> once it is sent or after at most <paramref name="maxSeconds"/>.</summary>
        void WhenUploadsSent(float maxSeconds, Action then)
        {
            FlushPending();
            if (!HasUnsent) { then(); return; }
            Api.StartCoroutine(WaitForUploads(maxSeconds, then));
        }

        IEnumerator WaitForUploads(float maxSeconds, Action then)
        {
            float until = Time.realtimeSinceStartup + maxSeconds;
            while (HasUnsent && Time.realtimeSinceStartup < until) yield return null;
            then();
        }

        void ScheduleUploadRetry()
        {
            if (retryScheduled) return;
            retryScheduled = true;
            float wait = UploadRetryDelays[Math.Min(networkFailures - 1, UploadRetryDelays.Length - 1)];
            Api.StartCoroutine(After(wait, () =>
            {
                retryScheduled = false;
                if (pendingUpload != null && !uploading) SendNext(retriedConflict: false);
            }));
        }

        static IEnumerator After(float seconds, Action action)
        {
            yield return new WaitForSecondsRealtime(seconds);
            action();
        }

        /// <summary>Keeps a snapshot that could not be sent unless a newer one waits; another character's one is parked, never dropped.</summary>
        void Requeue(string id, SaveData data)
        {
            if (pendingUpload == null) { pendingUpload = data; pendingId = id; }
            else if (pendingId != id) parked[id] = parked.TryGetValue(id, out var newer) ? newer : data;
        }

        /// <summary>
        /// Merges the server's character state into <paramref name="data"/>: per quest the further one (status, then step;
        /// a quest the server paid is complete), the union of story flags, and the career stage / awakening never below the
        /// server's. Applies quests and flags that moved ahead to the running journal when it is this character.
        /// </summary>
        void MergeServerState(string id, SaveData data, Dictionary<string, object> state)
        {
            bool live = id == ActiveCharacter && Game.Session != null && Game.Session.Journal != null;
            data.quests = data.quests ?? new List<QuestSave>();
            foreach (var q in MiniJson.Arr(state, "quests") ?? new List<object>())
            {
                string qid = MiniJson.Str(q, "id");
                if (string.IsNullOrEmpty(qid)) continue;
                int status = MiniJson.Int(q, "status"), step = MiniJson.Int(q, "step");
                if (ClaimedQuests.Contains(qid)) status = Math.Max(status, (int)QuestStatus.Completed);
                var mine = data.quests.Find(x => x.id == qid);
                bool ahead = mine == null || status > mine.status || (status == mine.status && step > mine.step);
                if (!ahead) continue;
                var counts = new List<int>();
                foreach (var n in MiniJson.Arr(q, "counts") ?? new List<object>()) counts.Add((int)Math.Round(Convert.ToDouble(n)));
                if (mine == null) data.quests.Add(mine = new QuestSave { id = qid });
                mine.status = status; mine.step = step; mine.counts = counts;
                if (live)
                {
                    var js = Game.Session.Journal.State(qid);
                    if (js.status < status || (js.status == status && js.step < step)) { js.status = status; js.step = step; js.counts = new List<int>(counts); }
                }
            }
            data.storyFlags = data.storyFlags ?? new List<string>();
            foreach (var f in MiniJson.Arr(state, "story_flags") ?? new List<object>())
            {
                string flag = f as string;
                if (string.IsNullOrEmpty(flag)) continue;
                if (!data.storyFlags.Contains(flag)) data.storyFlags.Add(flag);
                if (live) Game.Session.Journal.Flags.Add(flag);
            }
            // Career: the server records grants and quest stages; the build (nodes, styles) stays the client's.
            if (MiniJson.Obj(state, "career") is Dictionary<string, object> cj)
            {
                var server = JsonUtility.FromJson<CareerSave>(MiniJson.Write(cj));
                if (server != null)
                {
                    if (data.career == null) data.career = server;
                    else
                    {
                        if ((int)data.career.career == 0) data.career.career = server.career;
                        data.career.questStage = Math.Max(data.career.questStage, server.questStage);
                        data.career.awakened |= server.awakened;
                    }
                }
            }
            if (live) Game.Quest?.NotifyChanged();
        }

        void SendNext(bool retriedConflict)
        {
            if (pendingUpload == null && parked.Count > 0)
            {
                foreach (var kv in parked) { pendingId = kv.Key; pendingUpload = kv.Value; break; }
                parked.Remove(pendingId);
            }
            var data = pendingUpload;
            string id = pendingId;
            if (data == null || id == null) { uploading = false; return; }
            pendingUpload = null;
            uploading = true;
            stateVersions.TryGetValue(id, out int version);
            Api.Put($"/characters/{id}/state", ToState(data, version), r =>
            {
                if (r.ok) { stateVersions[id] = MiniJson.Int(r.data, "version", version + 1); networkFailures = 0; }
                else if (r.status == 429)
                {
                    // 1 save per second per character: wait and send the newest snapshot.
                    Requeue(id, data);
                    ApiClient.Instance.StartCoroutine(After(1.2f, () => SendNext(retriedConflict)));
                    return;
                }
                else if (r.code == "VERSION_CONFLICT" && !retriedConflict)
                {
                    // [AUDIT 2026-10-14] Something else changed the state on the server (another session, an operator, a
                    // server grant). The snapshot no longer simply wins: read the server's state, merge (a quest never goes
                    // back, story flags add up, the career stage keeps the server's progress) and send the merged one with
                    // the server's version. Quests the server pulls ahead are applied to the running game too.
                    Api.Get($"/characters/{id}", g =>
                    {
                        var state = g.ok ? MiniJson.Obj(MiniJson.Obj(g.data, "character"), "state") : null;
                        if (state != null)
                        {
                            MergeServerState(id, data, state);
                            stateVersions[id] = MiniJson.Int(state, "version", MiniJson.Int(r.errors, "current_version", version));
                        }
                        else stateVersions[id] = MiniJson.Int(r.errors, "current_version", version);
                        Requeue(id, data);
                        SendNext(retriedConflict: true);
                    });
                    return;
                }
                else
                {
                    Debug.LogWarning($"[Online] state save failed: {r.status} {r.code} {r.message}");
                    if (r.code != null && r.code.StartsWith("INVALID_")) GameEvents.RaiseToast(L.F("진행 상황을 서버에 보내지 못했습니다. ({0})", r.message));
                    else if (r.code == "NETWORK" || r.status >= 500)
                    {
                        // [J3] Keep this snapshot unless a newer one already waits (an old state never overwrites a new one).
                        // A server error (5xx: restart, DB hiccup) is retried the same way; it used to drop the snapshot.
                        Requeue(id, data);
                        if (networkFailures++ == 0) GameEvents.RaiseToast(L.T("서버에 연결할 수 없습니다. 연결되면 진행 상황을 다시 보냅니다."));
                        uploading = false;
                        ScheduleUploadRetry();
                        return;
                    }
                }
                SendNext(false);
            });
        }
    }
}
