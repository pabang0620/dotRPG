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
        /// <summary>
        /// [BALANCE 2026-10-06] Skills are used more often and reach wider: actives cost 0.65x mana, recharge in 0.7x
        /// the time and cover 1.5x the radius; damage per cast 0.85x (about 1.2x damage per second), heals 0.8x,
        /// buffs and shields last twice as long with cooldown 1.6x. The awakening skill goes the other way: cooldown 1.25x but duration and hit count 1.5x
        /// (radius 1.3x, mana 0.75x). Passives (slots
        /// 0 and 4) are untouched. "반경 Nm" in a description follows the new radius.
        /// </summary>
        static readonly string[] HealEffects = { "heal", "bloom", "cleanse" };
        static readonly string[] BuffEffects = { "guard", "oath", "bless", "counter", "frenzy" };
        /// <summary>Lasting buffs and shields: twice as long, cast less often (pressing them again and again is a chore).</summary>
        static readonly string[] LongBuffs = { "guard", "oath", "bless", "counter" };
        static CareerSkill S(Career c, int i, string id, string name, string desc, string effect, float power, int mp, float cd, float range, float radius, float cast, float duration = 0, int hits = 1, string synergy = "해당 없음")
        {
            bool passive = i == 0 || i == 4, awaken = i == 8;
            if (!passive)
            {
                float radiusK = awaken ? 1.3f : 1.5f;
                if (radius > 0f)
                {
                    float before = radius;
                    radius = Mathf.Round(radius * radiusK * 10f) / 10f;
                    desc = System.Text.RegularExpressions.Regex.Replace(desc, @"(반경|주변) (\d+(?:\.\d+)?)m", m =>
                        Mathf.Abs(float.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture) - before) < 0.05f ? $"{m.Groups[1].Value} {radius:0.#}m" : m.Value);
                }
                mp = Mathf.Max(1, Mathf.RoundToInt(mp * (awaken ? 0.75f : 0.65f)));
                // The awakening is the big moment: it comes back more slowly but lasts longer and lands more often.
                bool longBuff = System.Array.IndexOf(LongBuffs, effect) >= 0;
                // A signature skill keeps its own numbers: 0 = spammable (no cooldown), "frenzy" = the fighter's burst window.
                bool signature = cd <= 0f || effect == "frenzy";
                if (!signature) cd = Mathf.Max(0.5f, Mathf.Round(cd * (awaken ? 1.25f : longBuff ? 1.6f : 0.7f) * 2f) / 2f);
                if (longBuff)
                {
                    float was = duration;
                    duration *= 2f;
                    desc = System.Text.RegularExpressions.Regex.Replace(desc, @"(\d+(?:\.\d+)?)초", m =>
                        Mathf.Abs(float.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) - was) < 0.05f ? $"{duration:0.#}초" : m.Value);
                }
                if (awaken)
                {
                    duration *= 1.5f;
                    hits = Mathf.Max(hits, Mathf.CeilToInt(hits * 1.5f));
                }
                if (System.Array.IndexOf(HealEffects, effect) >= 0) power *= 0.8f;
                else if (System.Array.IndexOf(BuffEffects, effect) < 0 && !awaken) power *= 0.85f;
            }
            return Make(c, i, id, name, desc, effect, power, mp, cd, range, radius, cast, duration, hits, synergy);
        }

        static CareerSkill Make(Career c, int i, string id, string name, string desc, string effect, float power, int mp, float cd, float range, float radius, float cast, float duration, int hits, string synergy) => new CareerSkill {
            career=c,index=i,delivery=effect,id=id,name=name,description=desc,effect=effect,power=power,mp=mp,cooldown=cd,range=range,radius=radius,cast=cast,duration=duration,hits=hits,synergy=synergy,
            kind=i==8?CareerSkillKind.Awakening:(i==0||i==4)?CareerSkillKind.Passive:CareerSkillKind.Active,
            branch=i<4?0:1,tier=i%4,level=i==8?15:i%4<2?15:i%4==2?18:22 };
        public static readonly CareerSkill[] All = Build();
        static CareerSkill[] Build()
        {
            var f=Career.Fighter; var g=Career.Guardian; var m=Career.Arcanist; var b=Career.Bishop;
            return new[] {
                // Stable save keys and slot order (server careerRules.ts); designs: Docs/PLAN_CAREER_SKILLS.md.
                S(f,0,"f_rhythm","검기 연성","기본 공격 적중마다 검기 1개를 모은다. 최대 3개, 6초 유지. 3개일 때 다음 전직 스킬 피해 +20%, 전부 소모.","swordflow",20,0,0,0,0,0,6),
                S(f,1,"f_cross","십자참","전방 120도, 2.3m를 X자로 두 번 벤다. 두 번째 베기가 적을 밀어낸다.","cross",1.7f,10,3,2.3f,0,.08f,0,2),
                S(f,2,"f_rush","섬광보","전방 4m를 섬광처럼 돌진하며 경로의 적을 벤다. 벽 앞에서 멈춘다.","rush",2.8f,14,6,4,.75f,.06f),
                S(f,3,"f_flurry","검귀 해방","검기를 몸에 두르고 10초 동안 모든 공격 피해 +100%. 이 동안 몰아치는 폭딜 구간을 연다.","frenzy",100,20,45,0,0,.1f,10),
                S(f,4,"f_edge","파죽지세","현재 HP가 80% 이상인 적에게 직접 공격 피해 +15%. 전투의 첫 틈을 강하게 연다.","opening",15,0,0,0,0,0),
                S(f,5,"f_break","파쇄 검기","재사용 대기시간 없이 연달아 날리는 7m 관통 초승달 검기. 맞은 적은 4초 동안 내 공격에 15% 더 큰 피해를 받는다.","break",1.4f,6,0,7,.8f,.15f,4),
                S(f,6,"f_focus","일섬","전방 6m 직선에 칼금을 긋고 0.18초 뒤 그 위의 적을 한꺼번에 베어낸다.","iaido",4.4f,22,10,6,.55f,.25f),
                S(f,7,"f_execute","단죄","앞의 적에게 뛰어들어 내려찍는다(반경 1.3m). 대상 HP가 35% 미만이면 피해 1.6배.","execute",5.2f,26,12,3.5f,1.3f,.2f),
                S(f,8,"f_awake","천검귀일","전방 적 위로 검 6자루가 떨어진 뒤 거대한 검이 꽂힌다. 거대한 검은 반경 3.5m에 600% 피해와 0.8초 기절.","swordrain",1.6f,60,60,5,3.5f,.5f,0,6,"검기 연성 3개면 모든 낙검 강화"),
                S(g,0,"g_steel","강철의 심장","받는 피해 추가 6% 감소. 다른 감소와 합산, 최대 65%.","steel",6,0,0,0,0,0),
                S(g,1,"g_guard","강철의 보루","방패를 내려찍어 주변 1.6m의 적을 밀어내고, 5초 동안 받는 피해 30% 감소. 이동 속도 30% 감소.","guard",.8f,12,10,0,1.6f,.1f,5),
                S(g,2,"g_wall","회귀의 방패","재사용 대기시간 없이 방패를 5m 던졌다가 회수한다. 동시에 3개까지(강화 단계마다 +1, 최대 5개) 날리고, 강화할수록 더 빨리 던진다. 오가며 적을 치고 0.3초 기절, 닿은 아군과 자신에게 HP 18% 보호막 6초.","shieldthrow",.38f,6,0,5,0,.08f,6,2),
                S(g,3,"g_oath","수호의 맹세","6초 동안 자신을 따라가는 반경 4m 결계. 안의 아군은 받는 피해 20% 감소, HP 10% 보호막.","oath",.1f,28,20,0,4,.2f,6),
                S(g,4,"g_retal","수호의 반향","피격 후 다음 공격 피해 +20%. 재충전 2초.","retaliate",20,0,0,0,0,0,4),
                S(g,5,"g_taunt","대지의 호령","땅을 굴러 반경 5m의 적을 도발한다. 공격 대상을 자신으로 4초 고정, 보스 1.2초.","taunt",.8f,12,8,0,5,.15f,4),
                S(g,6,"g_bash","방패 강타","0.8m 전진하며 전방 110도, 2.2m를 방패로 친다. 기절 1.2초(보스 0.3초), 크게 밀어낸다.","bash",3f,16,7,2.2f,0,.18f,1.2f),
                S(g,7,"g_counter","응보의 방진","3초 동안 받는 피해 40% 감소. 첫 피격 때 반경 3m 반격 충격파. 피격이 없으면 끝날 때 70% 위력으로 방출.","counter",3.4f,20,13,0,3,.1f,3),
                S(g,8,"g_awake","천쇄방패","뛰어올라 반경 5m를 내려찍는다. 기절 1.5초(보스 0.5초)와 도발. 반경 6m 아군과 자신에게 HP 25% 보호막 8초.","aegis",5f,50,70,0,5,.55f,8,1,"수호자의 보호와 제어를 한 번에"),
                S(m,0,"m_elements","원소의 기억","다른 원소로 직접 적중하면 피해 +12%. 같은 원소 반복은 제외.","elements",12,0,0,0,0,0,5),
                S(m,1,"m_fire","홍련구","재사용 대기시간 없이 연달아 쏘는 화염구. 목표에 닿으면 반경 1.8m로 폭발하고 맞은 적은 3초 화상.","fire",1.2f,8,0,7,1.8f,.15f,3),
                S(m,2,"m_ice","빙결삼창","정면과 좌우 20도로 얼음창 3개를 쏜다. 6.5m 관통, 같은 적은 한 번만, 적중 시 1.2초 빙결.","ice",2.6f,24,8,6.5f,.4f,.3f,1.2f),
                S(m,3,"m_storm","연쇄전격","전방의 적에게 전격을 꽂고 3m 안의 다른 적으로 최대 5명까지 이어진다.","storm",2.8f,28,9,7,3,.25f,0,5),
                S(m,4,"m_flow","마력 순환","전직 주문의 MP 소모 10% 감소.","flow",10,0,0,0,0,0),
                S(m,5,"m_orbit","성운 폭발","키를 누르고 있는 동안 마력을 모으고(최대 2초), 떼면 목표 지점에 성운이 터진다. 오래 모을수록 폭발 범위가 최대 1.8배, 피해가 최대 3배로 커진다. 모으는 동안 천천히 움직인다.","nebula",2.6f,20,6,7,2.2f,0f),
                S(m,6,"m_veil","차원도약","3.5m 순간이동하고 HP 12% 보호막 2초. 떠난 자리의 잔상이 0.25초 뒤 반경 1.8m 폭발.","blink",2.4f,24,14,3.5f,1.8f,.05f,2),
                S(m,7,"m_rift","중력 균열","목표 지점에 반경 2.6m 균열을 연다. 3초 동안 적을 끌어당기며 6번 타격하고 마지막에 붕괴(160%).","rift",.8f,36,15,7,2.6f,.35f,3,6),
                S(m,8,"m_awake","천체 붕괴","화염·얼음·전격 운석이 차례로 떨어진 뒤(각 300%) 중심이 반경 4m로 붕괴한다(450%). 얼음 운석은 1.5초 빙결.","cataclysm",3f,65,65,7,4,.7f,0,4,"세 원소가 모두 터져 원소의 기억 최대 발동"),
                S(b,0,"b_mercy","생명의 숨","회복량 +10%.","mercy",10,0,0,0,0,0),
                S(b,1,"b_heal","치유의 깃","반경 5m 자신과 아군에게 치유의 깃이 날아가 도착하면 회복한다.","heal",2.8f,20,5,0,5,.25f),
                S(b,2,"b_bloom","생명의 파문","시전 위치 반경 3.5m에 6초 동안 생명의 파문이 6번 퍼져 아군을 회복한다.","bloom",.8f,26,12,0,3.5f,.25f,6,6),
                S(b,3,"b_cleanse","정화의 종","반경 4m에 종소리가 퍼져 아군의 저주·둔화를 풀고 회복한다. 적에게는 빛 피해(90%)와 밀어내기.","cleanse",1.4f,24,11,0,4,.2f),
                S(b,4,"b_grace","축복의 그릇","보호막량 +12%. 같은 보호막은 합산 없이 큰 값으로 갱신.","grace",12,0,0,0,0,0),
                S(b,5,"b_light","심판의 광창","재사용 대기시간 없이 연달아 던지는 빛의 창. 전방 7m를 관통한다.","light",.9f,4,0,7,.6f,.12f),
                S(b,6,"b_wing","신의 가호","반경 5m 안의 아군과 자신을 1초 동안 모든 피해로부터 지킨다. 보스의 큰 공격을 받아 내는 순간에 쓴다.","sanctuary",0f,20,25,0,5,0f,1),
                S(b,7,"b_bless","축복의 연결","반경 5m 아군에게 축복을 이어 공격 피해 +18%, 8초.","bless",18,28,20,0,5,.3f,8),
                S(b,8,"b_awake","천상의 행진","빛기둥이 반경 6m를 덮는다. 적은 250% 피해와 밀어내기, 아군은 정화·회복·HP 25% 보호막. 8초 동안 따라오는 성역이 8번 회복.","dawn",4.5f,60,70,0,6,.7f,8,8,"비숍의 회복과 보호를 한 번에"),
            };
        }
        public static CareerSkill Get(string id) { foreach(var s in All) if(s.id==id) return s; return null; }
        public static CareerSkill[] For(Career c) => Array.FindAll(All,s=>s.career==c);
    }
}
