using System.Collections.Generic;
using MarsSurvival.Base;
using UnityEngine;
using static MarsSurvival.Base.OutpostGeometry;

namespace MarsSurvival.Vehicles
{
    /// <summary>
    /// Builds the cargo rover in code: a long white pressurised hull with a rounded nose,
    /// a dark wrap-around windscreen, solar cells on the roof and four large wheels on a
    /// dark chassis. Flat-shaded, in the same materials as the outpost. The shape is
    /// drawn at a small size and enlarged by <see cref="Scale"/>, so the cabin is tall
    /// enough for a 1.8 m astronaut to stand in. The sizes below are the finished ones,
    /// in metres; the origin is on the ground between the wheels and +Z is forward.
    /// </summary>
    public static class MarsRoverModel
    {
        /// <summary>Enlargement of the drawn shape. At 1.6 the cabin is 2.16 m from floor to roof.</summary>
        public const float Scale = 1.6f;
        private const float DrawnRadius = 0.62f, DrawnBase = 3.5f, DrawnTrack = 2.9f;

        public const float WheelRadius = DrawnRadius * Scale;
        public const float WheelBase = DrawnBase * Scale;   // front axle to rear axle
        public const float Track = DrawnTrack * Scale;      // left wheel centre to right wheel centre
        public const float Length = 6.3f * Scale, Width = 3.45f * Scale, Height = 2.45f * Scale;

        public sealed class Parts
        {
            /// <summary>Front-left, front-right, rear-left, rear-right. Steering turns these; suspension moves them up and down.</summary>
            public Transform[] wheelPivots = new Transform[4];
            /// <summary>The spinning part of each wheel, a child of its pivot.</summary>
            public Transform[] wheelSpin = new Transform[4];
            /// <summary>The enlarged part that holds the body and wheels; wheel pivots are placed in its space.</summary>
            public Transform model;
            /// <summary>Wheel centre positions at rest, in the rover's local space (finished size).</summary>
            public Vector3[] wheelRest = new Vector3[4];
            public Transform headlightAnchor;
        }

