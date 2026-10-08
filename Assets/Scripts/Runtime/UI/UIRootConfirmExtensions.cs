using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>[UX] <c>Game.UI.Confirm</c> with a default answer, an action for "아니오" and an optional time limit.</summary>
    public static class UIRootConfirmExtensions
    {
        /// <summary>
        /// Call with the named argument <c>defaultYes:</c> (positional bools still go to <see cref="UIRoot.Confirm"/>).
        /// Same as <see cref="UIRoot.Confirm"/>, plus: <paramref name="defaultYes"/> puts the cursor on "예";
        /// <paramref name="onNo"/> runs on "아니오" / Esc / time out; <paramref name="timeoutSeconds"/> &gt; 0 answers
        /// "아니오" by itself after that many seconds ("{초}" in the message shows the seconds left).
        /// </summary>
        public static void Confirm(this UIRoot ui, string message, Action onYes, bool defaultYes, Action onNo = null, float timeoutSeconds = 0f, bool overlay = false)
        {
            ui.ConfirmDialog.PrepareNext(defaultYes, onNo, timeoutSeconds);
            ui.Confirm(message, onYes, overlay);
        }
    }
}
