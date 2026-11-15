using System;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Systems;

namespace MarsSurvival.Simulation
{
    /// <summary>
    /// The astronaut's needs are tracked by the MarsTrainer survival model: <see cref="GameState"/>,
    /// <see cref="SurvivalSystem"/> (hunger, water, tiredness, oxygen, fitness) and <see cref="HealthSystem"/>
    /// (damage from starvation, dehydration, hypoxia, temperature and cumulative dose against the
    /// NASA-STD-3001 Vol. 1 career limit). The outpost mission keeps the physical bookkeeping
    /// (oxygen grams, food portions, litres, absorbed dose) and feeds it in each fixed step;
    /// the results are stored in <see cref="AstronautState"/> for saves, warnings and the HUD.
    /// </summary>
    public sealed class SurvivalNeeds
    {
        /// <summary>One galley food portion. 2.5 portions cover the 2500 kcal daily need.</summary>
        public const double KcalPerPortion = 1000;
        /// <summary>Litres in one drink at a water station.</summary>
        public const double DrinkLitres = 0.5;

        public GameState Body { get; } = new GameState();
        public Inventory Pantry { get; } = new Inventory();
        public SurvivalSystem Survival { get; }
        public HealthSystem Health { get; }

        public SurvivalNeeds(AstronautState astronaut)
        {
            if (astronaut == null) throw new ArgumentNullException("astronaut");
            Pull(astronaut);
            Survival = new SurvivalSystem(Pantry);
            Health = new HealthSystem(Body, Survival);
        }

        /// <summary>Absorbed dose (microgray) to dose equivalent (mSv) with the MarsTrainer quality factor.</summary>
        public static double MilliSievert(double microgray)
        {
            return microgray / GameConstants.MicroSvPerMilliSv * GameConstants.RadiationQualityFactor;
        }

        /// <summary>Fraction of the gameplay radiation budget (NASA-STD-3001 career limit, scaled) already used.</summary>
        public static double DoseBudgetFraction(AstronautState astronaut)
        {
            return MilliSievert(astronaut.doseMicrogray) / GameConstants.H_limit;
        }

        /// <summary>
        /// Advances the needs by <paramref name="seconds"/>. <paramref name="oxygenSupply"/> is the fraction of
        /// breathing demand the habitat or suit actually delivered (0..1). Returns the damage cause, or null.
        /// </summary>
        public string Step(AstronautState astronaut, double seconds, double oxygenSupply, bool sleeping, bool exercising)
        {
            if (seconds <= 0) return null;
            Pull(astronaut);
            double sols = seconds / MissionSettings.SolSeconds;
            double before = Body.Health;
            Survival.Sleeping = sleeping;
            Survival.Exercising = exercising;
            Body.TemperatureC = astronaut.temperatureC;
            Body.TemperatureProtected = false;
            // Without an EnergySystem, SurvivalSystem treats the energy it can draw for life support as the
            // powered fraction. Supplying exactly that fraction makes it the delivered-oxygen fraction.
            Body.EnergyKWh = GameConstants.LifeSupportKWhPerSol * sols * Math.Max(0, Math.Min(1, oxygenSupply));
            Survival.Step(Body, sols);
            Health.Step(Body, sols);
            Body.EnergyKWh = 0;
            Push(astronaut);
            astronaut.hypoxiaSeconds = Body.Oxygen <= 0 ? astronaut.hypoxiaSeconds + seconds : 0;
            astronaut.starvationSeconds = Body.Hunger >= GameConstants.StatMaximum ? astronaut.starvationSeconds + seconds : 0;
            astronaut.dehydrationSeconds = Body.Water <= 0 ? astronaut.dehydrationSeconds + seconds : 0;
            // Health is also capped by fitness (MaxHealth); only a drop below that cap is damage.
            return Body.Health < Math.Min(before, Body.MaxHealth) - 1e-9 ? Describe(Body.LastDamageCause) : null;
        }

        /// <summary>Eats from the galley stores. Returns portions eaten (0 when not hungry or no food).</summary>
        public double Eat(AstronautState astronaut, ref double portions, double kcalPerPortion)
        {
            Pull(astronaut);
            SetFood(portions * kcalPerPortion);
            if (!Survival.Eat(Body, kcalPerPortion)) return 0;
            double eaten = portions - Pantry.FoodKcal / kcalPerPortion;
            portions = Pantry.FoodKcal / kcalPerPortion;
            Push(astronaut);
            return eaten;
        }

        /// <summary>Drinks from clean water. Returns litres drunk (0 when not thirsty or no water).</summary>
        public double Drink(AstronautState astronaut, ref double litres)
        {
            Pull(astronaut);
            SetWater(litres);
            if (!Survival.Drink(Body, DrinkLitres)) return 0;
            double drunk = litres - Pantry.WaterLitres;
            litres = Pantry.WaterLitres;
            Push(astronaut);
            return drunk;
        }

        public static string Describe(string cause)
        {
            switch (cause)
            {
                case "starvation": return "Prolonged starvation";
                case "dehydration": return "Prolonged dehydration";
                case "hypoxia": return "Oxygen shortage or unsafe cabin air";
                case "radiation": return "Radiation dose beyond the NASA-STD-3001 budget";
                case "temperature": return "Unsafe suit or habitat temperature";
                default: return string.IsNullOrEmpty(cause) ? "Health loss" : cause;
            }
        }

        private void SetFood(double kcal)
        {
            kcal = Math.Max(0, kcal);
            if (Pantry.FoodKcal < kcal) Pantry.Add(new SpaceSnack("Galley stores", kcal - Pantry.FoodKcal));
            else if (Pantry.FoodKcal > kcal) Pantry.TryConsumeFood(Pantry.FoodKcal - kcal);
        }

        private void SetWater(double litres)
        {
            litres = Math.Max(0, litres);
            if (Pantry.WaterLitres < litres) Pantry.AddWater(litres - Pantry.WaterLitres);
            else if (Pantry.WaterLitres > litres) Pantry.TryConsumeWater(Pantry.WaterLitres - litres);
        }

        private void Pull(AstronautState a)
        {
            Body.Health = a.health;
            Body.Hunger = GameConstants.StatMaximum - a.satiety;
            Body.Water = a.hydration;
            Body.Tiredness = a.fatigue;
            Body.Fitness = a.fitness;
            Body.Oxygen = a.oxygen;
            Body.RadiationDoseMSv = MilliSievert(a.doseMicrogray);
        }

        private void Push(AstronautState a)
        {
            a.health = Body.Health;
            a.satiety = GameConstants.StatMaximum - Body.Hunger;
            a.hydration = Body.Water;
            a.fatigue = Body.Tiredness;
            a.fitness = Body.Fitness;
            a.oxygen = Body.Oxygen;
        }
    }
}
