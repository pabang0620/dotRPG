using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>[SERVER] How the online session ends from outside the menus: closing the game window and account withdrawal.</summary>
    public sealed partial class OnlineSession
    {
        /// <summary>Longest the game stays open at quit to send a waiting state (then it quits anyway).</summary>
        const float QuitWaitSeconds = 2.5f;
        static bool quitWaiting, quitReleased;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void HookQuit()
        {
            quitWaiting = quitReleased = false;
            Application.wantsToQuit -= OnWantsToQuit;
            Application.wantsToQuit += OnWantsToQuit;
        }

        /// <summary>[J3] The window is closed (or the process asked to quit): one try to send the waiting state, at most <see cref="QuitWaitSeconds"/>.</summary>
        static bool OnWantsToQuit()
        {
#if UNITY_EDITOR
            return true; // leaving play mode in the editor is never held back
#else
            var session = Current;
            if (quitReleased || session == null || !session.HasUnsent) return true;
            if (!quitWaiting)
            {
                quitWaiting = true;
                session.FlushPending();
                Api.StartCoroutine(QuitWhenSent(session));
            }
            return false;
#endif
        }

        static IEnumerator QuitWhenSent(OnlineSession session)
        {
            float until = Time.realtimeSinceStartup + QuitWaitSeconds;
            while (session.HasUnsent && Time.realtimeSinceStartup < until) yield return null;
            quitReleased = true;
            Application.Quit();
        }

        /// <summary>
        /// The account is in withdrawal (W2 answered, or the server closed the socket with 4012): drop the tokens and the session,
        /// leave the world if an online character was playing and, then, open the login screen. Nothing more is uploaded.
        /// </summary>
        public static void EndByWithdrawal(bool openLogin)
        {
            var session = Current;
            bool playing = Playing;
            session?.Abandon();
            if (playing && Game.Flow != null) Game.Flow.ReturnToTitle(); // saves nothing to the server (abandoned) and no file
            Api.ClearTokens();
            Current = null;
            if (openLogin && Game.UI != null) Api.StartCoroutine(OpenLoginAfterTitle());
        }

        static IEnumerator OpenLoginAfterTitle()
        {
            yield return null;
            while (Game.Flow != null && Game.Flow.IsTransitioning) yield return null;
            if (Game.State != null && Game.State.Current == GameState.Title && Game.UI.OnlineLogin != null && Game.UI.Top == Game.UI.Title)
                Game.UI.Push(Game.UI.OnlineLogin);
        }
    }
}
