using Newtonsoft.Json;

namespace MarsTrainer.Data
{
    [JsonObject(ItemRequired = Required.Always)]
    public sealed class CropDto
    {
        public string id;
        public string name;
        public int daysToHarvest;
        public float kcalPerHarvest;
        public float parSat;
        public float waterLPerDay;
        public float co2Ppm;
        public float lightHours;
        public bool needsVerification;
    }
}
