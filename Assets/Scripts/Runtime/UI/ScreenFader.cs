using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>Full-screen fade used for scene-like transitions (runs on unscaled time).</summary>
    public class ScreenFader : MonoBehaviour
    {
        Image image;
        // [E4] Loading card shown on the black screen while a dungeon room is built: banner, name, a tip.
        CanvasGroup card;
        Image cardBanner;
        Text cardTitle, cardTip;
        static readonly string[] Tips =
        {
            "보스가 바닥에 그리는 범위가 차오르면 그 자리를 벗어나세요.",
            "+10부터는 강화에 실패하면 장비가 부서질 수 있습니다. 장비 보호권을 챙기세요.",
            "해골왕의 사령 토템 두 개는 8초 안에 함께 부숴야 다시 일어나지 않습니다.",
            "망자의 심판은 피할 수 없습니다. 시전 중에 때려서 끊으세요.",
            "요일 던전은 하루 3번. 주말에는 모든 던전이 열립니다.",
            "깔끔하고 빠르게 깰수록 랭크가 오르고 보상 카드가 좋아집니다.",
        };

        public bool IsBusy { get; private set; }

        public static ScreenFader Create(Transform canvas)
        {
            var img = UIFactory.Overlay(canvas, "Fader", new Color(0.05f, 0.04f, 0.06f, 0f));
            img.raycastTarget = true;
            var fader = img.gameObject.AddComponent<ScreenFader>();
            fader.image = img;
            img.enabled = false;
            fader.BuildCard(img.rectTransform);
            return fader;
        }

        public IEnumerator Fade(float to, float duration)
        {
            IsBusy = true;
            image.enabled = true;
            float from = image.color.a;
            float t = 0f;
            while (t < duration)
            {
                t += Time.unscaledDeltaTime;
                SetAlpha(Mathf.Lerp(from, to, t / duration));
                yield return null;
            }
            SetAlpha(to);
            image.enabled = to > 0.001f;
            IsBusy = false;
        }

        public void SetAlpha(float a)
        {
            var c = image.color;
            c.a = a;
            image.color = c;
            image.enabled = a > 0.001f;
            if (card != null) card.alpha = card.gameObject.activeSelf ? a : 0f;
        }

        void BuildCard(RectTransform parent)
        {
            var root = UIFactory.Place(UIFactory.Rect(parent, "LoadingCard"), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(720f, 420f));
            card = root.gameObject.AddComponent<CanvasGroup>();
            card.blocksRaycasts = false;
            cardBanner = UIFactory.Image(root, "Banner", null, Color.white);
            cardBanner.preserveAspect = true;
            UIFactory.Place(cardBanner.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), Vector2.zero, new Vector2(560f, 280f));
            cardTitle = UIFactory.Text(root, "Title", "", 34, UiTheme.Accent, TextAnchor.MiddleCenter, true);
            UIFactory.Place(cardTitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -292f), new Vector2(700f, 48f));
            cardTip = UIFactory.Text(root, "Tip", "", 19, UiTheme.TextSecondary, TextAnchor.UpperCenter, true);
            UIFactory.Place(cardTip.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -350f), new Vector2(700f, 60f));
            root.gameObject.SetActive(false);
        }

        /// <summary>[E4] Shows the loading card with the next fades (banner may be null).</summary>
        public void ShowCard(Sprite banner, string title)
        {
            if (card == null) return;
            cardBanner.sprite = banner;
            cardBanner.enabled = banner != null;
            cardTitle.text = title;
            cardTip.text = "TIP  " + Tips[Random.Range(0, Tips.Length)];
            card.gameObject.SetActive(true);
            card.alpha = image.color.a;
        }

        public void HideCard()
        {
            if (card != null) card.gameObject.SetActive(false);
        }
    }
}
