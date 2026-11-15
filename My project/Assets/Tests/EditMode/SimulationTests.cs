using System;
using System.Collections.Generic;
using System.Diagnostics;
using MarsTrainer.Sim;
using NUnit.Framework;

namespace MarsTrainer.Tests
{
    public sealed class SimulationTests
    {
        [Test]
        public void ClockUsesRunDurationAndSupportsSpeedsAndPause()
        {
            var clock = new SimClock();
            Assert.That(clock.SecondsPerSol, Is.EqualTo(GameConstants.RunDurationSeconds / GameConstants.MarsYearSols));
            clock.Tick(clock.SecondsPerSol);
            Assert.That(clock.Sol, Is.EqualTo(1));
            clock.Speed = 2;
            clock.Tick(clock.SecondsPerSol);
            clock.Speed = 4;
            clock.Tick(clock.SecondsPerSol);
            Assert.That(clock.ElapsedSols, Is.EqualTo(7).Within(1e-10));
            clock.Paused = true;
            Assert.That(clock.Tick(100), Is.Zero);
            Assert.That(clock.Sol, Is.EqualTo(7));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Speed = 3);
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Tick(double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => clock.Tick(-1));
        }

        [Test]
        public void ClockClampsAtMissionEnd()
        {
            var clock = new SimClock();
            clock.Tick(double.MaxValue);
            Assert.That(clock.Sol, Is.EqualTo(GameConstants.MissionDurationDays));
            Assert.That(clock.ElapsedSols, Is.EqualTo(GameConstants.MissionDurationDays));
            Assert.That(clock.Tick(1), Is.Zero);
        }

        [Test]
        public void MissionWinsExactlyAtDay99AndStopsAdvancing()
        {
            var sim = new Simulation();
            int wins = 0;
            sim.Events.MissionComplete += () => wins++;
            sim.Tick(sim.Clock.SecondsPerSol * 98.5);
            Assert.That(wins, Is.Zero);
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(sim.Clock.ElapsedSols, Is.EqualTo(99));
            Assert.That(wins, Is.EqualTo(1));
            sim.Tick(sim.Clock.SecondsPerSol * 1000);
            Assert.That(sim.Clock.ElapsedSols, Is.EqualTo(99));
            Assert.That(wins, Is.EqualTo(1));
            var clock = new SimClock(); clock.Tick(double.MaxValue);
            Assert.That(clock.ElapsedSols, Is.EqualTo(99));
        }

        [Test]
        public void StateClampsVitalStats()
        {
            var state = new GameState { Health = -1, Hunger = 101, Water = -1, Tiredness = 200, Oxygen = 101 };
            Assert.That(state.Health, Is.Zero);
            Assert.That(state.Hunger, Is.EqualTo(100));
            Assert.That(state.Water, Is.Zero);
            Assert.That(state.Tiredness, Is.EqualTo(100));
            Assert.That(state.Oxygen, Is.EqualTo(100));
            Assert.That(state.Alive, Is.True);
        }

        [Test]
        public void ModifiersStackReplaceAndRemoveBySourceAndStat()
        {
            var modifiers = new SimModifiers();
            modifiers.Set("energy", "research", 2, 1.5);
            modifiers.Set("energy", "gear", 3, 2);
            Assert.That(modifiers.Apply("energy", 10), Is.EqualTo(45));
            Assert.That(modifiers.Apply("oxygen", 10), Is.EqualTo(10));
            modifiers.Set("energy", "gear", 1, 1);
            Assert.That(modifiers.Apply("energy", 10), Is.EqualTo(19.5));
            modifiers.Remove("energy", "research");
            Assert.That(modifiers.Apply("energy", 10), Is.EqualTo(11));
            modifiers.Remove("energy", "gear");
            Assert.That(modifiers.Apply("energy", 10), Is.EqualTo(10));
        }

        [Test]
        public void SystemsRunInStableOrderWithSolTimeAndDefaultSlots()
        {
            var sim = new Simulation();
            var calls = new List<int>();
            Assert.That(sim.Systems.Count, Is.EqualTo(5));
            Assert.That(sim.Systems[0].ToString(), Is.EqualTo("Energy"));
            Assert.That(sim.Systems[1].ToString(), Is.EqualTo("Radiation"));
            Assert.That(sim.Systems[2].ToString(), Is.EqualTo("Weather"));
            Assert.That(sim.Systems[3].ToString(), Is.EqualTo("Farm"));
            Assert.That(sim.Systems[4].ToString(), Is.EqualTo("Survival"));
            sim.RegisterSystem(new RecordingSystem(30, calls));
            sim.RegisterSystem(new RecordingSystem(10, calls));
            sim.RegisterSystem(new RecordingSystem(30, calls));
            sim.Tick(sim.Clock.SecondsPerSol / 2);
            CollectionAssert.AreEqual(new[] { 10, 30, 30 }, calls);
            Assert.That(sim.State.EnergyKWh, Is.EqualTo(1.5));
        }

        [Test]
        public void HealthDeathIsImmediateAndEmittedOnceEvenWhilePaused()
        {
            var sim = new Simulation();
            int deaths = 0;
            sim.Events.PlayerDied += () => deaths++;
            sim.Clock.Paused = true;
            sim.State.Health = 0;
            sim.Tick(0);
            sim.Tick(100);
            Assert.That(sim.State.Alive, Is.False);
            Assert.That(deaths, Is.EqualTo(1));
            Assert.That(sim.Clock.ElapsedSols, Is.Zero);
        }

