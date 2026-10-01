using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// Standard feedback for code-built buttons (<see cref="UiTheme"/>): tint per state, a slight scale on
    /// hover / press, a soft select sound on hover, a "can't" sound + shake when a disabled button is clicked,
    /// and a dimmed label while disabled. Add with <see cref="Attach"/>; it never changes the click behaviour.
    /// </summary>
    [DisallowMultipleComponent]
    public class UiButton : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler,
        ISelectHandler, IDeselectHandler
    {
        const float HoverSoundGap = 0.06f;
        const float DisabledAlpha = 0.55f;
        const float ShakeTime = 0.25f, ShakeAmount = 5f;

        static float lastHoverSound;

        Button button;
        CanvasGroup group;
        /// <summary>False when other code drives this transform's scale (cards, animated icons).</summary>
        public bool ScaleFeedback = true;
        bool hovered, pressed, selected;
        bool lastInteractable = true;
        float shakeAt = -10f;
        Vector2 basePos;
        bool basePosSet;

        /// <summary>Adds the standard feedback to <paramref name="button"/> (idempotent).</summary>
        public static UiButton Attach(Button button, bool scaleFeedback = true)
        {
            if (button == null) return null;
            var ub = button.GetComponent<UiButton>();
            if (ub == null) ub = button.gameObject.AddComponent<UiButton>();
            ub.ScaleFeedback = scaleFeedback;
            button.transition = Selectable.Transition.ColorTint;
            button.colors = UiTheme.ButtonColors();
            return ub;
        }

        void Awake()
        {
            button = GetComponent<Button>();
            group = GetComponent<CanvasGroup>();
            if (group == null) group = gameObject.AddComponent<CanvasGroup>();
        }

        void OnDisable()
        {
            hovered = pressed = selected = false;
            if (ScaleFeedback) transform.localScale = Vector3.one;
        }

        bool Interactable => button == null || button.IsInteractable();

        public void OnPointerEnter(PointerEventData e)
        {
            hovered = true;
            if (Interactable) PlayHover();
        }

        public void OnPointerExit(PointerEventData e) { hovered = false; pressed = false; }

        public void OnPointerDown(PointerEventData e)
        {
            if (e.button != PointerEventData.InputButton.Left) return;
            if (Interactable) { pressed = true; return; }
            // Disabled: tell the player it can't be used instead of silently ignoring the click.
            Game.Audio?.PlaySfx("cancel", 0.45f);
            shakeAt = Time.unscaledTime;
        }

        public void OnPointerUp(PointerEventData e) => pressed = false;

        public void OnSelect(BaseEventData e)
        {
            selected = true;
            if (Interactable && !hovered) PlayHover();
        }

        public void OnDeselect(BaseEventData e) => selected = false;

        static void PlayHover()
        {
            if (Time.unscaledTime - lastHoverSound < HoverSoundGap) return;
            lastHoverSound = Time.unscaledTime;
            Game.Audio?.PlaySfx("select", 0.45f);
        }

        void LateUpdate()
        {
            bool on = Interactable;
            if (on != lastInteractable || group.alpha != (on ? 1f : DisabledAlpha))
            {
                lastInteractable = on;
                group.alpha = on ? 1f : DisabledAlpha;
            }

            var rt = (RectTransform)transform;
            float shakeAge = Time.unscaledTime - shakeAt;
            if (shakeAge < ShakeTime)
            {
                if (!basePosSet) { basePos = rt.anchoredPosition; basePosSet = true; }
                float k = 1f - shakeAge / ShakeTime;
                rt.anchoredPosition = basePos + new Vector2(Mathf.Sin(shakeAge * 70f) * ShakeAmount * k, 0f);
            }
            else if (basePosSet)
            {
                rt.anchoredPosition = basePos;
                basePosSet = false;
            }

            if (!ScaleFeedback) return;
            float target = !on ? 1f : pressed ? UiTheme.PressScale : (hovered || selected) ? UiTheme.HoverScale : 1f;
            float s = Mathf.Lerp(transform.localScale.x, target, 1f - Mathf.Exp(-24f * Time.unscaledDeltaTime));
            if (Mathf.Abs(s - target) < 0.002f) s = target;
            transform.localScale = new Vector3(s, s, 1f);
        }
    }
}
