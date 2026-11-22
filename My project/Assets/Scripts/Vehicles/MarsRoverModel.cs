using System.Collections.Generic;
using MarsSurvival.Base;
using UnityEngine;
using static MarsSurvival.Base.OutpostGeometry;

namespace MarsSurvival.Vehicles
{
    /// <summary>
    /// Assembles the rover: a futuristic wedge body and four off-road wheels (Blender models in
    /// Resources/Garden) on code-built arms, with a pivot and a spinning part per wheel for the
    /// suspension and steering. Sizes are in metres; the origin is on the ground between the
    /// wheels and +Z is forward.
    /// </summary>
    public static class MarsRoverModel
    {
        /// <summary>Enlargement of the drawn shape: 1, so the rover (6.3 x 3.45 x 2.45 m) fits the garage and its 3.6 m door.</summary>
        public const float Scale = 1f;
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

            // Body and wheels are Blender models (tools/blender/garden_assets.py: rover_body, rover_wheel); the
            // code draws only the arms out to the wheels, which the suspension moves.
            MarsSurvival.Gameplay.MarsWorldTasks.Model("RoverBody", root);
            float halfTrack = DrawnTrack * 0.5f, halfBase = DrawnBase * 0.5f;
            for (int i = 0; i < 4; i++)
            {
                float side = i % 2 == 0 ? -1f : 1f, z = i < 2 ? halfBase : -halfBase;
                Vector3 drawnRest = new Vector3(side * halfTrack, DrawnRadius, z);
                parts.wheelRest[i] = drawnRest * Scale;
                g.Box(White, Mathf.Min(side * 0.75f, side * 1.2f), 0.55f, z - 0.16f, Mathf.Max(side * 0.75f, side * 1.2f), 0.78f, z + 0.16f);
            }

            Material[] used;
            Mesh body = g.ToMesh("Rover Body", materials, out used);
            owned.Add(body);
            GameObject bodyObject = new GameObject("Body");
            bodyObject.transform.SetParent(root, false);
            bodyObject.AddComponent<MeshFilter>().sharedMesh = body;
            bodyObject.AddComponent<MeshRenderer>().sharedMaterials = used;

            string[] names = { "Wheel Front Left", "Wheel Front Right", "Wheel Rear Left", "Wheel Rear Right" };
            for (int i = 0; i < 4; i++)
            {
                GameObject pivot = new GameObject(names[i]);
                pivot.transform.SetParent(root, false);
                pivot.transform.localPosition = parts.wheelRest[i] / Scale;
                GameObject spin = new GameObject("Spin");
                spin.transform.SetParent(pivot.transform, false);
                Transform tyre = MarsSurvival.Gameplay.MarsWorldTasks.Model("RoverWheel", spin.transform);
                if (i % 2 == 0) tyre.localRotation = Quaternion.Euler(0f, 180f, 0f) * tyre.localRotation;   // left wheels face out too
                parts.wheelPivots[i] = pivot.transform;
                parts.wheelSpin[i] = spin.transform;
            }

            GameObject lamp = new GameObject("Headlights");
            lamp.transform.SetParent(root, false);
            lamp.transform.localPosition = new Vector3(0f, 1.25f, 3.1f);
            parts.headlightAnchor = lamp.transform;
            return parts;
        }
    }
}
