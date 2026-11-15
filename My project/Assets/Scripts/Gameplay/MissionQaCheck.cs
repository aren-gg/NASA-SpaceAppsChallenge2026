using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using MarsSurvival.Base;
using MarsSurvival.Interaction;
using MarsSurvival.Simulation;
using MarsSurvival.UI;
using MarsSurvival.Vehicles;
using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.Gameplay
{
    /// <summary>
    /// End-to-end play check, run only with the command-line flag --qa (optionally
    /// --qa-out=&lt;folder&gt;). It drives the real scene: walks the astronaut to every
    /// door, picks up every tool, performs every daily task at its station and checks the
    /// effect, then writes qa-result.txt and door/tool screenshots and quits.
    /// </summary>
    public sealed class MissionQaCheck : MonoBehaviour
    {
        private readonly StringBuilder log = new StringBuilder();
        private int passed, failed;
        private string outDir;
        private MarsMissionController mission;
        private MarsAstronautPlayer player;
        private MarsOutpost outpost;
        private MarsTerrain terrain;
        private MarsRover rover;
        private MethodInfo resolveWalls;

        private void Check(bool ok, string what, string detail = "")
        {
            if (ok) passed++; else failed++;
            log.AppendLine((ok ? "PASS  " : "FAIL  ") + what + (detail == "" ? "" : "   [" + detail + "]"));
        }

        private static string Arg(string prefix)
        {
            foreach (string a in Environment.GetCommandLineArgs()) if (a.StartsWith(prefix)) return a.Substring(prefix.Length);
            return null;
        }

        private IEnumerator Start()
        {
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "--qa") < 0) yield break;
            outDir = Arg("--qa-out=") ?? Path.Combine(Application.persistentDataPath, "qa");
            Directory.CreateDirectory(outDir);
            Application.logMessageReceived += (message, trace, type) =>
            {
                if (type == LogType.Exception || type == LogType.Error) log.AppendLine("LOG " + type + ": " + message + " " + trace.Split('\n')[0]);
            };
            float deadline = Time.realtimeSinceStartup + 120;
            MarsHomeScreen home = null;
            while (home == null && Time.realtimeSinceStartup < deadline) { home = FindObjectOfType<MarsHomeScreen>(); yield return null; }
            if (home != null) home.StartMission();
            mission = MarsMissionController.Instance;
            while ((mission == null || !mission.Ready) && Time.realtimeSinceStartup < deadline) { mission = MarsMissionController.Instance; yield return null; }
            Check(mission != null && mission.Ready, "Scene ready (terrain, outpost, astronaut, rover)");
            if (mission == null || !mission.Ready) { Finish(); yield break; }
            player = FindObjectOfType<MarsAstronautPlayer>();
            outpost = FindObjectOfType<MarsOutpost>();
            terrain = FindObjectOfType<MarsTerrain>();
            rover = FindObjectOfType<MarsRover>();
            resolveWalls = typeof(MarsAstronautPlayer).GetMethod("ResolveWalls", BindingFlags.NonPublic | BindingFlags.Instance);

            mission.NewMission(MissionLength.Month);
            mission.Execute("routine", "off");
            mission.SetPowerRequest(PowerChannel.LifeControl, 0.6);
            mission.SetPowerRequest(PowerChannel.Temperature, 0.6);
            mission.SetPowerRequest(PowerChannel.RadiationShielding, 0.1);
            mission.SetPanelOpen(false);
            yield return null;
            Check(!mission.InputBlocked, "Walking allowed once the console is closed");

            yield return Doors();
            yield return GatingAndTools();
            yield return Tasks();
            yield return Field();
            HudChecks();
            Finish();
        }

        // ------------------------------------------------------------------ helpers

        private Vector3 Floor(float x, float z) { return outpost.transform.TransformPoint(new Vector3(x, 0.02f, z)); }

        private Vector3 Ground(Vector3 p) { p.y = terrain.GetGroundY(p) + 0.02f; return p; }

        private void Place(Vector3 position, Vector3 lookAt)
        {
            mission.SetPanelOpen(false);
            Vector3 flat = lookAt - position; flat.y = 0;
            player.RestoreMissionPose(position, flat.sqrMagnitude > 1e-4f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : 0f);
            player.AimAt(lookAt);
        }

        private IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        private void Shot(string name)
        {
            Camera cam = player.ViewCamera;
            if (cam == null || SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) return;
            var rt = RenderTexture.GetTemporary(960, 540, 24);
            var old = cam.targetTexture; var active = RenderTexture.active;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            try
            {
                cam.targetTexture = rt; cam.Render(); RenderTexture.active = rt;
                tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0); tex.Apply();
                File.WriteAllBytes(Path.Combine(outDir, name + ".png"), tex.EncodeToPNG());
            }
            finally { cam.targetTexture = old; RenderTexture.active = active; RenderTexture.ReleaseTemporary(rt); Destroy(tex); }
        }

        private Vector3 Resolve(Vector3 p) { return (Vector3)resolveWalls.Invoke(player, new object[] { p }); }

        // ------------------------------------------------------------------ doors

        private IEnumerator Doors()
        {
            MarsOutpostDoor[] doors = FindObjectsOfType<MarsOutpostDoor>();
            Check(doors.Length >= 15, "Sliding doors in every doorway", doors.Length + " doors");
            Vector3 hub = Floor(0, 0);
            for (int i = 0; i < doors.Length; i++)
            {
                MarsOutpostDoor d = doors[i];
                Vector3 local = outpost.transform.InverseTransformPoint(d.transform.position);
                string name = "Door " + i + " (" + d.transform.parent.name + " @ " + local.x.ToString("F1") + "," + local.z.ToString("F1") + ")";
                Vector3 normal = d.transform.TransformDirection(Vector3.Cross(d.slideAxis, Vector3.up)).normalized;
                Vector3 centre = d.transform.position + Vector3.up * 1.2f;
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 start = d.transform.position + normal * side * 1.6f;
                    start.y = d.transform.position.y + 0.02f;
                    // Let the door shut first (stand at the hub, or outside for hub doors).
                    Place(Vector3.Distance(hub, d.transform.position) < 8f ? Floor(0, -30) : hub, hub);
                    yield return new WaitForSeconds(1.0f);
                    float shut = d.Open01;
                    Place(start, centre);
                    yield return new WaitForSeconds(1.1f);
                    bool airlockWait = d.partner != null && d.partner.Open01 > 0.01f;
                    Check(shut < 0.05f && (d.Open01 > 0.95f || airlockWait), name + " side " + side + ": shut when away, opens on approach",
                        "shut=" + shut.ToString("F2") + " open=" + d.Open01.ToString("F2") + (airlockWait ? " (waiting for partner)" : ""));
                    if (i < 40) Shot("door" + i + (side < 0 ? "a" : "b"));
                    // Nothing solid should remain in the open doorway.
                    if (d.Open01 > 0.95f)
                    {
                        Vector3 eye = player.ViewCamera.transform.position;
                        RaycastHit hit;
                        bool blocked = Physics.Raycast(eye, (centre - eye).normalized, out hit, Vector3.Distance(eye, centre) + 1.0f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                        Check(!blocked, name + " side " + side + ": doorway clear when open", blocked ? "hit " + hit.collider.name + " in " + hit.collider.transform.parent.name : "");
                        // Walk through at walking pace with the door working normally.
                        Vector3 p = start;
                        for (int f = 0; f < 120; f++)
                        {
                            Vector3 next = p - normal * side * 0.03f; next.y = start.y;
                            p = Resolve(next);
                            yield return null;
                        }
                        float crossed = Vector3.Dot(p - d.transform.position, normal * side);
                        Check(crossed < -1.0f, name + " side " + side + ": can walk through when open", "ended " + crossed.ToString("F2") + " m");
                    }
                }
                // A shut door must block.
                d.enabled = false;
                d.leafA.localPosition = d.slideAxis * -d.leafOffset; d.leafB.localPosition = d.slideAxis * d.leafOffset;
                d.blocker.enabled = true;
                yield return new WaitForFixedUpdate();
                Vector3 q = d.transform.position + normal * 1.2f; q.y = d.transform.position.y + 0.02f;
                for (int f = 0; f < 120; f++) { Vector3 next = q - normal * 0.05f; next.y = q.y; q = Resolve(next); }
                float stayed = Vector3.Dot(q - d.transform.position, normal);
                Check(stayed > 0.2f, name + ": shut door blocks walking", "ended " + stayed.ToString("F2") + " m");
                d.enabled = true;
            }
            Place(hub, Floor(0, 5));
            yield return new WaitForSeconds(1f);
        }

        // ------------------------------------------------------------------ tools

        private IEnumerator GatingAndTools()
        {
            MarsToolBelt belt = mission.Tools;
            Place(Floor(0, -12), Floor(0, -16));
            yield return Frames(3);
            mission.StartTask(TaskKind.Plant, "0:leafy");
            Check(mission.Notice.Contains("You need") && mission.Simulation.State.task.kind == TaskKind.None,
                "Tasks refuse to start without their tools", mission.Notice);

            var ids = new List<string>();
            foreach (MarsBaseTool t in MarsBaseTool.All) if (!ids.Contains(t.toolId)) ids.Add(t.toolId);
            Check(ids.Count == 15, "All 15 tool kinds are in the base", ids.Count + "");
            foreach (string id in ids)
            {
                MarsBaseTool tool = null;
                foreach (MarsBaseTool t in MarsBaseTool.All) if (t.toolId == id && !t.Carried) { tool = t; break; }
                if (tool == null) { Check(false, "Find " + id); continue; }
                Vector3 c = tool.Centre;
                Vector3 stand = Vector3.zero; bool found = false;
                for (int k = 0; k < 16 && !found; k++)
                {
                    float a = k * 22.5f * Mathf.Deg2Rad;
                    Vector3 cand = c + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 1.1f;
                    cand.y = outpost.transform.position.y + 0.02f;
                    Vector3 local = outpost.transform.InverseTransformPoint(cand);
                    if (Physics.CheckCapsule(cand + Vector3.up * 0.5f, cand + Vector3.up * 1.5f, 0.3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) continue;
                    bool inRoom = false;
                    foreach (var room in outpost.Rooms) if (room.Contains(local.x, local.z)) inRoom = true;
                    if (!inRoom) continue;
                    stand = cand; found = true;
                }
                Check(found, MarsBaseTool.NameOf(id) + ": reachable standing spot");
                if (!found) continue;
                Place(stand, c);
                yield return Frames(3);
                MarsBaseTool aimed = belt.FindAimed(player.ViewCamera.transform);
                Check(aimed == tool, MarsBaseTool.NameOf(id) + ": looking at it selects it", aimed == null ? "nothing aimed" : aimed.toolId);
                string msg = belt.TryPickUp(tool);
                yield return Frames(2);
                GameObject held = belt.HeldModel;
                bool inHand = held != null && held.transform.IsChildOf(player.ViewCamera.transform);
                bool visible = false;
                if (inHand)
                {
                    Renderer[] rs = held.GetComponentsInChildren<Renderer>();
                    Bounds hb = rs[0].bounds;
                    foreach (Renderer r in rs) hb.Encapsulate(r.bounds);
                    Vector3 v = player.ViewCamera.WorldToViewportPoint(hb.center);
                    visible = rs[0].enabled && v.z > 0 && v.x > 0.5f && v.x < 1f && v.y > 0f && v.y < 0.5f;
                }
                Check(tool.Carried && belt.Has(id) && inHand && visible, MarsBaseTool.NameOf(id) + ": picked up and shown in hand", msg);
                if (id == "core_drill" || id == "hydro_sprayer") Shot("held_" + id);
            }
            belt.CycleHeld();
            Check(belt.HeldId != "", "Q cycles the tool in hand", belt.HeldId);
        }

        // ------------------------------------------------------------------ tasks

        private IEnumerator RunTask(TaskKind kind, string target, Vector3 at, Vector3 look, string station, Func<bool> effect, string what)
        {
            Place(at, look);
            yield return Frames(3);
            Check(station == null || mission.Context.station == station, what + ": station detected", "station=" + mission.Context.station + " zone=" + mission.Context.zone);
            mission.StartTask(kind, target);
            bool started = mission.Simulation.State.task.kind == kind;
            Check(started, what + ": starts", mission.Notice);
            if (!started) yield break;
            bool indoor = mission.Context.zone != PlayerZone.Outdoors;
            float until = Time.realtimeSinceStartup + 40;
            while (mission.Simulation.State.task.kind == kind && Time.realtimeSinceStartup < until)
            {
                if (indoor)
                {
                    if (mission.AdvanceRemaining <= 0) mission.Advance(mission.Simulation.State.task.durationSeconds + 60);
                    yield return null;
                }
                else
                {
                    // Outside, time is not fast-forwarded; step the simulation as the controller does.
                    for (int k = 0; k < 20; k++) mission.Simulation.Step(1, mission.Environment, mission.Context);
                    yield return null;
                }
            }
            mission.StopAdvance("");
            Check(mission.Simulation.State.task.kind == TaskKind.None && effect(), what + ": completes with its effect", mission.Simulation.LastResult);
        }

        private Vector3 RoomCentre(FloorRect r) { return Floor((r.x0 + r.x1) * 0.5f, (r.z0 + r.z1) * 0.5f); }

        private IEnumerator Tasks()
        {
            MissionState s = mission.Simulation.State;
            s.resources.parts = Math.Max(s.resources.parts, 30);
            Vector3 gh = RoomCentre(MarsOutpostBuilder.Greenhouse), med = RoomCentre(MarsOutpostBuilder.Medical),
                q = RoomCentre(MarsOutpostBuilder.Quarters), en = RoomCentre(MarsOutpostBuilder.Energy), lab = RoomCentre(MarsOutpostBuilder.Lab);

            // Simple commands.
            Place(Floor(0, -2), Floor(0, 2)); yield return Frames(3);
            s.astronaut.satiety = 50; s.astronaut.hydration = 50; s.resources.suitOxygenGrams = 400;
            double sat = s.astronaut.satiety, hyd = s.astronaut.hydration, suit = s.resources.suitOxygenGrams;
            mission.Execute("eat"); Check(s.astronaut.satiety > sat, "Eat", mission.Notice);
            mission.Execute("drink"); Check(s.astronaut.hydration > hyd, "Drink", mission.Notice);
            mission.Execute("refill"); Check(s.resources.suitOxygenGrams > suit, "Refill suit (O2 canister)", mission.Notice);
            mission.Execute("radio", "listen"); Check(s.radioMode == RadioMode.Listen, "Radio listen (comm radio)", mission.Notice);
            int reports = s.reports.Count;
            mission.Execute("report"); Check(s.reports.Count > reports, "Queue Houston report", mission.Notice);

            s.astronaut.fatigue = 60; double fat = 60;
            yield return RunTask(TaskKind.Rest, "", q, q + Vector3.forward, null, () => s.astronaut.fatigue < fat, "Rest");
            double fit = s.astronaut.fitness;
            yield return RunTask(TaskKind.Exercise, "", q, q + Vector3.forward, null, () => s.astronaut.fitness > fit, "Exercise");
            s.astronaut.health = 80;
            yield return RunTask(TaskKind.Treat, "", med, med + Vector3.forward, "medical", () => s.astronaut.health > 80, "Medical treatment");
            yield return RunTask(TaskKind.Plant, "0:leafy", gh, gh + Vector3.forward, "greenhouse", () => s.crops[0].planted, "Plant bed 1");
            s.crops[0].ready = true; s.crops[0].growth = 1; s.resources.food = Math.Min(s.resources.food, 5);
            double food = s.totalFoodHarvested;
            yield return RunTask(TaskKind.Harvest, "0", gh, gh + Vector3.forward, "greenhouse", () => s.totalFoodHarvested > food, "Harvest");
            s.equipment.baseCondition = 0.7; int repairs = s.repairsCompleted;
            yield return RunTask(TaskKind.RepairBase, "", en, en + Vector3.forward, null, () => s.repairsCompleted > repairs, "Repair base");

            ResearchNode first = null;
            foreach (ResearchNode n in ResearchCatalog.Nodes) if (n.prerequisites.Length == 0) { first = n; break; }
            s.resources.researchPoints = Math.Max(s.resources.researchPoints, first.points + 10);
            mission.Research(first.id);
            Check(s.researched.Contains(first.id), "Research " + first.id, mission.Notice);
            yield return RunTask(TaskKind.Install, first.id, en, en + Vector3.forward, "energy", () => s.installed.Contains(first.id), "Install " + first.id);

            s.equipment.panelDust = 0.3; int cleaned = s.panelsCleaned;
            Vector3 array = Ground(outpost.transform.TransformPoint(new Vector3(14, 0, 10)));
            yield return RunTask(TaskKind.DustPanels, "", array, array + Vector3.forward, "array", () => s.panelsCleaned > cleaned, "Dust panels");

            s.equipment.roverCondition = 0.7; int rRepairs = s.repairsCompleted;
            Vector3 byRover = Ground(rover.transform.position + rover.transform.right * 3f);
            yield return RunTask(TaskKind.RepairRover, "", byRover, rover.transform.position, "rover", () => s.repairsCompleted > rRepairs, "Repair rover");
        }

        private IEnumerator Field()
        {
            MissionState s = mission.Simulation.State;
            SiteState site = s.sites[0];
            Vector3 sp = new Vector3((float)site.x, (float)site.y, (float)site.z);
            Transform marker = GameObject.Find("Science sites") != null ? GameObject.Find("Science sites").transform.Find(site.id) : null;
            Place(Ground(sp + new Vector3(80, 0, 0)), sp);
            yield return new WaitForSeconds(1f);
            Check(marker != null && !marker.gameObject.activeSelf, "Survey marker hidden when far (80 m)");
            Vector3 stand = Ground(sp + new Vector3(3, 0, 0));
            Place(stand, sp + Vector3.up * 1f);
            yield return new WaitForSeconds(1f);
            Check(marker != null && marker.gameObject.activeSelf && marker.localScale.y > 0.7f, "Survey marker pops up when near (3 m)");
            Shot("survey_marker");
            mission.SelectSite(site.id);
            mission.Execute("survey", site.id); Check(site.surveyed, "Survey site (rock hammer)", mission.Notice);
            mission.Execute("measure", site.id); Check(site.measured, "Measure site (dosimeter)", mission.Notice);
            yield return RunTask(TaskKind.SampleSoil, site.id, stand, sp, null, () => site.sampled, "Sample soil");
            yield return RunTask(TaskKind.PlaceMarker, site.id, stand, sp, null, () => site.markerPlaced, "Place survey marker");
            Place(stand, sp + Vector3.up * 1f);
            yield return Frames(3);
            mission.CapturePhoto(site.id);
            float until = Time.realtimeSinceStartup + 20;
            while (s.task.kind == TaskKind.Photograph && Time.realtimeSinceStartup < until) { for (int k = 0; k < 20; k++) mission.Simulation.Step(1, mission.Environment, mission.Context); yield return null; }
            Check(site.photographed, "Photograph marker (survey camera)", mission.Notice + " / " + mission.Simulation.LastResult);
            Vector3 lab = RoomCentre(MarsOutpostBuilder.Lab);
            int points = s.resources.researchPoints;
            yield return RunTask(TaskKind.Analyze, site.id, lab, lab + Vector3.forward, "lab", () => site.analyzed, "Analyze sample at lab");

            // Houston: queue a report, transmit, and wait (inside) for a radio window and acknowledgement.
            mission.Execute("report");
            mission.Execute("radio", "transmit");
            Check(s.radioMode == RadioMode.Transmit, "Radio transmit (comm radio)", mission.Notice);
            float limit = Time.realtimeSinceStartup + 60;
            while (!s.reports.Exists(r => r.acknowledged) && Time.realtimeSinceStartup < limit)
            {
                if (mission.AdvanceRemaining <= 0) mission.Advance(3600 * 6);
                yield return null;
            }
            mission.StopAdvance("");
            Check(s.reports.Exists(r => r.acknowledged), "Houston acknowledges a transmitted report", mission.Simulation.LastResult);
            Check(s.phase == MissionPhase.Running, "Astronaut still alive after all tasks", s.endingReason);
        }

        private void HudChecks()
        {
            var hud = FindObjectOfType<MarsMissionHud>();
            var field = typeof(MarsMissionHud).GetField("guide", BindingFlags.NonPublic | BindingFlags.Instance);
            var text = field != null ? field.GetValue(hud) as UnityEngine.UI.Text : null;
            Check(text != null && text.text.StartsWith("SURVIVE THE SOL"), "Bottom bar shows the survival guide", text != null ? text.text : "missing");
        }

        private void Finish()
        {
            string summary = "QA: " + passed + " passed, " + failed + " failed\n" + log;
            File.WriteAllText(Path.Combine(outDir, "qa-result.txt"), summary);
            Debug.Log(summary);
            Application.Quit(failed == 0 ? 0 : 1);
        }
    }
}
