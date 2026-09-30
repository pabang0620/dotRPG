using System;
using UnityEngine;

namespace DotRPG
{
    /// <summary>
    /// Generates placeholder chiptune sounds and music at runtime so the prototype has audio
    /// feedback without any third-party assets. Replace any sound by dropping a clip at
    /// Resources/Audio/{key} (see AudioManager).
    /// </summary>
    public static class SfxSynth
    {
        public const int SampleRate = 22050;

        enum Wave
        {
            Square,
            Pulse,
            Triangle,
            Sine,
        }

        public static AudioClip Create(string key)
        {
            float[] data = Render(key);
            if (data == null) return null;
            var clip = AudioClip.Create(key, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        /// <summary>Renders a sound to raw samples (also usable by offline tools/tests).</summary>
        public static float[] Render(string key)
        {
            switch (key)
            {
                case "swing":
                {
                    var b = Buffer(0.13f);
                    Noise(b, 0f, 0.13f, 0.35f, 0.35f, 7);
                    Tone(b, 0f, 0.1f, 900f, 300f, Wave.Triangle, 0.12f);
                    return b;
                }
                case "magic":
                {
                    var b = Buffer(0.22f);
                    Tone(b, 0f, 0.2f, 520f, 1480f, Wave.Triangle, 0.2f, vibrato: 18f);
                    Tone(b, 0.02f, 0.16f, 1040f, 2200f, Wave.Pulse, 0.06f);
                    Noise(b, 0f, 0.08f, 0.12f, 0.6f, 37);
                    return b;
                }
                case "hit":
                {
                    var b = Buffer(0.14f);
                    Tone(b, 0f, 0.1f, 520f, 160f, Wave.Square, 0.3f);
                    Noise(b, 0f, 0.06f, 0.35f, 0.6f, 3);
                    return b;
                }
                case "hurt":
                {
                    var b = Buffer(0.25f);
                    Tone(b, 0f, 0.22f, 320f, 110f, Wave.Square, 0.32f, vibrato: 30f);
                    Noise(b, 0f, 0.05f, 0.25f, 0.5f, 5);
                    return b;
                }
                case "player_down":
                {
                    var b = Buffer(0.8f);
                    Tone(b, 0f, 0.2f, 440f, 392f, Wave.Square, 0.25f);
                    Tone(b, 0.2f, 0.2f, 330f, 294f, Wave.Square, 0.25f);
                    Tone(b, 0.4f, 0.4f, 262f, 110f, Wave.Square, 0.25f);
                    return b;
                }
                case "enemy_windup":
                {
                    var b = Buffer(0.12f);
                    Tone(b, 0f, 0.12f, 500f, 800f, Wave.Pulse, 0.12f);
                    return b;
                }
                case "enemy_attack":
                {
                    var b = Buffer(0.1f);
                    Noise(b, 0f, 0.1f, 0.3f, 0.4f, 9);
                    return b;
                }
                case "enemy_die":
                {
                    var b = Buffer(0.4f);
                    Noise(b, 0f, 0.35f, 0.3f, 0.25f, 11);
                    Tone(b, 0f, 0.3f, 300f, 60f, Wave.Square, 0.22f);
                    Tone(b, 0.05f, 0.08f, 1200f, 900f, Wave.Triangle, 0.12f);
                    return b;
                }
                case "chop":
                {
                    var b = Buffer(0.12f);
                    Tone(b, 0f, 0.1f, 200f, 110f, Wave.Triangle, 0.45f);
                    Noise(b, 0f, 0.06f, 0.3f, 0.2f, 13);
                    return b;
                }
                case "mine":
                {
                    var b = Buffer(0.15f);
                    Tone(b, 0f, 0.12f, 1400f, 1100f, Wave.Square, 0.16f);
                    Tone(b, 0f, 0.14f, 2100f, 1900f, Wave.Triangle, 0.12f);
                    Noise(b, 0f, 0.04f, 0.3f, 0.7f, 17);
                    return b;
                }
                case "tree_fall":
                {
                    var b = Buffer(0.45f);
                    Noise(b, 0f, 0.45f, 0.4f, 0.12f, 19);
                    Tone(b, 0.05f, 0.35f, 160f, 60f, Wave.Triangle, 0.35f);
                    return b;
                }
                case "rock_break":
                {
                    var b = Buffer(0.3f);
                    Noise(b, 0f, 0.3f, 0.45f, 0.35f, 23);
                    Tone(b, 0f, 0.2f, 240f, 90f, Wave.Square, 0.15f);
                    return b;
                }
                case "pickup":
                {
                    var b = Buffer(0.14f);
                    Tone(b, 0f, 0.06f, 988f, 988f, Wave.Square, 0.18f);
                    Tone(b, 0.06f, 0.08f, 1319f, 1319f, Wave.Square, 0.18f);
                    return b;
                }
                case "pluck":
                {
                    var b = Buffer(0.12f);
                    Tone(b, 0f, 0.1f, 400f, 900f, Wave.Triangle, 0.35f);
                    Noise(b, 0f, 0.04f, 0.15f, 0.3f, 29);
                    return b;
                }
                case "heal":
                    return Arpeggio(new[] { 523f, 659f, 784f, 1047f }, 0.07f, Wave.Triangle, 0.35f);
                case "blip":
                {
                    var b = Buffer(0.03f);
                    Tone(b, 0f, 0.03f, 880f, 880f, Wave.Pulse, 0.1f);
                    return b;
                }
                case "select":
                {
                    var b = Buffer(0.05f);
                    Tone(b, 0f, 0.05f, 660f, 660f, Wave.Square, 0.15f);
                    return b;
                }
                case "confirm":
                    return Arpeggio(new[] { 660f, 990f }, 0.06f, Wave.Square, 0.16f);
                case "cancel":
                    return Arpeggio(new[] { 440f, 330f }, 0.06f, Wave.Square, 0.14f);
                case "deliver":
                    return Arpeggio(new[] { 392f, 523f, 659f }, 0.07f, Wave.Square, 0.16f);
                case "hammer":
                {
                    var b = Buffer(0.12f);
                    Tone(b, 0f, 0.08f, 1800f, 1500f, Wave.Triangle, 0.2f);
                    Tone(b, 0f, 0.1f, 180f, 120f, Wave.Square, 0.18f);
                    Noise(b, 0f, 0.03f, 0.3f, 0.8f, 31);
                    return b;
                }
                case "build_complete":
                    return Arpeggio(new[] { 523f, 659f, 784f, 1047f, 784f, 1047f }, 0.09f, Wave.Square, 0.18f, 0.3f);
                case "quest":
                    return Arpeggio(new[] { 659f, 784f, 988f, 1319f }, 0.08f, Wave.Pulse, 0.2f, 0.2f);
                case "ending":
                    return Arpeggio(new[] { 523f, 659f, 784f, 659f, 784f, 1047f, 988f, 1047f }, 0.14f, Wave.Square, 0.18f, 0.5f);
                case "music_village":
                    return Music(false);
                case "music_title":
                    return Music(true);
            }
            return null;
        }

        static float[] Buffer(float seconds) => new float[Mathf.CeilToInt(seconds * SampleRate) + 1];

        static float Osc(Wave wave, float phase)
        {
            float p = phase - Mathf.Floor(phase);
            switch (wave)
            {
                case Wave.Square: return p < 0.5f ? 1f : -1f;
                case Wave.Pulse: return p < 0.25f ? 1f : -1f;
                case Wave.Triangle: return 4f * Mathf.Abs(p - 0.5f) - 1f;
                default: return Mathf.Sin(p * Mathf.PI * 2f);
            }
        }

        static void Tone(float[] buffer, float start, float duration, float f0, float f1, Wave wave, float volume,
            float attack = 0.004f, float vibrato = 0f, float releaseShape = 1.5f)
        {
            int s0 = Mathf.FloorToInt(start * SampleRate);
            int n = Mathf.FloorToInt(duration * SampleRate);
            double phase = 0;
            for (int i = 0; i < n && s0 + i < buffer.Length; i++)
            {
                float t = (float)i / n;
                float freq = Mathf.Lerp(f0, f1, t);
                if (vibrato > 0f) freq *= 1f + 0.03f * Mathf.Sin(i / (float)SampleRate * vibrato * Mathf.PI * 2f);
                phase += freq / SampleRate;
                float env = Mathf.Min(1f, i / (attack * SampleRate + 1f)) * Mathf.Pow(1f - t, releaseShape);
                buffer[s0 + i] += Osc(wave, (float)phase) * env * volume;
            }
        }

        /// <summary>Low-passed noise. smoothing: 0 = very muffled, 1 = raw white noise.</summary>
        static void Noise(float[] buffer, float start, float duration, float volume, float smoothing, int seed)
        {
            var rng = new System.Random(seed);
            int s0 = Mathf.FloorToInt(start * SampleRate);
            int n = Mathf.FloorToInt(duration * SampleRate);
            float last = 0f;
            for (int i = 0; i < n && s0 + i < buffer.Length; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                last += (white - last) * Mathf.Clamp(smoothing, 0.02f, 1f);
                float env = Mathf.Pow(1f - (float)i / n, 2f);
                buffer[s0 + i] += last * env * volume;
            }
        }

        static float[] Arpeggio(float[] notes, float noteLength, Wave wave, float volume, float tail = 0.05f)
        {
            var b = Buffer(notes.Length * noteLength + tail);
            for (int i = 0; i < notes.Length; i++)
            {
                bool last = i == notes.Length - 1;
                Tone(b, i * noteLength, last ? noteLength + tail : noteLength * 1.3f, notes[i], notes[i], wave, volume, releaseShape: 0.8f);
            }
            return b;
        }

        // ---------- Music ----------

        static float Midi(int note) => 440f * Mathf.Pow(2f, (note - 69) / 12f);

        /// <summary>
        /// Short cozy loop: I–vi–IV–V in C, triangle bass, soft pulse arpeggio and a pentatonic
        /// melody. The title variant is slower and has no drums.
        /// </summary>
        static float[] Music(bool title)
        {
            float bpm = title ? 84f : 108f;
            float beat = 60f / bpm;
            int bars = 8;
            int beatsPerBar = 4;
            float length = bars * beatsPerBar * beat;
            var b = Buffer(length + 0.01f);

            int[][] chords =
            {
                new[] { 48, 52, 55 }, // C
                new[] { 45, 48, 52 }, // Am
                new[] { 41, 45, 48 }, // F
                new[] { 43, 47, 50 }, // G
            };
            // Melody in scale degrees of C major pentatonic, one entry per eighth note (-1 = rest).
            int[] melody =
            {
                76, -1, 79, 76, 74, -1, 72, -1,   69, -1, 72, 74, 76, -1, -1, -1,
                77, -1, 76, 74, 72, -1, 69, -1,   71, -1, 74, -1, 79, -1, -1, -1,
                76, -1, 79, 81, 79, -1, 76, -1,   72, -1, 74, 76, 69, -1, -1, -1,
                72, -1, 74, 76, 77, 76, 74, -1,   71, -1, 67, -1, 72, -1, -1, -1,
            };

            for (int bar = 0; bar < bars; bar++)
            {
                var chord = chords[bar % chords.Length];
                float barStart = bar * beatsPerBar * beat;

                // Bass on beats 1 and 3.
                Tone(b, barStart, beat * 1.8f, Midi(chord[0] - 12), Midi(chord[0] - 12), Wave.Triangle, 0.22f, 0.01f, 0f, 0.7f);
                Tone(b, barStart + 2 * beat, beat * 1.8f, Midi(chord[0] - 12 + (bar % 2 == 0 ? 7 : 0)), Midi(chord[0] - 12 + (bar % 2 == 0 ? 7 : 0)), Wave.Triangle, 0.2f, 0.01f, 0f, 0.7f);

                // Arpeggio (eighth notes).
                for (int i = 0; i < 8; i++)
                {
                    int note = chord[i % 3] + 12 + (i >= 4 ? 12 : 0) * (i % 2);
                    Tone(b, barStart + i * beat * 0.5f, beat * 0.45f, Midi(note), Midi(note), Wave.Pulse, 0.035f, 0.003f, 0f, 2.5f);
                }

                // Melody.
                for (int i = 0; i < 8; i++)
                {
                    int note = melody[(bar * 8 + i) % melody.Length];
                    if (note < 0) continue;
                    int len = 1;
                    while (i + len < 8 && melody[(bar * 8 + i + len) % melody.Length] < 0 && len < 3) len++;
                    float dur = len * beat * 0.5f * 0.95f;
                    Tone(b, barStart + i * beat * 0.5f, dur, Midi(note), Midi(note), title ? Wave.Triangle : Wave.Square,
                        title ? 0.1f : 0.055f, 0.01f, 5f, 0.9f);
                }

                if (!title)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        float t = barStart + i * beat;
                        if (i % 2 == 0) Tone(b, t, 0.09f, 120f, 45f, Wave.Sine, 0.28f, 0.001f, 0f, 2f); // kick
                        Noise(b, t + beat * 0.5f, 0.04f, 0.05f, 1f, bar * 10 + i); // hat
                    }
                }
            }

            // Soft limiter.
            for (int i = 0; i < b.Length; i++) b[i] = (float)Math.Tanh(b[i] * 1.2f) * 0.8f;
            return b;
        }
    }
}
