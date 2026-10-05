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
            string effect;
            switch(s.effect)
            {
                case "guard":effect=$"받는 피해 -{Mathf.RoundToInt(30*scale)}%";break;
                case "focus":effect=$"치명 확률 +{Mathf.RoundToInt(25*scale)}%";break;
                case "bless":effect=$"아군 공격 피해 +{Mathf.RoundToInt(s.power*scale)}%";break;
                case "shield":case "ward":case "citadel":case "wings":case "blink":effect=$"대상 최대 HP의 {s.power*scale*shielding*100:0.#}% 보호막";break;
                case "heal":case "hot":case "cleanse":effect=$"최대 회복 {Mathf.RoundToInt(n.damage*healing)}{(s.hits>1?$" × {s.hits}회":"")}";break;
                case "dawn":effect=$"즉시 회복 {Mathf.RoundToInt(n.damage*healing)} · 매초 {Mathf.RoundToInt((n.damage/6)*healing)} × {s.hits}회 · HP {25*shielding:0.#}% 보호막 · 정화";break;
                case "five":case "execute":case "light":effect=$"관통 피해 {n.damage} · 길이 {n.range:0.#}m / 폭 {n.radius*2:0.#}m";break;
                case "storm":effect=$"피해 {n.damage} · 최대 {s.hits}대상 · 연결 거리 {n.radius:0.#}m";break;
                default:effect=$"피해 {n.damage}{(s.hits>1?$" × {s.hits}회":"")}";break;
            }
            return effect+$"\n{(n.usesLife?"HP":"MP")} {n.manaCost} · 재사용 {n.cooldown:0.##}초 · 시전 {s.cast:0.##}초";
        }
        public static string Passive(CareerSkill s,int rank)
        {
            int r=Mathf.Max(1,rank)-1;
            switch(s.effect){case "rhythm":return $"중첩당 공격 속도 +{3+r}% (최대 5, 4초)";case "critical":return $"치명 확률 {8+4*r}%, 치명 피해 150%";case "steel":return $"받는 피해 추가 -{6+2*r}%";case "retaliate":return $"피격 후 다음 직접 공격 +{20+5*r}% (재발동 2초)";case "elements":return $"5초 이내 다른 원소 직접 공격 +{12+4*r}%";case "flow":return $"전직 주문 MP -{10+3*r}%";case "mercy":return $"회복량 +{10+5*r}%";case "grace":return $"보호막량 +{12+4*r}%";default:return s.description;}
        }
    }
}
