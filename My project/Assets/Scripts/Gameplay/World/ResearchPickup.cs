using System;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Research;
using UnityEngine;

namespace MarsTrainer.Gameplay.World
{
    [RequireComponent(typeof(SphereCollider))]
    public sealed class ResearchPickup : MonoBehaviour
    {
        private Simulation simulation;
        private ResearchSystem research;
        private Transform player;
        public bool Collected { get; private set; }
        public event Action CollectedChanged;

        public void Initialize(Simulation sim, ResearchSystem research, Transform player)
        {
            simulation = sim ?? throw new ArgumentNullException(nameof(sim));
            this.research = research ?? throw new ArgumentNullException(nameof(research));
            this.player = player != null ? player : throw new ArgumentNullException(nameof(player));
            var trigger = GetComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = GameConstants.PickupRadius;
        }

        public bool TryCollect(Transform collector)
        {
            if (Collected || simulation == null || collector == null || !collector.IsChildOf(player) ||
                !simulation.State.Alive || simulation.State.Health <= 0 || simulation.Clock.Paused ||
                simulation.Clock.ElapsedSols >= GameConstants.MissionDurationDays) return false;
            Collected = true;
            research.AddRP(GameConstants.PickupRP);
            CollectedChanged?.Invoke();
            gameObject.SetActive(false);
            return true;
        }

        private void OnTriggerEnter(Collider other) { TryCollect(other.transform); }
    }
}
