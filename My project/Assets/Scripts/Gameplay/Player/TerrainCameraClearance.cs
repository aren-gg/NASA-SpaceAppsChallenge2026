using Cinemachine;
using MarsTrainer.Sim;
using UnityEngine;

namespace MarsTrainer.Gameplay.Player
{
    public sealed class TerrainCameraClearance : CinemachineExtension
    {
        public IWalkingSurface Surface { get; set; }
        protected override void PostPipelineStageCallback(CinemachineVirtualCameraBase camera,
            CinemachineCore.Stage stage, ref CameraState state, float deltaTime)
        {
            if (stage != CinemachineCore.Stage.Body || Surface == null) return;
            var position = state.RawPosition;
            position.y = Mathf.Max(position.y, Surface.GroundHeight(position) + GameConstants.OrbitGroundClearance);
            state.RawPosition = position;
        }
    }
}
