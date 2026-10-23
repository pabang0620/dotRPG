using UnityEngine;

namespace DotRPG
{
    public static class CareerNumbers
    {
        public static string Summary(CareerSkill s,CharacterData data,SkillNumbers n)
        {
            float scale=n.careerPotency>0?n.careerPotency:1;
            int mercy=data.Progression.Rank("b_mercy"),grace=data.Progression.Rank("b_grace");
            float healing=mercy>0?1+(10+5*(mercy-1))/100f:1;
            float shielding=grace>0?1+(12+4*(grace-1))/100f:1;
            int d=n.damage;
            int Heal(float x)=>Mathf.RoundToInt(x*healing);
            string Shield(float fraction)=>L.F("HP {0:0.#}% 보호막", fraction*scale*shielding*100);
            string effect;
            switch(s.effect)
            {
                case "cross":effect=L.F("피해 {0} × 2회 · 부채꼴 {1:0.#}m", d, n.range);break;
                case "rush":effect=L.F("경로 피해 {0} · 돌진 {1:0.#}m", d, n.range);break;
                case "frenzy":effect=L.F("모든 공격 피해 +{0}% · {1:0.#}초", Mathf.RoundToInt(s.power*scale), s.duration);break;
                case "flurry":effect=L.F("피해 {0} × 4회 + 마무리 {1}", d, Mathf.RoundToInt(d*1.6f/.7f));break;
                case "break":effect=L.F("관통 피해 {0} · {1:0.#}m · 맞은 적 {2:0.#}초간 받는 피해 +15%", d, n.range, s.duration);break;
                case "iaido":effect=L.F("직선 피해 {0} · 길이 {1:0.#}m", d, n.range);break;
                case "execute":effect=L.F("피해 {0} · HP 35% 미만 {1} · 반경 {2:0.#}m", d, Mathf.RoundToInt(d*1.6f), n.radius);break;
                case "xslash":effect=L.F("십자 베기 {0} · 반경 {1:0.#}m · 경직 3초(보스 포함)", Mathf.RoundToInt(d*10f), n.radius);break;
                case "swordrain":effect=L.F("낙검 {0} × 6 · 거대한 검 {1} · 반경 {2:0.#}m", d, Mathf.RoundToInt(d*3.75f), n.radius);break;
                case "guard":effect=L.F("받는 피해 -{0}% · {1:0.#}초 · 밀쳐내기 피해 {2}", Mathf.RoundToInt(30*scale), s.duration, d);break;
                case "shieldthrow":effect=L.F("피해 {0} × 왕복 · 동시 {1}개 · {2}", d, Mathf.Clamp(2+Mathf.Max(1,data.Progression.Rank("g_wall")),3,5), Shield(.18f));break;
                case "oath":effect=L.F("결계 안 받는 피해 -20% · {0}", Shield(s.power));break;
                case "taunt":effect=L.F("도발 {0:0.#}초 · 피해 {1} · 반경 {2:0.#}m", s.duration, d, n.radius);break;
                case "bash":effect=L.F("피해 {0} · 기절 {1:0.#}초", d, s.duration);break;
                case "counter":effect=L.F("받는 피해 -40% · 반격 {0} (피격 없으면 {1})", d, Mathf.RoundToInt(d*.7f));break;
                case "aegis":effect=L.F("피해 {0} · 속박 2.6초(보스 포함) · 도발 · 방어 붕괴 8초 · 아군 {1}", d, Shield(.25f));break;
                case "fire":effect=L.F("폭발 피해 {0} · 반경 {1:0.#}m · 화상 {2} × 3", d, n.radius, Mathf.RoundToInt(d*.07f));break;
                case "ice":effect=L.F("피해 {0} · 얼음창 3갈래 · 빙결 {1:0.#}초", d, s.duration);break;
                case "storm":effect=L.F("피해 {0} · 최대 {1}명 연쇄", d, s.hits);break;
                case "nebula":effect=L.F("피해 {0} ~ {1} · 반경 {2:0.#} ~ {3:0.#}m · 최대 2초 모으기", d, d*3, n.radius, n.radius*1.8f);break;
                case "sanctuary":effect=L.F("반경 {0:0.#}m 아군 {1:0.#}초 무적", n.radius, s.duration);break;
                case "orbit":effect=L.F("착탄 피해 {0} × {1}발", d, s.hits);break;
                case "blink":effect=L.F("잔상 폭발 {0} · {1}", d, Shield(.12f));break;
                case "rift":effect=L.F("피해 {0} × {1}회 + 붕괴 {2} · 끌어당김", d, s.hits, d*2);break;
                case "cataclysm":effect=L.F("혜성 {0} × 28 · 원소 자국 {1}/0.5초(방어 무시) · 거대 혜성 {2} × 3", Mathf.RoundToInt(d*.5f), Mathf.RoundToInt(d*.05f), Mathf.RoundToInt(d*1.5f));break;
                case "heal":effect=L.F("회복 {0} · 반경 {1:0.#}m", Heal(d), n.radius);break;
                case "bloom":effect=L.F("회복 {0} × {1}회", Heal(d), s.hits);break;
                case "cleanse":effect=L.F("정화 · 회복 {0} · 적 피해 {1}", Heal(d), Mathf.RoundToInt(d*.9f/1.4f));break;
                case "light":effect=L.F("관통 피해 {0} · 길이 {1:0.#}m", d, n.range);break;
                case "wings":effect=Shield(s.power);break;
                case "bless":effect=L.F("아군 공격 피해 +{0}% · {1:0.#}초", Mathf.RoundToInt(s.power*scale), s.duration);break;
                case "dawn":effect=L.F("적 피해 {0} · 회복 {1} · {2} · 성역 {3} × {4}회", Mathf.RoundToInt(d*2.5f/4.5f), Heal(d), Shield(.25f), Heal(d/6), s.hits);break;
                // [ROGUE] 어쌔신 / 닌자 (Docs/PLAN_CLASS_ROGUE.md 2.3, 2.4)
                case "phantom":effect=L.F("환영 {0}갈래 · 피해 {1} · 길이 {2:0.#}m · 밀어내기(보스 제외)", s.hits, d, n.range);break;
                case "veil":effect=L.F("연막탄 피해 {0} · 앞 {1:0.#}m · 반경 {2:0.#}m · 공격 불가 {3:0.#}초 · 뒤로 {4:0.#}m", d, CareerCombat.VeilThrowRange, n.radius, s.duration, n.range);break;
                case "venom":effect=L.F("직접 공격 피해의 {0}% 독(3초, 방어 무시) · {1:0.#}초", Mathf.RoundToInt(s.power*scale), s.duration);break;
                case "rend":effect=L.F("피해 {0} × {1}회 · 부채꼴 {2:0.#}m", d, s.hits, n.range);break;
                case "rupture":effect=L.F("피해 {0} + 표식당 {1} · 표식 5개 {2}", d, Mathf.RoundToInt(d*.2f), Mathf.RoundToInt(d*2f));break;
                case "assassinate":effect=L.F("피해 {0} · 등 뒤 {1} · 표식 5개 {2} · 둘 다 {3}", d, Mathf.RoundToInt(d*1.5f), Mathf.RoundToInt(d*1.8f), Mathf.RoundToInt(d*2.7f));break;
                case "shadowform":effect=L.F("변신 {0:0.#}초 · 피해 +{1}% · 공격 속도 +{2}% · 끝날 때 폭발 {3} · 반경 {4:0.#}m", s.duration, CareerCombat.FormDamagePct, CareerCombat.FormSpeedPct, d, n.radius);break;
                case "stealth":effect=L.F("은신 {0:0.#}초 · 이동 속도 +30% · 첫 공격 +{1}%(등 뒤)", s.duration, Mathf.RoundToInt(s.power*scale));break;
                case "kunaiport":effect=L.F("관통 피해 {0} · {1:0.#}m · 다시 누르면 이동({2:0.#}초 안)", d, n.range, s.duration);break;
                case "clone":effect=L.F("분신 2 · {0:0.#}초 · 기본 공격·공격 스킬을 본체 피해의 {1}%로 따라 씀", s.duration*scale, Mathf.RoundToInt(s.power));break;
                case "recall":effect=L.F("쿠나이마다 피해 {0} · {1:0.#}m 안 최대 12개 · 1개당 MP 1%", d, n.range);break;
                case "volley":effect=L.F("피해 {0} × {1}발 · 직선 {2:0.#}m", d, s.hits, n.range);break;
                case "fuma":effect=L.F("관통 피해 {0} · 같은 적 최대 {1}회 · {2:0.#}m 왕복", d, s.hits, n.range);break;
                case "bomb":effect=L.F("폭발 {0} × {1} · 반경 {2:0.#}m · 속박 1초(보스 0.3초)", d, s.hits, n.radius);break;
                case "starstorm":effect=L.F("표창 {0} × {1}개 · {2:0.#}초 · 관통 {3:0.#}m", d, s.hits, s.duration, n.range);break;
                default:effect=L.F("피해 {0}", d);break;
            }
            return effect+L.F("\n{0} {1} · 재사용 {2:0.##}초 · 시전 {3:0.##}초", (n.usesLife?"HP":"MP"), n.manaCost, (s.id==SkillCaster.RushId?SkillCaster.RushCooldown:n.cooldown), s.cast);
        }
        public static string Passive(CareerSkill s,int rank)
        {
            int r=Mathf.Max(1,rank)-1;
            switch(s.effect){case "swordflow":return L.F("기본 공격 3회 적중 후 다음 전직 스킬 피해 +{0}% · 검기 6초 유지", 20+5*r);case "opening":return L.F("HP 80% 이상 적에게 직접 피해 +{0}%", 15+5*r);case "rhythm":return L.F("중첩당 공격 속도 +{0}% (최대 5, 4초)", 3+r);case "critical":return L.F("치명 확률 {0}%, 치명 피해 150%", 8+4*r);case "steel":return L.F("받는 피해 추가 -{0}%", 6+2*r);case "retaliate":return L.F("피격 후 다음 직접 공격 +{0}% (재발동 2초)", 20+5*r);case "elements":return L.F("5초 이내 다른 원소 직접 공격 +{0}%", 12+4*r);case "flow":return L.F("전직 주문 MP -{0}%", 10+3*r);case "mercy":return L.F("회복량 +{0}%", 10+5*r);case "grace":return L.F("보호막량 +{0}%", 12+4*r);case "vital":return L.F("표식당 받는 피해 +{0}% (최대 5, 4초)", 4+r);case "ambush":return L.F("등 뒤 직접 공격 피해 +{0}%", 15+5*r);case "throw":return L.F("쿠나이 투척 피해 +{0}% · 사거리 +1m", 10+5*r);case "far":return L.F("6m 이상 떨어진 적에게 직접 피해 +{0}%", 12+4*r);default:return s.description;}
        }
    }
}
