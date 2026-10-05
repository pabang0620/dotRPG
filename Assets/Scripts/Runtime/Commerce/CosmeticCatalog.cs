using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public enum CosmeticRarity { Free, Common, Rare, Legend }

    /// <summary>Legend auras move: a rainbow hue cycle or a slow glow pulse. Purely visual.</summary>
    public enum CosmeticEffect { None, Rainbow, Pulse }

    public sealed class CosmeticProduct
    {
        public string Id { get; }
        public string Name { get; }
        public Color Color { get; }
        public bool IsFree { get; }
        public CosmeticRarity Rarity { get; }
        public CosmeticEffect Effect { get; }

        public CosmeticProduct(string id, string name, Color color, bool isFree = false,
            CosmeticRarity rarity = CosmeticRarity.Rare, CosmeticEffect effect = CosmeticEffect.None)
        {
            Id = id;
            Name = name;
            Color = color;
            IsFree = isFree;
            Rarity = isFree ? CosmeticRarity.Free : rarity;
            Effect = effect;
        }

        /// <summary>A costume skin (whole-body repaint for one class) rather than a foot aura.</summary>
        public SkinDef Skin => SkinCatalog.Find(Id);
        public bool IsSkin => Skin != null;

        /// <summary>The colour at a moment (legend auras animate; the rest stay still).</summary>
        public Color ColorAt(float time)
        {
            switch (Effect)
            {
                case CosmeticEffect.Rainbow:
                    var c = Color.HSVToRGB(Mathf.Repeat(time * 0.25f, 1f), 0.55f, 1f);
                    c.a = Color.a;
                    return c;
                case CosmeticEffect.Pulse:
                    float k = 0.75f + 0.25f * Mathf.Sin(time * 3f);
                    return new Color(Color.r * k + (1f - k) * 0.4f, Color.g * k, Color.b * k + (1f - k) * 0.2f, Color.a * (0.8f + 0.2f * k));
                default:
                    return Color;
            }
        }
    }

    /// <summary>
    /// Permanent, cosmetic-only items. The paid ones come from the 별조각 cash shop (gacha or direct exchange); the
    /// server owns ownership, rates and prices (server/src/domains/starshop/starshopDefs.ts, ids must match).
    /// </summary>
    public static class CosmeticCatalog
    {
        public static IReadOnlyList<CosmeticProduct> All { get; } = System.Array.AsReadOnly(new[]
        {
            new CosmeticProduct("aura_none", "기본 모습", Color.clear, true),
            new CosmeticProduct("aura_sky", "하늘빛 오라", new Color32(120, 200, 255, 190), true),
            new CosmeticProduct("aura_dew", "이슬빛 오라", new Color32(130, 240, 200, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_maple", "단풍빛 오라", new Color32(240, 110, 70, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_blossom", "꽃잎빛 오라", new Color32(255, 160, 210, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_forest", "숲빛 오라", new Color32(110, 200, 90, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_ash", "잿빛 오라", new Color32(210, 210, 220, 180), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_sunset", "노을빛 오라", new Color32(255, 180, 100, 190)),
            new CosmeticProduct("aura_violet", "별빛 오라", new Color32(190, 145, 255, 190)),
            new CosmeticProduct("aura_frost", "서리별 오라", new Color32(170, 230, 255, 200)),
            new CosmeticProduct("aura_rose", "장미별 오라", new Color32(255, 110, 140, 200)),
            new CosmeticProduct("aura_jade", "비취별 오라", new Color32(90, 220, 170, 200)),
            new CosmeticProduct("aura_rainbow", "무지개 오라", new Color32(255, 255, 255, 220), false, CosmeticRarity.Legend, CosmeticEffect.Rainbow),
            new CosmeticProduct("aura_gold", "황금 오라", new Color32(255, 210, 80, 230), false, CosmeticRarity.Legend, CosmeticEffect.Pulse),
            new CosmeticProduct("aura_abyss", "심연 오라", new Color32(120, 80, 255, 230), false, CosmeticRarity.Legend, CosmeticEffect.Pulse),
            // Costume skins (SkinCatalog): direct purchase with 별조각, never in the gacha pool.
            new CosmeticProduct("skin_lion", "황금 사자 기사", Color.white, false, CosmeticRarity.Legend),
            new CosmeticProduct("skin_moon", "월광 검귀", Color.white, false, CosmeticRarity.Legend),
            new CosmeticProduct("skin_starnight", "성야의 마녀", Color.white, false, CosmeticRarity.Legend),
            new CosmeticProduct("skin_crimson", "홍염의 마녀", Color.white, false, CosmeticRarity.Legend),
        });

        public static CosmeticProduct Find(string id)
        {
            foreach (var product in All)
                if (product.Id == id) return product;
            return null;
        }

        public static string RarityName(CosmeticRarity r) =>
            r == CosmeticRarity.Legend ? "전설" : r == CosmeticRarity.Rare ? "희귀" : r == CosmeticRarity.Common ? "일반" : "무료";

        public static string RarityHex(CosmeticRarity r) =>
            r == CosmeticRarity.Legend ? "#ffb347" : r == CosmeticRarity.Rare ? "#9fc4ff" : r == CosmeticRarity.Common ? "#cfd6e2" : "#8fe28f";
    }
}