        public static Parts Build(Transform root, Material[] materials, List<Object> owned)
        {
            Parts parts = new Parts();
            GameObject model = new GameObject("Model");
            model.transform.SetParent(root, false);
            model.transform.localScale = Vector3.one * Scale;
            parts.model = model.transform;
            root = model.transform;
            OutpostGeometry g = new OutpostGeometry();

            // Hull: a skin over cross-sections from the tapered tail to the rounded nose.
            g.Loft(White, new[]
            {
                Section(-3.0f, 0.80f, 1.20f, 1.95f, 0.25f, 0.30f),
                Section(-2.55f, 1.15f, 0.95f, 2.30f, 0.30f, 0.45f),
                Section(1.40f, 1.15f, 0.95f, 2.30f, 0.30f, 0.45f),
                Section(2.35f, 1.10f, 1.00f, 2.20f, 0.30f, 0.45f),
                Section(3.0f, 0.80f, 1.15f, 1.75f, 0.25f, 0.30f),
            });
            // Wrap-around windscreen, standing slightly proud of the nose.
            g.Loft(Charcoal, new[]
            {
                Section(1.50f, 1.17f, 1.50f, 1.95f, 0.08f, 0.08f),
                Section(2.35f, 1.12f, 1.45f, 1.90f, 0.08f, 0.08f),
                Section(3.03f, 0.82f, 1.32f, 1.68f, 0.08f, 0.08f),
            });
            // Dark red waistline and charcoal side hatches.
            for (int side = -1; side <= 1; side += 2)
            {
                float x = side * 1.15f;
                g.Box(DarkRed, Mathf.Min(x, x + side * 0.02f), 1.28f, -2.5f, Mathf.Max(x, x + side * 0.02f), 1.38f, 1.4f);
                g.Box(Charcoal, Mathf.Min(x, x + side * 0.03f), 1.45f, -0.5f, Mathf.Max(x, x + side * 0.03f), 2.0f, 0.35f);
                g.Box(Charcoal, Mathf.Min(x, x + side * 0.03f), 1.45f, -1.6f, Mathf.Max(x, x + side * 0.03f), 1.8f, -0.9f);
                // Step below the hatch.
                g.Box(Charcoal, Mathf.Min(x, x + side * 0.3f), 0.95f, -0.45f, Mathf.Max(x, x + side * 0.3f), 1.0f, 0.3f);
            }

            // Roof: two strips of solar cells, an equipment box, sensors, an antenna and beacons.
            const float roof = 2.3f;
            g.Box(SolarBlue, -0.66f, roof, -2.2f, -0.05f, roof + 0.04f, 0.5f);
            g.Box(SolarBlue, 0.05f, roof, -2.2f, 0.66f, roof + 0.04f, 0.5f);
            for (int i = 0; i < 4; i++)
            {
                float z = -2.2f + i * 0.9f;
                g.Box(White, -0.68f, roof, z - 0.02f, 0.68f, roof + 0.05f, z + 0.02f);
            }
            g.Box(Charcoal, -0.3f, roof, 0.75f, 0.3f, roof + 0.22f, 1.25f);
            g.Prism(White, -0.4f, 1.75f, 0.16f, roof - 0.05f, roof + 0.1f, 8, 0.1f);
            g.Prism(White, 0.4f, 1.75f, 0.16f, roof - 0.05f, roof + 0.1f, 8, 0.1f);
            g.Prism(Charcoal, 0.55f, 1.0f, 0.03f, roof, roof + 0.9f, 6);
            foreach (float z in new[] { -2.4f, 1.3f })
            {
                g.Box(GlowOrange, -0.72f, roof - 0.02f, z, -0.62f, roof + 0.06f, z + 0.1f);
                g.Box(GlowOrange, 0.62f, roof - 0.02f, z, 0.72f, roof + 0.06f, z + 0.1f);
            }

            // Chassis, bumper, headlights, tail lights and the arms out to the wheels.
            g.Box(Charcoal, -0.8f, 0.55f, -2.45f, 0.8f, 0.98f, 2.45f);
            g.Box(Charcoal, -1.0f, 0.72f, 2.85f, 1.0f, 0.95f, 3.15f);
            g.Box(Charcoal, -0.9f, 0.75f, -3.1f, 0.9f, 0.95f, -2.85f);
            g.Box(GlowWarm, -0.62f, 1.17f, 3.0f, -0.38f, 1.29f, 3.04f);
            g.Box(GlowWarm, 0.38f, 1.17f, 3.0f, 0.62f, 1.29f, 3.04f);
            g.Box(GlowOrange, -0.6f, 1.3f, -3.03f, -0.4f, 1.4f, -3.0f);
            g.Box(GlowOrange, 0.4f, 1.3f, -3.03f, 0.6f, 1.4f, -3.0f);
            float halfTrack = DrawnTrack * 0.5f, halfBase = DrawnBase * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f, z = i < 2 ? halfBase : -halfBase;
                Vector3 drawnRest = new Vector3(side * halfTrack, DrawnRadius, z);
                parts.wheelRest[i] = drawnRest * Scale;
                g.Box(White, Mathf.Min(side * 0.75f, side * 1.2f), 0.55f, z - 0.16f, Mathf.Max(side * 0.75f, side * 1.2f), 0.78f, z + 0.16f);
                // Mudguard over each wheel.
                g.Box(White, Mathf.Min(side * 1.05f, side * 1.72f), 1.32f, z - 0.55f, Mathf.Max(side * 1.05f, side * 1.72f), 1.38f, z + 0.55f);
            }

            Material[] used;
            Mesh body = g.ToMesh("Rover Body", materials, out used);
            owned.Add(body);
            GameObject bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(root, false);
            bodyObject.AddComponent<MeshFilter>().sharedMesh = body;
            bodyObject.AddComponent<MeshRenderer>().sharedMaterials = used;

