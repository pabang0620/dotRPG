using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [NET 2026-10-30] A small line at the top while an online connection is coming back (<see cref="NetRecovery.Reconnecting"/>).
    /// Shown after a short delay so a blip of a second does not flash it; sits under the match queue bar.
    /// </summary>
    public class NetStatusIndicator : MonoBehaviour
    {
        const float ShowAfter = 2f;
        GameObject bar;
        Text text;
        float downFor;

        public static NetStatusIndicator Create(Transform canvas)
        {
            var root = UIFactory.Place(UIFactory.Rect(canvas, "NetStatus"), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -46f), new Vector2(300f, 28f));
            var v = root.gameObject.AddComponent<NetStatusIndicator>();
            var bg = UIFactory.Image(root, "Bg", Game.Art.Get("ui_white"), new Color32(18, 26, 40, 210));
            bg.preserveAspect = false;
            bg.raycastTarget = false;
            UIFactory.Stretch(bg.rectTransform);
            v.bar = bg.gameObject;
            v.text = UIFactory.Text(bg.transform, "Text", L.T("서버에 다시 연결하는 중..."), 16, new Color32(255, 224, 102, 255), TextAnchor.MiddleCenter, true);
            UIFactory.Stretch(v.text.rectTransform);
            v.bar.SetActive(false);
            return v;
        }

        void Update()
        {
            bool down = NetRecovery.Reconnecting && Game.State != null && Game.State.Current != GameState.Title;
            downFor = down ? downFor + Time.unscaledDeltaTime : 0f;
            bool show = downFor >= ShowAfter;
            if (bar.activeSelf != show) bar.SetActive(show);
        }
    }
}
