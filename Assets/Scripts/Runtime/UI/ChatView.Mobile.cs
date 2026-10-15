using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [TOUCH] Phones have no Enter key: the on-screen keyboard's Done sends the line, closing the keyboard cancels it.
    /// The box itself folds behind a bubble button (MobileHud).
    /// </summary>
    public partial class ChatView
    {
        void BindTouchKeyboard()
        {
            field.onEndEdit.AddListener(_ =>
            {
                if (!Typing) return;
                var keyboard = field.touchScreenKeyboard;
                if (keyboard != null && keyboard.status == TouchScreenKeyboard.Status.Done) SendTyped();
                else Close();
            });
        }
    }
}
