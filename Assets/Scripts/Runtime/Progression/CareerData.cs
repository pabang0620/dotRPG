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
        public string id, name, description, effect, synergy, delivery;
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
        public static string Role(Career c) => c == Career.Fighter ? "검기·기동·연계 / 검술 딜러" : c == Career.Guardian ? "도발·방벽 / 보호 탱커" : c == Career.Arcanist ? "원소 연계·광역 / 마법 딜러" : "회복·정화·축복 / 지원 힐러";
        public static string[] Branches(Career c) => c == Career.Fighter ? new[] { "검기의 흐름", "검의 현현" } : c == Career.Guardian ? new[] { "철벽과 보호", "도발과 반격" } : c == Career.Arcanist ? new[] { "원소의 공명", "마력 운용" } : new[] { "회복과 유지", "축복과 보호" };
        public static Color Color(Career c) => c == Career.Fighter ? new Color32(103, 179, 255, 255) : c == Career.Guardian ? new Color32(73, 205, 211, 255) : c == Career.Arcanist ? new Color32(174, 128, 250, 255) : new Color32(244, 215, 132, 255);
        static CareerSkill S(Career c, int i, string id, string name, string desc, string effect, float power, int mp, float cd, float range, float radius, float cast, float duration = 0, int hits = 1, string synergy = "해당 없음") => new CareerSkill {
            career=c,index=i,delivery=CareerMoves.Key(c,i),id=id,name=name,description=desc,effect=effect,power=power,mp=mp,cooldown=cd,range=range,radius=radius,cast=cast,duration=duration,hits=hits,synergy=synergy,
            kind=i==8?CareerSkillKind.Awakening:(i==0||i==4)?CareerSkillKind.Passive:CareerSkillKind.Active,
            branch=i<4?0:1,tier=i%4,level=i==8?15:i%4<2?15:i%4==2?18:22 };
        public static readonly CareerSkill[] All = Build();
        static CareerSkill[] Build()
        {
            var f=Career.Fighter; var g=Career.Guardian; var m=Career.Arcanist; var b=Career.Bishop;
            return new[] {
                // Stable save keys preserve learned ranks; all Fighter designs/deliveries below replace the former kit.
                S(f,0,"f_rhythm","검기 연성","기본 공격 적중마다 검기 1개를 모은다. 최대 3개, 6초 유지. 3개일 때 다음 전직 스킬 피해 +20%, 전부 소모.","swordflow",20,0,0,0,0,0,6),
                S(f,1,"f_cross","월아검","전방 6m로 청색 초승달 검기를 날린다. 폭 1.5m 관통, 대상당 한 번 적중.","f_wave",2.4f,12,4,6,.75f,.18f),
                S(f,2,"f_rush","섬광보","황금빛 검압과 함께 전방 3m를 돌파한다. 경로의 적을 한 번 타격하고 벽 앞에서 멈춘다.","f_dash",3.2f,16,7,3,.65f,.12f),
                S(f,3,"f_flurry","자월난무","시전 위치 반경 2.8m에 보랏빛 검기 소용돌이를 펼쳐 0.24초 간격으로 3번 벤다.","f_vortex",1.45f,25,11,0,2.8f,.25f,0,3),
                S(f,4,"f_edge","파죽지세","현재 HP가 80% 이상인 적에게 직접 공격 피해 +15%. 전투의 첫 틈을 강하게 연다.","opening",15,0,0,0,0,0),
                S(f,5,"f_break","천검낙","전방 5m 안의 적 위치에 청색 대검을 떨어뜨린다. 0.22초 낙하 후 반경 1.8m를 타격.","f_drop",4.0f,23,9,5,1.8f,.35f),
                S(f,6,"f_focus","염룡승천","전방 1.25·2.75·4.25m 지점에 0.18초 간격으로 검화가 솟는다. 각 분출 반경 1m.","f_eruption",1.6f,26,12,5.25f,1,.32f,0,3),
                S(f,7,"f_execute","검영추격","제자리에서 전방으로 잔영을 보내 세 지점을 0.18초 간격으로 벤다. 각 베기 반경 1.25m.","f_echo",1.8f,29,14,5.45f,1.25f,.2f,0,3),
                S(f,8,"f_awake","천검귀일","전방 5m 안 목표 주변에 검 3개가 낙하한 뒤, 반경 4m 검진이 한 점으로 모여 150% 결정타를 가한다.","f_convergence",3.2f,60,65,5,4,.65f,0,4,"검기 연성으로 전체 연출 강화"),
                S(g,0,"g_steel","강철의 심장","받는 피해 추가 6% 감소. 다른 감소와 합산, 최대 65%.","steel",6,0,0,0,0,0),
                S(g,1,"g_guard","강철의 보루","5초간 받는 피해 30% 감소, 이동 속도 30% 감소.","guard",30,12,9,0,0,.15f,5),
                S(g,2,"g_wall","회귀의 방패","방패를 4m 앞으로 던졌다가 회수한다. 자신과 경로 아군에게 HP 20% 보호막 6초. 경로 적 짧은 기절.","shield",.2f,24,14,0,4,.3f,6),
                S(g,3,"g_oath","동행의 보루","자신을 따라가는 반경 4m 보호 구역. 6초 동안 들어온 아군에게 남은 시간만큼 피해 감소 20%·보호막 12%.","ward",.12f,30,20,0,4,.4f,6),
                S(g,4,"g_retal","수호의 반향","피격 후 다음 공격 피해 +20%. 재충전 2초, 재귀 반격 없음.","retaliate",20,0,0,0,0,0,4),
                S(g,5,"g_taunt","대지의 호령","5m까지 퍼지는 충격파로 적을 도발한다. 공격 대상을 자신으로 4초 고정, 보스 1.2초.","taunt",.6f,12,8,0,5,.2f,4),
                S(g,6,"g_bash","방패 올려치기","0.75m 전진한 뒤 정면 110도, 3m 적에게 방패치기. 기절 1초, 보스 0.3초.", "bash",2.8f,18,7,3,1,.25f,1),
                S(g,7,"g_counter","응보의 방진","3초 피해 25% 감소. 받아낸 충격을 확장 파동으로 한 번 방출. 피격이 없으면 종료 시 방출.","counter",4.2f,22,13,0,3,.2f,3),
                S(g,8,"g_awake","천쇄방패","거대한 방패를 내려찍어 시전 위치 반경 6m의 적에게 공격력 1200% 피해를 1회 준다. 충격파가 바깥으로 확장된다.","shieldquake",12f,50,70,0,6,.65f),
                S(m,0,"m_elements","원소의 기억","다른 원소로 직접 적중하면 피해 +12%. 같은 원소 반복은 제외.","elements",12,0,0,0,0,0,5),
                S(m,1,"m_fire","홍련창","목표를 향한 화염창이 충돌 지점에서 반경 2m 폭발. 실제 적중 후 3초 화상.", "fire",4.2f,24,6,6,2,.55f,3),
                S(m,2,"m_ice","빙결삼창","정면과 좌우 22도로 얼음창 3개 발사. 6m 관통, 같은 적 중복 피해 없음, 적중 시 1.5초 빙결.","ice",3.1f,26,8,6,2.6f,.5f,1.5f),
                S(m,3,"m_storm","연쇄전격","전류가 전방 적에 도달한 뒤 3m 안의 다른 적으로 최대 5명 연결. 같은 적 중복 제외.","storm",3.1f,32,10,7,3,.4f,0,5),
                S(m,4,"m_flow","마력 순환","전직 공격 주문의 MP 소모 10% 감소. 중첩하지 않음.","flow",10,0,0,0,0,0),
                S(m,5,"m_orbit","성운낙하","전방 목표 주변에 마력성 3개를 시간차로 투척. 착탄마다 반경 1m 폭발.","orbit",1.65f,24,7,7,1,.3f,0,3),
                S(m,6,"m_veil","차원잔영","장애물 앞까지 3m 이동, HP 10% 보호막 2초. 출발 지점 잔영이 0.18초 뒤 반경 1.5m 폭발.","blink",.1f,28,16,3,0,.15f,2),
                S(m,7,"m_rift","유랑하는 균열","반경 3m 균열이 시전자에서 전방 목표까지 5초간 이동. 매초 현재 위치의 적을 타격.","rift",1.5f,38,15,6,3,.6f,5,5),
                S(m,8,"m_awake","천체 붕괴","반경 4m 전장 안 세 지점에 화염·얼음·전격이 시간차로 폭발. 두 번째 폭발은 1초 빙결.","eclipse",4.6f,65,65,7,4,.9f,0,3,"세 원소의 대규모 방출"),
                S(b,0,"b_mercy","생명의 숨","회복량 +10%. 최대 HP를 넘는 회복은 발생하지 않음.","mercy",10,0,0,0,0,0),
                S(b,1,"b_heal","치유의 깃","반경 4m 자신과 아군에게 치유의 깃을 보낸다. 깃이 도착하면 회복.","heal",2.8f,20,5,4,4,.3f),
                S(b,2,"b_bloom","생명의 파문","시전 위치 반경 3.5m에 6초간 생명의 파문이 6번 퍼진다. 각 파문이 닿은 아군을 한 번 회복.","hot",.8f,26,10,0,3.5f,.35f,6,6),
                S(b,3,"b_cleanse","정화의 종소리","반경 4m로 퍼지는 빛의 파동이 닿은 아군의 저주·둔화를 해제하고 소량 회복.","cleanse",1.4f,24,11,0,4,.25f),
                S(b,4,"b_grace","축복의 그릇","보호막량 +12%. 중복 사용은 합산 없이 큰 값으로 갱신.","grace",12,0,0,0,0,0),
                S(b,5,"b_light","심판의 광창","빛의 창이 전방 7m, 폭 1.2m 경로를 날아가며 적을 관통.","light",2.8f,14,4,7,.6f,.3f),
                S(b,6,"b_wing","천사의 품","반경 4m 아군에게 날개 깃을 보내 도착 시 HP 18% 보호막 6초.","wings",.18f,26,14,0,4,.3f,6),
                S(b,7,"b_bless","축복의 연결","반경 4.5m 아군에게 순서대로 축복을 이어 공격 피해 +18%, 8초. 같은 축복은 갱신.","bless",18,28,19,0,4.5f,.4f,8),
                S(b,8,"b_awake","천상의 행진","반경 6m 아군 즉시 정화·회복·HP 25% 보호막. 자신을 따라가는 8초 성역에서 회복 파문 8회.","dawn",4.5f,60,70,0,6,.75f,8,8),
            };
        }
        public static CareerSkill Get(string id) { foreach(var s in All) if(s.id==id) return s; return null; }
        public static CareerSkill[] For(Career c) => Array.FindAll(All,s=>s.career==c);
    }
}
