using System;
using System.Collections.Generic;

namespace MarsTrainer.Sim.Research
{
    // Immutable snapshot of authoring data; no Unity objects enter the simulation.
    public sealed class ResearchNode
    {
        public string Id { get; }
        public ResearchPath Path { get; }
        public double CostRP { get; }
        public int Tier { get; }
        public IReadOnlyList<string> Prereqs { get; }
        public IReadOnlyList<ResearchModifier> Modifiers { get; }
        internal string[] Sources { get; }

        public ResearchNode(string id, ResearchPath path, double costRP, string[] prereqs,
            int tier, ResearchModifier[] modifiers)
        {
            if (string.IsNullOrWhiteSpace(id) || !Enum.IsDefined(typeof(ResearchPath), path))
                throw new ArgumentException("A research node needs an id and a valid path.");
            ResearchState.ValidateAmount(costRP);
            if (tier < GameConstants.ResearchMinTier || tier > GameConstants.ResearchMaxTier)
                throw new ArgumentOutOfRangeException(nameof(tier));
            if (prereqs == null || modifiers == null) throw new ArgumentNullException(nameof(prereqs));
            var requirements = (string[])prereqs.Clone();
            for (int i = 0; i < requirements.Length; i++)
                if (string.IsNullOrWhiteSpace(requirements[i])) throw new ArgumentException("Prerequisite id is empty.");
            var effects = (ResearchModifier[])modifiers.Clone();
            Sources = new string[effects.Length];
            for (int i = 0; i < effects.Length; i++)
            {
                effects[i].Validate();
                Sources[i] = "research:" + id + ":" + i;
            }
            Id = id;
            Path = path;
            CostRP = costRP;
            Tier = tier;
            Prereqs = Array.AsReadOnly(requirements);
            Modifiers = Array.AsReadOnly(effects);
        }
    }
}
