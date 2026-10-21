using UnityEngine;

namespace DotRPG
{
    /// <summary>Offline, paged third-party notices; usable with keyboard, mouse and controller.</summary>
    public sealed class CreditsScreen : MenuScreen
    {
        UIRoot ui;
        string[] lines;
        int page;
        const int LinesPerPage = 12;

        public static CreditsScreen Create(Transform canvas, UIRoot ui)
        {
            var root = CreateRoot(canvas, "Credits", true);
            var screen = root.gameObject.AddComponent<CreditsScreen>();
            screen.ui = ui;
            var text = Resources.Load<TextAsset>("ThirdPartyNotices");
            screen.lines = (text != null ? text.text : L.T("볼론")).Replace("\r", "").Split('\n');
            screen.Rebuild();
            return screen;
        }

        void Rebuild()
        {
            if (panel != null) { panel.gameObject.SetActive(false); Destroy(panel.gameObject); }
            int pages = Mathf.Max(1, (lines.Length + LinesPerPage - 1) / LinesPerPage);
            int start = page * LinesPerPage;
            string body = string.Join("\n", lines, start, Mathf.Min(LinesPerPage, lines.Length - start));
            BuildPanel((RectTransform)transform, "Credits / Licenses  " + (page + 1) + " / " + pages, 1050, body, 16);
            if (page + 1 < pages) menu.AddButton("Next →", () => { page++; Rebuild(); });
            if (page > 0) menu.AddButton("← Previous", () => { page--; Rebuild(); });
            menu.AddButton(L.T("돌아가기"), () => ui.Pop());
            menu.OnCancel = () => ui.Pop();
            FitPanel();
            FitToScreen();
            menu.ResetSelection();
        }
    }
}
