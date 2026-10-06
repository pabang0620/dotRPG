using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DotRPG
{
    /// <summary>
    /// [UX] Drag and drop for code-built UI. A <see cref="DragSource"/> carries a payload (a skill id, a bag item...)
    /// and shows a ghost of its icon under the pointer; a <see cref="DropTarget"/> lights up while something it accepts
    /// is held over it and receives the payload on release. Clicks on the same element still work as before.
    /// </summary>
    public sealed class DragSource : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        /// <summary>What is being dragged (null = not draggable now).</summary>
        public Func<object> payload;
        /// <summary>Picture shown under the pointer while dragging.</summary>
        public Func<Sprite> ghostSprite;
        /// <summary>Called after a drop that no target took (for "drag out to remove").</summary>
        public Action<object> droppedNowhere;

        /// <summary>The payload of the drag in progress (null when nothing is held).</summary>
        public static object Held { get; private set; }
        public static event Action HeldChanged;

        Image ghost;
        bool taken;

        public void OnBeginDrag(PointerEventData e)
        {
            var p = payload?.Invoke();
            if (p == null) { e.pointerDrag = null; return; }
            Held = p;
            taken = false;
            var canvas = GetComponentInParent<Canvas>()?.rootCanvas;
            if (canvas != null)
            {
                ghost = UIFactory.Image(canvas.transform, "DragGhost", ghostSprite?.Invoke(), new Color(1f, 1f, 1f, .85f));
                ghost.raycastTarget = false;
                ghost.preserveAspect = true;
                ghost.rectTransform.sizeDelta = new Vector2(64f, 64f);
                ghost.transform.SetAsLastSibling();
                Move(e);
            }
            Game.Audio?.PlaySfx("select");
            HeldChanged?.Invoke();
        }

        public void OnDrag(PointerEventData e) => Move(e);

        void Move(PointerEventData e)
        {
            if (ghost == null) return;
            var parent = (RectTransform)ghost.rectTransform.parent;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, e.position, e.pressEventCamera, out var local))
                ghost.rectTransform.localPosition = local;
        }

        public void OnEndDrag(PointerEventData e)
        {
            if (ghost != null) Destroy(ghost.gameObject);
            ghost = null;
            var p = Held;
            Held = null;
            if (!taken && p != null) droppedNowhere?.Invoke(p);
            HeldChanged?.Invoke();
        }

        internal void MarkTaken() => taken = true;
    }

    public sealed class DropTarget : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
    {
        public Func<object, bool> accepts;
        public Action<object> onDrop;
        /// <summary>The graphic tinted while an accepted payload hovers (optional).</summary>
        public Graphic highlight;
        public Color hoverColor = new Color(1f, .85f, .35f, 1f);
        Color normal;
        bool lit;

        public void OnPointerEnter(PointerEventData e)
        {
            if (highlight == null || DragSource.Held == null || accepts != null && !accepts(DragSource.Held)) return;
            normal = highlight.color;
            highlight.color = hoverColor;
            lit = true;
        }

        public void OnPointerExit(PointerEventData e) => Unlight();

        public void OnDrop(PointerEventData e)
        {
            Unlight();
            var p = DragSource.Held;
            if (p == null || accepts != null && !accepts(p)) return;
            e.pointerDrag?.GetComponent<DragSource>()?.MarkTaken();
            onDrop?.Invoke(p);
        }

        void Unlight()
        {
            if (!lit || highlight == null) return;
            highlight.color = normal;
            lit = false;
        }
    }
}
