using System;
using System.Collections.Generic;
using MarsTrainer.Sim;
using UnityEngine;

namespace MarsTrainer.Gameplay.Radiation
{
    [RequireComponent(typeof(BoxCollider))]
    public sealed class ShelterZone : MonoBehaviour
    {
        [SerializeField, Range(0, (float)GameConstants.ShieldMaximum)]
        private float shielding = (float)GameConstants.ShelterShield;
        [SerializeField] private Transform arrivalPoint;
        private readonly HashSet<Collider> occupants = new HashSet<Collider>(GameConstants.ShelterOccupantCapacity);
        private SimModifiers modifiers;
        private Transform player;
        private string source;
        public Vector3 ArrivalPosition => arrivalPoint != null ? arrivalPoint.position : transform.position + Vector3.up * GameConstants.PlayerSpawnHeight;

        public void Initialize(SimModifiers modifiers, Transform player)
        {
            Clear();
            this.modifiers = modifiers ?? throw new ArgumentNullException(nameof(modifiers));
            this.player = player != null ? player : throw new ArgumentNullException(nameof(player));
            source = "shelter." + GetInstanceID();
            // Reserve modifier storage during setup, before an EVA enters a cave.
            modifiers.Set(GameConstants.RadiationShieldStat, source, 0);
            modifiers.Remove(GameConstants.RadiationShieldStat, source);
            GetComponent<BoxCollider>().isTrigger = true;
        }
        private void Reset() { GetComponent<BoxCollider>().isTrigger = true; }
        private void OnTriggerEnter(Collider other)
        {
            if (modifiers == null || player == null || !other.transform.IsChildOf(player)) return;
            if (occupants.Add(other)) modifiers.Set(GameConstants.RadiationShieldStat, source, shielding);
        }
        private void OnTriggerExit(Collider other)
        {
            if (occupants.Remove(other) && occupants.Count == 0)
                modifiers.Remove(GameConstants.RadiationShieldStat, source);
        }
        private void Clear()
        {
            if (modifiers != null && source != null) modifiers.Remove(GameConstants.RadiationShieldStat, source);
            occupants.Clear();
        }
        private void OnDisable() { Clear(); }
    }
}
