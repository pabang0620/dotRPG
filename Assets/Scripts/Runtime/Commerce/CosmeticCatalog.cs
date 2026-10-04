using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public sealed class CosmeticProduct
    {
        public string Id { get; }
        public string Name { get; }
        public Color Color { get; }
        public bool IsFree { get; }

        public CosmeticProduct(string id, string name, Color color, bool isFree = false)
        {
            Id = id;
            Name = name;
            Color = color;
            IsFree = isFree;
        }
    }

    /// <summary>Permanent, cosmetic-only items. Prices come from the platform, never this catalog.</summary>
    public static class CosmeticCatalog
    {
        public static IReadOnlyList<CosmeticProduct> All { get; } = System.Array.AsReadOnly(new[]
        {
            new CosmeticProduct("aura_none", "기본 모습", Color.clear, true),
            new CosmeticProduct("aura_sky", "하늘빛 오라", new Color32(120, 200, 255, 190), true),
            new CosmeticProduct("aura_sunset", "노을빛 오라", new Color32(255, 180, 100, 190)),
            new CosmeticProduct("aura_violet", "별빛 오라", new Color32(190, 145, 255, 190)),
        });

        public static CosmeticProduct Find(string id)
        {
            foreach (var product in All)
                if (product.Id == id) return product;
            return null;
        }
    }
}