            Material[] wheelMaterials;
            Mesh wheel = WheelMesh(materials, out wheelMaterials);
            owned.Add(wheel);
            string[] names = { "Wheel Front Left", "Wheel Front Right", "Wheel Rear Left", "Wheel Rear Right" };
            for (int i = 0; i < 4; i++)
            {
                GameObject pivot = new GameObject(names[i]);
                pivot.transform.SetParent(root, false);
                pivot.transform.localPosition = parts.wheelRest[i] / Scale;
                GameObject spin = new GameObject("Spin");
                spin.transform.SetParent(pivot.transform, false);
                spin.AddComponent<MeshFilter>().sharedMesh = wheel;
                spin.AddComponent<MeshRenderer>().sharedMaterials = wheelMaterials;
                parts.wheelPivots[i] = pivot.transform;
                parts.wheelSpin[i] = spin.transform;
            }

            GameObject lamp = new GameObject("Headlights");
            lamp.transform.SetParent(root, false);
            lamp.transform.localPosition = new Vector3(0f, 1.25f, 3.1f);
            parts.headlightAnchor = lamp.transform;
            return parts;
        }

        /// <summary>A hull cross-section at z: a rectangle with its corners cut, eight points going round.</summary>
        private static Vector3[] Section(float z, float halfWidth, float bottom, float top, float lowerCut, float upperCut)
        {
            return new[]
            {
                new Vector3(-halfWidth + lowerCut, bottom, z), new Vector3(halfWidth - lowerCut, bottom, z),
                new Vector3(halfWidth, bottom + lowerCut, z), new Vector3(halfWidth, top - upperCut, z),
                new Vector3(halfWidth - upperCut, top, z), new Vector3(-halfWidth + upperCut, top, z),
                new Vector3(-halfWidth, top - upperCut, z), new Vector3(-halfWidth, bottom + lowerCut, z),
            };
        }

        /// <summary>A wheel on the x axis: dark tyre with chunky tread, white hub, dark hub bolts.</summary>
        private static Mesh WheelMesh(Material[] materials, out Material[] used)
        {
            OutpostGeometry g = new OutpostGeometry();
            const float r = DrawnRadius, half = 0.28f;
            g.Loft(Charcoal, new[]
            {
                WheelRing(-half, r * 0.82f), WheelRing(-half * 0.75f, r * 0.96f),
                WheelRing(half * 0.75f, r * 0.96f), WheelRing(half, r * 0.82f),
            });
            g.Loft(White, new[] { WheelRing(-half - 0.02f, r * 0.5f), WheelRing(half + 0.02f, r * 0.5f) });
            g.Loft(Charcoal, new[] { WheelRing(-half - 0.04f, r * 0.16f), WheelRing(half + 0.04f, r * 0.16f) });
            for (int i = 0; i < 12; i++)
            {
                float angle = i * Mathf.PI / 6f;
                float c = Mathf.Cos(angle), s = Mathf.Sin(angle);
                // Tread block: a short bar across the tyre, pointing outward.
                Vector3 inner = new Vector3(0f, c * r * 0.94f, s * r * 0.94f), outer = new Vector3(0f, c * r, s * r);
                Vector3 along = new Vector3(0f, -s, c) * 0.07f, across = new Vector3(half * 0.7f, 0f, 0f);
                g.Hexa(Charcoal,
                    inner - along - across, inner + along - across, inner + along + across, inner - along + across,
                    outer - along - across, outer + along - across, outer + along + across, outer - along + across);
                if (i % 3 == 0)
                {
                    Vector3 bolt = new Vector3(0f, c * r * 0.33f, s * r * 0.33f);
                    g.Box(Charcoal, -half - 0.035f, bolt.y - 0.04f, bolt.z - 0.04f, half + 0.035f, bolt.y + 0.04f, bolt.z + 0.04f);
                }
            }
            return g.ToMesh("Rover Wheel", materials, out used);
        }

        private static Vector3[] WheelRing(float x, float radius)
        {
            Vector3[] ring = new Vector3[12];
            for (int i = 0; i < 12; i++)
            {
                float angle = (i + 0.5f) * Mathf.PI / 6f;
                ring[i] = new Vector3(x, Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
            }
            return ring;
        }
    }
}
