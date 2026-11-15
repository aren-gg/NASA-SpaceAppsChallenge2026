using Newtonsoft.Json;

namespace MarsTrainer.Data
{
    [JsonObject(ItemRequired = Required.Always)]
    public sealed class ScheduleDto
    {
        public int sol;
        public float ls;
        public float minTemp;
        public float maxTemp;
        public float dustOpacity;
        public float gcrDose_uGy;
        public int flareLevel;
        public bool auroraAlert;
        public string source;
    }
}
