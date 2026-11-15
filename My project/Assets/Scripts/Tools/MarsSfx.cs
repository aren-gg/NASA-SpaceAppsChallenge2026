using System.Collections.Generic;
using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// Small synthesized sound effects for the tools and HUD, made in code so no audio
    /// files are needed: clicks, chimes, hammer taps, a drill buzz, sweeping, spraying.
    /// One-shots play on a 2D source; actions that take time use a looping source.
    /// </summary>
    public sealed class MarsSfx : MonoBehaviour
    {
        public enum Sound { Select, Start, Success, Research, Deny, Hit, Dig, Shutter, Radio, Click, Hiss }
        public enum Loop { None, Drill, Sweep, Spray, Ratchet }

        [Range(0f, 1f)] public float volume = 0.5f;

        private static MarsSfx instance;
        private const int Rate = 44100;
        private readonly Dictionary<Sound, AudioClip> sounds = new Dictionary<Sound, AudioClip>();
        private readonly Dictionary<Loop, AudioClip> loops = new Dictionary<Loop, AudioClip>();
        private AudioSource oneShot;
        private AudioSource loopSource;
        private Loop playing = Loop.None;
        private float loopVolume;
        private float loopTarget;
        private System.Random random = new System.Random(7);

        public static void Play(Sound sound, float gain = 1f)
        {
            MarsSfx sfx = Get();
            if (sfx == null) return;
            sfx.oneShot.pitch = 1f + ((float)sfx.random.NextDouble() - 0.5f) * 0.06f;
            sfx.oneShot.PlayOneShot(sfx.Clip(sound), sfx.volume * gain);
        }

        /// <summary>Starts (or keeps) a looping sound; <see cref="Loop.None"/> fades it out.</summary>
        public static void SetLoop(Loop loop)
        {
            MarsSfx sfx = Get();
            if (sfx == null) return;
            if (loop == Loop.None)
            {
                sfx.loopTarget = 0f;
                return;
            }
            if (sfx.playing != loop)
            {
                sfx.playing = loop;
                sfx.loopSource.clip = sfx.LoopClip(loop);
                sfx.loopSource.Play();
            }
            sfx.loopTarget = 1f;
        }

        private static MarsSfx Get()
        {
            if (instance != null) return instance;
            GameObject go = new GameObject("Tool Sounds");
            instance = go.AddComponent<MarsSfx>();
            return instance;
        }

        private void Awake()
        {
            instance = this;
            oneShot = gameObject.AddComponent<AudioSource>();
            oneShot.playOnAwake = false;
            oneShot.spatialBlend = 0f;
            loopSource = gameObject.AddComponent<AudioSource>();
            loopSource.playOnAwake = false;
            loopSource.loop = true;
            loopSource.spatialBlend = 0f;
            loopSource.volume = 0f;
        }

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            loopVolume = Mathf.MoveTowards(loopVolume, loopTarget, Time.unscaledDeltaTime * 6f);
            loopSource.volume = loopVolume * volume * 0.45f;
            if (loopVolume <= 0f && loopSource.isPlaying)
            {
                loopSource.Stop();
                playing = Loop.None;
            }
        }

        // ------------------------------------------------------------------ synthesis

        private AudioClip Clip(Sound sound)
        {
            AudioClip clip;
            if (sounds.TryGetValue(sound, out clip)) return clip;
            switch (sound)
            {
                case Sound.Select: clip = Make("select", 0.05f, (t, i) => Tone(1400f, t) * Env(t, 0.003f, 0.05f) * 0.5f); break;
                case Sound.Start: clip = Make("start", 0.09f, (t, i) => Tone(Mathf.Lerp(500f, 800f, t / 0.09f), t) * Env(t, 0.005f, 0.09f) * 0.45f); break;
                case Sound.Success:
                    clip = Make("success", 0.32f, (t, i) => (Tone(1046f, t) * Env(t, 0.005f, 0.16f) +
                                                             Tone(1568f, t - 0.1f) * Env(t - 0.1f, 0.005f, 0.22f)) * 0.4f);
                    break;
                case Sound.Research:
                    clip = Make("research", 0.55f, (t, i) => (Tone(784f, t) * Env(t, 0.005f, 0.18f) +
                                                              Tone(988f, t - 0.1f) * Env(t - 0.1f, 0.005f, 0.18f) +
                                                              Tone(1318f, t - 0.2f) * Env(t - 0.2f, 0.005f, 0.33f)) * 0.38f);
                    break;
                case Sound.Deny:
                    clip = Make("deny", 0.2f, (t, i) => Square(t < 0.09f ? 220f : 165f, t) * Env(t, 0.005f, 0.2f) * 0.18f);
                    break;
                case Sound.Hit:
                    clip = Make("hit", 0.14f, (t, i) => (Noise() * Env(t, 0.001f, 0.05f) * 0.6f +
                                                         Tone(170f, t) * Env(t, 0.002f, 0.13f) * 0.7f) * 0.6f);
                    break;
                case Sound.Dig:
                    clip = Make("dig", 0.2f, (t, i) => Noise() * Env(t, 0.01f, 0.2f) * (0.6f + 0.4f * Mathf.Sin(t * 180f)) * 0.45f);
                    break;
                case Sound.Shutter:
                    clip = Make("shutter", 0.16f, (t, i) => Noise() * (Env(t, 0.001f, 0.03f) + Env(t - 0.09f, 0.001f, 0.05f)) * 0.55f);
                    break;
                case Sound.Radio:
                    clip = Make("radio", 0.3f, (t, i) => Tone(1200f, t) * (Env(t, 0.005f, 0.09f) + Env(t - 0.15f, 0.005f, 0.12f)) * 0.3f);
                    break;
                case Sound.Click:
                    clip = Make("click", 0.04f, (t, i) => Noise() * Env(t, 0.0005f, 0.02f) * 0.5f + Tone(2400f, t) * Env(t, 0.001f, 0.03f) * 0.2f);
                    break;
                default:
                    clip = Make("hiss", 0.5f, (t, i) => Noise() * Env(t, 0.03f, 0.5f) * 0.25f);
                    break;
            }
            sounds[sound] = clip;
            return clip;
        }

        private AudioClip LoopClip(Loop loop)
        {
            AudioClip clip;
            if (loops.TryGetValue(loop, out clip)) return clip;
            const float Length = 0.5f;
            switch (loop)
            {
                case Loop.Drill:
                    clip = Make("drill loop", Length, (t, i) => (Saw(120f, t) * 0.35f + Saw(240f, t) * 0.15f + Noise() * 0.12f) *
                                                                 (0.8f + 0.2f * Mathf.Sin(t * Mathf.PI * 2f * 16f)), true);
                    break;
                case Loop.Sweep:
                    clip = Make("sweep loop", Length, (t, i) => Noise() * (0.25f + 0.25f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 4f))), true);
                    break;
                case Loop.Spray:
                    clip = Make("spray loop", Length, (t, i) => Noise() * 0.3f, true);
                    break;
                default:
                    clip = Make("ratchet loop", Length, (t, i) => Noise() * Env(Mathf.Repeat(t, 0.125f), 0.001f, 0.03f) * 0.7f, true);
                    break;
            }
            loops[loop] = clip;
            return clip;
        }

        private delegate float Wave(float t, int i);

        private float lowpass;

        private AudioClip Make(string name, float seconds, Wave wave, bool smoothNoise = false)
        {
            int count = Mathf.CeilToInt(seconds * Rate);
            float[] data = new float[count];
            lowpass = 0f;
            for (int i = 0; i < count; i++)
            {
                float sample = Mathf.Clamp(wave(i / (float)Rate, i), -1f, 1f);
                if (smoothNoise)
                {
                    lowpass += (sample - lowpass) * 0.35f; // soften hiss so loops are not harsh
                    sample = lowpass;
                }
                data[i] = sample;
            }
            if (smoothNoise)
            {
                // Short crossfade at the ends so the loop has no click.
                int fade = Mathf.Min(400, count / 4);
                for (int i = 0; i < fade; i++)
                {
                    float k = i / (float)fade;
                    data[i] *= k;
                    data[count - 1 - i] *= k;
                }
            }
            AudioClip clip = AudioClip.Create(name, count, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Tone(float hz, float t)
        {
            return t < 0f ? 0f : Mathf.Sin(2f * Mathf.PI * hz * t);
        }

        private static float Square(float hz, float t)
        {
            return Mathf.Sign(Mathf.Sin(2f * Mathf.PI * hz * t));
        }

        private static float Saw(float hz, float t)
        {
            return 2f * Mathf.Repeat(hz * t, 1f) - 1f;
        }

        private float Noise()
        {
            return (float)random.NextDouble() * 2f - 1f;
        }

        /// <summary>Attack then exponential-ish decay; zero before t = 0.</summary>
        private static float Env(float t, float attack, float length)
        {
            if (t < 0f || t > length) return 0f;
            if (t < attack) return t / attack;
            float k = (t - attack) / Mathf.Max(1e-4f, length - attack);
            return (1f - k) * (1f - k);
        }
    }
}
