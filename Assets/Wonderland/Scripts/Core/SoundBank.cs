using System;
using UnityEngine;

namespace Wonderland
{
    /// <summary>Continuous sounds of places and things (each a seamless loop).</summary>
    public enum SoundLoop { Birds, Water, Engine, Motorbike, Steam, Diesel, Siren, Children, Fair, Disco, Chatter, Factory, Rain }

    /// <summary>Short sounds of moments.</summary>
    public enum SoundCue { Bark, Meow, Kick, Cheer, Whistle, Brakes, Burner, Horn, Bell }

    /// <summary>
    /// Every sound in the world, synthesised at start-up from a few oscillators, noise and envelopes: no recordings, no
    /// licences, nothing downloaded. Mono, 22 kHz, about 30 s of audio in all (under 3 MB), generated in a few tens of
    /// milliseconds the first time each is needed. Storybook sounds rather than realistic ones - they sit quietly
    /// under the scene.
    /// </summary>
    public static class SoundBank
    {
        public const int Rate = 22050;
        private static readonly AudioClip[] loops = new AudioClip[Enum.GetValues(typeof(SoundLoop)).Length];
        private static readonly AudioClip[] cues = new AudioClip[Enum.GetValues(typeof(SoundCue)).Length];

        public static AudioClip Loop(SoundLoop k) => loops[(int)k] ??= Clip(k.ToString(), Build(k));
        public static AudioClip Cue(SoundCue k) => cues[(int)k] ??= Clip(k.ToString(), Build(k));

