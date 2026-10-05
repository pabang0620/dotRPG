using System;
using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    public enum Career { None, Fighter, Guardian, Arcanist, Bishop }
    public enum CareerSkillKind { Passive, Active, Awakening }
    [Serializable] public sealed class CareerRank { public string id; public int rank; }
    [Serializable] public sealed class CareerSave
    {
        public int schema = 1;
        public Career career;
        public List<CareerRank> nodes = new List<CareerRank>();
        public List<string> training = new List<string>();
        public int refunded;
        public int questStage;
        public bool awakened;
    }
    [Serializable] public sealed class CareerSkill
    {
        public string id, name, description, effect, synergy;
        public Career career;
        public CareerSkillKind kind;
        public int index, level, branch, tier, mp, hits;
        public float power, cooldown, range, radius, cast, duration;
        public string Icon => "career_" + id;
        public string Prerequisite => tier == 0 || kind == CareerSkillKind.Awakening ? null : CareerCatalog.For(career)[index - 1].id;
        public SkillGem Gem => new SkillGem { id = id, name = name, description = description, icon = Icon,
            kind = GemKind.Active, classOnly = CareerCatalog.Base(career), unlockLevel = level,
            slot = kind == CareerSkillKind.Awakening ? 4 : -1, damageMult = power, cooldown = cooldown,
            manaCost = mp, range = range, radius = radius, hits = hits };
    }
    public static class CareerCatalog
    {
        public static CharacterClass Base(Career c) => c == Career.Fighter || c == Career.Guardian ? CharacterClass.Warrior : CharacterClass.Mage;
        public static string Name(Career c) => c == Career.Fighter ? "파이터" : c == Career.Guardian ? "수호자" : c == Career.Arcanist ? "메이지" : c == Career.Bishop ? "비숍" : "기본 직업";
        public static string Role(Career c) => c == Career.Fighter ? "연격·치명타 / 근접 딜러" : c == Career.Guardian ? "도발·방벽 / 보호 탱커" : c == Career.Arcanist ? "원소 연계·광역 / 마법 딜러" : "회복·정화·축복 / 지원 힐러";
        public static string[] Branches(Career c) => c == Career.Fighter ? new[] { "연격과 속도", "치명과 격파" } : c == Career.Guardian ? new[] { "철벽과 보호", "도발과 반격" } : c == Career.Arcanist ? new[] { "원소의 공명", "마력 운용" } : new[] { "회복과 유지", "축복과 보호" };
        public static Color Color(Career c) => c == Career.Fighter ? new Color32(245, 100, 65, 255) : c == Career.Guardian ? new Color32(73, 205, 211, 255) : c == Career.Arcanist ? new Color32(174, 128, 250, 255) : new Color32(244, 215, 132, 255);
        static CareerSkill S(Career c, int i, string id, string name, string desc, string effect, float power, int mp, float cd, float range, float radius, float cast, float duration = 0, int hits = 1, string synergy = "해당 없음") => new CareerSkill {
            career=c,index=i,id=id,name=name,description=desc,effect=effect,power=power,mp=mp,cooldown=cd,range=range,radius=radius,cast=cast,duration=duration,hits=hits,synergy=synergy,
            kind=i==8?CareerSkillKind.Awakening:(i==0||i==4)?CareerSkillKind.Passive:CareerSkillKind.Active,
            branch=i<4?0:1,tier=i%4,level=i==8?15:i%4<2?15:i%4==2?18:22 };
        public static readonly CareerSkill[] All = Build();
        static CareerSkill[] Build()
        {
            var f=Career.Fighter; var g=Career.Guardian; var m=Career.Arcanist; var b=Career.Bishop;
            return new[] {
                S(f,0,"f_rhythm","연격의 호흡","적중 시 4초간 공격 속도 +3%/중첩, 최대 5중첩.","rhythm",3,0,0,0,0,0,4),
                S(f,1,"f_cross","쌍흔 연격","짧은 간격으로 두 번 가르는 검격.","slash",1.5f,10,4,2.2f,1.1f,.12f,0,2,"연격의 호흡 중첩"),
                S(f,2,"f_rush","돌파 찌르기","전방으로 접근하고 좁은 범위를 찌른다.","rush",3.4f,16,8,3.5f,.85f,.18f),
                S(f,3,"f_flurry","질풍 삼연","전방에 세 번의 빠른 검격.","flurry",1.75f,24,11,2.4f,1.4f,.2f,0,3,"중첩마다 피해 +3%"),
                S(f,4,"f_edge","예리한 결의","직접 공격 치명타 확률 +8%, 치명 피해 150%.","critical",8,0,0,0,0,0),
                S(f,5,"f_break","갑주 가르기","적을 베어 6초간 자신의 후속 공격 피해 +15%.","break",2.6f,14,7,2.3f,1.1f,.25f,6),
                S(f,6,"f_focus","응축된 투지","5초간 치명타 확률 +25%; 방어 효과는 없다.","focus",0,20,18,0,0,.18f,5),
                S(f,7,"f_execute","일점 격파","단일 적에게 강타. 체력 35% 이하 대상 피해 +50%.","execute",5.5f,28,14,2.5f,.7f,.45f),
                S(f,8,"f_awake","불굴의 오검","다섯 검흔을 완성한다. 4연격 후 3배 위력의 결정타.","five",1.8f,50,60,3.2f,2.3f,.45f,0,5,"파괴 표식·치명 적용"),
                S(g,0,"g_steel","강철의 심장","받는 피해 추가 6% 감소. 다른 감소와 합산, 최대 65%.","steel",6,0,0,0,0,0),
                S(g,1,"g_guard","철벽 자세","4초간 받는 피해 30% 감소, 이동 속도 30% 감소.","guard",30,12,9,0,0,.15f,4),
                S(g,2,"g_wall","동료의 방벽","주변 동료에게 최대 HP의 18% 보호막, 6초.","shield",.18f,24,14,0,3.2f,.3f,6),
                S(g,3,"g_oath","불굴의 결계","5초간 주변 아군 피해 20% 감소, 보호막 10%.","ward",.1f,30,20,0,3.5f,.4f,5),
                S(g,4,"g_retal","수호의 반향","피격 후 다음 공격 피해 +20%. 재충전 2초, 재귀 반격 없음.","retaliate",20,0,0,0,0,0,4),
                S(g,5,"g_taunt","수호자의 호령","4초 강제 도발; 보스는 1.2초, 최대 위협의 150% 확보.","taunt",.6f,12,8,0,3.4f,.15f,4),
                S(g,6,"g_bash","강철 진군","전방 방패 강타. 기절 1초(보스 0.3초).", "bash",2.5f,18,7,2,1.3f,.3f,1),
                S(g,7,"g_counter","맹세의 반격","3초 피해 25% 감소. 피격 시 1회 반격, 없으면 종료 시 방출.","counter",3.8f,22,13,0,2.3f,.2f,3),
                S(g,8,"g_awake","무너지지 않는 맹세","8초간 아군에게 40% 보호막과 35% 피해 감소. 주변 적 도발.","citadel",.4f,50,70,0,5,.5f,8),
                S(m,0,"m_elements","원소의 기억","다른 원소로 직접 적중하면 피해 +12%. 같은 원소 반복은 제외.","elements",12,0,0,0,0,0,5),
                S(m,1,"m_fire","화염 룬창","전방 원소창 폭발. 3초간 3회 화상(회당 공격력 20%).", "fire",3.5f,22,5,6,1.2f,.4f,3),
                S(m,2,"m_ice","서리 결정진","지정 방향에 냉기 결정을 펼쳐 1.5초 빙결.","ice",2.9f,26,8,5,2,.5f,1.5f),
                S(m,3,"m_storm","뇌광 공명","가까운 적 4마리까지 전류 연결. 원소 전환 시 추가 피해.","storm",3.6f,32,10,6,3,.55f,0,4),
                S(m,4,"m_flow","마력 순환","전직 공격 주문의 MP 소모 10% 감소. 중첩하지 않음.","flow",10,0,0,0,0,0),
                S(m,5,"m_orbit","비전 궤도","앞쪽에 3회 수렴하는 마력구.","orbit",1.6f,24,7,5,1.4f,.35f,0,3),
                S(m,6,"m_veil","잔광 도약","장애물 앞까지 짧게 이동, 최대 HP 8% 보호막 2초.","blink",.08f,28,16,2.5f,0,.15f,2),
                S(m,7,"m_rift","마력 균열","4초간 고정 범위에 4회 폭발. 이동한 적에게는 적중하지 않음.","rift",1.65f,38,15,5,2.4f,.65f,4,4),
                S(m,8,"m_awake","삼원 일식","화염·냉기·번개 룬을 정렬하여 세 원소 폭발을 일으킨다.","eclipse",4.2f,65,65,5,3.3f,.8f,0,3,"서로 다른 원소 공명"),
                S(b,0,"b_mercy","생명의 숨","회복량 +10%. 최대 HP를 넘는 회복은 발생하지 않음.","mercy",10,0,0,0,0,0),
                S(b,1,"b_heal","치유의 기도","범위 안 체력 비율이 가장 낮은 아군 1명을 즉시 회복.","heal",3.2f,18,5,5,.7f,.25f),
                S(b,2,"b_bloom","새잎의 숨결","가장 다친 아군에게 5초 동안 매초 회복. 동일 효과 갱신.","hot",.85f,22,9,5,.7f,.25f,5,5),
                S(b,3,"b_cleanse","정화의 종","아군의 전직 시련 저주·둔화를 해제하고 소량 회복.","cleanse",1.4f,24,11,0,3.5f,.25f),
                S(b,4,"b_grace","축복의 그릇","보호막량 +12%. 중복 사용은 합산 없이 큰 값으로 갱신.","grace",12,0,0,0,0,0),
                S(b,5,"b_light","여명의 화살","응축한 빛으로 단일 적 공격. 솔로 성장용 기술.","light",2.5f,12,4,6,.8f,.25f),
                S(b,6,"b_wing","빛의 날개","주변 아군 최대 HP의 16% 보호막, 5초.","wings",.16f,26,14,0,3.2f,.3f,5),
                S(b,7,"b_bless","새벽의 축복","주변 아군 공격 피해 +15%, 7초. 같은 축복은 갱신.","bless",15,28,19,0,3.5f,.35f,7),
                S(b,8,"b_awake","돌아온 새벽","성역 안 아군 즉시 회복(공격력 500%), 25% 보호막과 정화.","dawn",5,55,70,0,5,.6f,8),
            };
        }
        public static CareerSkill Get(string id) { foreach(var s in All) if(s.id==id) return s; return null; }
        public static CareerSkill[] For(Career c) => Array.FindAll(All,s=>s.career==c);
    }
}
