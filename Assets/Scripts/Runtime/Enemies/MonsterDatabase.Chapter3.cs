using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [CH3] Monsters of 메말라 가는 협곡 (Docs/ch3/WORLD_AND_STORY.md 7). Health and damage are the underground role values
    /// (hollow_*) without the underground +40% / +20%, so a surface field at the same level plays like the surface roles.
    /// Their own art comes from ART_LIST (StreamingAssets/RegionalMonsters/&lt;id&gt;); until it exists the void kinds borrow
    /// the underground production sheets and the Grey Hand are drawn as armoured people (Chapter3MonsterArt).
    /// </summary>
    public static partial class MonsterDatabase
    {
        public const string VoidHound = "void_hound", VoidSentry = "void_sentry", VoidCaller = "void_caller", VoidBurster = "void_burster";
        public const string GreyRanger = "grey_ranger", GreyKnight = "grey_knight", GreyPriest = "grey_priest", GreyCaptain = "grey_captain";
        public const string XibakBoss = "boss_xibak", KamatzBoss = "boss_kamatz";

        static CharacterLook GreyLook(string id, Color32 cloth, Color32 plate, ArmorStyle armor) => new CharacterLook
        {
            id = id, body = BodyKind.Human, hairStyle = HairStyle.Short, hat = HatKind.None,
            skin = C(214, 168, 128), hair = C(70, 66, 70), shirt = cloth, pants = PixelCanvas.Shade(cloth, 0.8f),
            armor = armor, armorColor = plate,
        };

        static void AddChapter3Monsters()
        {
            void Ch3(MonsterDef d) { d.chapter = 3; Add(d); }
            Ch3(new MonsterDef
            {
                id = VoidHound, name = L.T("공허 들개"), look = Look(VoidHound, C(58, 44, 84)), kind = MonsterKind.Charger,
                hp = 45, damage = 13, xp = 28, chaseSpeed = 2.6f, attackRange = 1.0f, skillInterval = 3.2f, projectileSpeed = 11f, armor = .2f,
            });
            Ch3(new MonsterDef
            {
                id = VoidSentry, name = L.T("공허 파수석"), look = Look(VoidSentry, C(46, 40, 72)), kind = MonsterKind.ShieldGuard,
                hp = 60, damage = 12, xp = 32, chaseSpeed = 1.5f, attackRange = 1.1f, windup = 0.55f, recover = 0.9f, armor = .35f,
            });
            Ch3(new MonsterDef
            {
                id = VoidCaller, name = L.T("공허의 부름꾼"), look = Look(VoidCaller, C(84, 46, 112)), kind = MonsterKind.Archer,
                hp = 29, damage = 12, xp = 27, chaseSpeed = 2.2f, keepDistance = 5f,
                skillInterval = 2.4f, projectileSpeed = 10f, detect = 7.5f, loseInterest = 11f,
            });
            // Rushes in, glows for 1.2 s and bursts for three times its hit (Chapter3Monsters.BursterFuse); killed first, it never bursts.
            Ch3(new MonsterDef
            {
                id = VoidBurster, name = L.T("공허 폭발체"), look = Look(VoidBurster, C(120, 52, 150)), kind = MonsterKind.Charger,
                hp = 34, damage = 13, xp = 26, chaseSpeed = 2.8f, attackRange = 1.0f, skillInterval = 99f, armor = .1f,
            });
            Ch3(new MonsterDef
            {
                id = GreyRanger, name = L.T("잿빛 손 정찰병"), look = GreyLook(GreyRanger, C(92, 90, 96), C(120, 118, 126), ArmorStyle.Leather), kind = MonsterKind.Archer,
                hp = 31, damage = 12, xp = 28, chaseSpeed = 2.3f, keepDistance = 5.5f,
                skillInterval = 2.2f, projectileSpeed = 12f, detect = 8f, loseInterest = 11f,
            });
            Ch3(new MonsterDef
            {
                id = GreyKnight, name = L.T("잿빛 손 기사"), look = GreyLook(GreyKnight, C(84, 82, 90), C(150, 150, 158), ArmorStyle.Plate), kind = MonsterKind.Charger,
                hp = 52, damage = 14, xp = 32, chaseSpeed = 2.0f, attackRange = 1.0f, skillInterval = 3.5f, projectileSpeed = 11f, armor = .3f,
            });
            // Every 4 s a shield of 15% max HP on its allies within 4 m (Chapter3Monsters.PriestWard): kill it first.
            Ch3(new MonsterDef
            {
                id = GreyPriest, name = L.T("잿빛 손 사제"), look = GreyLook(GreyPriest, C(70, 68, 76), C(196, 190, 170), ArmorStyle.None), kind = MonsterKind.Archer,
                hp = 30, damage = 10, xp = 30, chaseSpeed = 2.0f, keepDistance = 6f,
                skillInterval = 2.8f, projectileSpeed = 9f, detect = 8f, loseInterest = 11f,
            });
            // Elite: every 60 field kills in 검은 수호석 잔해 / 공허의 틈 (EnemySpawner.Captain.cs); one sure equipment drop.
            Ch3(new MonsterDef
            {
                id = GreyCaptain, name = L.T("잿빛 손 정찰대장"), look = GreyLook(GreyCaptain, C(60, 58, 66), C(176, 170, 150), ArmorStyle.Plate), kind = MonsterKind.Charger,
                hp = 52 * 8, damage = 17, xp = 32 * 8, chaseSpeed = 2.1f, attackRange = 1.1f, skillInterval = 3.0f, projectileSpeed = 11f,
                armor = .3f, superArmor = true, size = 1.15f, artScale = 1.2f, sureGear = true, goldMin = 120, goldMax = 200,
            });

            // [CH3] Raid bosses (Docs/ch3/RAIDS.md). Their own generated frames: Art/char_boss_xibak_*, Art/char_boss_kamatz_*.
            // 지바크: 그라흐 x1.19 HP / x1.14 damage; from phase 2 the healing void rifts (BossBrain.VoidRift.cs).
            var xibak = Boss(XibakBoss, L.T("공허 침식체 지바크"), C(84, 46, 112), hp: 7600, dmg: 58, lines: 70, speed: 2.2f,
                new BossPattern(PatternKind.Slash3, 4f, 0.9f, 3, maxRange: 3.6f, weight: 1.5f),
                new BossPattern(PatternKind.Fields, 7f, 1.1f, 7, minPhase: 2),
                new BossPattern(PatternKind.Donut, 10f, 1.4f, minPhase: 2),
                new BossPattern(PatternKind.Summon, 14f, 1f, 2),
                new BossPattern(PatternKind.Repel, 16f, 0.8f, minPhase: 2),
                new BossPattern(PatternKind.Annihilate, 22f, 2.2f, minPhase: 2),
                new BossPattern(PatternKind.MarkOfFate, 40f, minPhase: 2, weight: .8f));
            xibak.artScale = 1.5f;
            xibak.raid = true;
            xibak.size = 2.6f;
            xibak.groggyMax = 760f;
            xibak.phases = new[] { 0.7f, 0.4f, 0.15f };
            xibak.maxMinions = 4;
            xibak.minionId = VoidHound; // 소환: void hounds (chapter 3 raid bosses summon their own kind, BossBrain.Summon)
            Ch3(xibak);

            // 카마츠: a heavily armoured person (Guard and arrows instead of summons); from phase 2 잿빛 낙인 (BossBrain.GreyBrand.cs).
            var kamatz = Boss(KamatzBoss, L.T("잿빛 손 지휘관 카마츠"), C(90, 88, 96), hp: 8800, dmg: 64, lines: 80, speed: 2.4f,
                new BossPattern(PatternKind.Slash3, 4f, 0.95f, 3, maxRange: 3.4f, weight: 1.5f),
                new BossPattern(PatternKind.Charge, 7f, 1.3f),
                new BossPattern(PatternKind.ArrowVolley, 8f, 1.0f, 9),
                new BossPattern(PatternKind.Guard, 12f),
                new BossPattern(PatternKind.BoltRing, 6f, 1.0f, 12, minPhase: 2),
                new BossPattern(PatternKind.Fields, 7f, 1.1f, 7, minPhase: 2),
                new BossPattern(PatternKind.Annihilate, 22f, 2.2f, minPhase: 2),
                new BossPattern(PatternKind.RuinRitual, 48f, minPhase: 2, weight: .8f),
                new BossPattern(PatternKind.Judgment, 35f, 8f, minPhase: 3, weight: 0f));
            kamatz.artScale = 1.4f;
            kamatz.raid = true;
            kamatz.size = 2.4f;
            kamatz.groggyMax = 800f;
            kamatz.phases = new[] { 0.7f, 0.4f, 0.15f };
            kamatz.maxMinions = 4;
            kamatz.minionId = GreyKnight;
            Ch3(kamatz);
        }
    }
}
