using System.Collections;
using System.IO;
using UnityEngine;

namespace DotRPG
{
    public partial class DevCapture
    {
        /// <summary>
        /// [VFX] Records the fighter's awakening (천검귀일) as numbered PNG frames at a fixed 30 fps: a level-40 awakened
        /// fighter in the village, a ring of harmless dummies, one cast. Frames go to &lt;folder&gt;/awake_clip; ffmpeg joins them.
        /// </summary>
        IEnumerator AwakeClipRun()
        {
            ApplyRequestedResolution();
            yield return Wait(1);
            Game.Config.autosave = false;
            Game.Flow.NewGame(CharacterClass.Warrior);
            yield return Wait(.4f);
            while (Game.Flow.IsTransitioning) yield return null;
            var player = Game.Player;
            player.Input = new ScriptedInput();
            var p = player.Data.Progression;
            p.SetFromServer(40, 0);
            p.Promote(Career.Fighter);
            for (int stage = 0; stage < 5; stage++) p.AdvanceAwakening(stage);
            var at = player.Position;
            var es = Instantiate(Game.Config.skeletonStats);
            es.maxHealth = 1000000; es.attackDamage = 0; es.xpReward = 0; es.wanderSpeed = es.chaseSpeed = es.knockbackSpeed = 0;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f + .3f;
                EnemyController.Create(es, CharacterLook.Skeleton, at + new Vector2(Mathf.Cos(a), Mathf.Sin(a) * .7f) * (2.5f + (i % 2) * 1.5f), Game.World.ObjectsRoot);
            }
            Game.UI.Hud?.gameObject.SetActive(false);
            yield return Wait(1.5f);
            var awake = CareerCatalog.For(Career.Fighter)[8];
            var numbers = player.Data.Stats.Skill(player.Class, 4, awake.Gem, new SkillGem[0]);
            string dir = Path.Combine(folder, "awake_clip");
            Directory.CreateDirectory(dir);
            const int Fps = 30, Frames = 30 * 5; // the 3 s skill plus its burst and fade
            var frame = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            Time.captureFramerate = Fps;
            for (int f = 0; f < Frames; f++)
            {
                if (f == 10) StartCoroutine(CareerCombat.For(player).Cast(awake, numbers));
                // ScreenCapture is an optional module this project does not include: read the back buffer (as Shot does).
                yield return new WaitForEndOfFrame();
                frame.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0);
                frame.Apply();
                File.WriteAllBytes(Path.Combine(dir, $"f{f:0000}.png"), frame.EncodeToPNG());
                yield return null;
            }
            Time.captureFramerate = 0;
            Destroy(frame);
            yield return Wait(.5f);
            Log($"awake clip: {Frames} frames at {Fps} fps in {dir}");
        }
    }
}
