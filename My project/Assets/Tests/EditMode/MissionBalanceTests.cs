using System;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using MarsTrainer.Core;
using MarsTrainer.Data;
using MarsTrainer.Data.Research;
using MarsTrainer.Sim.Research;
using NUnit.Framework;
using UnityEngine;

namespace MarsTrainer.Tests
{
    public sealed class MissionBalanceTests
    {
        private static DataRepository Repository()
        {
            var data = new DataRepository(message => Assert.Fail(message));
            data.LoadScheduleJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "mars_year_schedule.json")));
            data.LoadCropsJson(File.ReadAllText(Path.Combine(Application.streamingAssetsPath, "crop_parameters.json")));
            Assert.That(data.UsingFallback, Is.False, "Balance must use shipped JSON, not fallback data.");
            return data;
        }

        [Test]
        public void MissingDataRemainsPlayableAndReportsFallback()
        {
            int warnings = 0;
            var data = new DataRepository(message => warnings++);
            Assert.That(data.UsingFallback, Is.True);
            data.LoadScheduleJson("invalid"); data.LoadCropsJson("invalid");
            Assert.That(data.UsingFallback, Is.True);
            Assert.That(warnings, Is.EqualTo(2));
            Assert.That(new MissionSession(data, 42).Simulation.Survival, Is.Not.Null);
        }

        [Test]
        public void IdleStartsWithTheSameBaseShieldingAsThePlayer()
        {
            var bot = new BalanceBot(new MissionSession(Repository(), 42), BalancePolicy.Idle);
            Assert.That(bot.Mission.Simulation.Modifiers.Apply(MarsTrainer.Sim.GameConstants.RadiationShieldStat,
                MarsTrainer.Sim.GameConstants.SuitShield),
                Is.EqualTo(MarsTrainer.Sim.GameConstants.SuitShield + MarsTrainer.Sim.GameConstants.ShelterShield));
        }

        [Test]
        public void MissionIncludesEverySurvivalSystemAndReplaysItsSeed()
        {
            var data = Repository();
            var a = new MissionSession(data, 42);
            var b = new MissionSession(data, 42);
            Assert.That(a.Simulation.Survival, Is.Not.Null);
            Assert.That(a.Farm.Plots.Count, Is.GreaterThan(0));
            a.Simulation.Tick(a.Simulation.Clock.SecondsPerSol);
            b.Simulation.Tick(b.Simulation.Clock.SecondsPerSol);
            Assert.That(a.Simulation.State.RadiationDoseMSv, Is.EqualTo(b.Simulation.State.RadiationDoseMSv));
            Assert.That(a.Simulation.State.EnergyKWh, Is.EqualTo(b.Simulation.State.EnergyKWh));
            Assert.That(a.Simulation.State.Water, Is.LessThan(100));
        }

        [Test, Category("Balance")]
        public void ThousandSeedsPerPolicyMeetSurvivalTargets()
        {
            var data = Repository();
            var ids = UnityEditor.AssetDatabase.FindAssets("t:ResearchNodeSO", new[] { "Assets/Data/Research" });
            Array.Sort(ids, StringComparer.Ordinal);
            var catalog = new ResearchNode[ids.Length];
            for (int i = 0; i < ids.Length; i++) catalog[i] = UnityEditor.AssetDatabase.LoadAssetAtPath<ResearchNodeSO>(UnityEditor.AssetDatabase.GUIDToAssetPath(ids[i])).ToNode();
            int runCount = int.TryParse(Environment.GetEnvironmentVariable("MARS_BALANCE_RUNS"), out int requested) ? Math.Max(1, requested) : 1000;
            string folder = Environment.GetEnvironmentVariable("MARS_BALANCE_OUTPUT") ??
                Path.GetFullPath(Path.Combine(Application.dataPath, "../../P12Results"));
            Directory.CreateDirectory(folder);
            using (var runs = new StreamWriter(Path.Combine(folder, "runs.csv")))
            using (var summary = new StreamWriter(Path.Combine(folder, "balance.csv")))
            {
                summary.AutoFlush = true;
                runs.WriteLine("policy,seed,survived,death_cause,final_sol,final_dose_mSv,energy_margin_kWh_per_sol");
                summary.WriteLine("policy,runs,survival_percent,death_causes,mean_final_dose_mSv,mean_energy_margin_kWh_per_sol");
                double[] percentages = new double[3];
                for (int policy = 0; policy < 3; policy++)
                {
                    int survived = 0;
                    double dose = 0, margin = 0;
                    var deaths = new System.Collections.Generic.SortedDictionary<string, int>();
                    string name = ((BalancePolicy)policy).ToString().ToLowerInvariant();
                    var bots = new BalanceBot[runCount];
                    int completed = 0;
                    Parallel.For(0, runCount, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 2) }, seed =>
                    {
                        var bot = new BalanceBot(new MissionSession(data, seed, catalog), (BalancePolicy)policy);
                        bot.Run(); bots[seed] = bot;
                        int done = System.Threading.Interlocked.Increment(ref completed);
                        if (done % 100 == 0) TestContext.Progress.WriteLine(name + " " + done + "/" + runCount);
                    });
                    for (int seed = 0; seed < runCount; seed++)
                    {
                        var bot = bots[seed];
                        var sim = bot.Mission.Simulation;
                        bool complete = sim.State.Alive && sim.Clock.ElapsedSols >= MarsTrainer.Sim.GameConstants.MissionDurationDays;
                        string cause = complete ? "none" : sim.State.DeathCause ?? "unknown";
                        if (complete) survived++; else { deaths.TryGetValue(cause, out int count); deaths[cause] = count + 1; }
                        dose += sim.State.RadiationDoseMSv; margin += bot.MeanEnergyMargin;
                        runs.WriteLine(string.Join(",", name, seed, complete ? 1 : 0, cause,
                            Number(sim.Clock.ElapsedSols), Number(sim.State.RadiationDoseMSv), Number(bot.MeanEnergyMargin)));
                    }
                    percentages[policy] = survived * 100.0 / runCount;
                    string causes = string.Join(";", System.Linq.Enumerable.Select(deaths, pair => pair.Key + ":" + pair.Value));
                    string row = string.Join(",", name, runCount, Number(percentages[policy]), causes, Number(dose / runCount), Number(margin / runCount));
                    summary.WriteLine(row); TestContext.Progress.WriteLine(row);
                }
                Assert.That(percentages, Has.Length.EqualTo(3));
                    Assert.That(percentages[0], Is.InRange(0, 1), "idle");
                    Assert.That(percentages[1], Is.InRange(20, 40), "naive");
                    Assert.That(percentages[2], Is.InRange(70, 90), "good");

            }
        }

        private static string Number(double value) => value.ToString("0.000", CultureInfo.InvariantCulture);
    }
}
