using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Keeps a stretched RectTransform inside Screen.safeArea (notches, rounded corners, system bars).
    /// On a PC the safe area is the whole screen, so the anchors stay 0..1 and nothing changes.
    /// </summary>
    public sealed class SafeAreaFitter : MonoBehaviour
    {
        Rect applied;
        Vector2Int appliedScreen;

        /// <summary>A stretched child of <paramref name="parent"/> that follows the safe area (the window layout goes inside it, the background stays full-screen).</summary>
        public static RectTransform Wrap(Transform parent)
        {
            var rt = UIFactory.Stretch(UIFactory.Rect(parent, "Safe"));
            rt.gameObject.AddComponent<SafeAreaFitter>();
            return rt;
        }

        void OnEnable() => Apply();

        void Update()
        {
            if (Screen.safeArea != applied || Screen.width != appliedScreen.x || Screen.height != appliedScreen.y) Apply();
        }

        void Apply()
        {
            var rt = transform as RectTransform;
            if (rt == null || Screen.width <= 0 || Screen.height <= 0) return;
            var area = Screen.safeArea;
            applied = area;
            appliedScreen = new Vector2Int(Screen.width, Screen.height);
            rt.anchorMin = new Vector2(Mathf.Clamp01(area.xMin / Screen.width), Mathf.Clamp01(area.yMin / Screen.height));
            rt.anchorMax = new Vector2(Mathf.Clamp01(area.xMax / Screen.width), Mathf.Clamp01(area.yMax / Screen.height));
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
