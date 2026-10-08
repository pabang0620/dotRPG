using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace DotRPG
{
    /// <summary>
    /// Touch replacement for hover and right click: holding a finger on the element for <see cref="Delay"/> seconds runs
    /// <see cref="onLong"/>; lifting it runs <see cref="onRelease"/> (hide a tooltip). The click that follows a long press is
    /// swallowed by <see cref="TakeFired"/> (PointerRelay and MenuItemPointer ask). Added only when <see cref="TouchUi.Enabled"/>.
    /// </summary>
    public sealed class LongPress : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler
    {
        public const float Delay = 0.5f;

        public Action onLong, onRelease;
        bool pressing, fired;
        float downAt;

        /// <summary>Adds (or updates) the long press on <paramref name="go"/>; null on a PC without -touchUI.</summary>
        public static LongPress Add(GameObject go, Action onLong, Action onRelease = null)
        {
            if (!TouchUi.Enabled || go == null) return null;
            var lp = go.GetComponent<LongPress>();
            if (lp == null) lp = go.AddComponent<LongPress>();
            lp.onLong = onLong;
            lp.onRelease = onRelease;
            return lp;
        }

        /// <summary>True once (and only once) when a long press ran on this object, so the click after it is ignored.</summary>
        public static bool TakeFired(GameObject go)
        {
            var lp = go.GetComponent<LongPress>();
            if (lp == null || !lp.fired) return false;
            lp.fired = false;
            return true;
        }

        public void OnPointerDown(PointerEventData e)
        {
            pressing = true;
            fired = false;
            downAt = Time.unscaledTime;
        }

        public void OnDrag(PointerEventData e) => pressing = false; // a drag is not a long press

        public void OnPointerUp(PointerEventData e)
        {
            if (fired) onRelease?.Invoke();
            pressing = false;
        }

        void OnDisable()
        {
            if (fired) onRelease?.Invoke();
            pressing = fired = false;
        }

        void Update()
        {
            if (!pressing || fired || Time.unscaledTime - downAt < Delay) return;
            fired = true;
            onLong?.Invoke();
        }
    }

    /// <summary>
    /// Touch replacement for the mouse wheel on lists drawn row by row: a vertical swipe calls <see cref="onSteps"/> with
    /// the number of rows to move (positive = later rows). Drags that start on a child row bubble up to this object.
    /// </summary>
    public sealed class SwipeSteps : MonoBehaviour, IBeginDragHandler, IDragHandler
    {
        public float rowHeight = 54f;
        public Action<int> onSteps;
        float carried;

        public static SwipeSteps Add(GameObject go, float rowHeight, Action<int> onSteps)
        {
            if (!TouchUi.Enabled || go == null) return null;
            var graphic = go.GetComponent<UnityEngine.UI.Graphic>();
            if (graphic != null) graphic.raycastTarget = true; // empty space between rows can start a swipe
            var s = go.GetComponent<SwipeSteps>();
            if (s == null) s = go.AddComponent<SwipeSteps>();
            s.rowHeight = rowHeight;
            s.onSteps = onSteps;
            return s;
        }

        public void OnBeginDrag(PointerEventData e) => carried = 0f;

        public void OnDrag(PointerEventData e)
        {
            var canvas = GetComponentInParent<Canvas>();
            float scale = canvas != null && canvas.rootCanvas.scaleFactor > 0f ? canvas.rootCanvas.scaleFactor : 1f;
            carried += e.delta.y / (rowHeight * scale);
            int steps = (int)carried;
            if (steps == 0) return;
            carried -= steps;
            onSteps?.Invoke(steps);
        }
    }
}
