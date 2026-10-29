using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    public partial class DevCapture
    {
        // Invoke after the offline level fixture and HUD capture, before opening any inventory window.
        IEnumerator UiAuditHudStress()
        {
            string expected = Path.GetFullPath(Path.Combine(folder, "saves"));
            if (OnlineSession.Playing || string.IsNullOrEmpty(SaveSystem.DirectoryOverride)
                || !string.Equals(Path.GetFullPath(SaveSystem.DirectoryOverride), expected, StringComparison.OrdinalIgnoreCase)
                || Game.Player == null || uiAudit == null)
            { Log("UI AUDIT HUD STRESS ABORT: offline fixture isolation missing"); yield break; }

            var chat = Game.UI.GetComponentInChildren<ChatView>(true);
            if (chat != null)
            {
                // Presentation API only: never call Send or QuickSignal, which can contact a server.
                const string longChat = "긴말풍선검수용문구입니다동료들과함께던전을공략하고보상을확인해주세요"; // i18n-ignore offline fixture
                chat.ShowBubble(longChat);
                yield return UiAuditCapture("hud_long_chat", null);
                var bubble = UiAuditFind(Game.UI.transform, "ChatBubble");
                UiAuditEvidence(bubble != null && UiAuditStressHasText(bubble, longChat), "ShowBubble exact fixture text: " + longChat);
                yield return Wait(2.6f);
            }
            else Log("UI AUDIT HUD STRESS SKIP: ChatView missing");

            var buffs = Game.UI.GetComponentInChildren<BuffBarView>(true);
            if (buffs != null)
            {
                var player = Game.Player;
                var career = CareerCombat.For(player);
                // Transient, offline-only statuses populate the real row through its normal LateUpdate.
                player.Stun(15f);
                career.AddInvulnerable(15f);
                career.AddShield(123456, 15f, Career.Arcanist);
                career.AddGuard(30, 15f);
                career.AddCurse(15f);
                try
                {
                    yield return null;
                    yield return new WaitForEndOfFrame();
                    Transform last = null;
                    int active = 0;
                    foreach (Transform child in buffs.transform)
                        if (child.name.StartsWith("Back", StringComparison.Ordinal) && child.gameObject.activeInHierarchy)
                        { last = child; active++; }
                    bool pressed = last != null && ExecuteEvents.Execute(last.gameObject,
                        new PointerEventData(EventSystem.current), ExecuteEvents.pointerDownHandler);
                    yield return UiAuditCapture("hud_later_buff_tooltip", null);
                    var tip = UiAuditFind(buffs.transform, "Tip");
                    string rendered = "";
                    if (tip != null)
                        foreach (var text in tip.GetComponentsInChildren<Text>()) rendered += text.text + " ";
                    UiAuditEvidence(pressed && active >= 5 && tip != null && tip.gameObject.activeInHierarchy && rendered.Trim().Length > 0,
                        "Real status APIs; active chips=" + active + "; selected=" + (last != null ? last.name : "missing") + "; tooltip=" + rendered);
                }
                finally { career.ResetState(); player.ClearStun(); }
                yield return Wait(2f);
            }
            else Log("UI AUDIT HUD STRESS SKIP: BuffBarView missing");

            // Minimap labels refresh only on a new map texture. Do not mutate catalog data or private UI fields here.
            Log("UI AUDIT HUD STRESS UNVERIFIED: synthetic long region name requires an isolated map-transition fixture");
        }

        static bool UiAuditStressHasText(Transform root, string expected)
        {
            foreach (var text in root.GetComponentsInChildren<Text>())
                if (text.isActiveAndEnabled && text.text.Replace("\n", "") == expected) return true;
            return false;
        }
    }
}
