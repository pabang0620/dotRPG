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
        }
    }
}
