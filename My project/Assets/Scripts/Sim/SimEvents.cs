using System;
using MarsTrainer.Sim.Events;

namespace MarsTrainer.Sim
{
    // Instance-owned bus; no global subscribers or Unity dependency.
    public sealed class SimEvents
    {
        public event Action<WeatherEvent> WeatherWarned;
        public event Action<WeatherEvent> WeatherStarted;
        public event Action<WeatherEvent> WeatherPeaked;
        public event Action<WeatherEvent> WeatherEnded;

        public void ReportWeatherWarning(WeatherEvent weather) { WeatherWarned?.Invoke(weather); }
        public void ReportWeatherStarted(WeatherEvent weather)
        {
            WeatherStarted?.Invoke(weather);
            StartEvent(weather.Id);
        }
        public void ReportWeatherPeak(WeatherEvent weather) { WeatherPeaked?.Invoke(weather); }
        public void ReportWeatherEnded(WeatherEvent weather)
        {
            WeatherEnded?.Invoke(weather);
            EndEvent(weather.Id);
        }

        public event Action<SuitShieldState> SuitShieldChanged;
        public event Action<double, double> RadiationUpdated;
        public event Action<int, int> FlareAlert;
        public event Action RecallLaunched;

        public void ReportFlareAlert(int sol, int level) { FlareAlert?.Invoke(sol, level); }
        internal void ReportShieldState(SuitShieldState state) { SuitShieldChanged?.Invoke(state); }
        internal void ReportRadiation(double rate, double total) { RadiationUpdated?.Invoke(rate, total); }
        internal void ReportRecall() { RecallLaunched?.Invoke(); }
        public event Action<int> FarmPlotChanged;
        public event Action<int, SpaceSnack> FarmHarvested;
        internal void ReportFarmPlot(int index) { FarmPlotChanged?.Invoke(index); }
        internal void ReportHarvest(int index, SpaceSnack snack) { FarmHarvested?.Invoke(index, snack); }
        public event Action<EnergyReport> EnergyReported;
        public event Action Brownout;
        public event Action<int> SolStarted;
        public event Action<string, double> StatCritical;
        public event Action<string> EventStarted;
        public event Action<string> EventEnded;
        public event Action PlayerDied;
        public event Action MissionComplete;

        public void StartEvent(string eventId) { EventStarted?.Invoke(eventId); }
        public void EndEvent(string eventId) { EventEnded?.Invoke(eventId); }
        internal void ReportEnergy(EnergyReport report) { EnergyReported?.Invoke(report); }
        internal void ReportBrownout() { Brownout?.Invoke(); }
        internal void StartSol(int sol) { SolStarted?.Invoke(sol); }
        internal void ReportCritical(string statId, double value) { StatCritical?.Invoke(statId, value); }
        internal void ReportDeath() { PlayerDied?.Invoke(); }
        internal void CompleteMission() { MissionComplete?.Invoke(); }
    }
}
