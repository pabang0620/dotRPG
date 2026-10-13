using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    // =============================== Passive tree ===============================

    public enum PassiveStat
    {
        IncDamage,     // % increased damage (all attacks and skills)
        FlatHp,
        IncHp,         // % increased maximum HP
        FlatMp,
        ManaRegen,     // % increased mana regeneration
        Block,         // + % block chance
        Speed,         // % movement speed
        AttackSpeed,   // % faster attacks and skills (shorter cooldowns)
        Aoe,           // % increased area of effect
        ManaCost,      // % reduced mana cost
        LifeOnKill,
        ManaOnKill,
        // Only for the node's own skill slot (PassiveNode.skillSlot).
        SkillDamage,   // % increased damage of that skill
        SkillArea,     // % increased area of that skill
        SkillCooldown, // % reduced cooldown of that skill
        SkillRepeat,   // extra casts of that skill
    }

    public enum PassiveKind { Start, Small, Notable, Mastery, Keystone }

    public enum Keystone { None, Unwavering, GlassCannon, BloodMagic, Awakening }

    /// <summary>One node of the passive tree (allocate nodes connected to what you own).</summary>
    public sealed class PassiveNode
    {
        public string id, name;
        public PassiveKind kind;
        public Vector2 pos;               // UI position in pixels, tree centre = (0,0), y up
        /// <summary>0-3 = the arm of skill slot 1-4, 4 = keystones and the start.</summary>
        public int cluster;
        /// <summary>Skill slot the Skill* stats of this node apply to (-1 = none, 4 = awakening skill).</summary>
        public int skillSlot = -1;
        public Keystone keystone;
        public readonly List<(PassiveStat stat, int value)> stats = new List<(PassiveStat, int)>();
        public readonly List<string> links = new List<string>();
        public string keystoneText;

        public string StatText()
        {
            var lines = new List<string>();
            foreach (var (stat, value) in stats) lines.Add(PassiveTree.Describe(stat, value, skillSlot));
            if (!string.IsNullOrEmpty(keystoneText)) lines.Add(keystoneText);
            return string.Join("\n", lines);
        }
    }

    /// <summary>
    /// The passive tree: one start in the middle and four arms. Left = skill 1, right = skill 2,
    /// down = skill 3, up = skill 4. Each arm forks into three big nodes (area, damage, cooldown of
    /// that skill) with small stat nodes in between, and ends in a mastery (+1 cast). Keystones sit
    /// in the corners and link neighbouring arms, so a build can cross over.
    /// </summary>
    public static class PassiveTree
    {
        public const string Start = "S";

        static readonly Dictionary<string, PassiveNode> nodes = new Dictionary<string, PassiveNode>();
        public static IEnumerable<PassiveNode> All => nodes.Values;
        public static PassiveNode Get(string id) => id != null && nodes.TryGetValue(id, out var n) ? n : null;

        static PassiveNode N(string id, string name, PassiveKind kind, float x, float y, int cluster, params (PassiveStat, int)[] stats)
        {
            var n = new PassiveNode { id = id, name = name, kind = kind, pos = new Vector2(x, y), cluster = cluster };
            n.stats.AddRange(stats);
            nodes[id] = n;
            return n;
        }

        static void Link(string a, string b)
        {
            nodes[a].links.Add(b);
            nodes[b].links.Add(a);
        }

        /// <summary>
        /// One arm. <paramref name="along"/> points away from the centre, <paramref name="across"/> is the
        /// side the area branch goes to. Horizontal arms are long; vertical ones are squeezed to fit.
        /// </summary>
        static void Arm(string p, int slot, Vector2 along, Vector2 across, bool vertical,
            (string name, (PassiveStat, int) stat)[] smalls)
        {
            const PassiveKind S = PassiveKind.Small, No = PassiveKind.Notable;
            Vector2 P(float a, float c) => along * a + across * c;
            // Positions: (distance along the arm, distance across it).
            var trunk = vertical ? new[] { P(60, 0) } : new[] { P(80, 0), P(160, 0) };
            Vector2 fork = trunk[trunk.Length - 1];
            Vector2 areaSmall = vertical ? P(100, 80) : P(235, 60), area = vertical ? P(150, 150) : P(305, 110);
            Vector2 dmgSmall = vertical ? P(120, 0) : P(250, 0), dmg = vertical ? P(180, 0) : P(340, 0);
            Vector2 cdSmall = vertical ? P(100, -80) : P(235, -60), cd = vertical ? P(150, -150) : P(305, -110);
            Vector2 bridgeA = vertical ? P(215, 100) : P(395, 90), bridgeC = vertical ? P(215, -100) : P(395, -90);
            Vector2 mastery = vertical ? P(238, 0) : P(450, 0);

            int k = 0;
            PassiveNode Small(string id, Vector2 pos)
            {
                var (name, stat) = smalls[k++];
                return N(id, name, S, pos.x, pos.y, slot, stat);
            }
            string prev = Start;
            for (int i = 0; i < trunk.Length; i++)
            {
                Small(p + "t" + i, trunk[i]);
                Link(prev, p + "t" + i);
                prev = p + "t" + i;
            }
            string f = prev;
            Small(p + "a1", areaSmall); Small(p + "d1", dmgSmall); Small(p + "c1", cdSmall);
            Small(p + "x1", bridgeA); Small(p + "x2", bridgeC);
            N(p + "A", L.T("범위 확장"), No, area.x, area.y, slot, (PassiveStat.SkillArea, 25)).skillSlot = slot;
            N(p + "D", L.T("위력 강화"), No, dmg.x, dmg.y, slot, (PassiveStat.SkillDamage, 30)).skillSlot = slot;
            N(p + "C", L.T("재사용 단축"), No, cd.x, cd.y, slot, (PassiveStat.SkillCooldown, 20)).skillSlot = slot;
            N(p + "M", L.T("스킬 숙련"), PassiveKind.Mastery, mastery.x, mastery.y, slot, (PassiveStat.SkillRepeat, 1), (PassiveStat.SkillDamage, 10)).skillSlot = slot;
            Link(f, p + "a1"); Link(p + "a1", p + "A");
            Link(f, p + "d1"); Link(p + "d1", p + "D");
            Link(f, p + "c1"); Link(p + "c1", p + "C");
            Link(p + "A", p + "x1"); Link(p + "x1", p + "M");
            Link(p + "C", p + "x2"); Link(p + "x2", p + "M");
            Link(p + "D", p + "M");
        }

        static PassiveTree()
        {
            N(Start, L.T("시작점"), PassiveKind.Start, 0, 0, 4);
            // Left: skill 1. Strength flavoured small nodes.
            Arm("L", 0, Vector2.left, Vector2.up, false, new[]
            {
                (L.T("힘"), (PassiveStat.FlatHp, 10)), (L.T("무기 숙련"), (PassiveStat.IncDamage, 5)),
                (L.T("넓은 시야"), (PassiveStat.Aoe, 5)), (L.T("전투 감각"), (PassiveStat.IncDamage, 6)), (L.T("날렵함"), (PassiveStat.AttackSpeed, 5)),
                (L.T("체력 단련"), (PassiveStat.FlatHp, 15)), (L.T("민첩"), (PassiveStat.Speed, 5)),
            });
            // Right: skill 2. Intelligence flavoured.
            Arm("R", 1, Vector2.right, Vector2.up, false, new[]
            {
                (L.T("지능"), (PassiveStat.FlatMp, 10)), (L.T("주문 숙련"), (PassiveStat.IncDamage, 5)),
                (L.T("넓은 파장"), (PassiveStat.Aoe, 5)), (L.T("마나 순환"), (PassiveStat.ManaRegen, 15)), (L.T("절약"), (PassiveStat.ManaCost, 5)),
                (L.T("명상"), (PassiveStat.ManaRegen, 20)), (L.T("지능"), (PassiveStat.FlatMp, 15)),
            });
            // Down: skill 3. Hunter flavoured.
            Arm("D", 2, Vector2.down, Vector2.left, true, new[]
            {
                (L.T("민첩"), (PassiveStat.Speed, 5)),
                (L.T("사냥의 기쁨"), (PassiveStat.LifeOnKill, 5)), (L.T("전투 감각"), (PassiveStat.IncDamage, 6)), (L.T("날렵함"), (PassiveStat.AttackSpeed, 5)),
                (L.T("체력 단련"), (PassiveStat.FlatHp, 15)), (L.T("마나 흡수"), (PassiveStat.ManaOnKill, 4)),
            });
            // Up: skill 4. Guardian flavoured.
            Arm("U", 3, Vector2.up, Vector2.left, true, new[]
            {
                (L.T("방패술"), (PassiveStat.Block, 3)),
                (L.T("균형"), (PassiveStat.FlatMp, 10)), (L.T("무기 숙련"), (PassiveStat.IncDamage, 6)), (L.T("마나 순환"), (PassiveStat.ManaRegen, 15)),
                (L.T("방패술"), (PassiveStat.Block, 4)), (L.T("민첩"), (PassiveStat.Speed, 5)),
            });

            // Keystones in the corners, each joining two arms.
            const PassiveKind K = PassiveKind.Keystone;
            var k1 = N("K1", L.T("불굴"), K, -290, 205, 4);
            k1.keystone = Keystone.Unwavering; k1.keystoneText = L.T("막기 확률 +15%\n이동 속도 10% 감소");
            var k2 = N("K2", L.T("유리 대포"), K, 290, 205, 4);
            k2.keystone = Keystone.GlassCannon; k2.keystoneText = L.T("피해 40% 증가\n최대 HP 25% 감소");
            var k3 = N("K3", L.T("피의 마법"), K, -290, -205, 4);
            k3.keystone = Keystone.BloodMagic; k3.keystoneText = L.T("스킬이 MP 대신 HP를 소모한다\n최대 MP 없음\n최대 HP 30% 증가");
            var k4 = N("K4", L.T("각성"), K, 290, -205, 4, (PassiveStat.SkillDamage, 40), (PassiveStat.SkillCooldown, 30));
            k4.keystone = Keystone.Awakening; k4.skillSlot = SkillGems.UltimateSlot;
            Link("K1", "LA"); Link("K1", "UA");
            Link("K2", "RA"); Link("K2", "UC");
            Link("K3", "LC"); Link("K3", "DA");
            Link("K4", "RC"); Link("K4", "DC");
        }

        /// <summary>"1번 스킬(회전 베기)" for the current class.</summary>
        public static string SlotLabel(int slot)
        {
            var cls = Game.Player != null ? Game.Player.Class : Game.Session != null ? Game.Session.PlayerClass : CharacterClass.Warrior;
            var gem = SkillGems.ForSlot(cls, slot);
            string name = gem != null ? gem.name : "?";
            return slot == SkillGems.UltimateSlot ? L.F("각성 기술({0})", name) : L.F("{0}번 스킬({1})", slot + 1, name);
        }

        public static string Describe(PassiveStat stat, int v, int slot = -1)
        {
            switch (stat)
            {
                case PassiveStat.IncDamage: return L.F("피해 {0}% 증가", v);
                case PassiveStat.FlatHp: return L.F("최대 HP +{0}", v);
                case PassiveStat.IncHp: return L.F("최대 HP {0}% 증가", v);
                case PassiveStat.FlatMp: return L.F("최대 MP +{0}", v);
                case PassiveStat.ManaRegen: return L.F("MP 재생 {0}% 증가", v);
                case PassiveStat.Block: return L.F("막기 확률 +{0}%", v);
                case PassiveStat.Speed: return L.F("이동 속도 {0}% 증가", v);
                case PassiveStat.AttackSpeed: return L.F("공격·스킬 속도 {0}% 증가", v);
                case PassiveStat.Aoe: return L.F("스킬 범위 {0}% 증가", v);
                case PassiveStat.ManaCost: return L.F("MP 소모 {0}% 감소", v);
                case PassiveStat.LifeOnKill: return L.F("처치 시 HP +{0}", v);
                case PassiveStat.ManaOnKill: return L.F("처치 시 MP +{0}", v);
                // [SKILL v2] slot 4 is the defence skill (철벽 / 마나 보호막): see CharacterStatsCalc.Skill.
                case PassiveStat.SkillDamage when slot == 3: return L.F("{0} 받는 피해 감소 +{1}%", SlotLabel(slot), Mathf.RoundToInt(v / 4f));
                case PassiveStat.SkillArea when slot == 3: return L.F("{0} 지속시간 {1}% 증가", SlotLabel(slot), v);
                case PassiveStat.SkillRepeat when slot == 3: return L.F("{0} (방어 스킬은 추가 발동 없음)", SlotLabel(slot));
                case PassiveStat.SkillDamage: return L.F("{0} 피해 {1}% 증가", SlotLabel(slot), v);
                case PassiveStat.SkillArea: return L.F("{0} 범위 {1}% 증가", SlotLabel(slot), v);
                case PassiveStat.SkillCooldown: return L.F("{0} 재사용 대기시간 {1}% 감소", SlotLabel(slot), v);
                default: return L.F("{0} 추가 발동 +{1}회", SlotLabel(slot), v);
            }
        }
    }
}
