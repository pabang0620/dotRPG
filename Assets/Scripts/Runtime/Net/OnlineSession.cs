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

        static ApiClient Api => ApiClient.Instance;

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
                string serverData = MiniJson.Str(meta.data, "data_version");
                if (!string.IsNullOrEmpty(serverData) && serverData != ApiClient.DataVersion)
                {
                    done(new ApiResult { code = "DATA_OUTDATED", status = 426, message = "게임 데이터가 서버와 다릅니다. 게임을 업데이트해 주세요." });
                    return;
                }
                var body = new Dictionary<string, object> { ["login_id"] = loginId, ["password"] = password };
                Api.Post(register ? "/auth/dev/register" : "/auth/dev/login", body, r =>
                {
                    if (!r.ok) { done(r); return; }
                    Api.SetTokens(MiniJson.Str(r.data, "access_token"), MiniJson.Str(r.data, "refresh_token"));
                    Current = new OnlineSession { AccountId = MiniJson.Str(MiniJson.Obj(r.data, "account"), "id"), LoginId = loginId.ToLowerInvariant() };
                    Current.LoadCharacters(done);
                }, auth: false);
            }, auth: false);
        }

        public static void Logout()
        {
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
                ActiveCharacter = id;
                stateVersion = MiniJson.Int(MiniJson.Obj(detail, "state"), "version");
                done(r, ToSaveData(detail));
            });
        }

        /// <summary>Back to the title: the next save goes to a file again.</summary>
        public void LeaveCharacter()
        {
            PartyClient.DetachOnline(); // [PARTY]
            OnlineServices.DetachChat(); // [SERVER 5]
            OnlineServices.DetachAuction(); // [SERVER 6]
            ActiveCharacter = null; // a save still waiting keeps its own character id and is sent
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
            var supports = new Dictionary<int, List<object>>();
            foreach (var g in MiniJson.Arr(state, "skill_gems") ?? new List<object>())
                supports[MiniJson.Int(g, "slot")] = MiniJson.Arr(g, "supports") ?? new List<object>();
            data.gemSlots = new List<string>();
            for (int s = 0; s < SkillGems.Slots; s++)
                for (int k = 0; k <= SkillGems.SupportsPerSlot; k++)
                {
                    if (k == 0) { data.gemSlots.Add(""); continue; }
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
                    if (any) gems.Add(new Dictionary<string, object> { ["slot"] = s, ["supports"] = sup });
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
                if (r.ok) stateVersion = MiniJson.Int(r.data, "version", stateVersion + 1);
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
                    else if (r.code == "NETWORK") GameEvents.RaiseToast("서버에 연결할 수 없어 저장하지 못했습니다.");
                }
                SendNext(retriedConflict: false);
            });
        }
    }
}
