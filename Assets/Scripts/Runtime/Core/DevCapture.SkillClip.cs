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
        /// awakened character of the skill's career in the village, a ring of harmless dummies, one cast per skill.
        /// "-skill f_awake,m_storm" picks skills ("all" = every active and awakening skill of the four careers);
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
            EnemyStats es = null;
            Vector2 at = Vector2.zero;
            foreach (var id in ids)
            {
                var s = CareerCatalog.Get(id);
                if (s == null || s.kind == CareerSkillKind.Passive) { Log("skip " + id + " (unknown or passive)"); continue; }
                if (current != s.career)
                {
                    // A fresh character of this career: level 40, promoted, every node learned, awakened.
                    foreach (var f in foes) if (f != null) Destroy(f.gameObject);
                    foes.Clear();
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
                    if (es == null)
                    {
                        es = Instantiate(Game.Config.skeletonStats);
                        es.maxHealth = 1000000; es.attackDamage = 0; es.xpReward = 0; es.wanderSpeed = es.chaseSpeed = es.knockbackSpeed = 0;
                    }
                    for (int i = 0; i < 8; i++) foes.Add(EnemyController.Create(es, CharacterLook.Skeleton, DummySpot(at, i), Game.World.ObjectsRoot));
                    Game.UI.Hud?.gameObject.SetActive(false);
                    current = s.career;
                }
                // Same starting picture for every skill: full health, facing right, dummies back in their ring.
                var player = Game.Player;
                CareerCombat.For(player).ResetState();
                player.Place(at, Facing.Right);
                player.FaceTowards(at + Vector2.right);
                player.HealFull();
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
            if (es != null) Destroy(es);
        }

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
                return new[] { Career.Fighter, Career.Guardian, Career.Arcanist, Career.Bishop }
                    .SelectMany(c => CareerCatalog.For(c)).Where(k => k.kind != CareerSkillKind.Passive).Select(k => k.id).ToList();
            return value.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }

        IEnumerator RecordClip(CareerSkill s, PlayerController player)
        {
            const int Fps = 30, Lead = 10;
            // Long enough for the cast, its effect and the fade (the fighter awakening runs 3 s plus its burst).
            float seconds = Mathf.Clamp(s.cast + Mathf.Max(s.duration, s.effect == "xslash" ? 3.2f : 1.5f) + 1.2f, 2.5f, 8f);
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