        private static AudioClip Clip(string name, float[] data)
        {
            var clip = AudioClip.Create("Wonderland " + name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        // ------------------------------------------------------------------ the sounds

        public static float[] Build(SoundLoop k)
        {
            var r = new System.Random(100 + (int)k);
            switch (k)
            {
                case SoundLoop.Birds:
                {
                    var s = new float[Rate * 4];
                    for (int phrase = 0; phrase < 6; phrase++)
                    {
                        float at = (float)r.NextDouble() * 3.4f, f0 = 2400f + (float)r.NextDouble() * 2200f;
                        int notes = 2 + r.Next(5);
                        for (int n = 0; n < notes; n++)
                            Chirp(s, at + n * 0.085f, 0.06f + (float)r.NextDouble() * 0.04f, f0 * (1f + (n % 2) * 0.12f), f0 * (0.8f + (float)r.NextDouble() * 0.5f), 0.22f);
                    }
                    return Seamless(s, 0.05f);
                }
                case SoundLoop.Water:
                {
                    var s = Noise(Rate * 3, r, 0.05f, 0.5f);
                    for (int i = 0; i < 26; i++) Blip(s, (float)r.NextDouble() * 2.9f, 0.05f, 420f + (float)r.NextDouble() * 700f, 0.12f, 1.6f);
                    return Seamless(s, 0.3f);
                }
                case SoundLoop.Rain:
                {
                    // A soft hiss of rain with the odd heavier drop pattering.
                    var s = Noise(Rate * 3, r, 0.09f, 0.75f);
                    for (int i = 0; i < 120; i++) Blip(s, (float)r.NextDouble() * 2.95f, 0.012f, 1800f + (float)r.NextDouble() * 2600f, 0.05f, 2.2f);
                    return Seamless(s, 0.3f);
                }
                case SoundLoop.Engine: return Seamless(Hum(Rate, 55f, new[] { 1f, 0.6f, 0.35f, 0.2f, 0.12f }, 0.35f, r, 0.25f), 0.02f);
                case SoundLoop.Diesel: return Seamless(Hum(Rate, 44f, new[] { 1f, 0.7f, 0.5f, 0.25f }, 0.4f, r, 0.3f), 0.02f);
                case SoundLoop.Motorbike: return Seamless(Hum(Rate, 92f, new[] { 1f, 0f, 0.6f, 0f, 0.35f, 0f, 0.2f }, 0.3f, r, 0.35f), 0.02f);
                case SoundLoop.Steam:
                {
                    var s = Noise(Rate, r, 0.35f, 0.04f);   // a soft hiss
                    for (int c = 0; c < 4; c++) Burst(s, c * 0.25f, 0.2f, r, 0.55f, 0.18f, 14f);
                    return Seamless(s, 0.02f);
                }
                case SoundLoop.Siren:
                {
                    var s = new float[Rate * 2];
                    for (int i = 0; i < s.Length; i++)
                    {
                        float t = i / (float)Rate, f = t < 1f ? 660f : 880f;
                        float ph = t < 1f ? t * f : (t - 1f) * f;
                        float v = Mathf.Sin(ph * Mathf.PI * 2f) + Mathf.Sin(ph * Mathf.PI * 6f) / 4f + Mathf.Sin(ph * Mathf.PI * 10f) / 9f;
                        float edge = Mathf.Min(1f, Mathf.Min(Mathf.Repeat(t, 1f), 1f - Mathf.Repeat(t, 1f)) * 60f);
                        s[i] = v * 0.22f * edge;
                    }
                    return s;
                }
                case SoundLoop.Children: return Seamless(Voices(Rate * 3, r, 14, 330f, 640f, 0.16f, true), 0.2f);
                case SoundLoop.Chatter:
                {
                    var s = Voices(Rate * 3, r, 16, 140f, 260f, 0.12f, false);
                    for (int i = 0; i < 5; i++) Blip(s, (float)r.NextDouble() * 2.8f, 0.12f, 3200f + (float)r.NextDouble() * 900f, 0.08f, 30f);   // cutlery
                    return Seamless(s, 0.2f);
                }
                case SoundLoop.Fair:
                {
                    // A barrel-organ waltz: oom-pah-pah in the bass, a tune on top.
                    int[] tune = { 7, 9, 11, 12, 11, 9, 7, 4, 5, 7, 9, 7, 5, 4, 2, 0 };
                    int[] bass = { 0, 0, 5, 5, 7, 7, 0, 0 };
                    var s = new float[Rate * 4];
                    for (int n = 0; n < tune.Length; n++) Tone(s, n * 0.25f, 0.24f, Note(72 + tune[n]), 0.13f, new[] { 1f, 0.5f, 0.3f, 0.15f });
                    for (int beat = 0; beat < 16; beat++)
                    {
                        int root = bass[beat / 2 % bass.Length];
                        Tone(s, beat * 0.25f, 0.18f, Note(beat % 3 == 0 ? 48 + root : 60 + root), beat % 3 == 0 ? 0.16f : 0.07f, new[] { 1f, 0.4f });
                    }
                    return s;
                }
                case SoundLoop.Disco:
                {
                    var s = new float[Rate * 2];   // 120 bpm, four beats
                    int[] line = { 0, 0, 7, 5 };
                    for (int beat = 0; beat < 4; beat++)
                    {
                        float t = beat * 0.5f;
                        Kick(s, t, 0.3f);
                        Burst(s, t + 0.25f, 0.05f, r, 0.08f, 0.9f, 60f);                                           // open hi-hat
                        Tone(s, t, 0.22f, Note(36 + line[beat]), 0.16f, new[] { 1f, 0.5f, 0.33f, 0.25f });          // bass
                        Tone(s, t + 0.25f, 0.2f, Note(36 + line[beat] + 12), 0.08f, new[] { 1f, 0.5f, 0.33f });
                        Tone(s, t + 0.125f, 0.1f, Note(64 + (beat % 2) * 3), 0.05f, new[] { 1f, 0.3f });            // a stab of chord
                    }
                    return s;
                }
                default: // Factory
                {
                    var s = Hum(Rate * 2, 100f, new[] { 0.4f, 0.2f, 0.1f }, 0.15f, r, 0.1f);
                    for (int c = 0; c < 4; c++)
                    {
                        Burst(s, c * 0.5f, 0.08f, r, 0.3f, 0.7f, 30f);
                        Tone(s, c * 0.5f, 0.3f, 830f, 0.07f, new[] { 1f, 0f, 0.4f }, 12f);
                        Tone(s, c * 0.5f + 0.25f, 0.2f, 1240f, 0.035f, new[] { 1f }, 18f);
                    }
                    return Seamless(s, 0.02f);
                }
            }
        }

        public static float[] Build(SoundCue k)
        {
            var r = new System.Random(300 + (int)k);
            switch (k)
            {
                case SoundCue.Bark:
                {
                    var s = new float[(int)(Rate * 0.45f)];
                    foreach (float at in new[] { 0f, 0.22f })
                    {
                        Burst(s, at, 0.12f, r, 0.18f, 0.45f, 22f);
                        Tone(s, at, 0.13f, 520f, 0.32f, new[] { 1f, 0.6f, 0.3f }, 20f, 0.65f);
                    }
                    return s;
                }
                case SoundCue.Meow:
                {
                    var s = new float[(int)(Rate * 0.75f)];
                    double ph = 0;
                    for (int i = 0; i < s.Length; i++)
                    {
                        float t = i / (float)Rate, u = t / 0.75f;
                        float f = Mathf.Lerp(600f, 900f, Mathf.Sin(u * Mathf.PI)) * (1f + Mathf.Sin(t * 40f) * 0.02f) - u * 150f;
                        ph += f / Rate;
                        float v = Mathf.Sin((float)(ph * Math.PI * 2)) + 0.5f * Mathf.Sin((float)(ph * Math.PI * 4)) + 0.25f * Mathf.Sin((float)(ph * Math.PI * 6));
                        s[i] = v * 0.18f * Mathf.Sin(u * Mathf.PI) * Mathf.Min(1f, u * 12f);
                    }
                    return s;
                }
                case SoundCue.Kick: { var s = new float[(int)(Rate * 0.15f)]; Kick(s, 0f, 0.45f); return s; }
                case SoundCue.Cheer:
                {
                    var s = Noise((int)(Rate * 1.6f), r, 0.3f, 0.25f);
                    for (int i = 0; i < s.Length; i++) { float u = i / (float)s.Length; s[i] *= Mathf.Min(1f, u * 6f) * (1f - u); }
                    var v = Voices(s.Length, r, 10, 300f, 520f, 0.2f, true);
                    for (int i = 0; i < s.Length; i++) { float u = i / (float)s.Length; s[i] += v[i] * (1f - u); }
                    return s;
                }
                case SoundCue.Whistle:
                {
                    var s = new float[(int)(Rate * 1.3f)];
                    foreach (float f in new[] { 523f, 659f, 784f }) Tone(s, 0f, 1.25f, f, 0.1f, new[] { 1f, 0.2f }, 0f, 1f, 0.08f);
                    var hiss = Noise(s.Length, r, 0.5f, 0.05f);
                    for (int i = 0; i < s.Length; i++) s[i] += hiss[i] * Mathf.Min(1f, i / 2000f) * (1f - i / (float)s.Length);
                    return s;
                }
                case SoundCue.Brakes:
                {
                    var s = new float[(int)(Rate * 0.9f)];
                    Tone(s, 0f, 0.88f, 2900f, 0.06f, new[] { 1f, 0.2f }, 0f, 1f, 0.15f, 6f);
                    return s;
                }
                case SoundCue.Burner:
                {
                    var s = Noise(Rate, r, 0.12f, 0.6f);
                    for (int i = 0; i < s.Length; i++) { float u = i / (float)s.Length; s[i] *= Mathf.Min(1f, u * 8f) * (1f - u) * (1f - u); }
                    return s;
                }
                case SoundCue.Horn:
                {
                    var s = new float[(int)(Rate * 0.5f)];
                    foreach (float f in new[] { 392f, 494f }) Tone(s, 0f, 0.45f, f, 0.11f, new[] { 1f, 0.5f, 0.3f, 0.2f }, 0f, 1f, 0.03f);
                    return s;
                }
                default: // Bell
                {
                    var s = new float[(int)(Rate * 1.2f)];
                    Tone(s, 0f, 1.2f, 1320f, 0.15f, new[] { 1f, 0f, 0.5f, 0f, 0.25f }, 4f);
                    return s;
                }
            }
        }

        // ------------------------------------------------------------------ building blocks

        private static float Note(int midi) => 440f * Mathf.Pow(2f, (midi - 69) / 12f);

        /// <summary>A note with harmonics; decay (per second) 0 for a held note with a soft attack and release.</summary>
        private static void Tone(float[] s, float at, float length, float freq, float gain, float[] harmonics, float decay = 0f, float drop = 1f, float attack = 0.008f, float vibrato = 0f)
        {
            int i0 = (int)(at * Rate), n = (int)(length * Rate);
            double ph = 0;
            for (int i = 0; i < n; i++)
            {
                int k = i0 + i;
                if (k < 0) continue;
                if (k >= s.Length) k -= s.Length;   // wrap round (loops)
                float t = i / (float)Rate, u = i / (float)n;
                float f = freq * Mathf.Lerp(1f, drop, u) * (1f + vibrato * 0.003f * Mathf.Sin(t * 30f));
                ph += f / Rate;
                float v = 0f;
                for (int h = 0; h < harmonics.Length; h++) if (harmonics[h] != 0f) v += harmonics[h] * Mathf.Sin((float)(ph * Math.PI * 2 * (h + 1)));
                float env = decay > 0f ? Mathf.Exp(-t * decay) * Mathf.Min(1f, t / attack) : Mathf.Min(1f, t / attack) * Mathf.Min(1f, (length - t) / 0.03f);
                s[k] += v * gain * env;
            }
        }

        private static void Chirp(float[] s, float at, float length, float f0, float f1, float gain)
        {
            int i0 = (int)(at * Rate), n = (int)(length * Rate);
            double ph = 0;
            for (int i = 0; i < n && i0 + i < s.Length; i++)
            {
                float u = i / (float)n;
                ph += Mathf.Lerp(f0, f1, u) / Rate;
                float env = Mathf.Sin(u * Mathf.PI);
                s[i0 + i] += Mathf.Sin((float)(ph * Math.PI * 2)) * gain * env * env;
            }
        }

        private static void Blip(float[] s, float at, float length, float freq, float gain, float rise)
        {
            int i0 = (int)(at * Rate), n = (int)(length * Rate);
            double ph = 0;
            for (int i = 0; i < n && i0 + i < s.Length; i++)
            {
                float u = i / (float)n;
                ph += freq * (1f + rise * u) / Rate;
                s[i0 + i] += Mathf.Sin((float)(ph * Math.PI * 2)) * gain * Mathf.Sin(u * Mathf.PI);
            }
        }

        private static void Kick(float[] s, float at, float gain)
        {
            int i0 = (int)(at * Rate);
            double ph = 0;
            for (int i = 0; i < Rate * 0.15f && i0 + i < s.Length; i++)
            {
                float t = i / (float)Rate;
                ph += (50f + 90f * Mathf.Exp(-t * 30f)) / Rate;
                s[i0 + i] += Mathf.Sin((float)(ph * Math.PI * 2)) * gain * Mathf.Exp(-t * 22f);
            }
        }

        /// <summary>A puff of filtered noise (cutoff 0-1: dull to bright), decaying.</summary>
        private static void Burst(float[] s, float at, float length, System.Random r, float gain, float cutoff, float decay)
        {
            int i0 = (int)(at * Rate), n = (int)(length * Rate);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                int k = (i0 + i) % s.Length;
                float w = (float)r.NextDouble() * 2f - 1f;
                lp += (w - lp) * cutoff;
                float t = i / (float)Rate;
                s[k] += (cutoff > 0.6f ? w - lp : lp) * gain * Mathf.Exp(-t * decay) * Mathf.Min(1f, t * 400f);
            }
        }

        private static float[] Noise(int n, System.Random r, float cutoff, float gain)
        {
            var s = new float[n];
            float lp = 0f, slow = 0f;
            for (int i = 0; i < n; i++)
            {
                lp += ((float)r.NextDouble() * 2f - 1f - lp) * cutoff;
                slow += ((float)r.NextDouble() - slow) * 0.0004f;
                s[i] = lp * gain * (0.7f + slow);
            }
            return s;
        }

        private static float[] Hum(int n, float f, float[] harmonics, float gain, System.Random r, float rumble)
        {
            var s = new float[n];
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float v = 0f;
                for (int h = 0; h < harmonics.Length; h++) v += harmonics[h] * Mathf.Sin(t * f * (h + 1) * Mathf.PI * 2f);
                lp += ((float)r.NextDouble() * 2f - 1f - lp) * 0.06f;
                s[i] = (v * (1f + 0.15f * Mathf.Sin(t * Mathf.PI * 2f * 4f)) * gain + lp * rumble) * 0.5f;
            }
            return s;
        }

