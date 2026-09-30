using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>What a mercenary does in a fight (drives <see cref="CompanionBrain"/>).</summary>
    public enum MercRole
    {
        /// <summary>Warrior that stands in front, shouts war cry and holds aggro.</summary>
        Tank,
        /// <summary>Warrior that deals damage up close.</summary>
        MeleeDps,
        /// <summary>Mage that freezes monsters (frost nova, ice orb).</summary>
        Control,
        /// <summary>Mage that deals lightning damage from range.</summary>
        CasterDps,
    }

    /// <summary>One hireable AI companion.</summary>
    public sealed class MercenaryDef
    {
        public string id, name, roleName, description;
        public CharacterClass cls;
        public MercRole role;
        public CharacterLook look;
        /// <summary>Colour of the party frame stripe and the name.</summary>
        public Color32 color;
        /// <summary>Base max HP multiplier on top of a player's base.</summary>
        public float hpScale = 1f;
        /// <summary>Threat per point of damage dealt.</summary>
        public float threat = 1f;
        /// <summary>Skill slots in the order the AI prefers them.</summary>
        public int[] skillPriority;
        /// <summary>Support gems per skill slot (two each; locked ones are skipped until their level).</summary>
        public string[][] supports;

        public bool IsMage => cls == CharacterClass.Mage;
    }

    /// <summary>
    /// The four mercenaries (D1/D2): level always equals the local player's, gear is fixed per level band,
    /// and their damage is <see cref="DamageScale"/> of a same-level player so the player stays the main
    /// damage dealer. Skills open with the same slot levels as a player's.
    /// </summary>
    public static class MercenaryDatabase
    {
        // ---------- Tuning ----------
        /// <summary>Companion damage as a fraction of a same-level player's (plan: 60–70%).</summary>
        public const float DamageScale = 0.65f;
        /// <summary>Tank threat multiplier (other roles use 1).</summary>
        public const float TankThreat = 2.5f;

        /// <summary>Gear per level band: from this level on, (warrior weapon, mage weapon, top, bottom, necklace, ring).</summary>
        static readonly (int fromLevel, string sword, string staff, string top, string bottom, string neck, string ring)[] GearBands =
        {
            (1, "eq_sword_wood", "eq_staff_oak", "eq_top_cloth", null, null, null),
            (6, "eq_sword_iron", "eq_staff_crystal", "eq_top_cloth", "eq_bot_cloth", null, "eq_ring_copper"),
            (12, "eq_sword_bone", "eq_staff_moon", "eq_top_leather", "eq_bot_cloth", "eq_neck_leaf", "eq_ring_copper"),
            (20, "eq_sword_dragon", "eq_staff_star", "eq_top_leather", "eq_bot_leather", "eq_neck_bone", "eq_ring_copper"),
        };

        static Color32 C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

        static readonly List<MercenaryDef> Defs = new List<MercenaryDef>
        {
            new MercenaryDef
            {
                id = "merc_bron", name = "브론", roleName = "전사 · 탱커", cls = CharacterClass.Warrior, role = MercRole.Tank,
                description = "앞에 서서 전쟁 함성으로 적을 끌어모으는 방패잡이.\n체력이 높고 몬스터의 공격을 대신 받아 준다.",
                look = new CharacterLook("merc_bron", HairStyle.Short, C(222, 160, 110), C(70, 44, 30), C(120, 128, 140), C(66, 60, 72), HatKind.Bandana, C(170, 40, 40)),
                color = C(230, 110, 60), hpScale = 1.5f, threat = TankThreat,
                skillPriority = new[] { 3, 0, 1 },
                supports = new[] { new[] { "sup_aoe", "sup_leech" }, new[] { "sup_dmg", "sup_aoe" }, null, new[] { "sup_aoe", "sup_eff" }, null },
            },
            new MercenaryDef
            {
                id = "merc_kai", name = "카이", roleName = "전사 · 딜러", cls = CharacterClass.Warrior, role = MercRole.MeleeDps,
                description = "쌍검처럼 빠르게 베는 검사. 검기와 회전 베기로\n몰려든 적을 쓸어 낸다.",
                look = new CharacterLook("merc_kai", HairStyle.Spiky, C(250, 205, 160), C(40, 40, 52), C(40, 150, 110), C(40, 52, 70)),
                color = C(255, 196, 70), hpScale = 1.1f, threat = 1f,
                skillPriority = new[] { 4, 1, 2, 0 },
                supports = new[] { new[] { "sup_dmg", "sup_multi" }, new[] { "sup_dmg", "sup_aoe" }, new[] { "sup_dmg", "sup_multi" }, null, new[] { "sup_dmg", "sup_aoe" } },
            },
            new MercenaryDef
            {
                id = "merc_elin", name = "엘린", roleName = "마법사 · 빙결 제어", cls = CharacterClass.Mage, role = MercRole.Control,
                description = "서리 폭발과 빙뢰구로 몬스터를 얼려 묶어 두는 마법사.\n적이 다가오면 얼리고 물러난다.",
                look = new CharacterLook("merc_elin", HairStyle.Bun, C(250, 205, 160), C(150, 210, 240), C(70, 140, 210), C(40, 70, 130), HatKind.Wizard, C(210, 235, 250)) { robe = true },
                color = C(110, 200, 255), hpScale = 0.95f, threat = 1f,
                skillPriority = new[] { 1, 2, 0 },
                supports = new[] { new[] { "sup_chain", "sup_eff" }, new[] { "sup_aoe", "sup_eff" }, new[] { "sup_aoe", "sup_chain" }, null, null },
            },
            new MercenaryDef
            {
                id = "merc_sera", name = "세라", roleName = "마법사 · 번개 딜러", cls = CharacterClass.Mage, role = MercRole.CasterDps,
                description = "번개 사슬과 낙뢰를 퍼붓는 공격 마법사.\n멀리서 여러 적을 한꺼번에 공격한다.",
                look = new CharacterLook("merc_sera", HairStyle.Long, C(222, 160, 110), C(240, 200, 80), C(150, 50, 90), C(80, 30, 60), HatKind.Wizard, C(90, 30, 70)) { robe = true },
                color = C(255, 120, 200), hpScale = 0.9f, threat = 1f,
                skillPriority = new[] { 4, 3, 0, 2 },
                supports = new[] { new[] { "sup_chain", "sup_dmg" }, null, new[] { "sup_dmg", "sup_chain" }, new[] { "sup_chain", "sup_dmg" }, new[] { "sup_dmg", "sup_aoe" } },
            },
        };

        public static IReadOnlyList<MercenaryDef> All => Defs;

        public static MercenaryDef Get(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var d in Defs) if (d.id == id) return d;
            return null;
        }

        /// <summary>A fresh companion's data at <paramref name="level"/> (gear, skills and supports applied).</summary>
        public static CharacterData CreateData(MercenaryDef def, int level)
        {
            int baseHp = Game.Config != null ? Game.Config.playerStats.maxHealth : 20;
            var data = CharacterData.CreateCompanion(def.id, def.name, def.cls, Mathf.RoundToInt(baseHp * def.hpScale));
            data.DamageScale = DamageScale;
            data.ThreatMultiplier = def.threat;
            data.Look = def.look;
            ApplyLevel(data, def, level);
            return data;
        }

        /// <summary>Weapon, top, bottom, necklace and ring of a level band.</summary>
        public static string[] GearFor(MercenaryDef def, int level)
        {
            var band = GearBands[0];
            foreach (var b in GearBands) if (level >= b.fromLevel) band = b;
            string top = band.top;
            // The tank wears the iron breastplate from the third band on.
            if (def.role == MercRole.Tank && level >= GearBands[2].fromLevel) top = "eq_top_iron";
            return new[] { def.IsMage ? band.staff : band.sword, top, band.bottom, band.neck, band.ring };
        }

        /// <summary>Syncs a companion to the local player's level: level, supports and the gear of that band.</summary>
        public static void ApplyLevel(CharacterData data, MercenaryDef def, int level)
        {
            level = Mathf.Clamp(level, 1, Progression.MaxLevel);
            var prog = data.Progression;
            if (prog.Level != level || prog.Allocated.Count == 0)
            {
                // Progression.Restore sets the level and keeps only supports that are unlocked at it.
                var save = new SaveData { level = level, xp = 0, passives = new List<string>(), gemSlots = new List<string>() };
                for (int s = 0; s < SkillGems.Slots; s++)
                {
                    var sup = def.supports != null && s < def.supports.Length ? def.supports[s] : null;
                    save.gemSlots.Add("");
                    for (int k = 0; k < SkillGems.SupportsPerSlot; k++) save.gemSlots.Add(sup != null && k < sup.Length ? sup[k] ?? "" : "");
                }
                prog.Restore(save, def.cls);
            }
            var gear = GearFor(def, level);
            var eq = data.Equipment;
            SetIfDifferent(eq, EquipSlot.Weapon, gear[0]);
            SetIfDifferent(eq, EquipSlot.Top, gear[1]);
            SetIfDifferent(eq, EquipSlot.Bottom, gear[2]);
            SetIfDifferent(eq, EquipSlot.Necklace, gear[3]);
            SetIfDifferent(eq, EquipSlot.Ring1, gear[4]);
        }

        static void SetIfDifferent(Equipment eq, EquipSlot slot, string key)
        {
            if (eq[slot] != key) eq.Set(slot, key);
        }
    }
}
