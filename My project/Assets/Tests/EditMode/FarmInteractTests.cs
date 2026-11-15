using System.Reflection;
using MarsTrainer.Data;
using MarsTrainer.Gameplay.Farm;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;
using NUnit.Framework;
using UnityEngine;

namespace MarsTrainer.Tests
{
    public sealed class FarmInteractTests
    {
        [Test]
        public void PlotActionsUseLoadedCropsClockAndRealPowerStore()
        {
            var sim = new Simulation();
            var energy = new EnergySystem(sim.Clock, _ => new EnergyConditions(0, 0, 20),
                new SolarPanel(0, 1, 1), new Battery(100, 100, 100, 1, 100),
                new EnergyLoads(0, 0, 0, 0, 0, 20), sim.Modifiers, sim.Events);
            sim.RegisterSystem(energy);
            var owner = new GameObject("Farm");
            try
            {
                var interact = owner.AddComponent<FarmInteract>();
                interact.Initialize(sim, new DataRepository(), _ => new FarmConditions(0, 2, 1000, 20), energy);
                var plot = owner.AddComponent<GreenhousePlot>();
                plot.Initialize(interact, 0);
                int changes = 0;
                plot.Changed.AddListener(() => changes++);
                Assert.That(plot.Plant("mizuna"), Is.True);
                plot.Water(1000);
                plot.SetLamps(true);
                sim.Tick(sim.Clock.SecondsPerSol / 2);
                Assert.That(plot.Growth, Is.EqualTo(0.5 / 28).Within(1e-10));
                Assert.That(sim.State.EnergyKWh, Is.EqualTo(84).Within(1e-10));
                Assert.That(changes, Is.GreaterThan(0));
                Assert.That(plot.Harvest(), Is.False);
                // EditMode does not dispatch runtime MonoBehaviour lifecycle callbacks.
                typeof(FarmInteract).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(interact, null);
                typeof(GreenhousePlot).GetMethod("OnDisable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plot, null);
                sim.Tick(sim.Clock.SecondsPerSol / 2);
                Assert.That(plot.Growth, Is.EqualTo(0.5 / 28).Within(1e-10));
                typeof(FarmInteract).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(interact, null);
                typeof(GreenhousePlot).GetMethod("OnEnable", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(plot, null);
                sim.Tick(sim.Clock.SecondsPerSol / 2);
                Assert.That(plot.Growth, Is.EqualTo(1.0 / 28).Within(1e-10));
                Assert.That(interact.AddPlot(), Is.EqualTo(2));
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }
}
