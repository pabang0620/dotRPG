using System.Collections;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// [BGM] Music checks of the full <c>-dotrpgCapture</c> run, called right after travelling to the canyon:
    /// every BGM key loads as a real imported clip (not the SfxSynth placeholder) longer than 30 s, every map
    /// maps to its track, and the canyon is playing music_canyon. Report lines "BGM &lt;what&gt; PASS|FAIL",
    /// last line "BGM summary: N passed, M failed".
    /// </summary>
    public partial class DevCapture
    {
        int bgmPassed, bgmFailed;

        static readonly string[] BgmKeys =
        {
            "music_title", "music_village", "music_canyon", "music_winter", "music_forest",
            "music_dgn_canyon", "music_dgn_forest", "music_dgn_winter", "music_boss",
            "music_raid", "music_raid_enrage", "music_clear", "music_fail",
        };

        /// <summary>Map id → expected music key (towns, field, every dungeon room).</summary>
        static readonly (string map, string music)[] BgmMapTable =
        {
            (MapRegistry.Village, "music_village"), (MapRegistry.Forest, "music_forest"),
            (MapRegistry.Canyon, "music_canyon"), (MapRegistry.Winter, "music_winter"),
            (MapRegistry.DgnCanyon1, "music_dgn_canyon"), (MapRegistry.DgnCanyon2, "music_dgn_canyon"),
            (MapRegistry.DgnCanyon3, "music_dgn_canyon"), (MapRegistry.DgnCanyonBoss, "music_boss"),
            (MapRegistry.DgnForest1, "music_dgn_forest"), (MapRegistry.DgnForest2, "music_dgn_forest"),
            (MapRegistry.DgnForest3, "music_dgn_forest"), (MapRegistry.DgnForestBoss, "music_boss"),
            (MapRegistry.DgnWinter1, "music_dgn_winter"), (MapRegistry.DgnWinter2, "music_dgn_winter"),
            (MapRegistry.DgnWinter3, "music_dgn_winter"), (MapRegistry.DgnWinterBoss, "music_boss"),
            (MapRegistry.DgnRaid1, "music_dgn_forest"), (MapRegistry.DgnRaid2, "music_dgn_forest"),
            (MapRegistry.DgnRaidBoss, "music_raid"),
        };

        void BgmCheck(string what, bool ok)
        {
            if (ok) bgmPassed++;
            else bgmFailed++;
            log?.WriteLine($"BGM {what} {(ok ? "PASS" : "FAIL")}");
        }

        IEnumerator BgmChecks()
        {
            bgmPassed = bgmFailed = 0;
            foreach (string key in BgmKeys)
            {
                // Resources only: a hit here is the imported OGG, never the SfxSynth fallback.
                var clip = Resources.Load<AudioClip>("Audio/" + key);
                float len = clip != null ? clip.length : 0f;
                BgmCheck($"clip {key}: loaded={clip != null} length={len:0.0}s loadType={(clip != null ? clip.loadType.ToString() : "-")}",
                    clip != null && len > 30f);
            }
            foreach (var (map, music) in BgmMapTable)
            {
                var info = MapRegistry.Get(map);
                BgmCheck($"map {map} -> {info?.music} (want {music})", info != null && info.music == music);
            }
            BgmCheck($"canyon plays music: map={Game.World.MapId} current={Game.Audio.CurrentMusicKey}",
                Game.World.MapId == MapRegistry.Canyon && Game.Audio.CurrentMusicKey == "music_canyon");
            // Same key again must not restart / change anything.
            Game.Audio.PlayMusic("music_canyon");
            yield return Wait(AudioManager.MusicFadeSeconds + 0.2f);
            BgmCheck($"same key keeps playing: current={Game.Audio.CurrentMusicKey}", Game.Audio.CurrentMusicKey == "music_canyon");
            log?.WriteLine($"BGM summary: {bgmPassed} passed, {bgmFailed} failed");
        }
    }
}
