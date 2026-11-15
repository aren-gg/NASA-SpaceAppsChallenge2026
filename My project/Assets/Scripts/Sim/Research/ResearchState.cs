using System;
using System.Collections.Generic;

namespace MarsTrainer.Sim.Research
{
    public sealed class ResearchState
    {
        private readonly HashSet<string> purchased = new HashSet<string>(StringComparer.Ordinal);
        public double RP { get; private set; }
        public bool IsPurchased(string id) => id != null && purchased.Contains(id);

        internal void Credit(double amount)
        {
            ValidateAmount(amount);
            ValidateAmount(RP + amount);
            RP += amount;
        }

        internal void Purchase(ResearchNode node)
        {
            purchased.Add(node.Id);
            RP = Math.Max(0, RP - node.CostRP);
        }

        internal static void ValidateAmount(double amount)
        {
            if (double.IsNaN(amount) || double.IsInfinity(amount) || amount < 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
        }
    }
}
