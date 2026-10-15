using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>Grades low to high. Legend is reserved for a later release (nothing uses it yet).</summary>
    public enum CosmeticRarity { Free, Common, Rare, Epic, Unique, Legend }

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

        /// <summary>Attack bonus (%) while worn: auras common 1, rare 2, epic 3; skins common/rare 0, epic 4, unique 5 (server: starshopDefs).</summary>
        public int DamagePercent => IsSkin
            ? (Rarity == CosmeticRarity.Epic ? 4 : Rarity >= CosmeticRarity.Unique ? 5 : 0)
            : Rarity >= CosmeticRarity.Epic ? 3 : Rarity == CosmeticRarity.Rare ? 2 : Rarity == CosmeticRarity.Common ? 1 : 0;

        /// <summary>Unique skins come only from the selection gauge; everything else can also be exchanged.</summary>
        public bool GaugeOnly => IsSkin && Rarity == CosmeticRarity.Unique;

        /// <summary>Starlight Exchange only: bought with Star Crystals, never drawn, synthesized or exchanged for Star Shards.</summary>
        public bool ExchangeOnly => StarShopClient.ExchangeOnly.Contains(Id) || System.Array.IndexOf(CosmeticCatalog.ExchangeOnlyIds, Id) >= 0;

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
            new CosmeticProduct("aura_none", L.T("기본 모습"), Color.clear, true),
            new CosmeticProduct("aura_sky", L.T("하늘빛 오라"), new Color32(120, 200, 255, 190), true),
            new CosmeticProduct("aura_dew", L.T("이슬빛 오라"), new Color32(130, 240, 200, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_maple", L.T("단풍빛 오라"), new Color32(240, 110, 70, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_blossom", L.T("꽃잎빛 오라"), new Color32(255, 160, 210, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_forest", L.T("숲빛 오라"), new Color32(110, 200, 90, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_ash", L.T("잿빛 오라"), new Color32(210, 210, 220, 180), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_sunset", L.T("노을빛 오라"), new Color32(255, 180, 100, 190)),
            new CosmeticProduct("aura_violet", L.T("별빛 오라"), new Color32(190, 145, 255, 190)),
            new CosmeticProduct("aura_frost", L.T("서리별 오라"), new Color32(170, 230, 255, 200)),
            new CosmeticProduct("aura_rose", L.T("장미별 오라"), new Color32(255, 110, 140, 200)),
            new CosmeticProduct("aura_jade", L.T("비취별 오라"), new Color32(90, 220, 170, 200)),
            new CosmeticProduct("aura_rainbow", L.T("무지개 오라"), new Color32(255, 255, 255, 220), false, CosmeticRarity.Epic, CosmeticEffect.Rainbow),
            new CosmeticProduct("aura_gold", L.T("황금 오라"), new Color32(255, 210, 80, 230), false, CosmeticRarity.Epic, CosmeticEffect.Pulse),
            new CosmeticProduct("aura_abyss", L.T("심연 오라"), new Color32(120, 80, 255, 230), false, CosmeticRarity.Epic, CosmeticEffect.Pulse),
            // [2026-10-14] More auras (common 10, rare 10, epic 6).
            new CosmeticProduct("aura_mint", L.T("민트빛 오라"), new Color32(150, 245, 210, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_peach", L.T("복숭아빛 오라"), new Color32(255, 190, 160, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_lemon", L.T("레몬빛 오라"), new Color32(250, 240, 120, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_lilac", L.T("라일락빛 오라"), new Color32(205, 170, 250, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_sea", L.T("바닷빛 오라"), new Color32(80, 170, 230, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_sand", L.T("모래빛 오라"), new Color32(225, 200, 150, 185), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_berry", L.T("산딸기빛 오라"), new Color32(230, 70, 120, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_moss", L.T("이끼빛 오라"), new Color32(140, 170, 80, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_cloud", L.T("구름빛 오라"), new Color32(235, 240, 255, 185), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_coral", L.T("산호빛 오라"), new Color32(255, 130, 110, 190), false, CosmeticRarity.Common),
            new CosmeticProduct("aura_comet", L.T("혜성별 오라"), new Color32(160, 220, 255, 205)),
            new CosmeticProduct("aura_aurora", L.T("극광별 오라"), new Color32(110, 255, 200, 205)),
            new CosmeticProduct("aura_amber", L.T("호박별 오라"), new Color32(255, 175, 60, 205)),
            new CosmeticProduct("aura_sapphire", L.T("사파이어별 오라"), new Color32(70, 110, 255, 205)),
            new CosmeticProduct("aura_ruby", L.T("루비별 오라"), new Color32(230, 40, 70, 205)),
            new CosmeticProduct("aura_pearl", L.T("진주별 오라"), new Color32(245, 235, 250, 205)),
            new CosmeticProduct("aura_lavender", L.T("라벤더별 오라"), new Color32(180, 150, 255, 205)),
            new CosmeticProduct("aura_ember", L.T("불씨별 오라"), new Color32(255, 110, 40, 205)),
            new CosmeticProduct("aura_lagoon", L.T("석호별 오라"), new Color32(40, 200, 210, 205)),
            new CosmeticProduct("aura_dusk", L.T("황혼별 오라"), new Color32(200, 110, 170, 205)),
            new CosmeticProduct("aura_prism", L.T("프리즘 오라"), new Color32(255, 255, 255, 225), false, CosmeticRarity.Epic, CosmeticEffect.Rainbow),
            new CosmeticProduct("aura_starfall", L.T("유성우 오라"), new Color32(190, 210, 255, 230), false, CosmeticRarity.Epic, CosmeticEffect.Pulse),
            new CosmeticProduct("aura_sakura", L.T("벚꽃 오라"), new Color32(255, 160, 200, 230), false, CosmeticRarity.Epic, CosmeticEffect.Pulse),
            new CosmeticProduct("aura_thunder", L.T("천둥 오라"), new Color32(255, 235, 90, 230), false, CosmeticRarity.Epic, CosmeticEffect.Pulse),
            new CosmeticProduct("aura_deepsea", L.T("심해 오라"), new Color32(30, 120, 220, 230), false, CosmeticRarity.Epic, CosmeticEffect.Pulse),
            new CosmeticProduct("aura_spectrum", L.T("스펙트럼 오라"), new Color32(255, 240, 250, 225), false, CosmeticRarity.Epic, CosmeticEffect.Rainbow),
            // Starlight Exchange only (Star Crystals).
            new CosmeticProduct("aura_crystal", L.T("결정빛 오라"), new Color32(210, 180, 255, 230), false, CosmeticRarity.Epic, CosmeticEffect.Pulse),
            new CosmeticProduct("aura_nova", L.T("신성 폭발 오라"), new Color32(255, 250, 230, 230), false, CosmeticRarity.Epic, CosmeticEffect.Rainbow),
            // Costume skins (SkinCatalog): common/rare skins (recoloured class body) fill the skin draw's lower tiers,
            // epic skins drop at 1% or are exchanged; unique skins mostly come from the gauge.
            new CosmeticProduct("skin_w_sand", L.T("모래빛 여행복"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_w_moss", L.T("숲지기 옷"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_w_rust", L.T("적갈 견습복"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_m_ash", L.T("잿빛 견습 로브"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_m_pilgrim", L.T("순례자의 로브"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_m_sprout", L.T("새싹 로브"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_w_crimson", L.T("진홍 용병복"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_w_amethyst", L.T("자수정 기사복"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_w_brass", L.T("황동 경비복"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_m_rose", L.T("장밋빛 로브"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_m_emerald", L.T("에메랄드 로브"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_m_sunset", L.T("노을 로브"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_maple", L.T("단풍 무사"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_obsidian", L.T("흑요 기사"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_noir", L.T("칠흑의 검희"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_forest", L.T("숲의 정령"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_ice", L.T("얼음 여왕"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_lion", L.T("황금 사자 기사"), Color.white, false, CosmeticRarity.Unique),
            new CosmeticProduct("skin_moon", L.T("월광 검귀"), Color.white, false, CosmeticRarity.Unique),
            new CosmeticProduct("skin_starnight", L.T("성야의 마녀"), Color.white, false, CosmeticRarity.Unique),
            new CosmeticProduct("skin_crimson", L.T("홍염의 마녀"), Color.white, false, CosmeticRarity.Unique),
            new CosmeticProduct("skin_w_ranger", L.T("숲 순찰대복"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_w_sailor", L.T("견습 선원복"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_w_farmer", L.T("들녘 일꾼복"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_w_scout", L.T("정찰병 가죽옷"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_w_miner", L.T("광산 작업복"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_w_smith", L.T("대장간 앞치마"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_w_wanderer", L.T("방랑 검객 망토"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_w_bluecoat", L.T("청기사 견습복"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_w_violet", L.T("보랏빛 결투복"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_w_jade", L.T("비취 근위복"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_w_scarlet", L.T("진홍 검무복"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_w_frost", L.T("설원 순찰복"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_w_sunguard", L.T("태양 기사단복"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_w_shade", L.T("그림자 자객복"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_w_valkyrie", L.T("발키리 갑주"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_w_snowblade", L.T("백설 무사"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_w_dragonscale", L.T("용비늘 갑주"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_w_roseknight", L.T("장미 기사"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_w_phoenix", L.T("불사조 성기사"), Color.white, false, CosmeticRarity.Unique),
            new CosmeticProduct("skin_w_aurora", L.T("오로라 여제"), Color.white, false, CosmeticRarity.Unique),
            new CosmeticProduct("skin_w_voidblade", L.T("심연의 검성"), Color.white, false, CosmeticRarity.Unique),
            new CosmeticProduct("skin_m_lavender", L.T("라벤더 후드 로브"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_m_student", L.T("마법학교 교복"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_m_herbalist", L.T("약초꾼 로브"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_m_tea", L.T("찻잎 견습 로브"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_m_snowcloak", L.T("눈송이 망토"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_m_autumn", L.T("가을 낙엽 로브"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_m_dusk", L.T("밤하늘 견습 로브"), Color.white, false, CosmeticRarity.Common),
            new CosmeticProduct("skin_m_sky", L.T("하늘빛 마녀"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_m_cherry", L.T("벚꽃 마녀"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_m_mint", L.T("민트 마녀"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_m_amber", L.T("호박빛 마녀"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_m_ocean", L.T("바다 마녀"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_m_lilac", L.T("라일락 마녀"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_m_candy", L.T("사탕 마녀"), Color.white, false, CosmeticRarity.Rare),
            new CosmeticProduct("skin_m_moonshrine", L.T("달빛 무녀"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_m_storm", L.T("뇌운의 마녀"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_m_sakura", L.T("벚꽃 무녀"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_m_frostprincess", L.T("서리 공주"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_m_celestial", L.T("천공의 대마녀"), Color.white, false, CosmeticRarity.Unique),
            new CosmeticProduct("skin_m_flameempress", L.T("화염 여제"), Color.white, false, CosmeticRarity.Unique),
            new CosmeticProduct("skin_m_dream", L.T("몽환의 마녀"), Color.white, false, CosmeticRarity.Unique),
            new CosmeticProduct("skin_w_starwarden", L.T("별빛 수호 기사"), Color.white, false, CosmeticRarity.Epic),
            new CosmeticProduct("skin_m_starsage", L.T("별의 현자"), Color.white, false, CosmeticRarity.Epic),
        });

        /// <summary>Exchange-only cosmetics (server starshopDefs exchangeOnly), known before the server answers.</summary>
        public static readonly string[] ExchangeOnlyIds = { "aura_crystal", "aura_nova", "skin_w_starwarden", "skin_m_starsage" };

        public static CosmeticProduct Find(string id)
        {
            foreach (var product in All)
                if (product.Id == id) return product;
            return null;
        }

        public static string RarityName(CosmeticRarity r) =>
            r == CosmeticRarity.Legend ? L.T("전설") : r == CosmeticRarity.Unique ? L.T("유니크") : r == CosmeticRarity.Epic ? L.T("에픽") : r == CosmeticRarity.Rare ? L.T("희귀") : r == CosmeticRarity.Common ? L.T("일반") : L.T("무료");

        public static string RarityHex(CosmeticRarity r) =>
            r == CosmeticRarity.Legend ? "#7dffb0" : r == CosmeticRarity.Unique ? "#ffb347" : r == CosmeticRarity.Epic ? "#c58cff" : r == CosmeticRarity.Rare ? "#9fc4ff" : r == CosmeticRarity.Common ? "#cfd6e2" : "#8fe28f";
    }
}
