using System;
using System.Collections.Generic;

namespace MarsTrainer.Sim
{
    public sealed class SimModifiers
    {
        private readonly Dictionary<string, Dictionary<string, Modifier>> stats =
            new Dictionary<string, Dictionary<string, Modifier>>(StringComparer.Ordinal);

        // A source replaces its own contribution. Result = (base + sum(additive)) * product(multiplier).
        public void Set(string statId, string sourceId, double additive = 0, double multiplier = 1)
        {
            ValidateId(statId);
            ValidateId(sourceId);
            if (double.IsNaN(additive) || double.IsInfinity(additive) ||
                double.IsNaN(multiplier) || double.IsInfinity(multiplier))
                throw new ArgumentOutOfRangeException(nameof(additive));
            if (!stats.TryGetValue(statId, out var sources))
            {
                sources = new Dictionary<string, Modifier>(StringComparer.Ordinal);
                stats.Add(statId, sources);
            }
            sources[sourceId] = new Modifier { Additive = additive, Multiplier = multiplier };
        }

        public bool Remove(string statId, string sourceId)
        {
            ValidateId(statId);
            ValidateId(sourceId);
            return stats.TryGetValue(statId, out var sources) && sources.Remove(sourceId);
        }

        public double Apply(string statId, double baseValue)
        {
            ValidateId(statId);
            if (!stats.TryGetValue(statId, out var sources)) return baseValue;
            double additive = 0, multiplier = 1;
            foreach (var pair in sources)
            {
                additive += pair.Value.Additive;
                multiplier *= pair.Value.Multiplier;
            }
            return (baseValue + additive) * multiplier;
        }

        private static void ValidateId(string id)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("An id is required.", nameof(id));
        }

        private struct Modifier
        {
            public double Additive;
            public double Multiplier;
        }
    }
}
