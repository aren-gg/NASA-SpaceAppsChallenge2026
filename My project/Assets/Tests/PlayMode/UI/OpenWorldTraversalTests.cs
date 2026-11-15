#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using MarsTrainer.Gameplay.Player;
using MarsTrainer.Gameplay.Radiation;
using MarsTrainer.Gameplay.World;
using MarsTrainer.Sim;
using MarsTrainer.UI;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace MarsTrainer.Tests
{
    public sealed class OpenWorldTraversalTests
    {
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Physics.autoSimulation = true;
            var scene = SceneManager.CreateScene("Empty traversal fixture"); SceneManager.SetActiveScene(scene);
            for (int i = SceneManager.sceneCount - 1; i >= 0; i--)
                if (SceneManager.GetSceneAt(i) != scene) yield return SceneManager.UnloadSceneAsync(SceneManager.GetSceneAt(i));
        }

        [UnityTest]
        public IEnumerator MainWalksBeyondTheOldArenaOnOriginalElevationAndKitBlocksMovement()
        {
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/Main.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            TerrainGenerator world = null; HudView hud = null;
            for (int frame = 0; frame < 600; frame++)
            {
                world = Object.FindObjectOfType<TerrainGenerator>(); hud = Object.FindObjectOfType<HudView>();
                if (world != null && world.Mission != null && hud != null && hud.Source != null) break;
                yield return null;
            }
            Assert.That(world, Is.Not.Null); Assert.That(world.StreamedTerrain, Is.Not.Null);
            Assert.That(world.StreamedTerrain.Heightmap.SizeX, Is.GreaterThan(200000));
            Assert.That(world.StreamedTerrain.config.realMetersPerUnityUnit, Is.EqualTo(1));
            Assert.That(world.StreamedTerrain.config.verticalExaggeration, Is.EqualTo(1));
            Assert.That(world.GetComponentInChildren<Camera>().farClipPlane,
                Is.GreaterThanOrEqualTo(world.StreamedTerrain.config.ToUnits(world.StreamedTerrain.config.renderDistanceMeters)),
                "The distant data terrain must be visible immediately, before the walker crosses a streaming threshold.");
            yield return null;
            Capture(world, hud, "briefing");
            hud.GetComponentInChildren<MissionBriefing>(true).Begin();
            world.enabled = false; world.GetComponentInChildren<RecallDrone>().enabled = false;
            var player = world.Player; player.enabled = false;
            var orbit = world.GetComponentInChildren<OrbitCamera>(); Assert.That(orbit, Is.Not.Null);
            float heading = player.ViewYaw;
            orbit.Orbit(90, 0); Assert.That(player.ViewYaw, Is.EqualTo((heading + 90) % GameConstants.FullOrbitDegrees)); orbit.Orbit(-90, 0);
            yield return null; yield return null;
            Capture(world, hud, "hud");
            var controller = player.GetComponent<CharacterController>(); controller.enabled = false;
            var start = world.transform.TransformPoint(new Vector3(0, 0, -180));
            start.y = player.WalkingSurface.GroundHeight(start) + GameConstants.PlayerSpawnHeight;
            player.transform.position = start; controller.enabled = true; Physics.SyncTransforms();
            Physics.autoSimulation = false;
            for (int i = 0; i < 20; i++) { player.Move(Vector2.down, false, .1f); Physics.Simulate(.1f); }
            long maxBytes = 0;
            for (int i = 0; i < 1000; i++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                player.Move(Vector2.down, false, .1f);
                maxBytes = Math.Max(maxBytes, GC.GetAllocatedBytesForCurrentThread() - before);
                Physics.Simulate(.1f);
                if (i % 60 == 0) yield return null;
            }
            Assert.That(player.transform.localPosition.z, Is.LessThan(-500), "Walking must extend beyond the old 400 m arena.");
            Assert.That(player.transform.position.y,
                Is.EqualTo(world.StreamedTerrain.GetGroundY(player.transform.position) + GameConstants.PlayerSpawnHeight).Within(.03f));
            Assert.That(maxBytes, Is.Zero, "Grounded open-world walking must not allocate per frame.");
            var map = world.StreamedTerrain.Heightmap;
            var outside = world.StreamedTerrain.transform.TransformPoint(new Vector3(map.SizeX, 0, map.SizeZ));
            Assert.That(world.StreamedTerrain.IsInsideTile(player.WalkingSurface.ClampPosition(outside)), Is.True);
            Debug.Log("OPEN_WORLD_WALK distance=" + Vector3.Distance(start, player.transform.position) +
                "; allocations=" + maxBytes + "; terrainTriangles=" + world.StreamedTerrain.Stats.triangles);
            yield return new WaitForSeconds(GameConstants.CameraDamping * 8);
            world.Mission.AtBase = player.Expedition.AtBase;
            world.Mission.Sheltered = player.Expedition.AtBase ||
                world.Simulation.Modifiers.Apply(GameConstants.RadiationShieldStat, 0) >= GameConstants.ShelterShield;
            hud.SendMessage("Refresh");
            Capture(world, hud, "terrain");
            hud.ToggleKit(); Assert.That(hud.KitOpen, Is.True); Assert.That(player.InputBlocked, Is.True);
            var position = player.transform.position; player.Move(Vector2.up, false, 1);
            Assert.That(player.transform.position, Is.EqualTo(position));
            Capture(world, hud, "kit");
            hud.ToggleKit(); Assert.That(hud.KitOpen, Is.False); Assert.That(player.InputBlocked, Is.False);
        }

        private static void Capture(TerrainGenerator world, HudView hud, string name)
        {
            string folder = Environment.GetEnvironmentVariable("MARS_OPENWORLD_OUTPUT") ?? Path.GetTempPath();
            Directory.CreateDirectory(folder);
            var camera = world.GetComponentInChildren<Camera>(); var canvas = hud.GetComponent<Canvas>();
            var scaler = hud.GetComponent<CanvasScaler>();
            var target = new RenderTexture(1920, 1080, 24); target.Create();
            camera.targetTexture = target;
            canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = 1;
            scaler.enabled = false; canvas.scaleFactor = 1;
            Canvas.ForceUpdateCanvases(); camera.Render();
            var previous = RenderTexture.active; RenderTexture.active = target;
            var image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0); image.Apply();
            File.WriteAllBytes(Path.Combine(folder, "open-world-" + name + ".png"), image.EncodeToPNG());
            RenderTexture.active = previous; canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            scaler.enabled = true; camera.targetTexture = null; target.Release();
            Object.Destroy(target); Object.Destroy(image);
        }
    }
}
#endif
