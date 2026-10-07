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

        public static ApiResult Network(string message) => new ApiResult { ok = false, status = 0, code = "NETWORK", message = message };
    }

    /// <summary>
    /// [SERVER] HTTP client for the game server (Docs/server/phase1_2_api.md). Adds X-Client-Version and
    /// X-Data-Version to every request, keeps the access token in memory and the refresh token in PlayerPrefs,
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
        const string RefreshKey = "dotrpg.refresh", ServerKey = "dotrpg.server";
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
            get { try { return PlayerPrefs.GetString(RefreshKey, ""); } catch (Exception) { return ""; } }
            private set { try { PlayerPrefs.SetString(RefreshKey, value ?? ""); PlayerPrefs.Save(); } catch (Exception) { } }
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
                    dataVersion = System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path).Trim() : asset != null ? asset.text.Trim() : "";
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