        [Test]
        public void OxygenLossUsesContinuousSolTimeAndResetsOnRecovery()
        {
            var sim = new Simulation(oxygenGraceSols: 2);
            sim.State.Oxygen = 0;
            sim.Tick(sim.Clock.SecondsPerSol * 1.5);
            Assert.That(sim.State.Alive, Is.True);
            sim.State.Oxygen = 1;
            sim.Tick(sim.Clock.SecondsPerSol / 4);
            sim.State.Oxygen = 0;
            sim.Clock.Speed = 2;
            sim.Tick(sim.Clock.SecondsPerSol * 0.75);
            Assert.That(sim.State.Alive, Is.True);
            sim.Tick(sim.Clock.SecondsPerSol / 4);
            Assert.That(sim.State.Alive, Is.False);
            Assert.That(sim.Clock.ElapsedSols, Is.EqualTo(3.75).Within(1e-10));
        }

        [Test]
        public void PausingAndSpeedControlSystemTimeAndOxygenGrace()
        {
            var sim = new Simulation(oxygenGraceSols: 1);
            sim.State.Oxygen = 0;
            sim.Clock.Paused = true;
            sim.Tick(sim.Clock.SecondsPerSol * 10);
            Assert.That(sim.Clock.ElapsedSols, Is.Zero);
            Assert.That(sim.State.Alive, Is.True);
            sim.Clock.Paused = false;
            sim.Clock.Speed = 4;
            sim.Tick(sim.Clock.SecondsPerSol / 4);
            Assert.That(sim.Clock.ElapsedSols, Is.EqualTo(1));
            Assert.That(sim.State.Alive, Is.False);
        }

        [Test]
        public void LargeTickStopsExactlyAtOxygenDeadline()
        {
            var sim = new Simulation(oxygenGraceSols: 0.25);
            sim.State.Oxygen = 0;
            sim.Tick(sim.Clock.SecondsPerSol * 10);
            Assert.That(sim.State.Alive, Is.False);
            Assert.That(sim.Clock.ElapsedSols, Is.EqualTo(0.25));
        }

        [Test, Timeout(1000)]
        public void FractionalOxygenDeadlineAtLaterSolDoesNotStall()
        {
            var sim = new Simulation();
            sim.Tick(sim.Clock.SecondsPerSol * 2);
            sim.State.Oxygen = 0;
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(sim.State.Alive, Is.False);
            Assert.That(sim.Clock.ElapsedSols, Is.EqualTo(2 + GameConstants.OxygenGraceSols).Within(1e-10));
        }

        [Test]
        public void SystemDeathTakesPriorityOverMissionCompletion()
        {
            var sim = new Simulation();
            int wins = 0;
            sim.Events.MissionComplete += () => wins++;
            sim.RegisterSystem(new FatalSystem());
            sim.Tick(sim.Clock.SecondsPerSol * GameConstants.MissionDurationDays);
            Assert.That(sim.State.Alive, Is.False);
            Assert.That(wins, Is.Zero);
            Assert.That(sim.State.Sol, Is.EqualTo(GameConstants.MissionDurationDays));
        }

        [Test]
        public void TypedEventsReportTransitionsAndEventIds()
        {
            var sim = new Simulation();
            int critical = 0;
            string started = null, ended = null;
            sim.Events.StatCritical += (stat, value) => { if (stat == "water") critical++; };
            sim.Events.EventStarted += id => started = id;
            sim.Events.EventEnded += id => ended = id;
            sim.Events.StartEvent("storm");
            sim.Events.EndEvent("storm");
            sim.State.Water = 0;
            sim.Tick(0);
            sim.Tick(0);
            sim.State.Water = 100;
            sim.Tick(0);
            sim.State.Water = 0;
            sim.Tick(0);
            Assert.That(critical, Is.EqualTo(2));
            Assert.That(started, Is.EqualTo("storm"));
            Assert.That(ended, Is.EqualTo("storm"));
        }

        [Test]
        public void HeadlessMissionCompletesUnderOneSecondWithEverySolEmittedOnce()
        {
            var sim = new Simulation();
            int sols = 0, wins = 0;
            sim.Events.SolStarted += sol => { sols++; Assert.That(sol, Is.EqualTo(sols)); };
            sim.Events.MissionComplete += () => wins++;
            var timer = Stopwatch.StartNew();
            sim.Tick(GameConstants.RunDurationSeconds);
            timer.Stop();
            sim.Tick(GameConstants.RunDurationSeconds);
            Assert.That(sim.State.Sol, Is.EqualTo(GameConstants.MissionDurationDays));
            Assert.That(sim.State.Alive, Is.True);
            Assert.That(sols, Is.EqualTo(GameConstants.MissionDurationDays));
            Assert.That(wins, Is.EqualTo(1));
            Assert.That(timer.Elapsed.TotalSeconds, Is.LessThan(1));
            TestContext.WriteLine("99-day run: " + timer.Elapsed.TotalMilliseconds + " ms");
        }

        private sealed class RecordingSystem : ISimSystem
        {
            private readonly List<int> calls;
            public int Order { get; }
            public RecordingSystem(int order, List<int> calls) { Order = order; this.calls = calls; }
            public void Step(GameState state, double dtSol) { calls.Add(Order); state.EnergyKWh += dtSol; }
        }

        private sealed class FatalSystem : ISimSystem
        {
            public int Order => 0;
            public void Step(GameState state, double dtSol) { if (state.Sol == GameConstants.MissionDurationDays - 1) state.Health = 0; }
        }
    }
}
