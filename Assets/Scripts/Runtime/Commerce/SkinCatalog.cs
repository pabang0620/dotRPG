using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>The motes a costume skin leaves around its wearer (purely visual).</summary>
    public enum SkinTrailKind { GoldSparks, MoonMotes, Stars, Embers, None }

    /// <summary>
    /// One costume skin: a full repaint of a class's body in every direction and frame. Warrior skins swap the
    /// approved female body palette or dedicated sheets (SilverWarrior/Skins/&lt;sheet&gt;);
    /// mage skins are their own frame set (Art/char_&lt;id&gt;_&lt;dir&gt;_&lt;frame&gt;). No stats, no hitbox change.
    /// </summary>
    public sealed class SkinDef
    {
        public string id, name, blurb, sheet;
        public CharacterClass cls;
        public Color32 sleeve, leg, boot, trim, cape;
        public SkinTrailKind trail;
        public Color trailA, trailB;
        /// <summary>Unique skins also leave a fading afterimage while moving; epic skins only have motes.</summary>
        public bool afterimage;
    }

    public static class SkinCatalog
    {
        static Color32 C(string hex) => PixelCanvas.Hex(hex);

        /// <summary>Common skin: approved warrior runtime palette or mage frames, no motes.</summary>
        static SkinDef Plain(string id, string name, CharacterClass cls, string blurb) => new SkinDef
        {
            id = id, name = name, cls = cls, blurb = blurb, trail = SkinTrailKind.None,
            sheet = cls == CharacterClass.Warrior ? id.Substring("skin_".Length) : null,
        };

        /// <summary>Rare skin: a recoloured class body with a light mote trail.</summary>
        static SkinDef Tinted(string id, string name, CharacterClass cls, string blurb, Color a, Color b)
        {
            var s = Plain(id, name, cls, blurb);
            s.trail = SkinTrailKind.MoonMotes; s.trailA = a; s.trailB = b;
            return s;
        }

        public static readonly IReadOnlyList<SkinDef> All = new[]
        {
            Plain("skin_w_sand", L.T("모래빛 여행복"), CharacterClass.Warrior, L.T("먼 길을 걸어 온 모래빛 조끼와 바지.")),
            Plain("skin_w_moss", L.T("숲지기 옷"), CharacterClass.Warrior, L.T("숲을 지키는 이의 초록 조끼와 바지.")),
            Plain("skin_w_rust", L.T("적갈 견습복"), CharacterClass.Warrior, L.T("훈련장에서 닳은 적갈색 견습복.")),
            Plain("skin_m_ash", L.T("잿빛 견습 로브"), CharacterClass.Mage, L.T("마탑 견습생이 입는 잿빛 로브와 모자.")),
            Plain("skin_m_pilgrim", L.T("순례자의 로브"), CharacterClass.Mage, L.T("먼 길을 떠나는 순례자의 갈색 로브와 모자.")),
            Plain("skin_m_sprout", L.T("새싹 로브"), CharacterClass.Mage, L.T("봄 새싹 빛깔의 연둣빛 로브와 모자.")),
            Tinted("skin_w_crimson", L.T("진홍 용병복"), CharacterClass.Warrior, L.T("이름난 용병단의 진홍 조끼. 곁에 붉은 빛 조각이 떠오른다."), new Color(1f, .4f, .4f), new Color(1f, .85f, .8f)),
            Tinted("skin_w_amethyst", L.T("자수정 기사복"), CharacterClass.Warrior, L.T("자수정빛 기사 견습복. 곁에 보랏빛 빛 조각이 떠오른다."), new Color(.75f, .5f, 1f), new Color(.95f, .9f, 1f)),
            Tinted("skin_w_brass", L.T("황동 경비복"), CharacterClass.Warrior, L.T("성문 경비대의 황동빛 조끼. 곁에 금빛 빛 조각이 떠오른다."), new Color(1f, .8f, .4f), new Color(1f, .95f, .8f)),
            Tinted("skin_m_rose", L.T("장밋빛 로브"), CharacterClass.Mage, L.T("장밋빛 로브와 모자. 주위에 분홍 빛망울이 맴돈다."), new Color(1f, .55f, .75f), new Color(1f, .9f, .95f)),
            Tinted("skin_m_emerald", L.T("에메랄드 로브"), CharacterClass.Mage, L.T("에메랄드빛 로브와 모자. 주위에 초록 빛망울이 맴돈다."), new Color(.4f, 1f, .7f), new Color(.9f, 1f, .95f)),
            Tinted("skin_m_sunset", L.T("노을 로브"), CharacterClass.Mage, L.T("노을빛 로브와 모자. 주위에 주황 빛망울이 맴돈다."), new Color(1f, .6f, .3f), new Color(1f, .9f, .75f)),
            new SkinDef
            {
                id = "skin_lion", name = L.T("황금 사자 기사"), cls = CharacterClass.Warrior, sheet = "lion",
                blurb = L.T("금테 두른 은백 판금 갑옷과 진홍 망토, 금빛 서클릿. 걸음마다 금빛 불티가 흩날린다."),
                sleeve = C("#d1dae3"), leg = C("#bdc6d0"), boot = C("#8a96a6"), trim = C("#f6ab3e"), cape = C("#9c1d22"),
                trail = SkinTrailKind.GoldSparks, trailA = new Color(1f, .85f, .35f), trailB = new Color(1f, .97f, .8f), afterimage = true,
            },
            new SkinDef
            {
                id = "skin_moon", name = L.T("월광 검귀"), cls = CharacterClass.Warrior, sheet = "moon",
                blurb = L.T("청록 룬이 빛나는 남색 갑주와 별빛 망토. 달빛 조각이 곁을 맴돈다."),
                sleeve = C("#1d2547"), leg = C("#202a4e"), boot = C("#151b33"), trim = C("#6ff0ff"), cape = C("#1b2241"),
                trail = SkinTrailKind.MoonMotes, trailA = new Color(.45f, .95f, 1f), trailB = new Color(.9f, .97f, 1f), afterimage = true,
            },
            new SkinDef
            {
                id = "skin_starnight", name = L.T("성야의 마녀"), cls = CharacterClass.Mage,
                blurb = L.T("별자리가 수놓인 남빛 로브와 초승달 모자. 주위에 작은 별이 반짝인다."),
                trail = SkinTrailKind.Stars, trailA = new Color(1f, .9f, .55f), trailB = new Color(.75f, .9f, 1f), afterimage = true,
            },
            new SkinDef
            {
                id = "skin_crimson", name = L.T("홍염의 마녀"), cls = CharacterClass.Mage,
                blurb = L.T("금빛 불꽃 자수의 진홍 로브와 불사조 깃털. 발밑에서 불씨가 피어오른다."),
                trail = SkinTrailKind.Embers, trailA = new Color(1f, .45f, .12f), trailB = new Color(1f, .82f, .3f), afterimage = true,
            },
            new SkinDef
            {
                id = "skin_maple", name = L.T("단풍 무사"), cls = CharacterClass.Warrior, sheet = "maple",
                blurb = L.T("금빛 단풍 무늬 하오리와 옻칠 어깨받이. 걸을 때 단풍빛 불티가 인다."),
                sleeve = C("#bf512a"), leg = C("#522b22"), boot = C("#1c1a1a"), trim = C("#e8b84a"), cape = C("#bf512a"),
                trail = SkinTrailKind.Embers, trailA = new Color(1f, .5f, .18f), trailB = new Color(1f, .8f, .35f),
            },
            new SkinDef
            {
                // The warrior's first look (black bob, red eyes, black battle dress), kept as a skin when the default
                // became a plain novice warrior.
                id = "skin_noir", name = L.T("칠흑의 검희"), cls = CharacterClass.Warrior, sheet = "noir",
                blurb = L.T("검은 단발과 붉은 눈, 칠흑의 전투복. 곁에 붉은 빛 조각이 떠오른다."),
                sleeve = C("#23242a"), leg = C("#655861"), boot = C("#23242a"), trim = C("#71797e"),
                trail = SkinTrailKind.MoonMotes, trailA = new Color(1f, .35f, .4f), trailB = new Color(1f, .85f, .85f),
            },
            new SkinDef
            {
                id = "skin_obsidian", name = L.T("흑요 기사"), cls = CharacterClass.Warrior, sheet = "obsidian",
                blurb = L.T("은테 두른 흑요석 갑옷과 청록 보석. 곁에 푸른 빛 조각이 떠오른다."),
                sleeve = C("#343438"), leg = C("#3a3a40"), boot = C("#18181c"), trim = C("#c8d0d8"),
                trail = SkinTrailKind.MoonMotes, trailA = new Color(.35f, .9f, .85f), trailB = new Color(.85f, .95f, .95f),
            },
            new SkinDef
            {
                id = "skin_forest", name = L.T("숲의 정령"), cls = CharacterClass.Mage,
                blurb = L.T("덩굴 자수의 이끼빛 로브와 꽃 핀 모자. 주위에 연둣빛 빛망울이 맴돈다."),
                trail = SkinTrailKind.MoonMotes, trailA = new Color(.6f, 1f, .5f), trailB = new Color(1f, 1f, .85f),
            },
            new SkinDef
            {
                id = "skin_ice", name = L.T("얼음 여왕"), cls = CharacterClass.Mage,
                blurb = L.T("은빛 눈꽃 자수의 빙하빛 드레스와 얼음 티아라. 작은 눈송이가 반짝인다."),
                trail = SkinTrailKind.Stars, trailA = new Color(.75f, .92f, 1f), trailB = new Color(1f, 1f, 1f),
            },
            // [2026-10-14] Generated female skins (Codex image generation, Tools/art/process_skin.py), 21 per class.
            Plain("skin_w_ranger", L.T("숲 순찰대복"), CharacterClass.Warrior, L.T("숲길을 지키는 순찰대의 크림색·초록 튜닉.")),
            Plain("skin_w_sailor", L.T("견습 선원복"), CharacterClass.Warrior, L.T("바닷바람에 익숙한 견습 선원의 줄무늬 옷.")),
            Plain("skin_w_farmer", L.T("들녘 일꾼복"), CharacterClass.Warrior, L.T("밀밭에서 일하던 소녀의 멜빵 작업복.")),
            Plain("skin_w_scout", L.T("정찰병 가죽옷"), CharacterClass.Warrior, L.T("가볍게 움직이는 정찰병의 갈색 가죽옷.")),
            Plain("skin_w_miner", L.T("광산 작업복"), CharacterClass.Warrior, L.T("광산 마을 소녀의 튼튼한 회색 작업복.")),
            Plain("skin_w_smith", L.T("대장간 앞치마"), CharacterClass.Warrior, L.T("대장간 딸의 가죽 앞치마와 걷어 올린 소매.")),
            Plain("skin_w_wanderer", L.T("방랑 검객 망토"), CharacterClass.Warrior, L.T("먼 길을 떠도는 검객의 바랜 청회색 망토.")),
            Tinted("skin_w_bluecoat", L.T("청기사 견습복"), CharacterClass.Warrior, L.T("청기사단 견습생의 푸른 코트. 곁에 푸른 빛 조각이 떠오른다."), new Color(0.44f, 0.66f, 1.00f), new Color(0.88f, 0.93f, 1.00f)),
            Tinted("skin_w_violet", L.T("보랏빛 결투복"), CharacterClass.Warrior, L.T("귀족 결투사의 보랏빛 조끼. 곁에 보랏빛 조각이 떠오른다."), new Color(0.69f, 0.48f, 1.00f), new Color(0.94f, 0.90f, 1.00f)),
            Tinted("skin_w_jade", L.T("비취 근위복"), CharacterClass.Warrior, L.T("비취궁 근위대의 녹색 제복. 곁에 비취빛 조각이 떠오른다."), new Color(0.31f, 0.88f, 0.63f), new Color(0.90f, 1.00f, 0.96f)),
            Tinted("skin_w_scarlet", L.T("진홍 검무복"), CharacterClass.Warrior, L.T("검무를 추는 무희의 진홍 옷. 곁에 붉은 빛 조각이 떠오른다."), new Color(1.00f, 0.35f, 0.35f), new Color(1.00f, 0.88f, 0.88f)),
            Tinted("skin_w_frost", L.T("설원 순찰복"), CharacterClass.Warrior, L.T("설원 순찰대의 흰 털옷. 곁에 하얀 빛 조각이 떠오른다."), new Color(0.90f, 0.96f, 1.00f), new Color(1.00f, 1.00f, 1.00f)),
            Tinted("skin_w_sunguard", L.T("태양 기사단복"), CharacterClass.Warrior, L.T("태양 기사단의 금빛 제복. 곁에 금빛 조각이 떠오른다."), new Color(1.00f, 0.82f, 0.29f), new Color(1.00f, 0.96f, 0.82f)),
            Tinted("skin_w_shade", L.T("그림자 자객복"), CharacterClass.Warrior, L.T("밤에 움직이는 자객의 검은 옷. 곁에 어스름한 빛 조각이 떠오른다."), new Color(0.54f, 0.48f, 0.66f), new Color(0.85f, 0.82f, 0.91f)),
            new SkinDef { id = "skin_w_valkyrie", name = L.T("발키리 갑주"), cls = CharacterClass.Warrior, sheet = "w_valkyrie", blurb = L.T("날개 투구와 은빛 흉갑. 걸음마다 하얀 깃털빛이 흩날린다."), trail = SkinTrailKind.MoonMotes, trailA = new Color(1.00f, 1.00f, 1.00f), trailB = new Color(0.75f, 0.88f, 1.00f) },
            new SkinDef { id = "skin_w_snowblade", name = L.T("백설 무사"), cls = CharacterClass.Warrior, sheet = "w_snowblade", blurb = L.T("눈처럼 흰 하오리와 은빛 머리끈. 곁에 눈송이가 반짝인다."), trail = SkinTrailKind.Stars, trailA = new Color(0.87f, 0.95f, 1.00f), trailB = new Color(1.00f, 1.00f, 1.00f) },
            new SkinDef { id = "skin_w_dragonscale", name = L.T("용비늘 갑주"), cls = CharacterClass.Warrior, sheet = "w_dragonscale", blurb = L.T("붉은 용비늘로 짠 갑옷. 발밑에서 불씨가 피어오른다."), trail = SkinTrailKind.Embers, trailA = new Color(1.00f, 0.35f, 0.12f), trailB = new Color(1.00f, 0.75f, 0.29f) },
            new SkinDef { id = "skin_w_roseknight", name = L.T("장미 기사"), cls = CharacterClass.Warrior, sheet = "w_roseknight", blurb = L.T("장미 문장의 분홍 갑옷과 흰 망토. 분홍 꽃잎빛이 맴돈다."), trail = SkinTrailKind.MoonMotes, trailA = new Color(1.00f, 0.56f, 0.75f), trailB = new Color(1.00f, 0.88f, 0.93f) },
            new SkinDef { id = "skin_w_phoenix", name = L.T("불사조 성기사"), cls = CharacterClass.Warrior, sheet = "w_phoenix", blurb = L.T("불사조 날개 장식의 금홍 갑주. 걸음마다 불꽃 깃털과 잔상이 남는다."), trail = SkinTrailKind.Embers, trailA = new Color(1.00f, 0.48f, 0.10f), trailB = new Color(1.00f, 0.89f, 0.48f), afterimage = true },
            new SkinDef { id = "skin_w_aurora", name = L.T("오로라 여제"), cls = CharacterClass.Warrior, sheet = "w_aurora", blurb = L.T("오로라빛이 흐르는 수정 갑옷과 별빛 망토. 무지갯빛 잔상이 남는다."), trail = SkinTrailKind.Stars, trailA = new Color(0.48f, 1.00f, 0.84f), trailB = new Color(0.84f, 0.66f, 1.00f), afterimage = true },
            new SkinDef { id = "skin_w_voidblade", name = L.T("심연의 검성"), cls = CharacterClass.Warrior, sheet = "w_voidblade", blurb = L.T("보랏빛 심연 문양의 흑갑과 찢어진 망토. 어둠의 잔상이 남는다."), trail = SkinTrailKind.MoonMotes, trailA = new Color(0.60f, 0.36f, 1.00f), trailB = new Color(0.88f, 0.78f, 1.00f), afterimage = true },
            Plain("skin_m_lavender", L.T("라벤더 후드 로브"), CharacterClass.Mage, L.T("라벤더빛 후드가 달린 흰 견습 로브.")),
            Plain("skin_m_student", L.T("마법학교 교복"), CharacterClass.Mage, L.T("마법학교 신입생의 남색 교복과 베레모.")),
            Plain("skin_m_herbalist", L.T("약초꾼 로브"), CharacterClass.Mage, L.T("약초 주머니를 단 연둣빛 로브.")),
            Plain("skin_m_tea", L.T("찻잎 견습 로브"), CharacterClass.Mage, L.T("찻집 견습생의 갈색 로브와 흰 앞치마.")),
            Plain("skin_m_snowcloak", L.T("눈송이 망토"), CharacterClass.Mage, L.T("털 장식이 달린 하늘색 겨울 망토.")),
            Plain("skin_m_autumn", L.T("가을 낙엽 로브"), CharacterClass.Mage, L.T("단풍잎 장식의 주황빛 로브와 모자.")),
            Plain("skin_m_dusk", L.T("밤하늘 견습 로브"), CharacterClass.Mage, L.T("작은 별이 수놓인 짙은 보랏빛 로브.")),
            Tinted("skin_m_sky", L.T("하늘빛 마녀"), CharacterClass.Mage, L.T("구름 무늬의 하늘색 로브. 주위에 하늘빛 빛망울이 맴돈다."), new Color(0.56f, 0.82f, 1.00f), new Color(1.00f, 1.00f, 1.00f)),
            Tinted("skin_m_cherry", L.T("벚꽃 마녀"), CharacterClass.Mage, L.T("벚꽃 가지를 꽂은 분홍 로브. 주위에 분홍 빛망울이 맴돈다."), new Color(1.00f, 0.62f, 0.78f), new Color(1.00f, 0.94f, 0.96f)),
            Tinted("skin_m_mint", L.T("민트 마녀"), CharacterClass.Mage, L.T("민트색 리본 로브. 주위에 민트빛 빛망울이 맴돈다."), new Color(0.54f, 0.94f, 0.78f), new Color(0.94f, 1.00f, 0.97f)),
            Tinted("skin_m_amber", L.T("호박빛 마녀"), CharacterClass.Mage, L.T("호박 보석을 단 금갈색 로브. 주위에 금빛 빛망울이 맴돈다."), new Color(1.00f, 0.72f, 0.29f), new Color(1.00f, 0.94f, 0.82f)),
            Tinted("skin_m_ocean", L.T("바다 마녀"), CharacterClass.Mage, L.T("파도 무늬의 청록 로브. 주위에 물빛 빛망울이 맴돈다."), new Color(0.25f, 0.82f, 0.85f), new Color(0.88f, 1.00f, 1.00f)),
            Tinted("skin_m_lilac", L.T("라일락 마녀"), CharacterClass.Mage, L.T("라일락 꽃을 단 연보라 로브. 주위에 연보랏빛 빛망울이 맴돈다."), new Color(0.78f, 0.66f, 1.00f), new Color(0.96f, 0.93f, 1.00f)),
            Tinted("skin_m_candy", L.T("사탕 마녀"), CharacterClass.Mage, L.T("사탕 줄무늬의 알록달록한 로브. 주위에 색색의 빛망울이 맴돈다."), new Color(1.00f, 0.56f, 0.82f), new Color(0.75f, 0.91f, 1.00f)),
            new SkinDef { id = "skin_m_moonshrine", name = L.T("달빛 무녀"), cls = CharacterClass.Mage, blurb = L.T("초승달 장식의 흰 무녀복. 은빛 달빛 조각이 맴돈다."), trail = SkinTrailKind.MoonMotes, trailA = new Color(0.81f, 0.90f, 1.00f), trailB = new Color(1.00f, 1.00f, 1.00f) },
            new SkinDef { id = "skin_m_storm", name = L.T("뇌운의 마녀"), cls = CharacterClass.Mage, blurb = L.T("번개 무늬의 남보랏빛 로브. 작은 번개빛이 튄다."), trail = SkinTrailKind.Stars, trailA = new Color(1.00f, 0.91f, 0.35f), trailB = new Color(0.60f, 0.82f, 1.00f) },
            new SkinDef { id = "skin_m_sakura", name = L.T("벚꽃 무녀"), cls = CharacterClass.Mage, blurb = L.T("벚꽃잎이 흩날리는 분홍 무녀복. 꽃잎빛이 맴돈다."), trail = SkinTrailKind.MoonMotes, trailA = new Color(1.00f, 0.70f, 0.82f), trailB = new Color(1.00f, 0.94f, 0.96f) },
            new SkinDef { id = "skin_m_frostprincess", name = L.T("서리 공주"), cls = CharacterClass.Mage, blurb = L.T("얼음 결정 왕관과 푸른 드레스. 눈송이가 반짝인다."), trail = SkinTrailKind.Stars, trailA = new Color(0.75f, 0.91f, 1.00f), trailB = new Color(1.00f, 1.00f, 1.00f) },
            new SkinDef { id = "skin_m_celestial", name = L.T("천공의 대마녀"), cls = CharacterClass.Mage, blurb = L.T("성좌가 흐르는 남금빛 로브와 별 모자. 별빛 잔상이 남는다."), trail = SkinTrailKind.Stars, trailA = new Color(1.00f, 0.89f, 0.48f), trailB = new Color(0.74f, 0.85f, 1.00f), afterimage = true },
            new SkinDef { id = "skin_m_flameempress", name = L.T("화염 여제"), cls = CharacterClass.Mage, blurb = L.T("불꽃 왕관과 진홍 드레스. 불씨와 잔상이 남는다."), trail = SkinTrailKind.Embers, trailA = new Color(1.00f, 0.29f, 0.07f), trailB = new Color(1.00f, 0.82f, 0.29f), afterimage = true },
            new SkinDef { id = "skin_m_dream", name = L.T("몽환의 마녀"), cls = CharacterClass.Mage, blurb = L.T("구름과 달이 떠 있는 파스텔 로브. 몽환적인 잔상이 남는다."), trail = SkinTrailKind.MoonMotes, trailA = new Color(1.00f, 0.78f, 0.94f), trailB = new Color(0.78f, 0.90f, 1.00f), afterimage = true },
            // [2026-10-14] Starlight Exchange only (bought with Star Crystals, never in draws or synthesis).
            new SkinDef { id = "skin_w_starwarden", name = L.T("별빛 수호 기사"), cls = CharacterClass.Warrior, sheet = "w_starwarden", blurb = L.T("별빛 교환소에서만 얻는 남청색 갑주 드레스. 걸음마다 작은 별빛이 흩날린다."), trail = SkinTrailKind.Stars, trailA = new Color(1.00f, 0.91f, 0.63f), trailB = new Color(0.72f, 0.78f, 1.00f) },
            new SkinDef { id = "skin_m_starsage", name = L.T("별의 현자"), cls = CharacterClass.Mage, blurb = L.T("별빛 교환소에서만 얻는 별자리 로브. 은은한 달빛 결정이 따라온다."), trail = SkinTrailKind.MoonMotes, trailA = new Color(0.85f, 0.72f, 1.00f), trailB = new Color(1.00f, 1.00f, 1.00f) },
        };

        public static SkinDef Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var s in All) if (s.id == id) return s;
            return null;
        }

        /// <summary>
        /// [CAREER 2026-10-13] The base body of a promoted character with no skin on: the class body recoloured per career,
        /// shape unchanged. Warrior careers: FemaleWarriorPalette on the approved body; mage
        /// careers: Art/char_career_&lt;career&gt;_&lt;dir&gt;_&lt;frame&gt;. Null before the career change.
        /// </summary>
        public static CharacterLook CareerLook(CharacterClass cls, Career career)
        {
            if (career == Career.None || CareerCatalog.Base(career) != cls) return null;
            string id = "career_" + career.ToString().ToLowerInvariant();
            if (cls == CharacterClass.Warrior)
            {
                var l = CharacterLook.Player.Clone();
                l.id = "player_" + id; // the silver warrior renderer handles every "player_*" look
                l.skinSheet = id;
                l.armor = ArmorStyle.None;
                l.bottomTier = -1;
                return l;
            }
            if (cls == CharacterClass.Rogue)
            {
                // [ROGUE] Same rogue body, cloth recoloured per career (RoguePalette): "rogue_career_assassin" / "_ninja".
                var r = CharacterLook.Rogue.Clone();
                r.id = "rogue_" + id;
                r.skinSheet = id;
                r.armor = ArmorStyle.None;
                r.bottomTier = -1;
                return r;
            }
            var m = CharacterLook.Mage.Clone();
            m.id = id;
            return m;
        }

        /// <summary>The body look of a class wearing a skin (null when the skin doesn't fit the class).</summary>
        public static CharacterLook LookFor(CharacterClass cls, string skinId)
        {
            var s = Find(skinId);
            if (s == null || s.cls != cls) return null;
            if (cls == CharacterClass.Warrior)
            {
                var l = CharacterLook.Player.Clone();
                l.id = "player_" + s.id; // the silver warrior renderer handles every "player_*" look
                l.skinSheet = s.sheet;
                l.armor = ArmorStyle.None;
                l.bottomTier = -1;
                l.skinSleeve = s.sleeve; l.skinLeg = s.leg; l.skinBoot = s.boot; l.skinTrim = s.trim; l.skinCape = s.cape;
                return l;
            }
            var m = CharacterLook.Mage.Clone();
            m.id = s.id; // Art/char_<skin>_<dir>_<frame>
            return m;
        }
    }
}
