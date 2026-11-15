using System;

namespace MarsTrainer.Sim.Research
{
    [Serializable]
    public struct ResearchModifier
    {
        public string statId;
        public ModifierOp op;
        public double value;

        public ResearchModifier(string statId, ModifierOp op, double value)
        {
            this.statId = statId;
            this.op = op;
            this.value = value;
        }

        internal void Validate()
        {
            if (string.IsNullOrWhiteSpace(statId) || !Enum.IsDefined(typeof(ModifierOp), op))
                throw new ArgumentException("A research modifier needs a stat and a supported operation.");
            if (double.IsNaN(value) || double.IsInfinity(value) || (op == ModifierOp.Multiply && value < 0))
                throw new ArgumentOutOfRangeException(nameof(value));
        }
    }
}
