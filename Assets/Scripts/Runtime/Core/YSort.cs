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

        /// <summary>Re-collects child renderers (call after adding children at runtime).</summary>
        public void Refresh()
        {
            renderers = GetComponentsInChildren<SpriteRenderer>(true);
            localOrders = new int[renderers.Length];
            for (int i = 0; i < renderers.Length; i++) localOrders[i] = renderers[i].sortingOrder;
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
