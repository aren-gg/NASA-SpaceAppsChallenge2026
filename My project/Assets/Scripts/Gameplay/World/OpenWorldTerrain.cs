using System;
using MarsSurvival.World;
using MarsTrainer.Gameplay.Player;
using MarsTrainer.Sim;
using UnityEngine;

namespace MarsTrainer.Gameplay.World
{
    // Keeps the survey dataset and its streamed meshes at their authored real scale.
    public sealed class OpenWorldTerrain : MonoBehaviour, IWalkingSurface
    {
        private MarsTerrainConfig runtimeConfig;
        public MarsTerrain Terrain { get; private set; }
        public bool Ready => Terrain != null && Terrain.IsReady;

        public bool Initialize(MarsTerrainConfig source)
        {
            if (source == null) return false;
            try
            {
                runtimeConfig = Instantiate(source);
                runtimeConfig.generateColliders = false; // The walker queries exactly the rendered facets.
                runtimeConfig.rocksPerTile = GameConstants.OpenWorldRocksPerTile;
                Vector3 origin;
                using (var map = new MarsHeightmap(source.heightData, source.metadata,
                    source.realMetersPerUnityUnit, source.verticalExaggeration))
                {
                    var point = map.LatLonToLocal(GameConstants.OpenWorldLatitude, GameConstants.OpenWorldLongitude);
                    origin = new Vector3(point.x, map.SampleFacetedY(point.x, point.y, source.StrideForLod(0)), point.y);
                }
                var owner = new GameObject("Gale Crater / streamed elevation");
                owner.SetActive(false); owner.transform.SetParent(transform, false);
                owner.transform.localPosition = -origin;
                Terrain = owner.AddComponent<MarsTerrain>();
                Terrain.config = runtimeConfig; Terrain.viewer = transform; Terrain.manageFogAndSky = false;
                owner.SetActive(true);
                return Ready;
            }
            catch (Exception error)
            {
                Debug.LogWarning("Open-world terrain unavailable; using the compact fallback. " + error.Message, this);
                if (Terrain != null) Destroy(Terrain.gameObject);
                Terrain = null;
                return false;
            }
        }

        public float GroundHeight(Vector3 worldPosition)
        {
            float ground = Terrain.GetGroundY(worldPosition);
            var local = transform.InverseTransformPoint(worldPosition);
            // The physical outpost deck remains above the source surface.
            if (Mathf.Abs(local.x) <= GameConstants.BaseWidth / 2 && Mathf.Abs(local.z) <= GameConstants.BaseWidth / 2)
                ground = Mathf.Max(ground, transform.position.y + GameConstants.BaseDeckThickness);
            return ground;
        }

        public Vector3 ClampPosition(Vector3 worldPosition)
        {
            var local = Terrain.transform.InverseTransformPoint(worldPosition);
            var map = Terrain.Heightmap;
            local.x = Mathf.Clamp(local.x, -map.SizeX / 2 + GameConstants.PlayerRadius, map.SizeX / 2 - GameConstants.PlayerRadius);
            local.z = Mathf.Clamp(local.z, -map.SizeZ / 2 + GameConstants.PlayerRadius, map.SizeZ / 2 - GameConstants.PlayerRadius);
            return Terrain.transform.TransformPoint(local);
        }

        private void OnDestroy()
        {
            if (runtimeConfig != null)
            {
                if (Application.isPlaying) Destroy(runtimeConfig); else DestroyImmediate(runtimeConfig);
            }
        }
    }
}
