using System;
using System.Collections.Generic;
using MarsSurvival.Base;
using MarsSurvival.Interaction;
using MarsSurvival.Simulation;
using MarsSurvival.Vehicles;
using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.Gameplay
{
    public sealed class MissionWorld : MonoBehaviour
    {
        private Transform sitesRoot;
        private Transform equipmentRoot;
        private string visualKey = "";
        private readonly List<Transform> siteMarkers = new List<Transform>();
        private readonly List<float> sitePop = new List<float>();
        private MarsAstronautPlayer viewer;
        private MarsRover viewerRover;
        [Tooltip("Survey site indicators appear only within this distance (metres) of the astronaut or rover.")]
        public float siteRevealMeters = 30f;
        private void Update()
        {
            var controller = MarsMissionController.Instance;
            if (controller == null || controller.Simulation == null) return;
            UpdateSiteVisibility();
            var state = controller.Simulation.State;
            var outpost = FindObjectOfType<MarsOutpost>();
            if (outpost == null || !outpost.IsPlaced) return;
            string key = state.equipment.extraPanels + ":" + state.equipment.extraBatteries + ":" + string.Join(",", state.installed);
            foreach (var crop in state.crops) key += ":" + crop.planted + ":" + (int)(crop.growth * 4);
            foreach (var site in state.sites) key += ":" + site.markerPlaced;
            if (key == visualKey) return;
            visualKey = key;
            if (equipmentRoot != null) Destroy(equipmentRoot.gameObject);
            equipmentRoot = new GameObject("Installed mission equipment").transform;
            equipmentRoot.SetParent(outpost.transform, false);
            for (int i = 0; i < state.equipment.extraPanels; i++) Prop("Solar expansion", new Vector3(8 + i * 4, 1, 19), new Vector3(3, 0.15f, 2), MarsPalette.DeepPurple);
            for (int i = 0; i < state.equipment.extraBatteries; i++) Prop("Battery cabinet", new Vector3(8 + i * 1.5f, 1, -1), new Vector3(1, 2, 0.8f), MarsPalette.DeepPurple);
            foreach (var crop in state.crops)
                if (crop.planted) for (int i = 0; i < 6; i++)
                    Prop("Crop growth", new Vector3(-2 + crop.bed * 3, 0.7f + (float)crop.growth * 0.4f, -9 - i), new Vector3(0.4f, 0.1f + (float)crop.growth * 0.8f, 0.4f), MarsPalette.MutedOlive);
            foreach (var site in state.sites)
                if (site.markerPlaced)
                {
                    Vector3 local = outpost.transform.InverseTransformPoint(new Vector3((float)site.x, (float)site.y + 1.5f, (float)site.z));
                    Prop("Survey flag " + site.id, local, new Vector3(1, 0.5f, 0.05f), MarsPalette.WarmOrange);
                }
            if (state.installed.Contains("H2")) Prop("Medical upgrade", new Vector3(-10, 1, -1), new Vector3(0.8f, 1.2f, 0.6f), MarsPalette.WarmOrange);
            if (state.installed.Contains("P1")) Prop("Shelter shielding upgrade", new Vector3(-24, 3, 0), new Vector3(5, 0.5f, 5), MarsPalette.Terracotta);
        }
        private void Prop(string title, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = title;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(equipmentRoot, false); go.transform.localPosition = position; go.transform.localScale = scale;
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = color;
            go.GetComponent<Renderer>().material = material;
            go.AddComponent<MissionMaterialCleanup>().Owned = material;
        }
        public void CreateSites(MarsTerrain terrain, MarsOutpost outpost, MissionState state)
        {
            if (sitesRoot != null) Destroy(sitesRoot.gameObject);
            sitesRoot = new GameObject("Science sites").transform;
            siteMarkers.Clear();
            sitePop.Clear();
            visualKey = "";
            if (state.sites.Count == 0)
            {
                var random = new System.Random(state.seed);
                for (int i = 0; i < 16; i++)
                {
                    for (int attempt = 0; attempt < 100; attempt++)
                    {
                        float angle = (float)(random.NextDouble() * Math.PI * 2);
                        float radius = i < 5 ? 45 + i * 12 : 150 + (i - 5) * 160;
                        Vector3 p = outpost.transform.position + new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)) * radius;
                        if (!terrain.IsInsideTile(p) || terrain.GetSlopeDegrees(p) > 15) continue;
                        bool reachable = true;
                        for (int n = 1; n <= 30; n++) if (terrain.GetSlopeDegrees(Vector3.Lerp(outpost.DoorPosition, p, n / 30f)) > 20) reachable = false;
                        if (!reachable) continue;
                        p.y = terrain.GetGroundY(p); Vector2 ll = terrain.GetLatLon(p);
                        state.sites.Add(new SiteState { id = "site_" + i, name = "Survey " + (i + 1), x = p.x, y = p.y, z = p.z, latitude = ll.x, longitude = ll.y, distanceMeters = radius, reward = 20 + i * 3 });
                        terrain.ClearRocks(p, 3); break;
                    }
                }
            }
            foreach (var site in state.sites)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder); go.name = site.id; go.transform.SetParent(sitesRoot);
                go.transform.position = new Vector3((float)site.x, (float)site.y + 0.8f, (float)site.z); go.transform.localScale = new Vector3(0.25f, 0.8f, 0.25f);
                var label = new GameObject("Label").AddComponent<TextMesh>(); label.transform.SetParent(go.transform, false); label.transform.localPosition = new Vector3(0, 1.4f, 0); label.text = site.name; label.characterSize = 0.45f; label.fontSize = 32;
                label.anchor = TextAnchor.LowerCenter;
                go.SetActive(false);
                siteMarkers.Add(go.transform);
                sitePop.Add(0f);
            }
        }
        /// <summary>
        /// Survey indicators stay hidden until the astronaut (or the rover being driven) comes
        /// within <see cref="siteRevealMeters"/>, then grow in and turn their label to face the viewer.
        /// </summary>
        private void UpdateSiteVisibility()
        {
            if (siteMarkers.Count == 0) return;
            if (viewer == null) viewer = FindObjectOfType<MarsAstronautPlayer>();
            if (viewer == null) return;
            if (viewerRover == null) viewerRover = FindObjectOfType<MarsRover>();
            Vector3 from = viewer.IsDriving && viewerRover != null ? viewerRover.transform.position : viewer.transform.position;
            Camera cam = viewer.ViewCamera != null ? viewer.ViewCamera : Camera.main;
            for (int i = 0; i < siteMarkers.Count; i++)
            {
                Transform marker = siteMarkers[i];
                if (marker == null) continue;
                Vector3 offset = marker.position - from; offset.y = 0f;
                bool near = offset.magnitude <= siteRevealMeters;
                sitePop[i] = Mathf.MoveTowards(sitePop[i], near ? 1f : 0f, Time.deltaTime * 3f);
                bool show = sitePop[i] > 0.001f;
                if (marker.gameObject.activeSelf != show) marker.gameObject.SetActive(show);
                if (!show) continue;
                float grow = Mathf.SmoothStep(0f, 1f, sitePop[i]);
                marker.localScale = new Vector3(0.25f, 0.8f, 0.25f) * grow;
                if (cam != null && marker.childCount > 0)
                {
                    Transform label = marker.GetChild(0);
                    Vector3 look = label.position - cam.transform.position; look.y = 0f;
                    if (look.sqrMagnitude > 1e-4f) label.rotation = Quaternion.LookRotation(look);
                }
            }
        }
        public PlayerContext GetContext(MarsAstronautPlayer player, MarsRover rover, MarsOutpost outpost, MarsTerrain terrain, MissionState state)
        {
            Vector3 position = player.IsDriving && rover != null ? rover.transform.position : player.transform.position;
            Vector3 p = outpost.transform.InverseTransformPoint(position);
            var ctx = new PlayerContext { zone = PlayerZone.Outdoors, station = "", moving = player.Speed > 0.05, sprinting = player.IsSprinting, driving = player.IsDriving, distanceFromBase = Vector3.Distance(position, outpost.DoorPosition) };
            if (!ctx.driving && p.y > -0.5f && p.y < 3.5f)
            {
                if (MarsOutpostBuilder.Shelter.Contains(p.x, p.z)) { ctx.zone = PlayerZone.Shelter; ctx.station = "shelter"; }
                else if (MarsOutpostBuilder.Airlock.Contains(p.x, p.z)) { ctx.zone = PlayerZone.Airlock; ctx.station = "life_support"; }
                else
                {
                    foreach (var room in outpost.Rooms) if (room.Contains(p.x, p.z)) { ctx.zone = PlayerZone.Habitat; ctx.station = "habitat"; break; }
                    if ((Math.Abs(p.z) < 0.9 && p.x > -26 && p.x < 20.5) || (Math.Abs(p.x) < 0.9 && p.z > -18 && p.z < 16.75)) ctx.zone = PlayerZone.Habitat;
                    if (MarsOutpostBuilder.Greenhouse.Contains(p.x, p.z)) ctx.station = "greenhouse";
                    if (MarsOutpostBuilder.Medical.Contains(p.x, p.z)) ctx.station = "medical";
                    if (MarsOutpostBuilder.Quarters.Contains(p.x, p.z)) ctx.station = "quarters";
                    if (MarsOutpostBuilder.Energy.Contains(p.x, p.z)) ctx.station = "energy";
                    if (MarsOutpostBuilder.LifeSupport.Contains(p.x, p.z)) ctx.station = "life_support";
                    if (MarsOutpostBuilder.Lab.Contains(p.x, p.z)) ctx.station = "lab";
                }
            }
            if (p.x > 6 && p.x < 22 && p.z > 5 && p.z < 15) ctx.station = "array";
            if (rover != null)
            {
                Vector3 rp = outpost.transform.InverseTransformPoint(rover.transform.position); ctx.roverAtBase = MarsOutpostBuilder.Pad.Contains(rp.x, rp.z);
                if (Vector3.Distance(position, rover.transform.position) < 8 && !ctx.driving) ctx.station = "rover";
            }
            foreach (var site in state.sites)
            {
                double distance = Vector3.Distance(position, new Vector3((float)site.x, (float)site.y, (float)site.z));
                if (distance < 35) site.discovered = true;
                if (distance < 5) ctx.nearbySiteId = site.id;
            }
            return ctx;
        }
    }
    public sealed class MissionMaterialCleanup : MonoBehaviour
    {
        public Material Owned;
        private void OnDestroy() { if (Owned != null) Destroy(Owned); }
    }
}
