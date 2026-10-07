using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [UI] Sizes of the small buttons that repeat across windows (page arrows, 받기, 모두 받기),
    /// so the same control has the same size and font everywhere.
    /// </summary>
    public static class UiSizes
    {
        /// <summary>◀ / ▶ page arrows under a list.</summary>
        public static readonly Vector2 PageButton = new Vector2(50f, 34f);
        public const int PageFont = 18;

        /// <summary>받기 on one reward or one mail.</summary>
        public static readonly Vector2 ClaimButton = new Vector2(140f, 44f);
        public const int ClaimFont = 18;

        /// <summary>모두 받기 (mailbox, auction mail tab).</summary>
        public static readonly Vector2 ClaimAllButton = new Vector2(200f, 48f);
        public const int ClaimAllFont = 20;
    }
}
