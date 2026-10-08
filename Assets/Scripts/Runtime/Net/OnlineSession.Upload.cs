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
                    // Another session saved in between: this client's snapshot wins (non-economy state only).
                    stateVersions[id] = MiniJson.Int(r.errors, "current_version", version);
                    Requeue(id, data);
                    SendNext(retriedConflict: true);
                    return;
                }
                else
                {
                    Debug.LogWarning($"[Online] state save failed: {r.status} {r.code} {r.message}");
                    if (r.code != null && r.code.StartsWith("INVALID_")) GameEvents.RaiseToast("서버 저장에 실패했습니다. (" + r.message + ")");
                    else if (r.code == "NETWORK")
                    {
                        // [J3] Keep this snapshot unless a newer one already waits (an old state never overwrites a new one).
                        Requeue(id, data);
                        if (networkFailures++ == 0) GameEvents.RaiseToast("서버에 연결할 수 없어 저장하지 못했습니다. 연결되면 다시 저장합니다.");
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
