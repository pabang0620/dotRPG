using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    public class ConfirmScreen : MenuScreen
    {
        Text message;
        Action onYes;
        UIRoot ui;
        bool keepOpen, closing;
        // Options for the next question only (set through UIRootConfirmExtensions, cleared by Setup).
        bool nextDefaultYes, defaultYes;
        Action nextOnNo, onNo;
        float nextTimeout, deadline;
        string question;

        public static ConfirmScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Confirm", true);
            var screen = root.gameObject.AddComponent<ConfirmScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, "확인", 560, "\n", 20);
            screen.message = screen.panel.Find("Body").GetComponent<Text>();
            screen.menu.AddButton("예", () =>
            {
                if (screen.closing) return;
                var action = screen.onYes;
                if (screen.keepOpen)
                {
                    // Quitting: the dialog stays up (no flash of the game) while the last save goes out.
                    screen.closing = true;
                    screen.message.text = "게임을 종료하는 중...";
                    action?.Invoke();
                    return;
                }
                screen.onNo = null;
                screen.ui.Pop();
                action?.Invoke();
            });
            screen.menu.AddButton("아니오", screen.AnswerNo);
            screen.menu.OnCancel = screen.AnswerNo;
            screen.FitPanel();
            return screen;
        }

        // [ENH] Width per question and a body that grows with its line count.
        public const float DefaultWidth = 560f, WideWidth = 820f;
        const int BodySize = 20;

        public void Setup(string text, Action yes, float width = DefaultWidth, bool keepOpenOnYes = false)
        {
            message.text = question = text;
            onYes = yes;
            keepOpen = keepOpenOnYes;
            closing = false;
            defaultYes = nextDefaultYes; onNo = nextOnNo;
            deadline = nextTimeout > 0f ? Time.unscaledTime + nextTimeout : 0f;
            nextDefaultYes = false; nextOnNo = null; nextTimeout = 0f;
            if (deadline > 0f) message.text = question.Replace("{초}", Mathf.CeilToInt(deadline - Time.unscaledTime).ToString());
            // Same layout as BuildPanel: title, body under it, then the menu.
            int lines = Mathf.Max(2, (text ?? "").Split('\n').Length);
            float h = lines * BodySize * 1.45f + 8f;
            const float bodyTop = 86f;
            panel.sizeDelta = new Vector2(width, panel.sizeDelta.y);
            var title = panel.Find("Title") as RectTransform;
            if (title != null) title.sizeDelta = new Vector2(width - 40f, title.sizeDelta.y);
            message.rectTransform.anchoredPosition = new Vector2(0f, -bodyTop);
            message.rectTransform.sizeDelta = new Vector2(width - 70f, h);
            float menuTop = bodyTop + h + 10f;
            menu.RectTransform.anchoredPosition = new Vector2(0f, -menuTop);
            panel.sizeDelta = new Vector2(width, menuTop + menu.Height + 34f);
        }

        // ---------- Developer automation (DevCapture) ----------

        /// <summary>The question shown (rich text).</summary>
        public string DevMessage => message.text;

        /// <summary>Answers through the same menu path as a click on "예" / "아니오".</summary>
        public void DevAnswer(bool yes) => menu.Activate(yes ? 0 : 1, 1);

        /// <summary>
        /// Options for the next <see cref="Setup"/> only: cursor on "예", an action for "아니오" / Esc,
        /// and a time limit after which the dialog closes as "아니오".
        /// </summary>
        public void PrepareNext(bool defaultYes, Action onNo = null, float timeoutSeconds = 0f)
        {
            nextDefaultYes = defaultYes;
            nextOnNo = onNo;
            nextTimeout = timeoutSeconds;
        }

        void AnswerNo()
        {
            if (closing) return;
            var action = onNo;
            onNo = null;
            deadline = 0f;
            ui.Pop();
            action?.Invoke();
        }

        void Update()
        {
            if (deadline <= 0f || closing) return;
            float left = deadline - Time.unscaledTime;
            if (left <= 0f) { AnswerNo(); return; }
            message.text = question.Replace("{초}", Mathf.CeilToInt(left).ToString());
        }

        public override void Show()
        {
            base.Show();
            // Default to "아니오" for destructive questions (unless the caller asked for "예").
            if (!defaultYes) menu.Select(1);
        }
    }
}
