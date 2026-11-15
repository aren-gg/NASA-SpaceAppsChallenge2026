using System;
using System.Collections;
using System.IO;
using MarsTrainer.Gameplay.World;
using MarsTrainer.Sim;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.TestTools;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace MarsTrainer.Tests
{
    public sealed class WorldRouteTests
    {
        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            Physics.autoSimulation = true;
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            yield return null;
        }

        [UnityTest]
        public IEnumerator WalkFromBaseThroughCaveCollectResearchAndReturn()
        {
#if UNITY_EDITOR
            EditorSceneManager.LoadSceneInPlayMode("Assets/Scenes/P9World.unity", new LoadSceneParameters(LoadSceneMode.Single));
            yield return null;
            var world = UnityEngine.Object.FindObjectOfType<TerrainGenerator>();
            for (int frame = 0; frame < 600 && (world == null || world.Player == null); frame++)
            {
                yield return null;
                world = UnityEngine.Object.FindObjectOfType<TerrainGenerator>();
            }
            Assert.That(world, Is.Not.Null);
            Assert.That(world.Player, Is.Not.Null);
            world.enabled = false;
            world.Player.enabled = false;
            var controller = world.Player;
            var sim = world.Simulation;
            // Warm render/physics/input caches before recording the actual route.
            for (int i = 0; i < 30; i++) { controller.Move(Vector2.zero, false, 1f / 60); yield return null; }
            Physics.autoSimulation = false;
            double rp = world.Research.State.RP;
            long maxMoveBytes = 0;
            int frames = 0;
            float maxFrame = 0;
            var cave = world.CavePositions[0];
            var direction = cave.normalized;
            var entrance = cave - direction * GameConstants.CaveDepth;
            var targets = new[] { entrance, cave, direction * GameConstants.BaseFlatRadius / 2 };
            string profile = Path.Combine(Path.GetTempPath(), "p9-world-profile.raw");
            Profiler.logFile = profile;
            Profiler.enableBinaryLog = true;
            Profiler.enabled = true;
            for (int waypoint = 0; waypoint < targets.Length; waypoint++)
            {
                for (int step = 0; step < 3000; step++)
                {
                    var position = controller.transform.localPosition;
                    var delta = targets[waypoint] - new Vector2(position.x, position.z);
                    if (delta.magnitude < 0.4f) break;
                    const float dt = 1f / 60;
                    long before = GC.GetAllocatedBytesForCurrentThread();
                    Profiler.BeginSample("P9 movement and simulation");
                    controller.Move(delta.normalized, false, dt);
                    // The integrated mission consumes carried provisions during a multi-sol EVA.
                    if (sim.State.Water < 50) sim.Survival.Drink(sim.State, GameConstants.WaterLitresPerSol);
                    if (sim.State.Hunger > 50) sim.Survival.Eat(sim.State, GameConstants.AstronautKcalPerSol);
                    sim.Tick(dt);
                    Physics.Simulate(dt);
                    Profiler.EndSample();
                    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                    maxMoveBytes = Math.Max(maxMoveBytes, allocated);
                    frames++;
                    maxFrame = Mathf.Max(maxFrame, Time.unscaledDeltaTime);
                    if (!sim.State.Alive) Assert.Fail("The route must fit the oxygen budget.");
                    if (step == 2999) Assert.Fail("CharacterController did not reach the waypoint.");
                    yield return null;
                }
                if (waypoint == 1)
                {
                    Assert.That(world.Research.State.RP, Is.GreaterThanOrEqualTo(rp + GameConstants.PickupRP));
                    Assert.That(sim.Modifiers.Apply(GameConstants.RadiationShieldStat, 0), Is.GreaterThanOrEqualTo(GameConstants.ShelterShield));
                }
            }
            Profiler.enabled = false;
            Profiler.enableBinaryLog = false;
            Assert.That(controller.Expedition.AtBase, Is.True);
            Assert.That(controller.Expedition.TankSeconds, Is.EqualTo(GameConstants.OxygenTankSeconds));
            Debug.Log("P9 route passed; frames=" + frames + "; max movement/sim allocation=" + maxMoveBytes +
                " B; max editor frame=" + maxFrame + " s; profile=" + profile);
            Assert.That(maxMoveBytes, Is.Zero, "P9 movement/simulation should allocate no managed memory after warmup.");
            yield return new WaitForSeconds(1);
            var camera = world.GetComponentInChildren<Camera>();
            var target = new RenderTexture(960, 600, 24);
            var image = new Texture2D(960, 600, TextureFormat.RGB24, false);
            var previous = RenderTexture.active;
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0);
            image.Apply();
            File.WriteAllBytes(Path.Combine(Path.GetTempPath(), "p9-world.png"), image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = previous;
            UnityEngine.Object.Destroy(target);
            UnityEngine.Object.Destroy(image);
#else
            yield return null;
            Assert.Ignore("Scene-loading route harness runs in the Unity Editor.");
#endif
        }
    }
}
