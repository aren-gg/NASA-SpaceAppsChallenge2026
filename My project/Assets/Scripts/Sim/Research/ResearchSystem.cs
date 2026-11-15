using System;
using System.Collections.Generic;

namespace MarsTrainer.Sim.Research
{
    public sealed class ResearchSystem : ISimSystem
    {
        private readonly Dictionary<string, ResearchNode> nodes = new Dictionary<string, ResearchNode>(StringComparer.Ordinal);
        private readonly SimModifiers modifiers;
        private readonly double passiveRPPerSol;
        public ResearchState State { get; } = new ResearchState();
        public int Order => GameConstants.ResearchSystemOrder;

        // Register with Simulation before ticking. P9 pickups grant points through AddRP.
        public ResearchSystem(IReadOnlyList<ResearchNode> catalog, SimModifiers modifiers,
            double passiveRPPerSol = GameConstants.ResearchPassiveRPPerSol)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            this.modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
            ResearchState.ValidateAmount(passiveRPPerSol);
            this.passiveRPPerSol = passiveRPPerSol;
            for (int i = 0; i < catalog.Count; i++)
            {
                var node = catalog[i] ?? throw new ArgumentException("Research catalog contains a null node.");
                nodes.Add(node.Id, node);
            }
            foreach (var pair in nodes)
            {
                var node = pair.Value;
                bool gated = node.Tier == GameConstants.ResearchMinTier;
                for (int i = 0; i < node.Prereqs.Count; i++)
                {
                    if (!nodes.TryGetValue(node.Prereqs[i], out var prereq) || prereq.Tier >= node.Tier)
                        throw new ArgumentException("Prerequisites must exist in an earlier tier: " + node.Id);
                    if (prereq.Path == node.Path && prereq.Tier == node.Tier - 1) gated = true;
                }
                if (!gated) throw new ArgumentException("Higher tiers require the previous tier in their path: " + node.Id);
            }
        }

        public void AddRP(double amount) { State.Credit(amount); }

        public bool CanPurchase(string id)
        {
            if (id == null || !nodes.TryGetValue(id, out var node) || State.IsPurchased(id) || State.RP + GameConstants.ResearchRPTolerance < node.CostRP)
                return false;
            for (int i = 0; i < node.Prereqs.Count; i++)
                if (!State.IsPurchased(node.Prereqs[i])) return false;
            return true;
        }

        public bool TryPurchase(string id)
        {
            if (!CanPurchase(id)) return false;
            var node = nodes[id];
            for (int i = 0; i < node.Modifiers.Count; i++)
            {
                var effect = node.Modifiers[i];
                modifiers.Set(effect.statId, node.Sources[i],
                    additive: effect.op == ModifierOp.Add ? effect.value : 0,
                    multiplier: effect.op == ModifierOp.Multiply ? effect.value : 1);
            }
            State.Purchase(node);
            return true;
        }

        public void Step(GameState state, double dtSol)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            ResearchState.ValidateAmount(dtSol);
            if (state.Alive) AddRP(passiveRPPerSol * dtSol);
        }
    }
}
