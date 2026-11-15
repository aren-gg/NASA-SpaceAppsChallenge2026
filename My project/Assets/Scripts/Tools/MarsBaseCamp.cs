using System.Collections.Generic;
using UnityEngine;

namespace MarsSurvival.Tools
{
    /// <summary>
    /// A small stand-in camp the tools work on: three solar panels charging a battery
    /// cabinet, two crop beds, and a supply crate with an oxygen tank (sample drop-off and
    /// O2 refills). Simulates charging, dust on the panels and weather damage so the
    /// panel duster, field wrench, shovel and sprayer have jobs to do.
    ///
    /// Numbers are deliberately simple and live here, so the real energy and farming
    /// systems can replace them without changing the tools.
    /// </summary>
    public sealed class MarsBaseCamp : MonoBehaviour
    {
        [Header("Energy (stand-in values)")]
        public float baseLoadWatts = 1200f;
        [Tooltip("Real seconds of a full-strength dust storm to coat a clean panel.")]
        public float stormDustSeconds = 80f;
        [Tooltip("Real seconds of clear weather to coat a clean panel.")]
        public float clearDustSeconds = 2400f;
        [Tooltip("Real seconds between checks for heat-storm and cold-night damage.")]
        public float damageCheckSeconds = 25f;

        public readonly List<MarsSolarPanel> Panels = new List<MarsSolarPanel>();
        public readonly List<MarsCropBed> Beds = new List<MarsCropBed>();
        public MarsBatteryCabinet Battery { get; private set; }
        public MarsSupplyCrate Crate { get; private set; }

        public float SolarWatts { get; private set; }
        public float LoadWatts { get { return baseLoadWatts; } }

        private MarsExpedition expedition;
        private float damageTimer;
        private bool lowBatteryWarned;

        public void Build(MarsExpedition owner, Vector3 centre, Quaternion rotation)
        {
            expedition = owner;
            centre.y = owner.Terrain.GetGroundY(centre);
            transform.SetPositionAndRotation(centre, rotation);

            Crate = Place<MarsSupplyCrate>("Supply Crate", new Vector3(0f, 0f, 0f));
            Crate.Build();
            for (int i = 0; i < 3; i++)
            {
                MarsSolarPanel panel = Place<MarsSolarPanel>("Solar Panel " + (i + 1), new Vector3(5f, 0f, -2.4f + i * 2.4f));
                panel.Build();
                Panels.Add(panel);
            }
            Battery = Place<MarsBatteryCabinet>("Battery", new Vector3(2.4f, 0f, -3.6f));
            Battery.Build();
            for (int i = 0; i < 2; i++)
            {
                MarsCropBed bed = Place<MarsCropBed>("Crop Bed " + (i + 1), new Vector3(-3.6f, 0f, -1.4f + i * 2.8f));
                bed.Build(owner);
                Beds.Add(bed);
            }
            Physics.SyncTransforms();
        }

        /// <summary>Creates a camp object at a local offset (metres), on the ground, facing the camp centre.</summary>
        private T Place<T>(string name, Vector3 localMeters) where T : Component
        {
            float units = expedition.Units(1f);
            Vector3 position = transform.position + transform.rotation * (localMeters * units);
            position.y = expedition.Terrain.GetGroundY(position);
            Vector3 toCentre = transform.position - position;
            toCentre.y = 0f;
            Quaternion facing = toCentre.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(toCentre, Vector3.up) : transform.rotation;

            GameObject go = new GameObject(name);
            go.transform.SetParent(transform, true);
            go.transform.SetPositionAndRotation(position, facing);
            go.transform.localScale = Vector3.one * units;
            return go.AddComponent<T>();
        }

        private void Update()
        {
            if (expedition == null) return;
            float dt = Time.deltaTime;
            float daylight = expedition.Daylight;
            float airborne = expedition.DustAmount;

            // Panels: output falls with dust on the panel, dust in the air and damage.
            SolarWatts = 0f;
            float dustRate = airborne / Mathf.Max(1f, stormDustSeconds) + 1f / Mathf.Max(1f, clearDustSeconds);
            for (int i = 0; i < Panels.Count; i++)
            {
                MarsSolarPanel panel = Panels[i];
                panel.Dust = Mathf.Clamp01(panel.Dust + dustRate * dt);
                panel.OutputWatts = MarsSolarPanel.PeakWatts * daylight * (1f - 0.85f * panel.Dust)
                                    * (1f - 0.6f * airborne) * (panel.Damaged ? 0f : 1f);
                SolarWatts += panel.OutputWatts;
            }

            // Battery: net power over Mars time (the clock runs faster than real time).
            Battery.Charge((SolarWatts - LoadWatts) * expedition.MarsHoursThisFrame / 1000f);
            if (Battery.Fraction < 0.2f && !lowBatteryWarned)
            {
                lowBatteryWarned = true;
                expedition.Say("Base: The battery is almost empty! Clean and fix the solar panels.", MarsExpedition.Tone.Warning);
            }
            else if (Battery.Fraction > 0.3f) lowBatteryWarned = false;

            // Heat storms and cold nights can break equipment.
            float harsh = Mathf.Max(expedition.HeatAmount, expedition.ColdAmount);
            damageTimer += dt;
            if (damageTimer >= damageCheckSeconds)
            {
                damageTimer = 0f;
                if (harsh > 0.3f && Random.value < 0.35f * harsh) DamageSomething(expedition.HeatAmount >= expedition.ColdAmount);
            }
        }

        private void DamageSomething(bool heat)
        {
            List<int> candidates = new List<int>();
            for (int i = 0; i < Panels.Count; i++) if (!Panels[i].Damaged) candidates.Add(i);
            if (!Battery.Damaged) candidates.Add(-1);
            if (candidates.Count == 0) return;
            int pick = candidates[Random.Range(0, candidates.Count)];
            string cause = heat ? "The heat storm" : "The cold night";
            string wrench = MarsTool.Named("power_wrench", "Field Wrench");
            if (pick < 0)
            {
                Battery.Damaged = true;
                expedition.Say("Base: " + cause + " broke the battery! Fix it with " + wrench + ".", MarsExpedition.Tone.Warning);
            }
            else
            {
                Panels[pick].Damaged = true;
                expedition.Say("Base: " + cause + " broke " + Panels[pick].name + "! Fix it with " + wrench + ".", MarsExpedition.Tone.Warning);
            }
            MarsSfx.Play(MarsSfx.Sound.Deny);
        }
    }
}
