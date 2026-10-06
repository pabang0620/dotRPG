using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace DotRPG
{
    /// <summary>
    /// [ANTI-ABUSE] This install and machine, sent with every login and token refresh (Docs/server/phase9_anti_abuse.md 19.1).
    /// install_id: a UUID made once and kept in persistentDataPath. device_hash: SHA-256 of the OS device id (left out
    /// when the platform has none, so unsupported machines are not lumped together).
    /// </summary>
    public static class DeviceIdentity
    {
        static string installId, deviceHash;
        static bool hashed;

        public static string InstallId
        {
            get
            {
                if (installId != null) return installId;
                string path = Path.Combine(Application.persistentDataPath, "install_id");
                try
                {
                    if (File.Exists(path))
                    {
                        string text = File.ReadAllText(path).Trim();
                        if (Guid.TryParse(text, out _)) return installId = text;
                    }
                    installId = Guid.NewGuid().ToString();
                    File.WriteAllText(path, installId);
                }
                catch (Exception) { installId = installId ?? Guid.NewGuid().ToString(); }
                return installId;
            }
        }

        public static string DeviceHash
        {
            get
            {
                if (hashed) return deviceHash;
                hashed = true;
                string id = SystemInfo.deviceUniqueIdentifier;
                if (string.IsNullOrEmpty(id) || id == SystemInfo.unsupportedIdentifier) return deviceHash = null;
                using (var sha = SHA256.Create())
                {
                    var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes("dotrpg|" + id));
                    var sb = new StringBuilder(64);
                    foreach (var b in bytes) sb.Append(b.ToString("x2"));
                    return deviceHash = sb.ToString();
                }
            }
        }

        public static Dictionary<string, object> Body()
        {
            var d = new Dictionary<string, object> { ["install_id"] = InstallId };
            if (DeviceHash != null) d["device_hash"] = DeviceHash;
            return d;
        }
    }

    /// <summary>
    /// [ANTI-ABUSE] Presence heartbeat (19.2): before entering the world, every 30 s, and right after a map change. The
    /// server uses it for play time, the current map (kill reports elsewhere are refused) and the two-per-PC limit.
    /// </summary>
    public sealed class PresenceClient : MonoBehaviour
    {
        static PresenceClient instance;
        string character;
        float interval = 30f, nextAt;
        string lastMap;
        float lastInputAt = -999f;
        bool sending;

        static ApiClient Api => ApiClient.Instance;

        static PresenceClient Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("PresenceClient");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<PresenceClient>();
                }
                return instance;
            }
        }

        /// <summary>The first signal for <paramref name="characterId"/> on <paramref name="mapId"/>; done(ok, refusal message).</summary>
        public static void Begin(string characterId, string mapId, Action<bool, string> done)
        {
            var p = Instance;
            p.character = characterId;
            p.lastMap = mapId;
            p.lastInputAt = Time.unscaledTime;
            p.Send(mapId, r =>
            {
                if (r.ok) { done(true, null); return; }
                if (r.code == "DEVICE_LIMIT")
                {
                    int limit = MiniJson.Int(r.errors, "limit", 2);
                    p.character = null;
                    done(false, $"이 PC에서는 동시에 {limit}개까지 접속할 수 있습니다.");
                    return;
                }
                // Anything else (older server, network) does not keep the player out: the server is in log mode first.
                done(true, null);
            });
        }

        /// <summary>Sends a signal now (map change, or a kill report asked for one); then() runs when it is answered.</summary>
        public static void Ping(Action then = null)
        {
            var p = instance;
            if (p == null || string.IsNullOrEmpty(p.character)) { then?.Invoke(); return; }
            p.Send(CurrentMap(), _ => then?.Invoke());
        }

        /// <summary>Leaving to the character list or quitting: tell the server once, no retry.</summary>
        public static void Leave()
        {
            var p = instance;
            if (p == null || string.IsNullOrEmpty(p.character)) return;
            Api.Post($"/characters/{p.character}/presence/leave", new Dictionary<string, object>(), _ => { });
            p.character = null;
        }

        static string CurrentMap() => Game.World != null && !string.IsNullOrEmpty(Game.World.MapId) ? Game.World.MapId : Game.Session != null ? Game.Session.MapId : MapRegistry.Village;

        void Send(string mapId, Action<ApiResult> done)
        {
            if (string.IsNullOrEmpty(character)) { done?.Invoke(new ApiResult { ok = false, code = "NO_CHARACTER" }); return; }
            sending = true;
            nextAt = Time.unscaledTime + interval;
            var body = new Dictionary<string, object>
            {
                ["map_id"] = mapId,
                ["auto_play"] = QuestAutoPilot.Active,
                ["input_recent"] = Time.unscaledTime - lastInputAt <= 30f,
            };
            string who = character;
            Api.Post($"/characters/{who}/presence", body, r =>
            {
                sending = false;
                if (r.ok)
                {
                    float seconds = (float)MiniJson.Num(r.data, "interval_seconds", 30);
                    if (seconds >= 10f) interval = seconds;
                }
                else if (r.code == "DEVICE_LIMIT" && OnlineSession.Playing && who == OnlineSession.Current?.ActiveCharacter)
                {
                    GameEvents.RaiseToast("이 PC에서 동시에 접속할 수 있는 수를 넘었습니다.");
                    Game.Flow.ReturnToTitle();
                }
                done?.Invoke(r);
            });
        }

        void Update()
        {
            if (AnyInput()) lastInputAt = Time.unscaledTime;
            if (string.IsNullOrEmpty(character) || !OnlineSession.Playing) return;
            string map = CurrentMap();
            if (!sending && (map != lastMap || Time.unscaledTime >= nextAt))
            {
                lastMap = map;
                Send(map, null);
            }
        }

        static bool AnyInput()
        {
            var k = Keyboard.current;
            if (k != null && k.anyKey.isPressed) return true;
            var m = Mouse.current;
            if (m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.delta.ReadValue().sqrMagnitude > 1f)) return true;
            var g = Gamepad.current;
            if (g != null && (g.leftStick.ReadValue().sqrMagnitude > 0.04f || g.buttonSouth.isPressed || g.buttonEast.isPressed || g.buttonWest.isPressed || g.buttonNorth.isPressed)) return true;
            return false;
        }
    }
}
