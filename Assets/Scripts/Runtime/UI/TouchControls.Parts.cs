using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DotRPG
{
    /// <summary>
    /// A touch button that reports to <see cref="InputReader"/>: pressed on the way down, held while the finger stays
    /// (skill charge), released on lift or when the button is hidden. Each finger is its own pointer, so several buttons
    /// (and the stick) work at the same time.
    /// </summary>
    public sealed class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        const float RepeatDelay = 0.3f, RepeatInterval = 0.2f;

        public GameAction action;
        /// <summary>Presses again while held (the attack button), like tapping fast.</summary>
        public bool repeat;
        /// <summary>Ignores touches (a locked skill slot).</summary>
        public bool locked;
        bool down;
        float nextRepeat;

        public void OnPointerDown(PointerEventData e)
        {
            if (locked || Game.Input == null) return;
            down = true;
            nextRepeat = Time.unscaledTime + RepeatDelay;
            Game.Input.TouchHold(action, true);
            transform.localScale = Vector3.one * 0.92f;
        }

        public void OnPointerUp(PointerEventData e) => Release();

        void OnDisable() => Release();

        void Release()
        {
            transform.localScale = Vector3.one;
            if (!down) return;
            down = false;
            if (Game.Input != null) Game.Input.TouchHold(action, false);
        }

        void Update()
        {
            if (!down || !repeat || Time.unscaledTime < nextRepeat || Game.Input == null) return;
            nextRepeat = Time.unscaledTime + RepeatInterval;
            Game.Input.TouchTap(action);
        }
    }

    /// <summary>
    /// Floating stick: the first finger that goes down in the lower left area puts the stick where it landed; dragging
    /// moves the knob, and the stick base follows when the finger goes past the ring.
    /// </summary>
    public sealed class TouchStick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        const float Radius = 64f, DeadZone = 0.15f;

        public RectTransform area;   // the rect the stick is drawn in (its local space)
        public RectTransform ring, knob;
        int pointer = int.MinValue;
        Vector2 center;

        bool ToLocal(PointerEventData e, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(area, e.position, e.pressEventCamera, out local);

        public void OnPointerDown(PointerEventData e)
        {
            if (pointer != int.MinValue || Game.Input == null || !ToLocal(e, out var local)) return;
            pointer = e.pointerId;
            center = local;
            ring.localPosition = center;
            knob.localPosition = center;
            ring.gameObject.SetActive(true);
            knob.gameObject.SetActive(true);
        }

        public void OnDrag(PointerEventData e)
        {
            if (e.pointerId != pointer || Game.Input == null || !ToLocal(e, out var local)) return;
            Vector2 delta = local - center;
            if (delta.magnitude > Radius)
            {
                center += delta.normalized * (delta.magnitude - Radius); // the base follows the finger
                delta = local - center;
                ring.localPosition = center;
            }
            knob.localPosition = center + delta;
            Vector2 value = delta / Radius;
            Game.Input.TouchSetMove(value.magnitude < DeadZone ? Vector2.zero : value);
        }

        public void OnPointerUp(PointerEventData e)
        {
            if (e.pointerId == pointer) Release();
        }

        void OnDisable() => Release();

        /// <summary>Lets go of the stick (finger lifted, or the controls were hidden).</summary>
        public void Release()
        {
            if (pointer == int.MinValue) return;
            pointer = int.MinValue;
            if (Game.Input != null) Game.Input.TouchSetMove(Vector2.zero);
            if (ring != null) ring.gameObject.SetActive(false);
            if (knob != null) knob.gameObject.SetActive(false);
        }
    }
}
