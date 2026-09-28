using System.Collections.Generic;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Plays sound effects and music by key. Clips resolve from Resources/Audio/{key} first, then
    /// fall back to <see cref="SfxSynth"/> placeholders. Music / SFX volume come from settings.
    /// An AudioMixer can be introduced later by assigning outputAudioMixerGroup on the sources here.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        const int SfxVoices = 10;
        const float MinRepeatInterval = 0.035f;

        AudioSource music;
        readonly AudioSource[] voices = new AudioSource[SfxVoices];
        int nextVoice;
        float musicVolume = 0.6f;
        float sfxVolume = 0.8f;
        string currentMusic;

        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        readonly Dictionary<string, float> lastPlayed = new Dictionary<string, float>();

        public static AudioManager Create(Transform parent)
        {
            var go = new GameObject("Audio");
            go.transform.SetParent(parent, false);
            var manager = go.AddComponent<AudioManager>();
            manager.music = go.AddComponent<AudioSource>();
            manager.music.loop = true;
            manager.music.playOnAwake = false;
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
            if (music != null) music.volume = musicVolume * 0.7f;
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

        public void PlayMusic(string key)
        {
            if (currentMusic == key && music.isPlaying) return;
            currentMusic = key;
            var clip = GetClip(key);
            if (clip == null) return;
            music.clip = clip;
            music.volume = musicVolume * 0.7f;
            music.Play();
        }

        public void StopMusic()
        {
            currentMusic = null;
            music.Stop();
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