        /// <summary>Babble: overlapping syllables of vowel-ish tones gliding up and down (children shout higher and more).</summary>
        private static float[] Voices(int n, System.Random r, int count, float lo, float hi, float gain, bool lively)
        {
            var s = new float[n];
            float length = n / (float)Rate;
            for (int v = 0; v < count; v++)
            {
                float at = (float)r.NextDouble() * length, f = lo + (float)r.NextDouble() * (hi - lo);
                int syllables = 1 + r.Next(lively ? 4 : 3);
                for (int k = 0; k < syllables; k++)
                {
                    float len = 0.08f + (float)r.NextDouble() * 0.12f;
                    Tone(s, at + k * (len + 0.04f), len, f * (1f + ((float)r.NextDouble() - 0.5f) * 0.3f), gain * (0.5f + (float)r.NextDouble() * 0.5f),
                        new[] { 1f, 0.7f, 0.5f, 0.3f, 0.2f }, 0f, lively && r.NextDouble() < 0.4 ? 1.3f : 0.9f, 0.02f);
                }
            }
            return s;
        }

        /// <summary>Makes a clip loop without a click: its tail is faded into its head.</summary>
        private static float[] Seamless(float[] s, float fade)
        {
            int n = Mathf.Min((int)(fade * Rate), s.Length / 3);
            var o = new float[s.Length - n];
            Array.Copy(s, o, o.Length);
            for (int i = 0; i < n; i++) { float u = i / (float)n; o[i] = o[i] * u + s[o.Length + i] * (1f - u); }
            float peak = 0f;
            foreach (float v in o) peak = Mathf.Max(peak, Mathf.Abs(v));
            if (peak > 0.95f) for (int i = 0; i < o.Length; i++) o[i] *= 0.95f / peak;
            return o;
        }
    }
}
