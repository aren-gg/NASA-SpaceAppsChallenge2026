using System;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Research;
using UnityEngine;

namespace MarsTrainer.Data.Research
{
    [CreateAssetMenu(menuName = "Mars Trainer/Research Node")]
    public sealed class ResearchNodeSO : ScriptableObject
    {
        public string id;
        public ResearchPath path;
        [Min(0)] public double costRP;
        public string[] prereqs = Array.Empty<string>();
        [Range(GameConstants.ResearchMinTier, GameConstants.ResearchMaxTier)] public int tier = GameConstants.ResearchMinTier;
        public ResearchModifier[] modifiers = Array.Empty<ResearchModifier>();
        public string title;
        [TextArea] public string description;
        public string iconId;

        public ResearchNode ToNode() => new ResearchNode(id, path, costRP, prereqs, tier, modifiers);
    }
}
