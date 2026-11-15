using System;
using System.Collections.Generic;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Events;
using MarsTrainer.Sim.Systems;
using UnityEngine;

namespace MarsTrainer.Gameplay.Weather
{
    public sealed class WeatherPresenter : MonoBehaviour
    {
        private readonly List<WeatherEvent> active = new List<WeatherEvent>();
        private readonly List<WeatherEvent> warnings = new List<WeatherEvent>();
        public MarsWeather.WeatherController visuals;
        public Renderer suitShell;
        public Transform viewer;
        private SimEvents events;
        private Simulation simulation;
        private bool subscribed;
        public event Action<string, int> OnEventStarted;
        public float DustStrength => Strength("dust-storm");
        public float FlareStrength => Strength("solar-flare");
        public float HeatStrength => Strength("heat-storm");
        public float ColdStrength => Strength("cold-night");
        public float SolHour => simulation == null ? GameConstants.LookPreviewHour
            : (float)((simulation.Clock.ElapsedSols % 1) * GameConstants.LookHoursPerSol);
        public bool FlareWarning => HasFlareWarning();
        public SuitShieldState ShieldState { get; private set; }

        public void Initialize(SimEvents bus)
        {
            Unsubscribe();
            simulation = null;
            ShieldState = SuitShieldState.Safe;
            active.Clear(); warnings.Clear();
            events = bus ?? throw new ArgumentNullException(nameof(bus));
            if (isActiveAndEnabled) Subscribe();
        }
        public void Initialize(Simulation sim)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            Initialize(sim.Events);
            simulation = sim;
            Snapshot();
        }
        private void OnEnable() { Subscribe(); Snapshot(); }
        private void OnDisable()
        {
            Unsubscribe(); active.Clear(); warnings.Clear();
            if (visuals != null) visuals.ReleasePresentation();
        }
        private void Start() { if (visuals != null) visuals.EnablePostProcessing(Camera.main); }
        private void Update()
        {
            if (visuals == null) return;
            if (viewer != null) visuals.transform.position = viewer.position;
            visuals.SetPresentation(DustStrength, FlareStrength, HeatStrength, ColdStrength, SolHour, FlareWarning);
            if (suitShell == null || visuals.look == null) return;
            var look = visuals.look;
            Material material = ShieldState == SuitShieldState.Critical ? look.criticalShell
                : ShieldState == SuitShieldState.Danger ? look.dangerShell
                : ShieldState == SuitShieldState.Warn ? look.warnShell : look.safeShell;
            if (material != null && suitShell.sharedMaterial != material) suitShell.sharedMaterial = material;
        }
        private void Subscribe()
        {
            if (events == null || subscribed) return;
            events.WeatherStarted += Started;
            events.WeatherEnded += Ended;
            events.WeatherWarned += Warned;
            events.SuitShieldChanged += ShieldChanged;
            subscribed = true;
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            events.WeatherStarted -= Started;
            events.WeatherEnded -= Ended;
            events.WeatherWarned -= Warned;
            events.SuitShieldChanged -= ShieldChanged;
            subscribed = false;
        }
        private void Started(WeatherEvent weather)
        {
            if (!active.Contains(weather)) active.Add(weather);
            warnings.Remove(weather);
            OnEventStarted?.Invoke(weather.Type, weather.Severity);
        }
        private void Ended(WeatherEvent weather) { active.Remove(weather); warnings.Remove(weather); }
        private void Warned(WeatherEvent weather) { if (!warnings.Contains(weather)) warnings.Add(weather); }
        private void ShieldChanged(SuitShieldState state) { ShieldState = state; }
        private float Strength(string type)
        {
            float strength = 0;
            for (int i = 0; i < active.Count; i++)
            {
                var item = active[i];
                if (item.Type != type) continue;
                float ramp = 1;
                if (simulation != null)
                {
                    double now = simulation.Clock.ElapsedSols;
                    if (now < item.StartSol || now >= item.EndSol) continue;
                    ramp = (float)(now <= item.PeakSol
                        ? (now - item.StartSol) / (item.PeakSol - item.StartSol)
                        : (item.EndSol - now) / (item.EndSol - item.PeakSol));
                }
                strength = Mathf.Max(strength, Mathf.Clamp01(ramp) * item.Severity / GameConstants.MaxFlareLevel);
            }
            return strength;
        }
        private bool HasFlareWarning()
        {
            for (int i = 0; i < warnings.Count; i++)
                if (warnings[i].Type == "solar-flare") return true;
            return false;
        }
        private void Snapshot()
        {
            if (simulation == null) return;
            active.Clear(); warnings.Clear();
            double now = simulation.Clock.ElapsedSols;
            for (int i = 0; i < simulation.Systems.Count; i++)
            {
                if (simulation.Systems[i] is RadiationSystem radiation) ShieldState = radiation.ShieldState;
                if (!(simulation.Systems[i] is WeatherSystem weather)) continue;
                for (int j = 0; j < weather.Events.Count; j++)
                {
                    var item = weather.Events[j];
                    if (now >= item.StartSol && now < item.EndSol) active.Add(item);
                    else if (now >= item.WarningSol && now < item.StartSol) warnings.Add(item);
                }
            }
        }
    }
}
