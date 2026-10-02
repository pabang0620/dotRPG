using System.Collections;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [F2] <c>-dotrpgNetPair &lt;folder&gt; -dotrpgNet host|join</c>: run two windows at once; each waits for the
    /// other, walks a little, says hello in chat and checks that it sees the other hero and the other's line.
    /// Report lines "NET &lt;what&gt; PASS|FAIL" and "NET summary: N passed, M failed".
    /// </summary>
    public partial class DevCapture
    {
        int netPassed, netFailed;

        void NCheck(string what, bool ok)
        {
            if (ok) netPassed++;
            else netFailed++;
            log?.WriteLine($"NET {what} {(ok ? "PASS" : "FAIL")}");
        }

        IEnumerator NetPairRun()
        {
            Game.Config.autosave = false;
            var net = NetPresence.Instance;
            if (net == null) { NCheck("no -dotrpgNet host|join argument", false); yield break; }
            bool host = net.IsHost;
            yield return Wait(1.5f);
            Game.Flow.NewGame(host ? CharacterClass.Warrior : CharacterClass.Mage);
            yield return Wait(2f);
            if (Game.State.Current != GameState.Playing) Game.State.Set(GameState.Playing);

            float t = 0f;
            while (!net.Connected && t < 40f) { t += 0.25f; yield return Wait(0.25f); }
            NCheck($"windows connected: host={host} after={t:0.0}s sent={net.Sent} received={net.Received}", net.Connected);

            // Step apart so both heroes are visible side by side.
            Game.Input.MoveOverride = host ? Vector2.left : Vector2.right;
            yield return Wait(0.8f);
            Game.Input.MoveOverride = null;
            yield return Wait(1.5f);

            string hello = host ? "호스트 창에서 인사합니다" : "참가 창에서 인사합니다";
            string err = OnlineServices.Chat.Send(ChatChannel.General, hello);
            t = 0f;
            ChatLine got = null;
            while (got == null && t < 15f)
            {
                got = OnlineServices.Chat.Lines.LastOrDefault(l => !l.mine && l.text.Contains("창에서 인사합니다"));
                t += 0.25f;
                yield return Wait(0.25f);
            }
            NCheck($"other window's chat arrives: sent={(err ?? "ok")} got='{got?.from}: {got?.text}'", err == null && got != null);
            var me = Game.Player.Position;
            NCheck($"other hero visible: ghost={net.GhostVisible} remote={net.RemoteName} map={net.RemoteMap} at=({net.RemotePosition.x:0.0},{net.RemotePosition.y:0.0}) me=({me.x:0.0},{me.y:0.0})",
                net.GhostVisible && net.RemoteMap == Game.Session.MapId && (net.RemotePosition - me).magnitude < 12f);
            yield return Shot(host ? "net_host" : "net_join");
            // Stay up so the other window can finish its own checks.
            yield return Wait(6f);
            log.WriteLine($"NET summary: {netPassed} passed, {netFailed} failed");
        }
    }
}
