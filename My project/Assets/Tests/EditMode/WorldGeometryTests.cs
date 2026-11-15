using System;
using MarsTrainer.Gameplay.World;
using MarsTrainer.Sim;
using MarsTrainer.Sim.Research;
using NUnit.Framework;
using UnityEngine;

namespace MarsTrainer.Tests
{
    public sealed class WorldGeometryTests
    {
        private GameObject owner;
        [TearDown] public void Cleanup() { if (owner != null) UnityEngine.Object.DestroyImmediate(owner); }

        [Test]
        public void FlatMeshDuplicatesVerticesAndUsesHardNormals()
        {
            var mesh = FlatShadedMesh.Create(new[] { Vector3.zero, Vector3.right, Vector3.forward, Vector3.up },
                new[] { 0, 2, 1, 0, 1, 3 });
            Assert.That(mesh.vertexCount, Is.EqualTo(6));
            Assert.That(Vector3.Dot(mesh.normals[0], mesh.normals[3]), Is.LessThan(0.1f));
            Assert.That(mesh.normals[0], Is.EqualTo(mesh.normals[1]));
            UnityEngine.Object.DestroyImmediate(mesh);
        }

        [Test]
        public void RockFacesPointOutward()
        {
            var rock = FlatShadedMesh.Rock();
            var vertices = rock.vertices;
            var normals = rock.normals;
            for (int i = 0; i < vertices.Length; i++) Assert.That(Vector3.Dot(vertices[i], normals[i]), Is.GreaterThan(0));
            UnityEngine.Object.DestroyImmediate(rock);
        }

        [Test]
        public void ScatterIsSeededSpacedAndKeepsBaseAndCavesClear()
        {
            var caves = new[] { new Vector2(80, 0), new Vector2(-80, 0) };
            var a = PropScatter.Sample(42, caves);
            var b = PropScatter.Sample(42, caves);
            Assert.That(a.Count, Is.GreaterThan(GameConstants.PropCount / 2));
            Assert.That(a, Is.EqualTo(b));
            for (int i = 0; i < a.Count; i++)
            {
                Assert.That(a[i].magnitude, Is.GreaterThanOrEqualTo(GameConstants.BaseClearRadius));
                foreach (var cave in caves)
                    Assert.That(Vector2.Distance(a[i], cave), Is.GreaterThanOrEqualTo(GameConstants.CaveClearRadius));
                for (int j = 0; j < i; j++)
                    Assert.That(Vector2.Distance(a[i], a[j]), Is.GreaterThanOrEqualTo(GameConstants.PropSpacing));
            }
        }

        [Test]
        public void PickupCreditsResearchOnlyOnceAndRejectsPausedCollection()
        {
            owner = new GameObject("Pickup");
            var sim = new Simulation();
            var research = new ResearchSystem(Array.Empty<ResearchNode>(), sim.Modifiers, 0);
            var player = new GameObject("Player");
            player.transform.SetParent(owner.transform);
            var pickup = owner.AddComponent<ResearchPickup>();
            pickup.Initialize(sim, research, player.transform);
            sim.Clock.Paused = true;
            Assert.That(pickup.TryCollect(player.transform), Is.False);
            sim.Clock.Paused = false;
            Assert.That(pickup.TryCollect(player.transform), Is.True);
            Assert.That(pickup.TryCollect(player.transform), Is.False);
            Assert.That(research.State.RP, Is.EqualTo(GameConstants.PickupRP));
        }

        [Test]
        public void TerrainHasContinuousChunksClearBaseAndBoundedTriangles()
        {
            owner = new GameObject("World");
            var terrain = owner.AddComponent<TerrainGenerator>();
            var sim = new Simulation();
            var research = new ResearchSystem(Array.Empty<ResearchNode>(), sim.Modifiers, 0);
            terrain.Build(sim, research, 42);
            Assert.That(terrain.CavePositions.Count, Is.InRange(GameConstants.CaveMinCount, GameConstants.CaveMaxCount));
            Assert.That(terrain.SampleHeight(0, 0), Is.EqualTo(terrain.SampleHeight(5, 0)).Within(0.01f));
            int triangles = 0;
            foreach (var filter in owner.GetComponentsInChildren<MeshFilter>())
            {
                triangles += filter.sharedMesh.triangles.Length / 3;
                if (filter.name == "Crater rim")
                    foreach (var normal in filter.sharedMesh.normals) Assert.That(normal.y, Is.GreaterThan(0));
                if (!filter.name.StartsWith("Terrain chunk")) continue;
                foreach (var vertex in filter.sharedMesh.vertices)
                {
                    if (vertex.x != 0 && vertex.x != GameConstants.WorldSize / 4) continue;
                    var point = vertex + filter.transform.localPosition;
                    Assert.That(point.y, Is.EqualTo(terrain.SampleHeight(point.x, point.z)).Within(0.0001f));
                }
            }
            Debug.Log("P9 complete world triangles: " + triangles);
            Assert.That(triangles, Is.LessThanOrEqualTo(GameConstants.WorldTriangleBudget));
            Assert.That(owner.GetComponentsInChildren<MeshCollider>().Length, Is.EqualTo(16));
            foreach (var cave in terrain.CavePositions)
                Assert.That(cave.magnitude, Is.InRange(GameConstants.CaveMinDistance, GameConstants.CaveMaxDistance));
            Assert.That(owner.GetComponentsInChildren<CaveShelter>().Length, Is.EqualTo(terrain.CavePositions.Count));
            Assert.That(owner.GetComponentsInChildren<ResearchPickup>().Length, Is.EqualTo(terrain.CavePositions.Count));
        }
    }
}
