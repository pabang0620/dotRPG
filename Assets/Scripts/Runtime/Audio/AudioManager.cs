using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Plays sound effects and music by key. Clips resolve from Resources/Audio/{key} first, then
    /// fall back to <see cref="SfxSynth"/> placeholders. Music / SFX volume come from settings.
    /// Music crossfades between two sources (equal-power, unscaled time, so it keeps fading while a
    /// window pauses the game). An AudioMixer can be introduced later by assigning outputAudioMixerGroup
    /// on the sources here.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        const int SfxVoices = 10;
        const float MinRepeatInterval = 0.035f;
        /// <summary>Length of a music crossfade in real seconds.</summary>
        public const float MusicFadeSeconds = 1f;
        /// <summary>Music sits a little under the settings value so SFX stay readable.</summary>
        const float MusicHeadroom = 0.7f;

        // Two music sources: one fading in (active), the other fading out.
        readonly AudioSource[] musicSources = new AudioSource[2];
        readonly string[] musicKeys = new string[2];
        // Fade position per source, 0 = silent, 1 = full; gain = sin(p * π/2) so the crossfade is equal-power.
        readonly float[] musicFade = new float[2];
        int activeMusic = -1;

        readonly AudioSource[] voices = new AudioSource[SfxVoices];
        int nextVoice;
        float musicVolume = 0.6f;
        float sfxVolume = 0.8f;
        string currentMusic;

        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();

        /// <summary>Key of the track that is playing / fading in (null when music is stopped). For tests.</summary>
        public string CurrentMusicKey => currentMusic;

        public static AudioManager Create(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var manager = go.AddComponent<AudioManager>();
            for (int i = 0; i < manager.musicSources.Length; i++)
            {
                var m = go.AddComponent<AudioSource>();
                m.loop = true;
                m.playOnAwake = false;
                m.ignoreListenerPause = true;
                m.volume = 0f;
                manager.musicSources[i] = m;
            }
            for (int i = 0; i < SfxVoices; i++)
            {
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                manager.voices[i] = src;
            }
            return manager;
        }

        public void SetVolumes(float musicVol, float sfxVol)
        {
            musicVolume = musicVol;
            sfxVolume = sfxVol;
            ApplyMusicVolumes();
        }

        public void PlaySfx(string key, float volume = 1f)
        {
            float now = Time.unscaledTime;
            if (lastPlayed.TryGetValue(key, out float last) && now - last < MinRepeatInterval) return;
            lastPlayed[key] = now;

            var clip = GetClip(key);
            if (clip == null) return;
            var src = voices[nextVoice];
            nextVoice = (nextVoice + 1) % voices.Length;
            src.pitch = Random.Range(0.94f, 1.06f);
            src.PlayOneShot(clip, Mathf.Clamp01(volume) * sfxVolume);
        }

        /// <summary>Crossfades to the track <paramref name="key"/>; the same key again keeps playing without a restart.</summary>
        public void PlayMusic(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            if (activeMusic >= 0 && musicKeys[activeMusic] == key && musicSources[activeMusic].isPlaying)
            {
                currentMusic = key;
                return;
            }
            int other = activeMusic < 0 ? 0 : 1 - activeMusic;
            // Asked back for the track that is still fading out: fade it back in where it is.
            if (musicKeys[other] == key && musicSources[other].isPlaying)
            {
                activeMusic = other;
                currentMusic = key;
                return;
            }
            var clip = GetClip(key);
            if (clip == null) return;
            currentMusic = key;
            var src = musicSources[other];
            src.Stop();
            src.clip = clip;
            musicKeys[other] = key;
            musicFade[other] = 0f;
            src.volume = 0f;
            src.Play();
            activeMusic = other;
            ApplyMusicVolumes();
        }

        void Update()
        {
            float step = Time.unscaledDeltaTime / MusicFadeSeconds;
            for (int i = 0; i < musicSources.Length; i++)
            {
                var src = musicSources[i];
                if (src == null) continue;
                float target = i == activeMusic ? 1f : 0f;
                musicFade[i] = Mathf.MoveTowards(musicFade[i], target, step);
                if (target == 0f && musicFade[i] <= 0f && src.isPlaying)
                {
                    src.Stop();
                    src.clip = null;
                    musicKeys[i] = null;
                }
            }
            ApplyMusicVolumes();
        }

        void ApplyMusicVolumes()
        {
            for (int i = 0; i < musicSources.Length; i++)
            {
                var src = musicSources[i];
                if (src == null) continue;
                src.volume = Mathf.Sin(musicFade[i] * Mathf.PI * 0.5f) * musicVolume * MusicHeadroom;
            }
        }

        AudioClip GetClip(string key)
        {
            if (clips.TryGetValue(key, out var clip)) return clip;
            clip = Resources.Load<AudioClip>("Audio/" + key);
            if (clip == null) clip = SfxSynth.Create(key);
            if (clip == null) Debug.LogWarning($"[dotRPG] Unknown sound '{key}'.");
            clips[key] = clip;
            return clip;
        }
    }
}
