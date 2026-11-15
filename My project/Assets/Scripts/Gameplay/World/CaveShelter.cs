using MarsTrainer.Gameplay.Radiation;
using MarsTrainer.Sim;
using UnityEngine;

namespace MarsTrainer.Gameplay.World
{
    public sealed class CaveShelter : MonoBehaviour
    {
        public ShelterZone Zone { get; private set; }
        public void Build(Simulation sim, Transform player, Material material)
        {
            float halfWidth = GameConstants.CaveWidth / 2;
            float halfHeight = GameConstants.CaveHeight / 2;
            float wall = GameConstants.CaveWall;
            TerrainGenerator.Box(transform, "Left wall", new Vector3(-halfWidth, halfHeight, 0),
                new Vector3(wall, GameConstants.CaveHeight, GameConstants.CaveDepth), material);
            TerrainGenerator.Box(transform, "Right wall", new Vector3(halfWidth, halfHeight, 0),
                new Vector3(wall, GameConstants.CaveHeight, GameConstants.CaveDepth), material);
            TerrainGenerator.Box(transform, "Roof", new Vector3(0, GameConstants.CaveHeight, 0),
                new Vector3(GameConstants.CaveWidth + wall, wall, GameConstants.CaveDepth), material);
            TerrainGenerator.Box(transform, "Back", new Vector3(0, halfHeight, GameConstants.CaveDepth / 2),
                new Vector3(GameConstants.CaveWidth, GameConstants.CaveHeight, wall), material);
            var trigger = gameObject.AddComponent<BoxCollider>();
            trigger.center = Vector3.up * GameConstants.ShelterTriggerHeight / 2;
            trigger.size = new Vector3(GameConstants.CaveWidth - wall, GameConstants.ShelterTriggerHeight,
                GameConstants.CaveDepth - wall);
            trigger.isTrigger = true;
            Zone = gameObject.AddComponent<ShelterZone>();
            Zone.Initialize(sim.Modifiers, player);
        }
    }
}
