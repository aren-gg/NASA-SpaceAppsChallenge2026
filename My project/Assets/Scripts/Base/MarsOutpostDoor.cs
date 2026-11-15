using UnityEngine;

namespace MarsSurvival.Base
{
    /// <summary>
    /// A two-leaf sliding door that opens when the astronaut comes close and closes
    /// behind them. Airlock doors are paired through <see cref="partner"/>: one stays
    /// shut while the other is open, as on a real airlock.
    /// </summary>
    public sealed class MarsOutpostDoor : MonoBehaviour
    {
        public Transform leafA, leafB;
        [Tooltip("Direction the leaves slide apart, in this object's local space.")]
        public Vector3 slideAxis = Vector3.right;
        [Tooltip("Rest offset of each leaf from the door centre, along the slide axis (metres).")]
        public float leafOffset = 0.33f;
        public float travel = 0.62f;
        public BoxCollider blocker;
        public MarsOutpostDoor partner;
        [Tooltip("The door opens when the astronaut is within this distance (metres).")]
        public float openRange = 1.9f;
        public float secondsToOpen = 0.7f;

        /// <summary>0 shut, 1 fully open.</summary>
        public float Open01 { get; private set; }
        public bool Opening { get; private set; }

        private void Update()
        {
            Transform viewer = MarsOutpost.Viewer;
            bool near = false;
            if (viewer != null)
            {
                Vector3 local = transform.InverseTransformPoint(viewer.position);
                near = new Vector2(local.x, local.z).magnitude < openRange && Mathf.Abs(local.y) < 4f;
            }
            // Wait for the other airlock door to shut first.
            Opening = near && (partner == null || (!partner.Opening && partner.Open01 <= 0.001f) || Open01 > 0f);
            Open01 = Mathf.MoveTowards(Open01, Opening ? 1f : 0f, Time.deltaTime / Mathf.Max(0.05f, secondsToOpen));

            float eased = Mathf.SmoothStep(0f, 1f, Open01);
            if (leafA != null) leafA.localPosition = slideAxis * -(leafOffset + travel * eased);
            if (leafB != null) leafB.localPosition = slideAxis * (leafOffset + travel * eased);
            if (blocker != null) blocker.enabled = Open01 < 0.75f;
        }
    }
}
