using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [TOUCH] The settings list is taller than a phone screen: shrunk to fit, its 46 px rows got too small to tap. On a
    /// phone the rows go into two columns of thumb-height rows (the panel then fits at about 0.8: rows stay over 44).
    /// Keyboard / pad order is unchanged (down the left column, then the right one).
    /// </summary>
    public static class MobileSettings
    {
        const float RowH = 56f, RowStep = 60f, ColumnGap = 20f, SideMargin = 80f, BottomMargin = 34f;

        /// <summary>Call after MenuScreen.FitPanel: <paramref name="panel"/> wraps <paramref name="menu"/> plus its margins.</summary>
        public static void TwoColumns(MenuList menu, RectTransform panel)
        {
            if (menu == null || panel == null) return;
            var list = menu.RectTransform;
            int count = list.childCount;
            if (count < 2) return;
            int perColumn = (count + 1) / 2;
            float rowW = list.sizeDelta.x;
            float menuTop = panel.sizeDelta.y - menu.Height - BottomMargin; // FitPanel: top + rows + bottom margin
            for (int i = 0; i < count; i++)
            {
                var row = list.GetChild(i) as RectTransform;
                if (row == null) continue;
                int column = i / perColumn;
                row.anchoredPosition = new Vector2((column == 0 ? -1f : 1f) * (rowW + ColumnGap) * 0.5f, -(i % perColumn) * RowStep);
                row.sizeDelta = new Vector2(rowW, RowH);
                foreach (var arrow in new[] { "ArrowLeft", "ArrowRight" })
                    if (row.Find(arrow) is RectTransform a) a.sizeDelta = new Vector2(a.sizeDelta.x, RowH);
            }
            list.sizeDelta = new Vector2(rowW * 2f + ColumnGap, perColumn * RowStep);
            panel.sizeDelta = new Vector2(rowW * 2f + ColumnGap + SideMargin, menuTop + perColumn * RowStep + BottomMargin);
        }
    }
}
