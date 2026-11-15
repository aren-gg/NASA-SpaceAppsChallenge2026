using System;
using MarsTrainer.Sim;

namespace MarsTrainer.Gameplay.Radiation
{
    public sealed class SunZapMeterModel : IDisposable
    {
        private readonly GameState state;
        private readonly SimEvents events;
        public double DoseRateMSvPerSol { get; private set; }
        public double CumulativeMSv => state.RadiationDoseMSv;
        public SunZapMeterModel(GameState state, SimEvents events)
        {
            this.state = state ?? throw new ArgumentNullException(nameof(state));
            this.events = events ?? throw new ArgumentNullException(nameof(events));
            events.RadiationUpdated += OnRadiation;
        }
        private void OnRadiation(double rate, double total) { DoseRateMSvPerSol = rate; }
        public void Dispose() { events.RadiationUpdated -= OnRadiation; }
    }
}
