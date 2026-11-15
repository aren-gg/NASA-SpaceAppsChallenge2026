using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using MarsTrainer.Sim;
using Newtonsoft.Json;
using UnityEngine;
using UnityEngine.Networking;

namespace MarsTrainer.Data
{
    public sealed class DataRepository
    {
        private readonly Action<string> warning;
        public IReadOnlyList<ScheduleDto> Schedule { get; private set; }
        public IReadOnlyList<CropDto> Crops { get; private set; }
        private bool scheduleFallback = true;
        private bool cropsFallback = true;
        public bool UsingFallback => scheduleFallback || cropsFallback;

        public DataRepository(Action<string> warning = null)
        {
            this.warning = warning ?? (message => Debug.LogWarning(message));
            Schedule = Array.AsReadOnly(DataDefaults.Schedule());
            Crops = Array.AsReadOnly(DataDefaults.Crops());
        }

        // Yield from a MonoBehaviour coroutine; the repository itself owns no GameObjects.
        public IEnumerator Load(string basePath = null)
        {
            string root = basePath ?? Application.streamingAssetsPath;
            yield return Read(root, "mars_year_schedule.json", LoadScheduleJson, basePath == null);
            yield return Read(root, "crop_parameters.json", LoadCropsJson, basePath == null);
        }

        public void LoadScheduleJson(string json)
        {
            var rows = Deserialize<ScheduleDto>(json);
            scheduleFallback = !DataValidation.Schedule(rows);
            if (scheduleFallback)
            {
                rows = DataDefaults.Schedule();
                warning("Schedule unavailable or invalid; using defaults.");
            }
            Schedule = Array.AsReadOnly(rows);
        }

        public void LoadCropsJson(string json)
        {
            var rows = Deserialize<CropDto>(json);
            cropsFallback = !DataValidation.Crops(rows);
            if (cropsFallback)
            {
                rows = DataDefaults.Crops();
                warning("Crops unavailable or invalid; using defaults.");
            }
            Crops = Array.AsReadOnly(rows);
        }

        private static T[] Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonConvert.DeserializeObject<T[]>(json); }
            catch (JsonException) { return null; }
        }

        private static IEnumerator Read(string root, string file, Action<string> receive, bool applicationRoot)
        {
            string url = LocalUrl(root, file, applicationRoot);
            if (url == null) { receive(null); yield break; }
            using (var request = UnityWebRequest.Get(url))
            {
                request.timeout = GameConstants.DataTimeoutSeconds;
                var operation = request.SendWebRequest();
                while (!operation.isDone) yield return null;
                receive(request.result == UnityWebRequest.Result.Success ? request.downloadHandler.text : null);
            }
        }

        private static string LocalUrl(string root, string file, bool applicationRoot)
        {
            // WebGL StreamingAssets may be HTTP-hosted; only the application-supplied
            // origin is accepted. Explicit remote basePath overrides are rejected.
            if (root.StartsWith("jar:file:", StringComparison.OrdinalIgnoreCase))
                return root.TrimEnd('/') + "/" + file;
            if (Uri.TryCreate(root, UriKind.Absolute, out var uri) && !uri.IsFile)
            {
                if (applicationRoot && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
                    return root.TrimEnd('/') + "/" + file;
                return null;
            }
            try
            {
                string path = uri != null && uri.IsFile ? uri.LocalPath : root;
                return new Uri(Path.Combine(Path.GetFullPath(path), file)).AbsoluteUri;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is NotSupportedException || exception is PathTooLongException)
            { return null; }
        }
    }
}
