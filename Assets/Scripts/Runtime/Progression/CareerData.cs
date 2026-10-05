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
                S(f,1,"f_cross","월하쌍섬","전방 120도 부채꼴을 좌우 교차로 두 번 벤다.","slash",1.65f,10,4,2.8f,1.4f,.16f,0,2,"연격의 호흡 중첩"),
                S(f,2,"f_rush","무영일섬","전방 4m를 돌파하며 지나친 적을 한 번씩 벤다. 벽 앞에서 정지.","rush",3.6f,16,8,4,.8f,.18f),
                S(f,3,"f_flurry","비연검무","전방 140도에 사선·역베기·수평검의 세 박자를 펼친다.","flurry",1.9f,24,11,3.2f,1.6f,.24f,0,3,"중첩마다 피해 +3%"),
                S(f,4,"f_edge","예리한 결의","직접 공격 치명타 확률 +8%, 치명 피해 150%.","critical",8,0,0,0,0,0),
                S(f,5,"f_break","파공검기","초승달 검기가 7m를 관통. 적중한 적에게 6초간 자신의 후속 피해 +15%.","break",3.2f,18,7,7,.85f,.3f,6),
                S(f,6,"f_focus","검신합일","검의를 응축해 6초간 치명타 확률 +25%.","focus",0,20,18,0,0,.3f,6),
                S(f,7,"f_execute","발도·단공","전방 8m, 폭 1.4m의 직선을 관통하는 발도. 체력 35% 이하 적에게 +50%.","execute",5.2f,28,14,8,.7f,.48f),
                S(f,8,"f_awake","천지일선","검의를 한 줄에 모아 전방 18m, 폭 3.6m의 전장을 가르는 일격.","five",12.6f,55,65,18,1.8f,.85f,0,1,"파괴 표식·치명 적용"),
                S(g,0,"g_steel","강철의 심장","받는 피해 추가 6% 감소. 다른 감소와 합산, 최대 65%.","steel",6,0,0,0,0,0),
                S(g,1,"g_guard","강철의 보루","5초간 받는 피해 30% 감소, 이동 속도 30% 감소.","guard",30,12,9,0,0,.15f,5),
                S(g,2,"g_wall","수호의 방패","반경 4m 아군에게 최대 HP의 20% 보호막, 6초.","shield",.2f,24,14,0,4,.3f,6),
                S(g,3,"g_oath","결속의 성벽","6초간 반경 4m 아군 피해 20% 감소, 보호막 12%.","ward",.12f,30,20,0,4,.4f,6),
                S(g,4,"g_retal","수호의 반향","피격 후 다음 공격 피해 +20%. 재충전 2초, 재귀 반격 없음.","retaliate",20,0,0,0,0,0,4),
                S(g,5,"g_taunt","불퇴의 호령","반경 5m 적의 공격 대상을 본인으로 4초 고정. 보스 1.2초, 최대 위협 150%.","taunt",.6f,12,8,0,5,.2f,4),
                S(g,6,"g_bash","방패 돌격","전방 3m를 돌진하며 경로의 적에게 방패치기. 기절 1초(보스 0.3초).", "bash",2.8f,18,7,3,1,.25f,1),
                S(g,7,"g_counter","응보의 방진","3초 피해 25% 감소. 받은 충격을 1회 방출, 피격이 없으면 종료 시 방출.","counter",4.2f,22,13,0,3,.2f,3),
                S(g,8,"g_awake","불멸의 성채","거대한 성채 전개. 8초간 반경 6m 아군에게 40% 보호막·35% 피해 감소, 적 도발.","citadel",.4f,50,70,0,6,.65f,8),
                S(m,0,"m_elements","원소의 기억","다른 원소로 직접 적중하면 피해 +12%. 같은 원소 반복은 제외.","elements",12,0,0,0,0,0,5),
                S(m,1,"m_fire","홍련 운석","전방 목표에 운석 낙하. 반경 2m 폭발과 3초 화상(매초 공격력 20%).", "fire",4.2f,24,6,6,2,.55f,3),
                S(m,2,"m_ice","빙하의 왕관","전방 목표 주위 반경 2.6m에 얼음 기둥을 솟구쳐 1.5초 빙결.","ice",3.1f,26,8,6,2.6f,.5f,1.5f),
                S(m,3,"m_storm","연쇄 낙뢰","전방 적에서 시작해 3m 안의 다른 적으로 최대 5번 연결. 같은 적 중복 제외.","storm",3.1f,32,10,7,3,.4f,0,5),
                S(m,4,"m_flow","마력 순환","전직 공격 주문의 MP 소모 10% 감소. 중첩하지 않음.","flow",10,0,0,0,0,0),
                S(m,5,"m_orbit","비전 추적성","시간차로 날아가는 마력탄 3개가 전방의 적을 추적한다.","orbit",1.65f,24,7,7,1,.3f,0,3),
                S(m,6,"m_veil","차원 보행","잔광을 남기고 장애물 앞까지 3m 이동. 최대 HP 10% 보호막 2초.","blink",.1f,28,16,3,0,.15f,2),
                S(m,7,"m_rift","별의 소용돌이","반경 3m의 고정 마법진에 5초간 매초 마력 폭발. 영역을 벗어나면 피해 없음.","rift",1.5f,38,15,6,3,.6f,5,5),
                S(m,8,"m_awake","천체 붕괴","겹쳐진 천체 마법진 위로 운석·빙하·낙뢰가 차례로 강림. 두 번째 폭발은 1초 빙결.","eclipse",4.6f,65,65,7,4,.9f,0,3,"세 원소의 대규모 방출"),
                S(b,0,"b_mercy","생명의 숨","회복량 +10%. 최대 HP를 넘는 회복은 발생하지 않음.","mercy",10,0,0,0,0,0),
                S(b,1,"b_heal","치유의 합창","반경 4m 안 자신과 파티원을 동시에 즉시 회복.","heal",2.8f,20,5,4,4,.3f),
                S(b,2,"b_bloom","생명의 성역","시전 위치 반경 3.5m에 6초간 성역 생성. 안에 있는 아군을 매초 회복.","hot",.8f,26,10,0,3.5f,.35f,6,6),
                S(b,3,"b_cleanse","정화의 종소리","반경 4m 아군의 저주·둔화를 해제하고 소량 회복.","cleanse",1.4f,24,11,0,4,.25f),
                S(b,4,"b_grace","축복의 그릇","보호막량 +12%. 중복 사용은 합산 없이 큰 값으로 갱신.","grace",12,0,0,0,0,0),
                S(b,5,"b_light","심판의 광창","전방 7m, 폭 1.2m에 빛의 창을 관통시킨다.","light",2.8f,14,4,7,.6f,.3f),
                S(b,6,"b_wing","천사의 품","빛의 날개로 반경 4m 아군을 감싸 최대 HP 18% 보호막, 6초.","wings",.18f,26,14,0,4,.3f,6),
                S(b,7,"b_bless","영광의 축복","반경 4.5m 아군 공격 피해 +18%, 8초. 같은 축복은 갱신.","bless",18,28,19,0,4.5f,.4f,8),
                S(b,8,"b_awake","천상의 문","반경 6m 아군 정화·즉시 회복·25% 보호막 8초. 시전 지점에 8초 회복 성역 전개.","dawn",4.5f,60,70,0,6,.75f,8,8),
            };
        }
        public static CareerSkill Get(string id) { foreach(var s in All) if(s.id==id) return s; return null; }
        public static CareerSkill[] For(Career c) => Array.FindAll(All,s=>s.career==c);
    }
}
