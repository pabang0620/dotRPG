using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [SERVER] The logged-in online account and its server characters (Docs/server/phase1_2_api.md).
    /// Online characters never touch the offline save files (PLAN_ONLINE O1): entering one builds a
    /// <see cref="SaveData"/> from the server detail and plays it with the normal restore path; while
    /// <see cref="Playing"/> is true, saves go to <c>PUT /characters/{id}/state</c> instead of a file.
    /// Phase 2: level, XP, gold and items are read-only server values (phase 3 moves their changes to the server).
    /// </summary>
    public sealed class OnlineSession
    {
        public sealed class CharacterSummary
        {
            public string id;
            public string name;
            public CharacterClass cls;
            public int level;
            public string mapId;
        }

        /// <summary>Null while not logged in.</summary>
        public static OnlineSession Current { get; private set; }
        /// <summary>True while an online character is in the world (saves go to the server).</summary>
        public static bool Playing => Current != null && Current.ActiveCharacter != null;

        public string AccountId { get; private set; }
        public string LoginId { get; private set; }
        public int CharacterLimit { get; private set; } = 4;
        public readonly List<CharacterSummary> Characters = new List<CharacterSummary>();
        public string ActiveCharacter { get; private set; }
        int stateVersion;
        bool uploading;
        SaveData pendingUpload;
        string pendingId;
        // [J3] Network failures: the newest snapshot stays queued and goes out again after a growing pause.
        int networkFailures;
        bool retryScheduled;
        static readonly float[] UploadRetryDelays = { 5f, 15f, 30f, 60f };

        static ApiClient Api => ApiClient.Instance;

        /// <summary>[ANTI-ABUSE] Another login took this account over: leave play and go back to the title.</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void HookSessionReplaced()
        {
            ApiClient.SessionReplaced += () =>
            {
                if (Current == null) return;
                bool playing = Playing;
                Current = null;
                if (playing && Game.Flow != null) Game.Flow.ReturnToTitle();
                GameEvents.RaiseToast("다른 곳에서 로그인하여 이 접속이 종료되었습니다.");
            };
        }

        /// <summary>Kept for the older automated check: offline unless someone logged in.</summary>
        public static bool TryConnect(out string error)
        {
            error = Current == null ? "로그인하지 않았습니다." : null;
            return Current != null;
        }

        // ---------------- account ----------------

        /// <summary>Checks /meta first (outdated client or data = clear message), then logs in or registers.</summary>
        public static void Login(string loginId, string password, bool register, Action<ApiResult> done)
        {
            Api.Get("/meta", meta =>
            {
                if (!meta.ok) { done(meta); return; }
                // [SERVER 7] Maintenance: no logins once the block time has come.
                string blocked = MaintenanceBlock(MiniJson.Obj(meta.data, "maintenance"));
                if (blocked != null) { done(new ApiResult { code = "MAINTENANCE", status = 503, message = blocked }); return; }
                string serverData = MiniJson.Str(meta.data, "data_version");
                if (!string.IsNullOrEmpty(serverData) && serverData != ApiClient.DataVersion)
                {
                    done(new ApiResult { code = "DATA_OUTDATED", status = 426, message = "게임 데이터가 서버와 다릅니다. 게임을 업데이트해 주세요." });
                    return;
                }
                var body = new Dictionary<string, object> { ["login_id"] = loginId, ["password"] = password, ["device"] = DeviceIdentity.Body() };
                Api.Post(register ? "/auth/dev/register" : "/auth/dev/login", body, r =>
                {
                    if (!r.ok) { done(r); return; }
                    Api.SetTokens(MiniJson.Str(r.data, "access_token"), MiniJson.Str(r.data, "refresh_token"));
                    Current = new OnlineSession { AccountId = MiniJson.Str(MiniJson.Obj(r.data, "account"), "id"), LoginId = loginId.ToLowerInvariant() };
                    Current.LoadCharacters(done);
                }, auth: false);
            }, auth: false);
        }

        /// <summary>[SERVER 7] Korean notice for a maintenance window (null = nothing to say).</summary>
        public static string MaintenanceText(Dictionary<string, object> m)
        {
            if (m == null) return null;
            string ends = LocalTime(MiniJson.Str(m, "ends_at")), starts = LocalTime(MiniJson.Str(m, "starts_at"));
            string notice = MiniJson.Str(m, "notice", "");
            string head = MiniJson.Str(m, "phase") == "active" ? $"서버 점검 중입니다. ({ends} 종료 예정)" : $"서버 점검 예정: {starts} ~ {ends}";
            return string.IsNullOrEmpty(notice) ? head : head + "\n" + notice;
        }

        /// <summary>The login refusal while maintenance blocks logins, else null.</summary>
        static string MaintenanceBlock(Dictionary<string, object> m)
        {
            string phase = MiniJson.Str(m, "phase");
            return phase == "pre_block" || phase == "active" ? MaintenanceText(m) : null;
        }

        static string LocalTime(string iso) =>
            DateTime.TryParse(iso, null, System.Globalization.DateTimeStyles.RoundtripKind, out var t) ? t.ToLocalTime().ToString("M/d HH:mm") : "?";

        /// <summary>
        /// [PARTY 8] Log in with the Steam account running this game (no password). The ticket's identity and
        /// the expected app id come from /meta; a test client (app 480) never logs into a live server.
        /// </summary>
        public static void LoginSteam(Action<ApiResult> done)
        {
            var steam = SteamBridge.Current;
            if (steam == null || !steam.Ready) { done(new ApiResult { code = "STEAM_OFF", message = "Steam이 실행 중이 아닙니다." }); return; }
            Api.Get("/meta", meta =>
            {
                if (!meta.ok) { done(meta); return; }
                string blocked = MaintenanceBlock(MiniJson.Obj(meta.data, "maintenance"));
                if (blocked != null) { done(new ApiResult { code = "MAINTENANCE", status = 503, message = blocked }); return; }
                var info = MiniJson.Obj(meta.data, "steam");
                string identity = MiniJson.Str(info, "identity");
                uint appId = (uint)MiniJson.Num(info, "app_id");
                if (info == null || string.IsNullOrEmpty(identity)) { done(new ApiResult { code = "STEAM_OFF", message = "이 서버는 Steam 로그인을 받지 않습니다." }); return; }
                if (appId != 0 && appId != steam.AppId) { done(new ApiResult { code = "STEAM_APP_MISMATCH", message = "이 게임 빌드는 이 서버에 접속할 수 없습니다. (Steam 앱 ID 불일치)" }); return; }
                string serverData = MiniJson.Str(meta.data, "data_version");
                if (!string.IsNullOrEmpty(serverData) && serverData != ApiClient.DataVersion)
                {
                    done(new ApiResult { code = "DATA_OUTDATED", status = 426, message = "게임 데이터가 서버와 다릅니다. 게임을 업데이트해 주세요." });
                    return;
                }
                steam.GetAuthTicket(identity, ticket =>
                {
                    if (string.IsNullOrEmpty(ticket)) { done(new ApiResult { code = "STEAM_TICKET", message = "Steam 인증 티켓을 받지 못했습니다." }); return; }
                    Api.Post("/auth/steam", new Dictionary<string, object> { ["ticket"] = ticket, ["device"] = DeviceIdentity.Body() }, r =>
                    {
                        if (!r.ok) { done(r); return; }
                        Api.SetTokens(MiniJson.Str(r.data, "access_token"), MiniJson.Str(r.data, "refresh_token"));
                        Current = new OnlineSession { AccountId = MiniJson.Str(MiniJson.Obj(r.data, "account"), "id"), LoginId = "Steam" };
                        Current.LoadCharacters(done);
                    }, auth: false);
                });
            }, auth: false);
        }

        public static void Logout()
        {
            Current?.FlushPending(); // [J3] the access token is still valid here
            string refresh = Api.RefreshToken;
            if (!string.IsNullOrEmpty(refresh))
                Api.Post("/auth/logout", new Dictionary<string, object> { ["refresh_token"] = refresh }, _ => { }, auth: false);
            Api.ClearTokens();
            Current = null;
        }

        // ---------------- characters ----------------

        public void LoadCharacters(Action<ApiResult> done)
        {
            Api.Get("/characters", r =>
            {
                if (r.ok)
                {
                    Characters.Clear();
                    CharacterLimit = MiniJson.Int(r.data, "limit", 4);
                    foreach (var o in MiniJson.Arr(r.data, "characters") ?? new List<object>())
                        Characters.Add(Summary(o));
                }
                done(r);
            });
        }

        static CharacterSummary Summary(object o) => new CharacterSummary
        {
            id = MiniJson.Str(o, "id"),
            name = MiniJson.Str(o, "name"),
            cls = CharacterClassInfo.Parse(MiniJson.Str(o, "class", "warrior")),
            level = MiniJson.Int(o, "level", 1),
            mapId = MiniJson.Str(o, "map_id", MapRegistry.Village),
        };

        /// <summary>request_id is made once per creation attempt; resending after a lost answer reuses it.</summary>
        public void CreateCharacter(string name, CharacterClass cls, string requestId, Action<ApiResult> done)
        {
            var body = new Dictionary<string, object>
            {
                ["request_id"] = requestId,
                ["name"] = name,
                ["class"] = cls == CharacterClass.Mage ? "mage" : "warrior",
            };
            Api.Post("/characters", body, r =>
            {
                if (r.ok) LoadCharacters(_ => done(r));
                else done(r);
            });
        }

        public void DeleteCharacter(string id, Action<ApiResult> done)
        {
            Api.Delete("/characters/" + id, r =>
            {
                if (r.ok) Characters.RemoveAll(c => c.id == id);
                done(r);
            });
        }

        /// <summary>Loads a character's detail and turns it into SaveData for the normal restore path.</summary>
        public void EnterCharacter(string id, Action<ApiResult, SaveData> done)
        {
            Api.Get("/characters/" + id, r =>
            {
                if (!r.ok) { done(r, null); return; }
                var detail = MiniJson.Obj(r.data, "character");
                var save = ToSaveData(detail);
                // [ANTI-ABUSE] The presence signal goes first (the server refuses a third character on this PC).
                PresenceClient.Begin(id, save != null && !string.IsNullOrEmpty(save.mapId) ? save.mapId : MapRegistry.Village, (ok, refusal) =>
                {
                    if (!ok) { done(new ApiResult { ok = false, code = "DEVICE_LIMIT", message = refusal }, null); return; }
                    ActiveCharacter = id;
                    Game.State?.RefreshTimeScale();
                    stateVersion = MiniJson.Int(MiniJson.Obj(detail, "state"), "version");
                    done(r, save);
                });
            });
        }

        /// <summary>Back to the title: the next save goes to a file again.</summary>
        public void LeaveCharacter()
        {
            PresenceClient.Leave(); // [ANTI-ABUSE]
            PartyClient.DetachOnline(); // [PARTY]
            OnlineServices.DetachChat(); // [SERVER 5]
            OnlineServices.DetachAuction(); // [SERVER 6]
            FlushPending(); // [J3] a snapshot that failed to upload gets one more try now
            ActiveCharacter = null; // a save still waiting keeps its own character id and is sent
            Game.State?.RefreshTimeScale();
        }

        // ---------------- server detail <-> SaveData ----------------

        /// <summary>[SERVER] Quests whose reward the server has paid (from the last character detail).</summary>
        public static HashSet<string> ClaimedQuests = new HashSet<string>();

        static SaveData ToSaveData(Dictionary<string, object> c)
        {
            var state = MiniJson.Obj(c, "state");
            var data = new SaveData
            {
                savedAtUtc = DateTime.UtcNow.ToString("o"),
                playerClass = MiniJson.Str(c, "class", "warrior"),
                playerName = MiniJson.Str(c, "name", ""),
                level = MiniJson.Int(c, "level", 1),
                xp = MiniJson.Int(c, "xp"),
                mapId = MiniJson.Str(state, "map_id", MapRegistry.Village),
                playerFacing = MiniJson.Int(state, "facing"),
                playerHealth = int.MaxValue, // full: GameSession.Restore clamps to the max
                trackedQuest = MiniJson.Str(state, "tracked_quest", ""),
            };
            var pos = MiniJson.Obj(state, "pos");
            // Negative coordinates = the map's start point (GameSession.Restore).
            data.playerX = pos != null ? (float)MiniJson.Num(pos, "x") : -1f;
            data.playerY = pos != null ? (float)MiniJson.Num(pos, "y") : -1f;

            int gold = MiniJson.Int(c, "gold");
            if (gold > 0) data.inventory.Add(new ItemStack(ConsumableDatabase.Gold, gold));
            var worn = new string[Equipment.SlotCount];
            OnlineEconomy.ClearStackRows();
            foreach (var o in MiniJson.Arr(c, "items") ?? new List<object>())
            {
                string key = MiniJson.Str(o, "item_key");
                int count = MiniJson.Int(o, "count");
                string location = MiniJson.Str(o, "location");
                if (location == "bag" || location == "storage") OnlineEconomy.SetStackRow(location, key, MiniJson.Str(o, "bind", "none"), count); // [SERVER 6]
                switch (location)
                {
                    case "bag": data.inventory.Add(new ItemStack(key, count)); break;
                    case "storage": data.storage.Add(new ItemStack(key, count)); break;
                    case "worn":
                        int slot = MiniJson.Int(o, "slot", -1);
                        if (slot >= 0 && slot < worn.Length) worn[slot] = key;
                        break;
                }
            }
            data.equipped = worn.Select(k => k ?? "").ToList();

            foreach (var q in MiniJson.Arr(state, "quests") ?? new List<object>())
                data.quests.Add(new QuestSave
                {
                    id = MiniJson.Str(q, "id"),
                    status = MiniJson.Int(q, "status"),
                    step = MiniJson.Int(q, "step"),
                    counts = (MiniJson.Arr(q, "counts") ?? new List<object>()).Select(n => (int)Math.Round((double)n)).ToList(),
                });
            foreach (var f in MiniJson.Arr(state, "story_flags") ?? new List<object>()) data.storyFlags.Add((string)f);
            // [SERVER] Phase 3 records the server keeps (phase3_api §1).
            if (Game.Config != null) data.playerMaxHealth = Game.Config.playerStats.maxHealth + MiniJson.Int(c, "bonus_max_health");
            foreach (var o in MiniJson.Arr(c, "opened_chests") ?? new List<object>()) data.openedChests.Add((string)o);
            foreach (var o in MiniJson.Arr(c, "enhance_pity") ?? new List<object>())
                data.enhancePity.Add(new ItemStack(MiniJson.Str(o, "item_key"), MiniJson.Int(o, "pity")));
            foreach (var site in MiniJson.Arr(c, "deliveries") ?? new List<object>())
            {
                if (MiniJson.Str(site, "site_id") != "workshop") continue;
                bool done = true;
                foreach (var it in MiniJson.Arr(site, "items") ?? new List<object>())
                {
                    int got = MiniJson.Int(it, "delivered");
                    if (got < MiniJson.Int(it, "required")) done = false;
                    if (MiniJson.Str(it, "item_key") == ItemIds.Wood) data.quest.woodDelivered = got;
                    if (MiniJson.Str(it, "item_key") == ItemIds.Stone) data.quest.stoneDelivered = got;
                }
                if (done) data.quest.workshopBuilt = true;
            }
            ClaimedQuests = new HashSet<string>();
            foreach (var o in MiniJson.Arr(c, "claimed_quests") ?? new List<object>()) ClaimedQuests.Add((string)o);
            data.passives = new List<string> { PassiveTree.Start };
            foreach (var p in MiniJson.Arr(state, "passives") ?? new List<object>()) data.passives.Add((string)p);

            // gemSlots: per slot [active, support1, support2]; the active one comes from the class, supports from the server.
            data.career=MiniJson.Obj(state,"career") is Dictionary<string,object> careerJson ? JsonUtility.FromJson<CareerSave>(MiniJson.Write(careerJson)) : null;
            var activeSkills=new Dictionary<int,string>();
            foreach(var item in MiniJson.Arr(state,"skill_gems")??new List<object>()) activeSkills[MiniJson.Int(item,"slot")]=MiniJson.Str(item,"active");
            var supports = new Dictionary<int, List<object>>();
            foreach (var g in MiniJson.Arr(state, "skill_gems") ?? new List<object>())
                supports[MiniJson.Int(g, "slot")] = MiniJson.Arr(g, "supports") ?? new List<object>();
            data.gemSlots = new List<string>();
            for (int s = 0; s < SkillGems.Slots; s++)
                for (int k = 0; k <= SkillGems.SupportsPerSlot; k++)
                {
                    if (k == 0) { data.gemSlots.Add(activeSkills.TryGetValue(s,out var chosen)?chosen:""); continue; }
                    var list = supports.TryGetValue(s, out var l) ? l : null;
                    data.gemSlots.Add(list != null && k - 1 < list.Count ? (list[k - 1] as string ?? "") : "");
                }
            return data;
        }

        static Dictionary<string, object> ToState(SaveData d, int version)
        {
            var gems = new List<object>();
            if (d.gemSlots != null)
                for (int s = 0; s < SkillGems.Slots; s++)
                {
                    var sup = new List<object>();
                    bool any = false;
                    for (int k = 1; k <= SkillGems.SupportsPerSlot; k++)
                    {
                        int i = s * (1 + SkillGems.SupportsPerSlot) + k;
                        string id = i < d.gemSlots.Count && !string.IsNullOrEmpty(d.gemSlots[i]) ? d.gemSlots[i] : null;
                        any |= id != null;
                        sup.Add(id);
                    }
                    gems.Add(new Dictionary<string, object> { ["slot"] = s, ["supports"] = sup, ["active"]=d.gemSlots[s*3] });
                }
            return new Dictionary<string, object>
            {
                ["version"] = version,
                ["map_id"] = d.mapId,
                ["pos"] = d.playerX >= 0f && d.playerY >= 0f ? new Dictionary<string, object> { ["x"] = d.playerX, ["y"] = d.playerY } : null,
                ["facing"] = d.playerFacing,
                ["quests"] = (d.quests ?? new List<QuestSave>()).Select(q => (object)new Dictionary<string, object>
                {
                    ["id"] = q.id, ["status"] = q.status, ["step"] = q.step, ["counts"] = q.counts ?? new List<int>(),
                }).ToList(),
                ["story_flags"] = d.storyFlags ?? new List<string>(),
                ["tracked_quest"] = d.trackedQuest ?? "",
                ["passives"] = (d.passives ?? new List<string>()).Where(p => p != PassiveTree.Start).ToList(),
                ["skill_gems"] = gems,
                ["career"]=MiniJson.Parse(JsonUtility.ToJson(d.career??new CareerSave())),
            };
        }

        // ---------------- saving ----------------

        /// <summary>
        /// Called by SaveSystem.Write while <see cref="Playing"/>. Uploads run one at a time; a save that comes in
        /// meanwhile replaces the waiting one (only the newest snapshot matters).
        /// </summary>
        public void UploadState(SaveData data)
        {
            if (ActiveCharacter == null) return;
            pendingUpload = data;
            pendingId = ActiveCharacter;
            if (!uploading) SendNext(retriedConflict: false);
        }

        /// <summary>[J3] Sends a snapshot left over from a network failure right away (title / logout).</summary>
        void FlushPending()
        {
            if (pendingUpload != null && !uploading) SendNext(retriedConflict: false);
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

        static System.Collections.IEnumerator After(float seconds, Action action)
        {
            yield return new WaitForSecondsRealtime(seconds);
            action();
        }

        void SendNext(bool retriedConflict)
        {
            var data = pendingUpload;
            string id = pendingId;
            if (data == null || id == null) { uploading = false; return; }
            pendingUpload = null;
            uploading = true;
            Api.Put($"/characters/{id}/state", ToState(data, stateVersion), r =>
            {
                if (r.ok) { stateVersion = MiniJson.Int(r.data, "version", stateVersion + 1); networkFailures = 0; }
                else if (r.status == 429)
                {
                    // 1 save per second per character: wait and send the newest snapshot.
                    if (pendingUpload == null) { pendingUpload = data; pendingId = id; }
                    ApiClient.Instance.StartCoroutine(After(1.2f, () => SendNext(retriedConflict)));
                    return;
                }
                else if (r.code == "VERSION_CONFLICT" && !retriedConflict)
                {
                    // Another session saved in between: this client's snapshot wins (non-economy state only).
                    stateVersion = MiniJson.Int(r.errors, "current_version", stateVersion);
                    if (pendingUpload == null) { pendingUpload = data; pendingId = id; }
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
                        if (pendingUpload == null) { pendingUpload = data; pendingId = id; }
                        if (networkFailures++ == 0) GameEvents.RaiseToast("서버에 연결할 수 없어 저장하지 못했습니다. 연결되면 다시 저장합니다.");
                        uploading = false;
                        ScheduleUploadRetry();
                        return;
                    }
                }
                SendNext(retriedConflict: false);
            });
        }
    }
}
