using System;
using MarsTrainer.Core;
using MarsTrainer.Sim;

namespace MarsTrainer.Tests
{
    public sealed class BalanceBot
    {
        public MissionSession Mission { get; }
        private readonly BalancePolicy policy;
        private double net;
        private int reports;
        public double MeanEnergyMargin => reports > 0 ? net / reports : 0;

        public BalanceBot(MissionSession mission, BalancePolicy policy)
        {
            Mission = mission; this.policy = policy;
            mission.Simulation.Modifiers.Set(GameConstants.RadiationShieldStat, "bot.shelter",
                mission.Sheltered ? GameConstants.ShelterShield : 0);
            mission.Simulation.Events.EnergyReported += report => { net += report.NetKWh; reports++; };
        }

        public void Run()
        {
            var sim = Mission.Simulation;
            while (sim.State.Alive && sim.Clock.ElapsedSols < GameConstants.MissionDurationDays)
            {
                double fraction = sim.Clock.ElapsedSols % 1;
                bool good = policy == BalancePolicy.Good;
                if (policy != BalancePolicy.Idle)
                {
                    // Naive goes outside every daylight shift. Good checks today's flare forecast.
                    Mission.Sheltered = fraction < GameConstants.SunriseSol || fraction >= GameConstants.SunsetSol ||
                        good && Mission.Weather.RadiationAt(sim.Clock.Sol).FlareLevel > 0;
                    sim.Modifiers.Set(GameConstants.RadiationShieldStat, "bot.shelter", Mission.Sheltered ? GameConstants.ShelterShield : 0);
                    Mission.TendFarm();
                    sim.Survival.Eat(sim.State, Math.Min(Mission.Inventory.FoodKcal, GameConstants.AstronautKcalPerSol));
                    sim.Survival.Drink(sim.State, Math.Min(Mission.Inventory.WaterLitres, GameConstants.WaterLitresPerSol));
                    // One medbay visit per sol; actions consume the same finite battery as the player.
                    if (fraction == GameConstants.SunriseSol && sim.State.Health < (good ? GameConstants.BotGoodHealBelow : GameConstants.BotNaiveHealBelow))
                        sim.Survival.Medbay(sim.State, GameConstants.BotMedbayKWh);
                    sim.Survival.Sleeping = sim.State.Tiredness >= GameConstants.BotSleepAbove;
                    if (good)
                        for (int i = 0; i < Mission.Catalog.Length; i++) Mission.Research.TryPurchase(Mission.Catalog[i].Id);
                }
                sim.Tick(sim.Clock.SecondsPerSol * GameConstants.BotStepSols);
            }
        }
    }
}
