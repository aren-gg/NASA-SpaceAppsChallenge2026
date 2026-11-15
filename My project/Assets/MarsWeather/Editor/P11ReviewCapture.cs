using System.IO;
using MarsWeather;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace MarsTrainer.Look.Editor
{
    [InitializeOnLoad]
    public static class P11ReviewCapture
    {
        private const string Key = "P11.Capture";
        private static int index, frames, maxDraws, lastFrame;
        private static double next, stateStart;
        private static readonly WeatherEvent[] States = { WeatherEvent.Clear, WeatherEvent.DustStorm, WeatherEvent.SolarFlare, WeatherEvent.ColdNight };
        private static readonly string[] Names = { "day", "storm", "flare", "cold-night" };
        static P11ReviewCapture() { EditorApplication.update += Step; }
        [MenuItem("Mars/P11/Capture Review")]
        public static void Begin()
        {
            SessionState.SetBool(Key, true);
            SessionState.SetBool(Key + ".started", false);
            EditorApplication.isPlaying = true;
        }
        private static void Step()
        {
            if (!SessionState.GetBool(Key, false) || !EditorApplication.isPlaying || EditorApplication.isCompiling) return;
            var weather = Object.FindObjectOfType<WeatherController>();
            if (weather == null || Camera.main == null) return;
            if (!SessionState.GetBool(Key + ".started", false))
            {
                SessionState.SetBool(Key + ".started", true);
                index = frames = maxDraws = 0;
                EditorWindow.GetWindow(System.Type.GetType("UnityEditor.GameView,UnityEditor")).Focus();
                Directory.CreateDirectory(P11Verification.Output);
                File.WriteAllText(Path.Combine(P11Verification.Output, "performance.txt"), "Unity Editor review, 1600x900 offscreen captures. Frame rate includes editor overhead; verify 60 fps on target player hardware.\n");
                SetState(weather);
            }
            if (Time.renderedFrameCount == lastFrame) return;
            lastFrame = Time.renderedFrameCount;
            frames++;
            maxDraws = Mathf.Max(maxDraws, UnityStats.drawCalls);
            if (EditorApplication.timeSinceStartup < next || frames < 60) return;
            Capture(Camera.main, Names[index]);
            File.AppendAllText(Path.Combine(P11Verification.Output, "performance.txt"),
                $"{Names[index]}: editor max draws={maxDraws}, mean frame={(EditorApplication.timeSinceStartup - stateStart) / frames * 1000:F2}ms, particles={weather.ParticleCount}\n");
            index++;
            if (index == States.Length)
            {
                SessionState.SetBool(Key, false); SessionState.SetBool(Key + ".started", false);
                EditorApplication.isPlaying = false;
                Debug.Log("P11_REVIEW_CAPTURED " + P11Verification.Output);
                return;
            }
            frames = maxDraws = 0; SetState(weather);
        }
        private static void SetState(WeatherController weather)
        {
            weather.transitionSeconds = 0;
            weather.Activate(States[index]);
            stateStart = EditorApplication.timeSinceStartup;
            next = stateStart + 3;
        }
        private static void Capture(Camera camera, string name)
        {
            var rt = RenderTexture.GetTemporary(1600, 900, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var previousTarget = camera.targetTexture;
            camera.targetTexture = rt;
            var canvases = Object.FindObjectsOfType<Canvas>();
            var modes = new RenderMode[canvases.Length]; var cameras = new Camera[canvases.Length];
            for (int i = 0; i < canvases.Length; i++)
            {
                modes[i] = canvases[i].renderMode; cameras[i] = canvases[i].worldCamera;
                if (modes[i] != RenderMode.ScreenSpaceOverlay) continue;
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera; canvases[i].worldCamera = camera;
                canvases[i].planeDistance = camera.nearClipPlane + .1f;
            }
            var previous = RenderTexture.active;
            try
            {
                Canvas.ForceUpdateCanvases();
                var request = new UniversalRenderPipeline.SingleCameraRequest { destination = rt };
                RenderPipeline.SubmitRenderRequest(camera, request);
                RenderTexture.active = rt;
                var png = new Texture2D(rt.width, rt.height, TextureFormat.RGB24, false);
                png.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0); png.Apply();
                File.WriteAllBytes(Path.Combine(P11Verification.Output, name + ".png"), png.EncodeToPNG()); Object.DestroyImmediate(png);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt);
                for (int i = 0; i < canvases.Length; i++) { canvases[i].renderMode = modes[i]; canvases[i].worldCamera = cameras[i]; }
            }
        }
    }
}
