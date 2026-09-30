using UnityEngine;

namespace DotRPG
{
    /// <summary>[PARTY] Party art: the side-menu "파티" icon.</summary>
    public static partial class ProceduralArt
    {
        /// <summary>24x24 side-menu icon (canvas y = 0 at the top): three companions, the leader in front with a gold star.</summary>
        static PixelCanvas DrawPartyMenuIcon()
        {
            var c = new PixelCanvas(24, 24);
            var skin = PixelCanvas.Hex("#f7c492");
            var mid = PixelCanvas.Hex("#3f86d6");
            var hairM = PixelCanvas.Hex("#d66c30");

            // Back row: warrior (left, orange) and mage (right, violet with a pointed hat).
            Person(c, 5, PixelCanvas.Hex("#e0703c"), PixelCanvas.Hex("#5a3a24"), skin, false);
            Person(c, 18, PixelCanvas.Hex("#8a5ad6"), PixelCanvas.Hex("#d8d8ec"), skin, true);
            // Front: the leader, a bit larger and lower.
            c.Circle(11.5f, 10.5f, 4.2f, skin);           // head
            c.Rect(8, 6, 8, 2, hairM);                     // hair
            c.Rect(7, 7, 2, 3, hairM);
            c.Rect(15, 7, 2, 3, hairM);
            c.Set(10, 11, Outline); c.Set(13, 11, Outline); // eyes
            c.Rect(6, 15, 12, 9, mid);                     // body
            c.HLine(7, 16, 15, PixelCanvas.Shade(mid, 1.25f));
            c.VLine(11, 16, 23, PixelCanvas.Shade(mid, 0.8f));
            // Gold star over the leader.
            c.Set(11, 0, Gold); c.Set(12, 0, Gold); c.HLine(10, 13, 1, Gold); c.Set(11, 2, Gold); c.Set(12, 2, Gold);
            c.Outline(Outline);
            return c;
        }

        static void Person(PixelCanvas c, int cx, Color32 shirt, Color32 hair, Color32 skin, bool hat)
        {
            c.Circle(cx - 0.5f, 8.5f, 3.4f, skin);
            if (hat)
            {
                c.Rect(cx - 4, 5, 7, 2, shirt);
                c.Rect(cx - 2, 3, 3, 2, shirt);
                c.Set(cx - 1, 2, shirt);
            }
            else c.Rect(cx - 3, 5, 6, 2, hair);
            c.Set(cx - 2, 9, Outline); c.Set(cx + 1, 9, Outline);
            c.Rect(cx - 4, 12, 8, 9, shirt);
        }
    }
}
