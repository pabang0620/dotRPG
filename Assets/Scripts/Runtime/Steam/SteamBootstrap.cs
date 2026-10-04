using System;
using Steamworks;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [PARTY 8] Starts Steam when the game runs under the Steam client (Standalone builds with
    /// Steamworks.NET). If Steam is not running the game carries on without it and combat uses our relay.
    /// Registers itself as <see cref="SteamBridge.Current"/> so the runtime assembly never references Steam.
    /// The app id comes from the Steam client (steam_appid.txt only in test builds); it is never written in code.
    /// </summary>
    public sealed class SteamBootstrap : MonoBehaviour, ISteamBridge
    {
        static SteamBootstrap instance;
        Callback<GetTicketForWebApiResponse_t> ticketCallback;
        Action<string> pendingTicket;
        HAuthTicket pendingHandle = HAuthTicket.Invalid;

        public bool Ready { get; private set; }
        public ulong SteamId { get; private set; }
        public uint AppId { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            var go = new GameObject("Steam");
            DontDestroyOnLoad(go);
            instance = go.AddComponent<SteamBootstrap>();
            instance.Init();
        }

        void Init()
        {
            try
            {
                if (!Packsize.Test() || !DllCheck.Test()) { Debug.LogWarning("[STEAM] wrong Steamworks binaries; Steam off"); return; }
                if (!SteamAPI.Init()) { Debug.Log("[STEAM] Steam is not running; playing without Steam"); return; }
            }
            catch (DllNotFoundException e) { Debug.LogWarning("[STEAM] steam_api missing: " + e.Message); return; }
            Ready = true;
            SteamId = SteamUser.GetSteamID().m_SteamID;
            AppId = SteamUtils.GetAppID().m_AppId;
            SteamNetworkingUtils.InitRelayNetworkAccess(); // Valve relay (SDR): no port forwarding between friends
            ticketCallback = Callback<GetTicketForWebApiResponse_t>.Create(OnTicket);
            SteamBridge.Current = this;
            Debug.Log($"[STEAM] ready: app {AppId}, user {SteamId}");
        }

        void Update()
        {
            if (Ready) SteamAPI.RunCallbacks();
        }

        void OnDestroy()
        {
            if (!Ready) return;
            if (SteamBridge.Current == (ISteamBridge)this) SteamBridge.Current = null;
            SteamAPI.Shutdown();
            Ready = false;
        }

        /// <summary>Web API ticket for the server's /auth/steam (identity comes from /meta.steam.identity).</summary>
        public void GetAuthTicket(string identity, Action<string> done)
        {
            if (!Ready) { done?.Invoke(null); return; }
            if (pendingHandle != HAuthTicket.Invalid) SteamUser.CancelAuthTicket(pendingHandle);
            pendingTicket = done;
            pendingHandle = SteamUser.GetAuthTicketForWebApi(identity);
        }

        void OnTicket(GetTicketForWebApiResponse_t r)
        {
            if (r.m_hAuthTicket != pendingHandle) return;
            var done = pendingTicket;
            pendingTicket = null;
            if (r.m_eResult != EResult.k_EResultOK) { done?.Invoke(null); return; }
            var hex = new System.Text.StringBuilder(r.m_cubTicket * 2);
            for (int i = 0; i < r.m_cubTicket; i++) hex.Append(r.m_rgubTicket[i].ToString("x2"));
            done?.Invoke(hex.ToString());
        }

        /// <summary>The server finished checking the ticket: it must not be used again.</summary>
        public void CancelTicket()
        {
            if (pendingHandle == HAuthTicket.Invalid) return;
            SteamUser.CancelAuthTicket(pendingHandle);
            pendingHandle = HAuthTicket.Invalid;
        }

        public ITransport CreateTransport(RoomTransportInfo info) => Ready ? new SteamP2PTransport(info, SteamId) : null;
    }
}
