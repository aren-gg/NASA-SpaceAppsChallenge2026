using System;
using System.IO;
using MarsTrainer.Data;
using UnityEngine;

namespace MarsSurvival.Simulation
{
    /// <summary>
    /// Loads the MarsTrainer bundled data (StreamingAssets, prepared offline by tools/*.py) for the outpost mission:
    /// <list type="bullet">
    /// <item>mars_year_schedule.json: 668 sols of daily temperature range (Curiosity REMS / InSight), dust opacity,
    /// GCR dose (Curiosity RAD) and flare level (NASA DONKI), used by <see cref="MissionEnvironment"/>.</item>
    /// </list>
    /// Missing or invalid files fall back to the repository defaults with a warning; nothing is fetched at runtime.
    /// </summary>
    public static class MissionDataSources
    {
        public const string ScheduleFile = "mars_year_schedule.json";

        private static DataRepository repository;
        private static bool scheduleFromFile;

        public static DataRepository Repository
        {
            get
            {
                if (repository == null) Load();
                return repository;
            }
        }

        public static string ScheduleSourceLabel
        {
            get
            {
                if (repository == null) Load();
                return scheduleFromFile ? "MarsTrainer seasonal schedule" : "MarsTrainer default schedule (bundled file missing)";
            }
        }

        public static MissionEnvironment.SeasonDay[] Seasons()
        {
            var rows = Repository.Schedule;
            var days = new MissionEnvironment.SeasonDay[rows.Count];
            for (int i = 0; i < days.Length; i++)
            {
                ScheduleDto row = rows[i];
                days[i] = new MissionEnvironment.SeasonDay
                {
                    minC = row.minTemp, maxC = row.maxTemp, dustOpacity = row.dustOpacity,
                    gcrMicrograyPerDay = row.gcrDose_uGy, flareLevel = row.flareLevel,
                    measured = !string.Equals(row.source, "synthetic", StringComparison.OrdinalIgnoreCase),
                };
            }
            return days;
        }

        public static void Load()
        {
            repository = new DataRepository(message => Debug.LogWarning("Mission data: " + message));
            scheduleFromFile = Read(ScheduleFile, repository.LoadScheduleJson);
        }

        // Desktop builds and the editor can read StreamingAssets directly; the repository validates the rows.
        private static bool Read(string file, Action<string> receive)
        {
            string path = Path.Combine(Application.streamingAssetsPath, file);
            try
            {
                if (!File.Exists(path)) { receive(null); return false; }
                receive(File.ReadAllText(path));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("Mission data: could not read " + file + ": " + exception.Message);
                receive(null);
                return false;
            }
        }
    }
}
