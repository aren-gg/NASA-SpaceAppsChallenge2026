using System.Reflection;
using MarsTrainer.Gameplay.Weather;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MarsTrainer.Tests
{
    public sealed class WeatherPresenterTests
    {
        [Test]
        public void LateBindingRestoresHazardsAndUsesClockWithoutAdvancingIt()
        {
            var host = new GameObject("P11 late binding");
            try
            {
                var sim = new Simulation();
                var weather = new WeatherSystem(sim.Clock,
                    _ => new WeatherConditions(0, 3, -80, -20, 0, 3, true),
                    2, sim.Modifiers, sim.Events);
                sim.RegisterSystem(weather);
                sim.Tick(sim.Clock.SecondsPerSol / 2);
                var presenter = host.AddComponent<WeatherPresenter>();
                presenter.Initialize(sim);
                Assert.That(presenter.SolHour, Is.EqualTo(12).Within(.001));
                Assert.That(presenter.DustStrength, Is.GreaterThan(0));
                Assert.That(presenter.FlareStrength, Is.GreaterThan(0));
                double before = sim.Clock.ElapsedSols;
                sim.Clock.Paused = true;
                typeof(WeatherPresenter).GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(presenter, null);
                Assert.That(sim.Clock.ElapsedSols, Is.EqualTo(before));
                presenter.Initialize(sim);
                Assert.That(presenter.DustStrength, Is.GreaterThan(0));
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void OverlappingWeatherEndsIndependentlyAndDisableClearsVisualState()
        {
            var host = new GameObject("P11 hooks");
            try
            {
                var sim = new Simulation();
                var presenter = host.AddComponent<WeatherPresenter>();
                presenter.Initialize(sim.Events);
                var weather = new WeatherSystem(sim.Clock,
                    _ => new WeatherConditions(0, 3, -80, -20, 0, 3, true),
                    2, sim.Modifiers, sim.Events);
                var dust = weather.Events[0];
                var flare = weather.Events[1];
                sim.Events.ReportWeatherStarted(dust);
                sim.Events.ReportWeatherStarted(flare);
                Assert.That(presenter.DustStrength, Is.GreaterThan(0));
                Assert.That(presenter.FlareStrength, Is.GreaterThan(0));
                sim.Events.ReportWeatherEnded(flare);
                Assert.That(presenter.FlareStrength, Is.Zero);
                Assert.That(presenter.DustStrength, Is.GreaterThan(0));
                typeof(WeatherPresenter).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(presenter, null);
                Assert.That(presenter.DustStrength, Is.Zero);
                typeof(WeatherPresenter).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(presenter, null);
                sim.Events.ReportWeatherStarted(flare);
                Assert.That(presenter.FlareStrength, Is.GreaterThan(0));
                Assert.That(presenter.DustStrength, Is.Zero);
            }
            finally { Object.DestroyImmediate(host); }
        }

        [Test]
        public void ForwardsTypeAndSeverityAndRebindsWithoutDuplicateSubscriptions()
        {
            var host = new GameObject("weather hooks");
            try
            {
                var sim = new Simulation();
                var presenter = host.AddComponent<WeatherPresenter>();
                string type = null;
                int severity = 0, calls = 0;
                presenter.OnEventStarted += (kind, level) => { type = kind; severity = level; calls++; };
                presenter.Initialize(sim.Events);
                presenter.Initialize(sim.Events);
                var weather = new WeatherSystem(sim.Clock,
                    _ => new WeatherConditions(0, 0.35, -80, -20, 0, 2, true),
                    2, sim.Modifiers, sim.Events);
                sim.RegisterSystem(weather);
                sim.Tick(sim.Clock.SecondsPerSol / 4);
                Assert.That(type, Is.EqualTo("solar-flare"));
                Assert.That(severity, Is.EqualTo(2));
                Assert.That(calls, Is.EqualTo(1));
                typeof(WeatherPresenter).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(presenter, null);
                sim.Events.ReportWeatherStarted(weather.Events[0]);
                Assert.That(calls, Is.EqualTo(1));
                typeof(WeatherPresenter).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(presenter, null);
                sim.Events.ReportWeatherStarted(weather.Events[0]);
                Assert.That(calls, Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(host); }
        }
    }
}
