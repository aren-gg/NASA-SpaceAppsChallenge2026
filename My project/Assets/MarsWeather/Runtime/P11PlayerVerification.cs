#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.IO;
using Unity.Profiling;
using UnityEngine;
using MarsWeather;

namespace MarsTrainer.Look
{
    /// <summary>Opt-in development-player acceptance capture. Never runs in a normal game.</summary>
    public sealed class P11PlayerVerification : MonoBehaviour
    {
        private string output;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void LaunchIfRequested()
        {
            if (Application.isEditor) return;
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i + 1 < args.Length; i++)
                if (args[i] == "-p11ReviewOutput")
                {
                    var capture = new GameObject("P11 player verification").AddComponent<P11PlayerVerification>();
                    capture.output = args[i + 1]; return;
                }
        }
        private IEnumerator Start()
        {
            Directory.CreateDirectory(output);
            var weather = FindObjectOfType<WeatherController>();
            if (weather == null) { File.WriteAllText(Path.Combine(output, "player-error.txt"), "Weather controller missing"); Application.Quit(1); yield break; }
            QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
            weather.transitionSeconds = 0;
            var states = new[] { WeatherEvent.Clear, WeatherEvent.DustStorm, WeatherEvent.SolarFlare, WeatherEvent.ColdNight };
            var names = new[] { "day", "storm", "flare", "cold-night" };
            var intervals = new float[16384];
            string report = "macOS development player, 1600x900, VSync off, uncapped. 2s warmup and 3s sample per state.\n";
            using (var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count"))
            {
                for (int state = 0; state < states.Length; state++)
                {
                    weather.Activate(states[state]);
                    yield return new WaitForSecondsRealtime(2);
                    double start = Time.realtimeSinceStartupAsDouble, end = start + 3;
                    int count = 0; long maxDraws = 0;
                    while (Time.realtimeSinceStartupAsDouble < end)
                    {
                        yield return null;
                        if (count < intervals.Length) intervals[count++] = Time.unscaledDeltaTime;
                        maxDraws = Math.Max(maxDraws, draws.LastValue);
                    }
                    Array.Sort(intervals, 0, count);
                    double mean = (Time.realtimeSinceStartupAsDouble - start) * 1000 / count;
                    float p95 = intervals[Math.Min(count - 1, (int)(count * .95))] * 1000;
                    report += $"{names[state]}: max draws={maxDraws}, mean={mean:F2}ms, p95={p95:F2}ms, mean fps={1000 / mean:F1}, particles={weather.ParticleCount}\n";
                    File.WriteAllText(Path.Combine(output, "player-performance.txt"), report);
                    yield return new WaitForEndOfFrame();
                    ScreenCapture.CaptureScreenshot(Path.Combine(output, names[state] + ".png"));
                    yield return new WaitForSecondsRealtime(.3f);
                }
            }
            File.WriteAllText(Path.Combine(output, "player-complete.txt"), "Four player frames captured.");
            Application.Quit();
        }
    }
}
#endif
