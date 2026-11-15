using UnityEngine;

namespace MarsTrainer.Gameplay.Player
{
    public interface IWalkingSurface
    {
        float GroundHeight(Vector3 worldPosition);
        Vector3 ClampPosition(Vector3 worldPosition);
    }
}
