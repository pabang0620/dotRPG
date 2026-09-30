using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Top-down depth sorting: lower on screen = drawn in front. Works with any render pipeline
    /// because it only sets sortingOrder. Child renderers keep their initial order as an offset
    /// (e.g. shadow = -1, weapon = +1).
    /// </summary>
    public class YSort : MonoBehaviour
    {
        public const int OrdersPerUnit = 32;

        [Tooltip("Static objects are sorted once; moving characters every frame.")]
        [SerializeField] bool isStatic;
        [SerializeField] int orderOffset;

        SpriteRenderer[] renderers;
        int[] localOrders;
        bool applied;

        public static int OrderFor(float y) => -Mathf.RoundToInt(y * OrdersPerUnit);

        public void Configure(bool staticObject, int offset = 0)
        {
            isStatic = staticObject;
            orderOffset = offset;
            Refresh();
        }

        /// <summary>
        /// Re-collects child renderers (call after adding children at runtime). Renderers seen before
        /// keep their local order; only new ones take their current sortingOrder as their local order.
        /// (Reading every renderer's sortingOrder again would fold the already applied depth into the
        /// offset, so each call used to push the object further back: props sorted at twice their
        /// depth and the player drifted a little more on every spawn.)
        /// </summary>
        public void Refresh()
        {
            var all = GetComponentsInChildren<SpriteRenderer>(true);
            // Ground shadows lie flat under everything and keep their own order.
            var found = new System.Collections.Generic.List<SpriteRenderer>(all.Length);
            foreach (var r in all)
                if (r.GetComponent<CastShadow>() == null) found.Add(r);
            var orders = new int[found.Count];
            for (int i = 0; i < found.Count; i++)
            {
                int known = renderers != null ? System.Array.IndexOf(renderers, found[i]) : -1;
                orders[i] = known >= 0 ? localOrders[known] : found[i].sortingOrder;
            }
            renderers = found.ToArray();
            localOrders = orders;
            applied = false;
            Apply();
        }

        void Awake()
        {
            if (renderers == null) Refresh();
        }

        void LateUpdate()
        {
            if (isStatic && applied) return;
            Apply();
        }

        void Apply()
        {
            if (renderers == null) return;
            int order = OrderFor(transform.position.y) + orderOffset;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] != null) renderers[i].sortingOrder = order + localOrders[i];
            applied = true;
        }

        /// <summary>Changes a child's relative order at runtime (e.g. weapon behind the body when facing up).</summary>
        public void SetLocalOrder(SpriteRenderer target, int localOrder)
        {
            if (renderers == null) return;
            for (int i = 0; i < renderers.Length; i++)
                if (renderers[i] == target) localOrders[i] = localOrder;
        }
    }
}
