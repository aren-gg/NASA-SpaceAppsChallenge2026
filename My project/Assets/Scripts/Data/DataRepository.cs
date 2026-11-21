using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using UnityEngine;

namespace MarsTrainer.Data
{
    public sealed class DataRepository
    {
        private readonly Action<string> warning;
        public IReadOnlyList<ScheduleDto> Schedule { get; private set; }

        public DataRepository(Action<string> warning = null)
        {
            this.warning = warning ?? (message => Debug.LogWarning(message));
            Schedule = Array.AsReadOnly(DataDefaults.Schedule());
        }

        public void LoadScheduleJson(string json)
        {
            var rows = Deserialize<ScheduleDto>(json);
            if (!DataValidation.Schedule(rows))
            {
                rows = DataDefaults.Schedule();
                warning("Schedule unavailable or invalid; using defaults.");
            }
            Schedule = Array.AsReadOnly(rows);
        }

        private static T[] Deserialize<T>(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;
            try { return JsonConvert.DeserializeObject<T[]>(json); }
            catch (JsonException) { return null; }
        }
    }
}
