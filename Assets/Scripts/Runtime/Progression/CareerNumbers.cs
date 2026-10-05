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
            string Shield(float fraction)=>$"HP {fraction*scale*shielding*100:0.#}% 보호막";
            string effect;
            switch(s.effect)
            {
                case "cross":effect=$"피해 {d} × 2회 · 부채꼴 {n.range:0.#}m";break;
                case "rush":effect=$"경로 피해 {d} · 돌진 {n.range:0.#}m";break;
                case "flurry":effect=$"피해 {d} × 4회 + 마무리 {Mathf.RoundToInt(d*1.6f/.7f)}";break;
                case "break":effect=$"관통 피해 {d} · {n.range:0.#}m · 맞은 적 {s.duration:0.#}초간 받는 피해 +15%";break;
                case "iaido":effect=$"직선 피해 {d} · 길이 {n.range:0.#}m";break;
                case "execute":effect=$"피해 {d} · HP 35% 미만 {Mathf.RoundToInt(d*1.6f)} · 반경 {n.radius:0.#}m";break;
                case "swordrain":effect=$"낙검 {d} × 6 · 거대한 검 {Mathf.RoundToInt(d*3.75f)} · 반경 {n.radius:0.#}m";break;
                case "guard":effect=$"받는 피해 -{Mathf.RoundToInt(30*scale)}% · {s.duration:0.#}초 · 밀쳐내기 피해 {d}";break;
                case "shieldthrow":effect=$"피해 {d} × 왕복 · {Shield(.18f)}";break;
                case "oath":effect=$"결계 안 받는 피해 -20% · {Shield(s.power)}";break;
                case "taunt":effect=$"도발 {s.duration:0.#}초 · 피해 {d} · 반경 {n.radius:0.#}m";break;
                case "bash":effect=$"피해 {d} · 기절 {s.duration:0.#}초";break;
                case "counter":effect=$"받는 피해 -40% · 반격 {d} (피격 없으면 {Mathf.RoundToInt(d*.7f)})";break;
                case "aegis":effect=$"피해 {d} · 기절 1.5초 · 도발 · 아군 {Shield(.25f)}";break;
                case "fire":effect=$"폭발 피해 {d} · 반경 {n.radius:0.#}m · 화상 {Mathf.RoundToInt(d*.07f)} × 3";break;
                case "ice":effect=$"피해 {d} · 얼음창 3갈래 · 빙결 {s.duration:0.#}초";break;
                case "storm":effect=$"피해 {d} · 최대 {s.hits}명 연쇄";break;
                case "orbit":effect=$"착탄 피해 {d} × {s.hits}발";break;
                case "blink":effect=$"잔상 폭발 {d} · {Shield(.12f)}";break;
                case "rift":effect=$"피해 {d} × {s.hits}회 + 붕괴 {d*2} · 끌어당김";break;
                case "cataclysm":effect=$"원소 운석 {d} × 3 · 붕괴 {Mathf.RoundToInt(d*1.5f)} · 반경 {n.radius:0.#}m";break;
                case "heal":effect=$"회복 {Heal(d)} · 반경 {n.radius:0.#}m";break;
                case "bloom":effect=$"회복 {Heal(d)} × {s.hits}회";break;
                case "cleanse":effect=$"정화 · 회복 {Heal(d)} · 적 피해 {Mathf.RoundToInt(d*.9f/1.4f)}";break;
                case "light":effect=$"관통 피해 {d} · 길이 {n.range:0.#}m";break;
                case "wings":effect=Shield(s.power);break;
                case "bless":effect=$"아군 공격 피해 +{Mathf.RoundToInt(s.power*scale)}% · {s.duration:0.#}초";break;
                case "dawn":effect=$"적 피해 {Mathf.RoundToInt(d*2.5f/4.5f)} · 회복 {Heal(d)} · {Shield(.25f)} · 성역 {Heal(d/6)} × {s.hits}회";break;
                default:effect=$"피해 {d}";break;
            }
            return effect+$"\n{(n.usesLife?"HP":"MP")} {n.manaCost} · 재사용 {n.cooldown:0.##}초 · 시전 {s.cast:0.##}초";
        }
        public static string Passive(CareerSkill s,int rank)
        {
            int r=Mathf.Max(1,rank)-1;
            switch(s.effect){case "swordflow":return $"기본 공격 3회 적중 후 다음 전직 스킬 피해 +{20+5*r}% · 검기 6초 유지";case "opening":return $"HP 80% 이상 적에게 직접 피해 +{15+5*r}%";case "rhythm":return $"중첩당 공격 속도 +{3+r}% (최대 5, 4초)";case "critical":return $"치명 확률 {8+4*r}%, 치명 피해 150%";case "steel":return $"받는 피해 추가 -{6+2*r}%";case "retaliate":return $"피격 후 다음 직접 공격 +{20+5*r}% (재발동 2초)";case "elements":return $"5초 이내 다른 원소 직접 공격 +{12+4*r}%";case "flow":return $"전직 주문 MP -{10+3*r}%";case "mercy":return $"회복량 +{10+5*r}%";case "grace":return $"보호막량 +{12+4*r}%";default:return s.description;}
        }
    }
}
