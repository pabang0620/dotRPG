using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace DotRPG
{
    /// <summary>[SERVER] One API answer: data on success, the server's errors.code and Korean message on failure.</summary>
    public sealed class ApiResult
    {
        public bool ok;
        public long status;
        public Dictionary<string, object> data;
        public Dictionary<string, object> errors;
        public string code;
        public string message;
        /// <summary>Seconds from the Retry-After header (429 / 503), 0 when absent.</summary>
        public float retryAfter;

        public static ApiResult Network(string message) => new ApiResult { ok = false, status = 0, code = "NETWORK", message = message };
    }

    /// <summary>
    /// [SERVER] HTTP client for the game server (Docs/server/phase1_2_api.md). Adds X-Client-Version and
    /// X-Data-Version to every request, keeps the access token in memory and the refresh token in PlayerPrefs (encrypted with DPAPI on Windows, see TokenVault),
    /// and refreshes once (one refresh at a time) when the server answers TOKEN_EXPIRED.
    /// Server address: -dotrpgServer &lt;url&gt; on the command line, else PlayerPrefs "dotrpg.server", else localhost.
    /// </summary>
    public class ApiClient : MonoBehaviour
    {
        public const string DefaultServer = "http://127.0.0.1:3000";
        /// <summary>
        /// [RELEASE] The live server (https). A release build (BuildScript.BuildWindowsRelease, define DOTRPG_RELEASE)
        /// always uses it and ignores -dotrpgServer / PlayerPrefs; that build fails while this is not an https address.
        /// </summary>
        public const string ReleaseServer = "";
        const string ServerKey = "dotrpg.server";
        const int TimeoutSeconds = 10;

        static ApiClient instance;
        public static ApiClient Instance
        {
            get
            {
                if (instance == null)
                {
                    var go = new GameObject("ApiClient");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<ApiClient>();
                }
                return instance;
            }
        }

        public string BaseUrl { get; private set; }
        public string AccessToken { get; private set; }
        public string RefreshToken
        {
            get => TokenVault.Load(); // [SECURITY] DPAPI-encrypted on Windows, plain PlayerPrefs elsewhere
            private set => TokenVault.Save(value);
        }
        public bool HasRefreshToken => !string.IsNullOrEmpty(RefreshToken);

        public static string ClientVersion => Application.version;
        static string dataVersion;
        public static string DataVersion
        {
            get
            {
                if (dataVersion == null)
                {
                    var asset = Resources.Load<TextAsset>("Data/DataVersion");
                    var path = System.IO.Path.Combine(Application.streamingAssetsPath, "DataVersion.txt");
                    dataVersion = StreamingFiles.Exists(path) ? StreamingFiles.ReadAllText(path).Trim() : asset != null ? asset.text.Trim() : "";
                }
                return dataVersion;
            }
        }

        bool refreshing;

        void Awake()
        {
#if DOTRPG_RELEASE
            BaseUrl = ReleaseServer.TrimEnd('/');
#else
            string url = null;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++) if (args[i] == "-dotrpgServer") url = args[i + 1];
            if (string.IsNullOrEmpty(url)) { try { url = PlayerPrefs.GetString(ServerKey, ""); } catch (Exception) { } }
            BaseUrl = (string.IsNullOrEmpty(url) ? DefaultServer : url).TrimEnd('/');
#endif
        }

#if !DOTRPG_RELEASE
        /// <summary>
        /// [ANDROID] Development builds: the login screen sets the server address on a phone (no command line there).
        /// Stored under the same PlayerPrefs key Awake reads; empty goes back to the default. A new address drops the old tokens.
        /// </summary>
        public void SetServer(string url)
        {
            url = (url ?? "").Trim().TrimEnd('/');
            if (url.Length > 0 && !url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) url = "http://" + url;
            try
            {
                if (url.Length == 0) PlayerPrefs.DeleteKey(ServerKey); else PlayerPrefs.SetString(ServerKey, url);
                PlayerPrefs.Save();
            }
            catch (Exception) { }
            string next = url.Length == 0 ? DefaultServer : url;
            if (next == BaseUrl) return;
            BaseUrl = next;
            ClearTokens();
        }
#endif

        public void SetTokens(string access, string refresh)
        {
            AccessToken = access;
            if (refresh != null) RefreshToken = refresh;
        }

        public void ClearTokens()
        {
            AccessToken = null;
            RefreshToken = "";
        }

        // ---------------- requests ----------------

        public void Get(string path, Action<ApiResult> done, bool auth = true) => StartCoroutine(Send("GET", path, null, auth, done, true));
        public void Post(string path, object body, Action<ApiResult> done, bool auth = true) => StartCoroutine(Send("POST", path, body, auth, done, true));
        public void Put(string path, object body, Action<ApiResult> done, bool auth = true) => StartCoroutine(Send("PUT", path, body, auth, done, true));
        public void Patch(string path, object body, Action<ApiResult> done, bool auth = true) => StartCoroutine(Send("PATCH", path, body, auth, done, true));
        public void Delete(string path, Action<ApiResult> done, bool auth = true) => StartCoroutine(Send("DELETE", path, null, auth, done, true));

        /// <summary>
        /// [J2] POST for a body that carries a request_id (money paths). A lost answer (NETWORK) is sent again up to 3 times
        /// after 1 s, 2 s, 4 s with the SAME body and request_id, so the server replays the stored result instead of granting twice.
        /// 429 / 503 with a Retry-After header wait that long (at most 10 s) and use the same 3 tries. Other refusals (4xx) are never resent.
        /// A body without request_id is sent once.
        /// </summary>
        public void PostIdempotent(string path, Dictionary<string, object> body, Action<ApiResult> done) => StartCoroutine(SendIdempotent(path, body, done));

        static readonly float[] IdempotentDelays = { 1f, 2f, 4f };
        const float MaxRetryAfterSeconds = 10f;

        IEnumerator SendIdempotent(string path, Dictionary<string, object> body, Action<ApiResult> done)
        {
            bool keyed = body != null && body.ContainsKey("request_id");
            for (int attempt = 0; ; attempt++)
            {
                ApiResult result = null;
                yield return Send("POST", path, body, true, r => result = r, true);
                float wait = -1f;
                if (keyed && attempt < IdempotentDelays.Length)
                {
                    if (result.code == "NETWORK") wait = IdempotentDelays[attempt];
                    else if ((result.status == 429 || result.status == 503) && result.retryAfter > 0f) wait = Mathf.Min(result.retryAfter, MaxRetryAfterSeconds);
                }
                if (wait < 0f)
                {
                    // The server may have processed it: the answer was lost, not necessarily the action.
                    if (keyed && result.code == "NETWORK") result.message = "결과를 확인하는 중 연결이 끊겼습니다. 다시 접속하면 반영됩니다.";
                    done?.Invoke(result);
                    yield break;
                }
                yield return new WaitForSecondsRealtime(wait);
            }
        }

        IEnumerator Send(string method, string path, object body, bool auth, Action<ApiResult> done, bool mayRefresh)
        {
            ApiResult result = null;
            yield return Raw(method, path, body, auth, r => result = r);
            // Expired access token: refresh once (serialized), then repeat the request.
            if (auth && mayRefresh && result.status == 401 && result.code == "TOKEN_EXPIRED" && HasRefreshToken)
            {
                bool refreshed = false;
                yield return RefreshOnce(ok => refreshed = ok);
                if (refreshed)
                {
                    yield return Raw(method, path, body, auth, r => result = r);
                }
            }
            done?.Invoke(result);
        }

        /// <summary>Exchanges the stored refresh token for a new pair. Concurrent callers wait for the one in flight.</summary>
        public IEnumerator RefreshOnce(Action<bool> done)
        {
            if (refreshing)
            {
                while (refreshing) yield return null;
                done(!string.IsNullOrEmpty(AccessToken));
                yield break;
            }
            refreshing = true;
            ApiResult r = null;
            yield return Raw("POST", "/auth/refresh", new Dictionary<string, object> { ["refresh_token"] = RefreshToken, ["device"] = DeviceIdentity.Body() }, false, x => r = x);
            if (r.ok)
            {
                // Store the new refresh token before dropping the old one (phase1_2_api §2.5 client rule).
                SetTokens(MiniJson.Str(r.data, "access_token"), MiniJson.Str(r.data, "refresh_token"));
            }
            else if (r.code != null && r.code.StartsWith("REFRESH_"))
            {
                ClearTokens();
            }
            refreshing = false;
            done(r.ok);
        }

        IEnumerator Raw(string method, string path, object body, bool auth, Action<ApiResult> done)
        {
            using (var req = new UnityWebRequest(BaseUrl + path, method))
            {
                if (body != null)
                {
                    req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(MiniJson.Write(body)));
                    req.SetRequestHeader("Content-Type", "application/json");
                }
                req.downloadHandler = new DownloadHandlerBuffer();
                req.timeout = TimeoutSeconds;
                req.SetRequestHeader("X-Client-Version", ClientVersion);
                req.SetRequestHeader("X-Data-Version", DataVersion);
                if (auth && !string.IsNullOrEmpty(AccessToken)) req.SetRequestHeader("Authorization", "Bearer " + AccessToken);
                yield return req.SendWebRequest();

                if (req.result == UnityWebRequest.Result.ConnectionError || req.result == UnityWebRequest.Result.DataProcessingError)
                {
                    done(ApiResult.Network("서버에 연결할 수 없습니다."));
                    yield break;
                }
                var result = new ApiResult { status = req.responseCode };
                object json = null;
                try { json = MiniJson.Parse(req.downloadHandler.text); }
                catch (Exception) { /* not JSON (proxy error page etc.) */ }
                result.ok = json != null && MiniJson.Has(json, "success") && ((Dictionary<string, object>)json)["success"] is bool s && s;
                result.data = MiniJson.Obj(json, "data");
                result.errors = MiniJson.Obj(json, "errors");
                result.code = MiniJson.Str(result.errors, "code");
                if (float.TryParse(req.GetResponseHeader("Retry-After"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float after) && after > 0f) result.retryAfter = after;
                result.message = MiniJson.Str(json, "message") ?? (result.ok ? "" : $"서버 오류 ({req.responseCode})");
                if (!result.ok && result.status == 426) result.code = result.code ?? "CLIENT_OUTDATED";
                if (result.code == "SESSION_REPLACED" && Time.unscaledTime - replacedAt > 5f)
                {
                    replacedAt = Time.unscaledTime;
                    ClearTokens();
                    SessionReplaced?.Invoke();
                }
                done(result);
            }
        }

        /// <summary>[ANTI-ABUSE] The account logged in somewhere else: this session is over (no refresh, no retry).</summary>
        public static event Action SessionReplaced;
        static float replacedAt = -99f;

        /// <summary>A new request_id (one per user action; resend the same id on retry).</summary>
        public static string NewRequestId() => Guid.NewGuid().ToString();
    }
}
