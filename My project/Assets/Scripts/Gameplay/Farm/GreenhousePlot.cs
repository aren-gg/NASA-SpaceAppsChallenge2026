using System;
using MarsTrainer.Sim;
using UnityEngine;
using UnityEngine.Events;

namespace MarsTrainer.Gameplay.Farm
{
    // Bind Changed to plot visuals/UI; no sim state or frame polling lives here.
    public sealed class GreenhousePlot : MonoBehaviour
    {
        private FarmInteract farm;
        private SimEvents events;
        private int index;
        private bool subscribed;
        public UnityEvent Changed = new UnityEvent();
        public double Growth => farm == null ? 0 : farm.Farm.Plots[index].Growth;
        public bool Ready => farm != null && farm.Farm.Plots[index].Ready;

        public void Initialize(FarmInteract farm, int plotIndex)
        {
            if (farm == null) throw new ArgumentNullException(nameof(farm));
            if (farm.Farm == null) throw new InvalidOperationException("Initialize the farm before binding plots.");
            if (plotIndex < 0 || plotIndex >= farm.Farm.Plots.Count) throw new ArgumentOutOfRangeException(nameof(plotIndex));
            Unsubscribe();
            this.farm = farm;
            events = farm.Events;
            index = plotIndex;
            if (isActiveAndEnabled) Subscribe();
            Changed.Invoke();
        }

        public bool Plant(string cropId) { return farm != null && farm.Plant(index, cropId); }
        public void Water(float litres) { if (farm != null) farm.Water(index, litres); }
        public bool Harvest() { return farm != null && farm.Harvest(index); }
        public void SetLamps(bool enabled) { if (farm != null) farm.SetLamps(index, enabled); }

        private void OnEnable() { Subscribe(); }
        private void OnDisable() { Unsubscribe(); }
        private void Subscribe()
        {
            if (events == null || subscribed) return;
            events.FarmPlotChanged += PlotChanged;
            subscribed = true;
        }
        private void Unsubscribe()
        {
            if (!subscribed) return;
            events.FarmPlotChanged -= PlotChanged;
            subscribed = false;
        }
        private void PlotChanged(int plotIndex) { if (plotIndex == index) Changed.Invoke(); }
    }
}
