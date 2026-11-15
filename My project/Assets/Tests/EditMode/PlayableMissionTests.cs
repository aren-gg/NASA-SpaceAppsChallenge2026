using System;
using MarsTrainer.Data;
using MarsTrainer.Gameplay.Radiation;
using MarsTrainer.Gameplay.World;
using MarsTrainer.Gameplay.Farm;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Research;
using NUnit.Framework;
using UnityEngine;

namespace MarsTrainer.Tests
{
    public sealed class PlayableMissionTests
    {
        private GameObject owner;
        [TearDown] public void Cleanup() { if (owner != null) UnityEngine.Object.DestroyImmediate(owner); }

        [Test]
        public void MissionElevationReadsNorthFirstSignedSamplesAndInterpolates()
        {
            var field = new MissionElevation(new byte[] { 0, 0, 100, 0, 156, 255, 0, 0 },
                "{\"width\":2,\"height\":2,\"minElevationM\":-100,\"maxElevationM\":100}");
            Assert.That(field.Sample(0, 0), Is.EqualTo(0));
            Assert.That(field.Sample(1, 1), Is.EqualTo(1));
            Assert.That(field.Sample(.5f, .5f), Is.EqualTo(.5f));
            Assert.That(field.Sample(-1, -1), Is.EqualTo(0));
            Assert.Throws<ArgumentException>(() => new MissionElevation(new byte[1],
                "{\"width\":2,\"height\":2,\"minElevationM\":-100,\"maxElevationM\":100}"));
        }

        [Test]
        public void FarmViewBorrowsMissionInventoryAndDoesNotRegisterAnotherFarm()
        {
            owner = new GameObject("Shared greenhouse");
            var mission = new MarsTrainer.Core.MissionSession(new DataRepository(), 42);
            int systems = mission.Simulation.Systems.Count;
            var view = owner.AddComponent<FarmInteract>(); view.Bind(mission.Simulation, mission.Farm);
            mission.TendFarm();
            Assert.That(view.Farm, Is.SameAs(mission.Farm));
            Assert.That(view.Farm.Inventory, Is.SameAs(mission.Inventory));
            view.enabled = false; view.enabled = true;
            Assert.That(mission.Simulation.Systems.Count, Is.EqualTo(systems));
            Assert.That(view.Farm.Plots[0].Crop, Is.Not.Null);
        }

        [Test]
        public void PlayableWorldConnectsRecallToItsShelters()
        {
            owner = new GameObject("Playable mission");
            var world = owner.AddComponent<TerrainGenerator>();
            var mission = new MarsTrainer.Core.MissionSession(new DataRepository(), 42);
            world.Build(mission.Simulation, mission.Research, 42);
            Assert.That(owner.GetComponentInChildren<RecallDrone>(), Is.Not.Null,
                "The HUD promises a recall drone, so the playable world must compose one.");
        }
    }
}
