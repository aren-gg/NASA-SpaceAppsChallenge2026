using System;
using System.Collections.Generic;
using MarsTrainer.Data.Research;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Research;
using MarsTrainer.Sim.Systems;
using NUnit.Framework;
using UnityEditor;

namespace MarsTrainer.Tests
{
    public sealed class ResearchTests
    {
        private static ResearchNode Node(string id, double cost = 10, string[] prereqs = null,
            int tier = 1, params ResearchModifier[] modifiers) =>
            new ResearchNode(id, ResearchPath.Solar, cost, prereqs ?? Array.Empty<string>(), tier, modifiers);

        [Test]
        public void PrerequisitesCostAndDuplicatePurchaseAreAtomic()
        {
            var modifiers = new SimModifiers();
            var research = new ResearchSystem(new[] { Node("first"), Node("second", 20, new[] { "first" }, 2) }, modifiers);
            research.AddRP(30);
            Assert.That(research.TryPurchase("second"), Is.False);
            Assert.That(research.State.RP, Is.EqualTo(30));
            Assert.That(research.TryPurchase("first"), Is.True);
            Assert.That(research.State.RP, Is.EqualTo(20));
            Assert.That(research.TryPurchase("first"), Is.False);
            Assert.That(research.TryPurchase("second"), Is.True);
            Assert.That(research.State.RP, Is.Zero);
            Assert.That(research.State.IsPurchased("second"), Is.True);
            Assert.That(research.TryPurchase("missing"), Is.False);
        }

        [Test]
        public void InsufficientFundsLeaveModifiersAndBalanceUnchanged()
        {
            var modifiers = new SimModifiers();
            var research = new ResearchSystem(new[] { Node("panel", modifiers: new[] {
                new ResearchModifier(GameConstants.PanelEfficiencyStat, ModifierOp.Multiply, 1.2) }) }, modifiers);
            research.AddRP(9);
            Assert.That(research.TryPurchase("panel"), Is.False);
            Assert.That(research.State.RP, Is.EqualTo(9));
            Assert.That(modifiers.Apply(GameConstants.PanelEfficiencyStat, 0.2), Is.EqualTo(0.2));
        }

        [Test]
        public void PassiveRPUsesSimTimeAndPickupsCanGrantRP()
        {
            var sim = new Simulation();
            var research = new ResearchSystem(Array.Empty<ResearchNode>(), sim.Modifiers, passiveRPPerSol: 0.5);
            sim.RegisterSystem(research);
            sim.Clock.Paused = true;
            sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(research.State.RP, Is.Zero);
            sim.Clock.Paused = false;
            sim.Clock.Speed = 2;
            sim.Tick(sim.Clock.SecondsPerSol / 2);
            research.AddRP(3);
            Assert.That(research.State.RP, Is.EqualTo(3.5).Within(1e-10));
            Assert.Throws<ArgumentOutOfRangeException>(() => research.AddRP(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => research.AddRP(double.NaN));
        }

        [Test]
        public void FractionalPassiveRPReachesAnExactPurchasePrice()
        {
            var sim = new Simulation();
            var research = new ResearchSystem(new[] { Node("first", cost: 5) }, sim.Modifiers);
            sim.RegisterSystem(research);
            for (int i = 0; i < 50; i++) sim.Tick(sim.Clock.SecondsPerSol);
            Assert.That(research.TryPurchase("first"), Is.True);
            Assert.That(research.State.RP, Is.Zero.Within(1e-9));
        }

        [Test]
        public void MultipleOperationsOnSameStatAndDifferentNodesStackOnce()
        {
            var modifiers = new SimModifiers();
            var research = new ResearchSystem(new[] {
                Node("a", modifiers: new[] { new ResearchModifier("stat", ModifierOp.Add, 2),
                    new ResearchModifier("stat", ModifierOp.Multiply, 1.5) }),
                Node("b", modifiers: new[] { new ResearchModifier("stat", ModifierOp.Multiply, 2) }) }, modifiers);
            research.AddRP(100);
            research.TryPurchase("a");
            research.TryPurchase("b");
            Assert.That(research.TryPurchase("a"), Is.False);
            Assert.That(modifiers.Apply("stat", 10), Is.EqualTo(36));
        }

        [Test]
        public void CatalogRejectsMissingPrerequisitesAndUngatedHigherTiers()
        {
            Assert.Throws<ArgumentException>(() => new ResearchSystem(new[] { Node("bad", prereqs: new[] { "missing" }, tier: 2) }, new SimModifiers()));
            Assert.Throws<ArgumentException>(() => new ResearchSystem(new[] { Node("bad", tier: 2) }, new SimModifiers()));
            Assert.Throws<ArgumentException>(() => new ResearchSystem(new[] { Node("a"), Node("a") }, new SimModifiers()));
        }

        [Test]
        public void BundledCatalogHasSixNodesPerPathAndAllCanBeBoughtInTierOrder()
        {
            var nodes = LoadCatalog();
            Assert.That(nodes.Count, Is.EqualTo(24));
            var research = new ResearchSystem(nodes, new SimModifiers());
            research.AddRP(10000);
            foreach (ResearchPath path in Enum.GetValues(typeof(ResearchPath)))
            {
                int count = 0;
                for (int tier = 1; tier <= 4; tier++)
                    foreach (var node in nodes)
                        if (node.Path == path && node.Tier == tier)
                        {
                            count++;
                            Assert.That(research.TryPurchase(node.Id), Is.True, node.Id);
                        }
                Assert.That(count, Is.EqualTo(6), path.ToString());
            }
        }

        [Test]
        public void BuyingBundledSolarNodeRaisesActualEnergySystemGeneration()
        {
            var nodes = LoadCatalog();
            ResearchNode solar = nodes.Find(node => node.Path == ResearchPath.Solar && node.Tier == 1);
            Assert.That(solar, Is.Not.Null);
            double Run(bool upgrade)
            {
                var sim = new Simulation();
                var research = new ResearchSystem(nodes, sim.Modifiers);
                if (upgrade) { research.AddRP(solar.CostRP); Assert.That(research.TryPurchase(solar.Id), Is.True); }
                var energy = new EnergySystem(sim.Clock, _ => new EnergyConditions(251, 0, 20),
                    new SolarPanel(1, 10, 0.2), new Battery(1000, 1000, 1000, 1),
                    new EnergyLoads(0, 0, 0, 0, 0, 0), sim.Modifiers, sim.Events);
                sim.RegisterSystem(energy);
                sim.Tick(sim.Clock.SecondsPerSol);
                return energy.LastReport.GenerationKWh;
            }
            double baseline = Run(false), upgraded = Run(true);
            Assert.That(upgraded, Is.EqualTo(baseline * 1.2).Within(1e-9));
            TestContext.WriteLine("Solar purchase: " + baseline + " -> " + upgraded + " kWh/sol (+20%).");
        }

        private static List<ResearchNode> LoadCatalog()
        {
            var nodes = new List<ResearchNode>();
            foreach (string guid in AssetDatabase.FindAssets("t:ResearchNodeSO", new[] { "Assets/Data/Research" }))
            {
                var asset = AssetDatabase.LoadAssetAtPath<ResearchNodeSO>(AssetDatabase.GUIDToAssetPath(guid));
                Assert.That(asset.title, Is.Not.Empty);
                Assert.That(asset.description, Is.Not.Empty);
                Assert.That(asset.iconId, Is.Not.Empty);
                nodes.Add(asset.ToNode());
            }
            return nodes;
        }
    }
}
