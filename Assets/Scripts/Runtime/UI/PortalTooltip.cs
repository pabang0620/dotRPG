using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace DotRPG
{
    /// <summary>One non-interactive destination label, shared by world portals and map pins.</summary>
    public sealed class PortalTooltip : MonoBehaviour
    {
        RectTransform box;
        Text label;
        readonly System.Collections.Generic.List<RaycastResult> hits = new System.Collections.Generic.List<RaycastResult>();
        PointerEventData pointer;
        EventSystem pointerSystem;
        public string ShownTarget { get; private set; }
        public static PortalTooltip Instance { get; private set; }

        public static void Create(Transform parent)
        {
            if (Instance != null) return;
            var root = parent.GetComponentInParent<Canvas>().rootCanvas.transform;
            var host = new GameObject("Portal destinations"); host.transform.SetParent(root, false);
            Instance = host.AddComponent<PortalTooltip>();
            var bg = UIFactory.Image(root, "Portal destination tooltip", Game.Art.Get("ui_tooltip"), Color.white);
            bg.preserveAspect = false;
            Instance.box = UIFactory.Place(bg.rectTransform, Vector2.one * .5f, new Vector2(0, 1), Vector2.zero, new Vector2(300, 44));
            Instance.label = UIFactory.Text(bg.transform, "Destination", "", 19, UiTheme.AccentLight, TextAnchor.MiddleCenter);
            UIFactory.Stretch(Instance.label.rectTransform, 12, 4, 12, 4);
            bg.gameObject.SetActive(false);
        }

        public static void Hook(Image icon, string target)
        {
            icon.raycastTarget = true;
            var pin = icon.GetComponent<PortalDestinationPin>();
            if (pin == null) pin = icon.gameObject.AddComponent<PortalDestinationPin>();
            pin.Target = target;
        }

        void LateUpdate()
        {
            string target = null;
            var mouse = Mouse.current;
            if (mouse != null && Game.World != null && Game.Flow != null && !Game.Flow.IsTransitioning)
            {
                Vector2 screen = mouse.position.ReadValue();
                bool uiHit = false;
                if (EventSystem.current != null)
                {
                    if (pointer == null || pointerSystem != EventSystem.current) { pointerSystem = EventSystem.current; pointer = new PointerEventData(pointerSystem); }
                    pointer.position = screen; hits.Clear(); EventSystem.current.RaycastAll(pointer, hits);
                    uiHit = hits.Count > 0;
                    if (uiHit)
                    {
                        var pin = hits[0].gameObject.GetComponent<PortalDestinationPin>();
                        if (pin != null) target = pin.Target;
                    }
                }
                if (!uiHit && Game.IsPlaying && Game.UI.Top == null && Game.Camera != null)
                {
                    Vector2 world = Game.Camera.Camera.ScreenToWorldPoint(screen);
                    float nearest = .95f * .95f;
                    foreach (var portal in MapPortal.Active)
                    {
                        if (portal == null || !portal.isActiveAndEnabled) continue;
                        float d = ((Vector2)portal.transform.position - world).sqrMagnitude;
                        if (d < nearest) { nearest = d; target = portal.TargetMap; }
                    }
                }
                if (target != null) Show(target, screen);
            }
            if (target == null) { ShownTarget = null; box.gameObject.SetActive(false); }
        }

        void Show(string target, Vector2 screen)
        {
            ShownTarget = target;
            label.text = "→ " + (MapRegistry.Get(target)?.displayName ?? target);
            box.sizeDelta = new Vector2(Mathf.Clamp(label.preferredWidth + 30, 190, 420), 44);
            var root = (RectTransform)box.parent;
            var canvas = root.GetComponent<Canvas>();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(root, screen, canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera, out var pos);
            pos += new Vector2(18, -18);
            pos.x = Mathf.Clamp(pos.x, root.rect.xMin + 8, root.rect.xMax - box.sizeDelta.x - 8);
            pos.y = Mathf.Clamp(pos.y, root.rect.yMin + box.sizeDelta.y + 8, root.rect.yMax - 8);
            box.anchoredPosition = pos; box.SetAsLastSibling(); box.gameObject.SetActive(true);
        }
        void OnDestroy() { if (box != null) Destroy(box.gameObject); if (Instance == this) Instance = null; }
    }
    public sealed class PortalDestinationPin : MonoBehaviour { public string Target; }
}
