using System;
using MarsSurvival.Base;
using MarsSurvival.Interaction;
using MarsSurvival.Simulation;
using UnityEngine;

namespace MarsSurvival.Gameplay
{
    /// <summary>Works out where the astronaut is (outdoors, habitat, which room) for the mission rules.</summary>
    public sealed class MissionWorld : MonoBehaviour
    {
        private void Awake()
        {
            if (GetComponent<MarsWorldTasks>() == null) gameObject.AddComponent<MarsWorldTasks>();
        }

        public PlayerContext GetContext(MarsAstronautPlayer player, MarsOutpost outpost)
        {
            Vector3 position = player.transform.position;
            Vector3 p = outpost.transform.InverseTransformPoint(position);
            var ctx = new PlayerContext { zone = PlayerZone.Outdoors, station = "", moving = player.Speed > 0.05, sprinting = player.IsSprinting, distanceFromBase = Vector3.Distance(position, outpost.DoorPosition) };
            if (p.y > -0.5f && p.y < 3.5f)
            {
                if (MarsOutpostBuilder.Airlock.Contains(p.x, p.z)) { ctx.zone = PlayerZone.Airlock; ctx.station = "airlock"; }
                else
                {
                    foreach (var room in outpost.Rooms) if (room.Contains(p.x, p.z)) { ctx.zone = PlayerZone.Habitat; ctx.station = "habitat"; break; }
                    // the tunnels between the wings
                    if ((Math.Abs(p.z) < 1.5 && p.x > -16 && p.x < 16) || (Math.Abs(p.x) < 1.5 && p.z > -18 && p.z < 16.75)) ctx.zone = PlayerZone.Habitat;
                    if (MarsOutpostBuilder.Greenhouse.Contains(p.x, p.z)) ctx.station = "greenhouse";
                }
            }
            return ctx;
        }
    }
}
