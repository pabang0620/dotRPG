using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// A question with any number of answers (the yes/no <see cref="ConfirmScreen"/> has two). Esc / the time limit runs
    /// the cancel action. "{초}" in the message shows the seconds left. Opened with <see cref="UIRoot.Choose"/>.
    /// </summary>
    public class ChoiceScreen : MenuScreen
    {
        Text message;
        UIRoot ui;
        Action onCancel;
        float deadline;
        string question;
        const int BodySize = 20;
        public const float Width = 680f;

        public static ChoiceScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Choice", true);
            var screen = root.gameObject.AddComponent<ChoiceScreen>();
            screen.ui = ui;
            screen.BuildPanel(root, L.T("선택"), Width, "\n", BodySize);
            screen.message = screen.panel.Find("Body").GetComponent<Text>();
            screen.menu.OnCancel = screen.Cancel;
            screen.FitPanel();
            // [UI 2026-10-30] "×" in the corner (every popup has one); same as Esc: the cancel action.
            var x = UIFactory.Image(screen.panel, "CloseX", Game.Art.Get("ui_btngray"), Color.white);
            x.preserveAspect = false;
            x.raycastTarget = true;
            UIFactory.Place(x.rectTransform, new Vector2(1f, 1f), new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(44f, 44f));
            var xb = x.gameObject.AddComponent<Button>();
            xb.targetGraphic = x;
            xb.onClick.AddListener(screen.Cancel);
            UiButton.Attach(xb);
            var xt = UIFactory.Text(x.transform, "Text", "×", 26, Color.white, TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(xt.rectTransform);
            xt.horizontalOverflow = HorizontalWrapMode.Overflow;
            return screen;
        }

        public void Setup(string text, IList<(string label, Action act)> options, Action cancel, float timeoutSeconds)
        {
            question = text;
            onCancel = cancel;
            deadline = timeoutSeconds > 0f ? Time.unscaledTime + timeoutSeconds : 0f;
            message.text = deadline > 0f ? question.Replace("{초}", Mathf.CeilToInt(timeoutSeconds).ToString()) : question; // i18n-ignore seconds token
            menu.Clear();
            foreach (var o in options)
            {
                var act = o.act;
                menu.AddButton(o.label, () =>
                {
                    deadline = 0f;
                    onCancel = null;
                    ui.Pop();
                    act?.Invoke();
                });
            }
            // Same layout as ConfirmScreen.Setup: title, body under it, then the menu.
            int lines = Mathf.Max(2, (text ?? "").Split('\n').Length);
            float h = lines * BodySize * 1.45f + 8f;
            // [UI 2026-10-30] A long NPC line wraps: measure the wrapped height too, or it ran into the answers.
            var settings = message.GetGenerationSettings(new Vector2(Width - 70f, 0f));
            float wrapped = message.cachedTextGeneratorForLayout.GetPreferredHeight(message.text, settings) / Mathf.Max(0.01f, message.pixelsPerUnit);
            h = Mathf.Max(h, wrapped + 8f);
            const float bodyTop = 86f;
            var title = panel.Find("Title") as RectTransform;
            if (title != null) title.sizeDelta = new Vector2(Width - 120f, title.sizeDelta.y); // clear of the × button
            message.rectTransform.anchoredPosition = new Vector2(0f, -bodyTop);
            message.rectTransform.sizeDelta = new Vector2(Width - 70f, h);
            float menuTop = bodyTop + h + 10f;
            menu.RectTransform.anchoredPosition = new Vector2(0f, -menuTop);
            panel.sizeDelta = new Vector2(Width, menuTop + menu.Height + 34f);
        }

        void Cancel()
        {
            var c = onCancel;
            onCancel = null;
            deadline = 0f;
            ui.Pop();
            c?.Invoke();
        }

        void Update()
        {
            if (deadline <= 0f) return;
            float left = deadline - Time.unscaledTime;
            if (left <= 0f) { Cancel(); return; }
            message.text = question.Replace("{초}", Mathf.CeilToInt(left).ToString()); // i18n-ignore seconds token
        }

        /// <summary>The question shown and an answer by index (automated checks).</summary>
        public string DevMessage => message.text;
        public void DevAnswer(int index) => menu.Activate(index, 1);
    }
}
