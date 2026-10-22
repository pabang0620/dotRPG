using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        /// <summary>
        /// [VFX] Records career skills as numbered PNG frames at a fixed 30 fps (Docs/SKILL_VIDEO_CAPTURE.md): a level-40
        /// awakened character of the skill's career in the village, three standing party members (all at half health, so
        /// heals and shields show), a ring of harmless dummies, one cast per skill.
        /// "-skill f_awake,m_storm" picks skills ("all" = every active and awakening skill of the six careers plus the
        /// rogue's five base skills, "rogue" = the rogue's 24: base 5 + assassin and ninja 19);
        /// without it the fighter awakening is recorded. Frames go to &lt;folder&gt;/clips/&lt;skill id&gt;/f0000.png.
        /// </summary>
        IEnumerator SkillClipRun()
        {
            ApplyRequestedResolution();
            yield return Wait(1);
            Game.Config.autosave = false;
            var ids = RequestedClipSkills();
            Career? current = null;
            var foes = new List<EnemyController>();
            var allies = new List<PlayerController>();
            EnemyStats es = null;
            Vector2 at = Vector2.zero;
            foreach (var id in ids)
            {
                var s = CareerCatalog.Get(id);
                var baseGem = s == null ? SkillGems.Get(id) : null;
                if (baseGem != null && baseGem.classOnly == CharacterClass.Rogue && baseGem.slot >= 0)
                {
                    // [ROGUE] Base rogue skills (no career): a level-40 rogue casts from its own key.
                    foreach (var f in foes) if (f != null) Destroy(f.gameObject);
                    foes.Clear();
                    foreach (var a in allies) if (a != null) Game.Party?.RemoveNetMember(a);
                    allies.Clear();
                    current = null;
                    yield return BaseClip(baseGem, foes, es = es ?? DummyStats());
                    continue;
                }
                if (s == null || s.kind == CareerSkillKind.Passive) { Log("skip " + id + " (unknown or passive)"); continue; }
                if (current != s.career)
                {
                    // A fresh character of this career: level 40, promoted, every node learned, awakened.
                    foreach (var f in foes) if (f != null) Destroy(f.gameObject);
                    foes.Clear();
                    foreach (var a in allies) if (a != null) Game.Party?.RemoveNetMember(a);
                    allies.Clear();
                    Game.Flow.NewGame(CareerCatalog.Base(s.career));
                    yield return Wait(.4f);
                    while (Game.Flow.IsTransitioning) yield return null;
                    Game.Player.Input = new ScriptedInput();
                    var p = Game.Player.Data.Progression;
                    p.SetFromServer(40, 0);
                    p.Promote(s.career);
                    foreach (var k in CareerCatalog.For(s.career)) if (k.kind != CareerSkillKind.Awakening) p.Learn(k.id);
                    for (int stage = 0; stage < 5; stage++) p.AdvanceAwakening(stage);
                    at = Game.Player.Position;
                    if (es == null) es = DummyStats();
                    for (int i = 0; i < 8; i++) foes.Add(EnemyController.Create(es, CharacterLook.Skeleton, DummySpot(at, i), Game.World.ObjectsRoot));
                    // Three standing party members, so heals, shields and buffs on allies are visible.
                    if (Game.Party != null)
                        for (int i = 0; i < AllySpots.Length; i++)
                        {
                            var cls = i == 1 ? CharacterClass.Mage : i == 2 ? CharacterClass.Rogue : CharacterClass.Warrior; // [ROGUE] one of each class
                            var data = CharacterData.CreateCompanion("clip_" + i, "파티원 " + (i + 1), cls, 300);
                            allies.Add(Game.Party.AddNetMember(data, null, true, at + AllySpots[i], Facing.Right));
                        }
                    Game.UI.Hud?.gameObject.SetActive(false);
                    current = s.career;
                }
                // Same starting picture for every skill: full health, facing right, dummies back in their ring.
                var player = Game.Player;
                CareerCombat.For(player).ResetState();
                player.Place(at, Facing.Right);
                player.FaceTowards(at + Vector2.right);
                // Everyone at half health: a heal shows as a rising bar and green numbers.
                player.HealFull();
                player.Health.Drain(player.Health.Max / 2);
                for (int i = 0; i < allies.Count; i++)
                {
                    if (allies[i] == null) continue;
                    allies[i].Place(at + AllySpots[i], Facing.Right);
                    allies[i].HealFull();
                    allies[i].Health.Drain(allies[i].Health.Max / 2);
                }
                for (int i = 0; i < foes.Count; i++)
                {
                    if (foes[i] == null) continue;
                    foes[i].transform.position = DummySpot(at, i);
                    foes[i].GetComponent<Rigidbody2D>().position = foes[i].transform.position;
                    foes[i].Health.Heal(10000000);
                }
                yield return Wait(1f);
                yield return RecordClip(s, player);
            }
            foreach (var f in foes) if (f != null) Destroy(f.gameObject);
            foreach (var a in allies) if (a != null) Game.Party?.RemoveNetMember(a);
            if (es != null) Destroy(es);
        }

        /// <summary>Where the three clip party members stand, around and behind the caster.</summary>
        static readonly Vector2[] AllySpots = { new Vector2(-1.6f, .9f), new Vector2(-1.9f, -.6f), new Vector2(-.4f, -1.3f) };

        static Vector2 DummySpot(Vector2 at, int i)
        {
            float a = i * Mathf.PI / 4f + .3f;
            return at + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .7f) * (2.5f + (i % 2) * 1.5f);
        }

        /// <summary>-skill a,b,c / -skill all / nothing (= f_awake).</summary>
        static List<string> RequestedClipSkills()
        {
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "-skill");
            string value = i >= 0 && i + 1 < args.Length ? args[i + 1] : "f_awake";
            if (value == "all")
                return new[] { Career.Fighter, Career.Guardian, Career.Arcanist, Career.Bishop, Career.Assassin, Career.Ninja }
                    .SelectMany(c => CareerCatalog.For(c)).Where(k => k.kind != CareerSkillKind.Passive).Select(k => k.id).Concat(RogueBaseClips).ToList();
            if (value == "rogue")
                return RogueBaseClips.Concat(new[] { Career.Assassin, Career.Ninja }
                    .SelectMany(c => CareerCatalog.For(c)).Select(k => k.id)).ToList();
            return value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        /// <summary>How long a skill's own scripted sequence runs (awakenings with phases), else 1.5 s.</summary>
        static float ScriptedLength(string effect)
        {
            switch (effect)
            {
                case "xslash": return 3.2f;    // 천검귀일: cut, charge, crack, burst
                case "cataclysm": return 7.6f; // 유성우: storm cloud, 4 s of comets, finale, marks fade
                case "aegis": return 3.2f;     // 천쇄방패: sigil, walls, close, burst
                case "shadows": return 3.8f;   // [ROGUE] 천영참: six shadows cut for 3 s, then the marks burst
                case "cloneburst": return 3.8f; // [ROGUE] 분신 난무: six clones throw for 3 s, then the kunai burst
                default: return 1.5f;
            }
        }

        /// <summary>[ROGUE] The rogue's base skills in key order (급소 찌르기, 연쇄 도약, 백어택, 그림자 습격, 천인난무).</summary>
        static readonly string[] RogueBaseClips = { "stab", "leap", "backstab", "raid", "dance" };

        static EnemyStats DummyStats()
        {
            var es = Instantiate(Game.Config.skeletonStats);
            es.maxHealth = 1000000; es.attackDamage = 0; es.xpReward = 0; es.wanderSpeed = es.chaseSpeed = es.knockbackSpeed = 0;
            return es;
        }

        /// <summary>[ROGUE] One base rogue skill: a fresh level-40 rogue in the dummy ring presses the skill's key.</summary>
        IEnumerator BaseClip(SkillGem gem, List<EnemyController> foes, EnemyStats es)
        {
            Game.Flow.NewGame(CharacterClass.Rogue);
            yield return Wait(.4f);
            while (Game.Flow.IsTransitioning) yield return null;
            var player = Game.Player;
            player.Input = new ScriptedInput();
            player.Data.Progression.SetFromServer(40, 0);
            Vector2 at = player.Position;
            for (int i = 0; i < 8; i++) foes.Add(EnemyController.Create(es, CharacterLook.Skeleton, DummySpot(at, i), Game.World.ObjectsRoot));
            Game.UI.Hud?.gameObject.SetActive(false);
            player.Place(at, Facing.Right);
            player.FaceTowards(at + Vector2.right);
            yield return Wait(1f);
            const int Fps = 30, Lead = 10;
            float seconds = Mathf.Clamp(gem.cooldown > 10f ? 3f : 2f, 2f, 4f);
            int frames = Lead + Mathf.CeilToInt(seconds * Fps);
            string dir = Path.Combine(folder, "clips", gem.id);
            Directory.CreateDirectory(dir);
            var frame = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            Time.captureFramerate = Fps;
            for (int f = 0; f < frames; f++)
            {
                if (f == Lead) player.Skills.TryCast(gem.slot);
                yield return new WaitForEndOfFrame();
                frame.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                frame.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"f{f:0000}.png"), frame.EncodeToPNG());
                yield return null;
            }
            Time.captureFramerate = 0;
            Destroy(frame);
            yield return Wait(.6f);
            Log($"clip {gem.id} {gem.name}: {frames} frames at {Fps} fps in {dir}");
        }

        IEnumerator RecordClip(CareerSkill s, PlayerController player)
        {
            const int Fps = 30, Lead = 10;
            // Long enough for the cast, its effect and the fade (the fighter awakening runs 3 s plus its burst).
            float seconds = Mathf.Clamp(s.cast + Mathf.Max(s.duration, ScriptedLength(s.effect)) + 1.2f, 2.5f, 8f);
            int frames = Lead + Mathf.CeilToInt(seconds * Fps);
            var numbers = player.Data.Stats.Skill(player.Class, s.kind == CareerSkillKind.Awakening ? 4 : 0, s.Gem, new SkillGem[0]);
            string dir = Path.Combine(folder, "clips", s.id);
            Directory.CreateDirectory(dir);
            // ScreenCapture is an optional module this project does not include: read the back buffer (as Shot does).
            var frame = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            Time.captureFramerate = Fps;
            for (int f = 0; f < frames; f++)
            {
                if (f == Lead) StartCoroutine(CareerCombat.For(player).Cast(s, numbers));
                yield return new WaitForEndOfFrame();
                frame.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                frame.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"f{f:0000}.png"), frame.EncodeToPNG());
                yield return null;
            }
            Time.captureFramerate = 0;
            Destroy(frame);
            yield return Wait(.6f);
            Log($"clip {s.id} {s.name}: {frames} frames at {Fps} fps in {dir}");
        }
    }
}
