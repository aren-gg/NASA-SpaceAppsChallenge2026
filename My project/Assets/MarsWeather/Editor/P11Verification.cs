using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace MarsTrainer.Look.Editor
{
    // Local editor automation only; requests never enter a player build.
    [InitializeOnLoad]
    public static class P11Verification
    {
        private static TestRunnerApi runner;
        public static readonly string Output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../P11Review"));
        static P11Verification() { EditorApplication.update += Poll; }
        private static void Poll()
        {
            const string request = "Temp/P11.request";
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || !File.Exists(request)) return;
            string command = File.ReadAllText(request).Trim();
            File.Delete(request);
            if (command == "refresh") { AssetDatabase.Refresh(); return; }
            if (command == "tests" || command == "focused") RunTests(command == "focused");
            else EditorApplication.ExecuteMenuItem("Mars/P11/" + command);
        }
        [MenuItem("Mars/P11/Run EditMode Tests")]
        public static void RunAll() { RunTests(false); }
        private static void RunTests(bool focused)
        {
            Directory.CreateDirectory(Output);
            runner = ScriptableObject.CreateInstance<TestRunnerApi>();
            runner.RegisterCallbacks(new Results());
            var filter = new Filter { testMode = TestMode.EditMode };
            if (focused) filter.testNames = new[] { "MarsTrainer.Tests.WeatherPresenterTests" };
            runner.Execute(new ExecutionSettings(filter));
        }
        [MenuItem("Mars/P11/Build Review Player")]
        public static void BuildPlayer()
        {
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/MarsWeather/Scenes/P11LookReview.unity" },
                locationPathName = "/tmp/P11ReviewPlayer.app",
                target = BuildTarget.StandaloneOSX,
                options = BuildOptions.Development
            });
            File.WriteAllText(Path.Combine(Output, "build.txt"), report.summary.result + " errors=" + report.summary.totalErrors);
        }

        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor tests) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                File.WriteAllText(Path.Combine(Output, "tests.xml"), result.ToXml().OuterXml);
                Debug.Log($"P11_TESTS passed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount}");
                runner.UnregisterCallbacks(this);
                Object.DestroyImmediate(runner);
            }
        }
    }
}
