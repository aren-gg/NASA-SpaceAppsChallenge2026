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
            if (home != null)
            {
                yield return new WaitForSecondsRealtime(3f);
                yield return ScreenShot("screen_home");
                home.StartMission();
            }
            mission = MarsMissionController.Instance;
            while ((mission == null || !mission.Ready) && Time.realtimeSinceStartup < deadline) { mission = MarsMissionController.Instance; yield return null; }
            Check(mission != null && mission.Ready, "Scene ready (terrain, outpost, astronaut, rover)");
            if (mission == null || !mission.Ready) { Finish(); yield break; }
            player = FindObjectOfType<MarsAstronautPlayer>();
            outpost = FindObjectOfType<MarsOutpost>();
            terrain = FindObjectOfType<MarsTerrain>();
            rover = FindObjectOfType<MarsRover>();
            resolveWalls = typeof(MarsAstronautPlayer).GetMethod("ResolveWalls", BindingFlags.NonPublic | BindingFlags.Instance);

            // Before any mission: the briefing, not the console.
            yield return new WaitForSecondsRealtime(1f);
            var briefHud = FindObjectOfType<MarsMissionHud>();
            var briefCard = typeof(MarsMissionHud).GetField("briefing", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(briefHud) as RectTransform;
            Check(briefCard != null && briefCard.gameObject.activeSelf && !mission.PanelOpen, "A mission briefing opens first, not the console");
            yield return ScreenShot("screen_briefing");
            HitboxChecks();

            mission.NewMission(MissionLength.Month);
            double startHour = mission.Simulation.LocalHour;
            Check(startHour > 6.5 && startHour < 7.5, "Sol 1 starts at 07:00", startHour.ToString("0.00"));
            Check(mission.Simulation.State.astronaut.satiety < 70 && mission.Simulation.State.astronaut.hydration < 70,
                "The astronaut wakes hungry and thirsty on sol 1");
            // The clock runs about 98x real time (one sol in about 15 minutes).
            mission.SetPanelOpen(false);
            double t0 = mission.Simulation.State.elapsedSeconds;
            yield return new WaitForSecondsRealtime(2f);
            double gained = mission.Simulation.State.elapsedSeconds - t0;
            Check(gained > 120 && gained < 260, "The clock runs about 98x real time", gained.ToString("0") + " game s in 2 real s");
            mission.ClockScale = 1; // the rest of the run checks behaviour, not pacing
            // Standing still or waiting never eats, drinks or starts work by itself.
            PlayerContext still = mission.Context;
            still.zone = PlayerZone.Habitat; still.moving = false; still.driving = false; still.advancing = true;
            double sat0 = mission.Simulation.State.astronaut.satiety, hyd0 = mission.Simulation.State.astronaut.hydration;
            for (int k = 0; k < 6; k++) mission.Simulation.Step(1800, mission.Environment, still);
            Check(mission.Simulation.State.task.kind == TaskKind.None, "Standing still inside starts no task by itself", mission.Simulation.State.task.kind.ToString());
            Check(mission.Simulation.State.astronaut.satiety <= sat0 && mission.Simulation.State.astronaut.hydration <= hyd0,
                "Waiting never refills food or water", sat0.ToString("0") + "->" + mission.Simulation.State.astronaut.satiety.ToString("0"));
            MiniGameChecks();
            mission.NewMission(MissionLength.Month);
            mission.Execute("routine", "off");
            Check(!mission.PanelOpen, "Starting a mission goes straight into the game");
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
            yield return HudScreens();
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

        /// <summary>Full-screen capture including the UI overlays.</summary>
        private IEnumerator ScreenShot(string name)
        {
            yield return new WaitForEndOfFrame();
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), shot.EncodeToPNG());
            Destroy(shot);
        }

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
                        Vector3 eye = player.AimTransform.position;
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
            MarsToolPickup belt = mission.Tools;
            MarsSurvival.Tools.MarsToolBelt hotbar = MarsSurvival.Tools.MarsExpedition.Instance == null ? null : MarsSurvival.Tools.MarsExpedition.Instance.Belt;
            Place(Floor(0, -12), Floor(0, -16));
            yield return Frames(3);
            mission.StartTask(TaskKind.Plant, "0:leafy");
            Check(mission.Notice.Contains("You need") && mission.Simulation.State.task.kind == TaskKind.None,
                "Tasks refuse to start without their tools", mission.Notice);

            var ids = new List<string>();
            foreach (MarsBaseTool t in MarsBaseTool.All) if (MarsToolPickup.IsHandTool(t.toolId) && !ids.Contains(t.toolId)) ids.Add(t.toolId);
            Check(ids.Count == 10, "All 10 hand tools are on racks in the base", ids.Count + "");
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
                MarsBaseTool aimed = belt.FindAimed(player.AimTransform);
                Check(aimed == tool, MarsBaseTool.NameOf(id) + ": looking at it selects it", aimed == null ? "nothing aimed" : aimed.toolId);
                string msg = belt.TryPickUp(tool);
                yield return new WaitForSeconds(0.8f); // the hotbar raises a newly selected tool into view
                Transform held = hotbar == null ? null : hotbar.HeldView;
                bool inHand = held != null && held.gameObject.activeInHierarchy && hotbar.Current.Id == id && held.IsChildOf(player.AimTransform);
                bool visible = false;
                if (inHand)
                {
                    Renderer[] rs = held.GetComponentsInChildren<Renderer>();
                    Bounds hb = rs[0].bounds;
                    foreach (Renderer r in rs) hb.Encapsulate(r.bounds);
                    Vector3 v = player.ViewCamera.WorldToViewportPoint(hb.center);
                    visible = rs[0].enabled && v.z > 0 && v.x > 0.2f && v.x < 1f && v.y > 0f && v.y < 0.7f;
                }
                Camera main = Camera.main;
                string why = hotbar == null ? "no hotbar" : "active=" + hotbar.Active + " carried=" + hotbar.CurrentCarried + " selected=" + hotbar.Current.Id +
                    " view=" + (hotbar.HeldView != null) + " main=" + (main == null ? "none" : main.name) + " child=" + (main != null && main.transform.IsChildOf(player.transform)) +
                    " blocked=" + mission.InputBlocked + " home=" + MarsHomeScreen.IsOpen + " inHand=" + inHand + " visible=" + visible;
                Check(tool.Carried && belt.Has(id) && inHand && visible, MarsBaseTool.NameOf(id) + ": picked up into its hotbar slot and shown in hand", msg + " | " + why);
                if (id == "core_drill" || id == "hydro_sprayer") { Shot("held_" + id); yield return ScreenShot("screen_held_" + id); }
            }
            if (hotbar != null) hotbar.Select(0);
            Check(hotbar != null && hotbar.Current.Id == "rock_hammer" && hotbar.CurrentCarried, "Hotbar slot 1 selects the Rock Hammer",
                hotbar == null ? "no hotbar" : hotbar.Current.Id);
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

        /// <summary>Uses a hotbar tool as the player would (its click action), then waits for the task it starts.</summary>
        private IEnumerator ToolTask(string toolId, TaskKind kind, Vector3 at, Vector3 look, string station, Func<bool> effect, string what)
        {
            Place(at, look);
            yield return Frames(3);
            Check(station == null || mission.Context.station == station, what + ": station detected", "station=" + mission.Context.station + " zone=" + mission.Context.zone);
            if (!UseTool(toolId, what)) yield break;
            bool started = mission.Simulation.State.task.kind == kind;
            Check(started, what + ": tool starts " + kind, mission.Notice);
            if (!started) yield break;
            bool indoor = mission.Context.zone != PlayerZone.Outdoors;
            float until = Time.realtimeSinceStartup + 40;
            while (mission.Simulation.State.task.kind == kind && Time.realtimeSinceStartup < until)
            {
                if (indoor) { if (mission.AdvanceRemaining <= 0) mission.Advance(mission.Simulation.State.task.durationSeconds + 60); }
                else for (int k = 0; k < 20; k++) mission.Simulation.Step(1, mission.Environment, mission.Context);
                yield return null;
            }
            mission.StopAdvance("");
            Check(mission.Simulation.State.task.kind == TaskKind.None && effect(), what + ": completes with its effect", mission.Simulation.LastResult);
        }

        /// <summary>Selects the tool's hotbar slot and runs its click action at once. False (and a failed check) when it refuses.</summary>
        private bool UseTool(string toolId, string what)
        {
            MarsSurvival.Tools.MarsToolBelt hotbar = MarsSurvival.Tools.MarsExpedition.Instance == null ? null : MarsSurvival.Tools.MarsExpedition.Instance.Belt;
            if (hotbar == null) { Check(false, what + ": hotbar exists"); return false; }
            hotbar.Select(Array.IndexOf(MarsSurvival.Tools.MarsTool.Order, toolId));
            MarsSurvival.Tools.ToolAction plan = hotbar.Current.Plan(new MarsSurvival.Tools.ToolTarget { SiteId = mission.Context.nearbySiteId ?? "" });
            bool usable = hotbar.CurrentCarried && plan != null && !plan.Blocked;
            if (!usable) { Check(false, what + ": " + MarsBaseTool.NameOf(toolId) + " can be used", plan == null ? "no action" : plan.Label); return false; }
            if (plan.Complete != null) plan.Complete();
            return true;
        }

        private Vector3 RoomCentre(FloorRect r) { return Floor((r.x0 + r.x1) * 0.5f, (r.z0 + r.z1) * 0.5f); }

        /// <summary>Uses a tool on a task object, winning its mini-game with scripted input as a player would.</summary>
        private bool UseToolOn(string toolId, MarsTaskTarget on, string what)
        {
            MarsSurvival.Tools.MarsToolBelt hotbar = MarsSurvival.Tools.MarsExpedition.Instance == null ? null : MarsSurvival.Tools.MarsExpedition.Instance.Belt;
            if (hotbar == null) { Check(false, what + ": hotbar exists"); return false; }
            hotbar.Select(Array.IndexOf(MarsSurvival.Tools.MarsTool.Order, toolId));
            var target = new MarsSurvival.Tools.ToolTarget { SiteId = mission.Context.nearbySiteId ?? "" };
            if (on != null) { target.Hit = true; target.Collider = on.GetComponent<Collider>(); target.Point = on.transform.position; }
            MarsSurvival.Tools.ToolAction plan = hotbar.Current.Plan(target);
            bool usable = hotbar.CurrentCarried && plan != null && !plan.Blocked;
            if (!usable) { Check(false, what + ": " + MarsBaseTool.NameOf(toolId) + " can be used", plan == null ? "no action" : plan.Label); return false; }
            if (plan.Game != null && !Win(plan.Game)) { Check(false, what + ": mini-game can be won", plan.Game.Status); return false; }
            if (plan.Complete != null) plan.Complete();
            return true;
        }

        /// <summary>Plays a mini-game the way a careful player would: click in the green, let the drill cool, stop spraying in the band.</summary>
        private static bool Win(MarsSurvival.Tools.MiniGame g)
        {
            bool drilling = true;
            for (int i = 0; i < 20000 && !g.Done; i++)
            {
                const float dt = 0.02f;
                if (g is MarsSurvival.Tools.TimingGame)
                {
                    g.Tick(dt, false, false, false);
                    if (g.Needle > g.ZoneStart + 0.005f && g.Needle < g.ZoneEnd - 0.005f) g.Tick(0f, true, false, false);
                }
                else if (g is MarsSurvival.Tools.HeatGame)
                {
                    if (g.Heat > 0.8f) drilling = false;
                    if (g.Heat < 0.2f) drilling = true;
                    g.Tick(dt, false, drilling && !g.Jammed, false);
                }
                else
                {
                    bool hold = g.Fill < 0.9f;
                    g.Tick(dt, false, hold, !hold);
                }
            }
            return g.Done;
        }

        private void MiniGameChecks()
        {
            var timing = new MarsSurvival.Tools.TimingGame(3, 1.3f, 0.17f, MarsSurvival.Tools.MiniGameView.Dial, "bolt");
            for (int i = 0; i < 500 && timing.Needle >= timing.ZoneStart - 0.02f && timing.Needle <= timing.ZoneEnd + 0.02f; i++) timing.Tick(0.02f, false, false, false);
            timing.Tick(0f, true, false, false);
            Check(timing.Count == 0 && timing.MissedAt > 0, "Wrench timing: a click outside the green misses");
            Check(Win(timing) && timing.Count == 3, "Wrench timing: three clicks in the green tighten all bolts");
            var heat = new MarsSurvival.Tools.HeatGame();
            for (int i = 0; i < 500 && !heat.Jammed; i++) heat.Tick(0.02f, false, true, false);
            Check(heat.Jammed && !heat.Done, "Core drill: holding too long overheats and jams the bit");
            var careful = new MarsSurvival.Tools.HeatGame();
            Check(Win(careful), "Core drill: drilling in bursts extracts the core");
            var soak = new MarsSurvival.Tools.SoakGame(0f);
            soak.Tick(0.5f, false, true, false); soak.Tick(0f, false, false, true);
            Check(!soak.Done, "Sprayer: letting go too early keeps spraying");
            var soak2 = new MarsSurvival.Tools.SoakGame(0f);
            Check(Win(soak2) && soak2.Fill >= MarsSurvival.Tools.SoakGame.BandLow && soak2.Fill <= MarsSurvival.Tools.SoakGame.BandHigh, "Sprayer: letting go in the green band waters the bed");
        }

        /// <summary>Roofs, the dome, the tunnel tubes and the shelter mound are solid where they are drawn.</summary>
        private void HitboxChecks()
        {
            float RoofAt(float x, float z)
            {
                Vector3 from = outpost.transform.TransformPoint(new Vector3(x, 12f, z));
                RaycastHit hit;
                if (!Physics.Raycast(from, -outpost.transform.up, out hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return -99f;
                return outpost.transform.InverseTransformPoint(hit.point).y;
            }
            float mound = RoofAt(-24f, 0f);
            Check(mound > 3.45f, "Shelter mound: you stand on its drawn top, not inside it", mound.ToString("0.00") + " m");
            float vault = RoofAt(-9.5f, 0f);
            Check(vault > 3.75f, "Vaulted roof: solid up to its ridge", vault.ToString("0.00") + " m");
            float dome = RoofAt(0f, 0f);
            Check(dome > 5f, "Hub dome: solid up to its top", dome.ToString("0.00") + " m");
            float eave = RoofAt(-11.8f, 0f);
            Check(eave < vault - 0.3f && eave > 2.7f, "Vaulted roof: the collider follows the curve down to the eaves", eave.ToString("0.00") + " m");
            float tube = RoofAt(-6f, 0f);
            Check(tube > 2.8f, "Tunnel tube: solid up to its drawn top", tube.ToString("0.00") + " m");
            Vector3 side = outpost.transform.TransformPoint(new Vector3(-6f, 1.2f, 1.65f));
            Check(Physics.CheckSphere(side, 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "Tunnel tube: its round sides block you");
        }

        private static MarsTaskTarget FindTarget(Func<MarsTaskTarget, bool> match)
        {
            foreach (MarsTaskTarget t in FindObjectsOfType<MarsTaskTarget>()) if (match(t)) return t;
            return null;
        }

        private IEnumerator Tasks()
        {
            MissionState s = mission.Simulation.State;
            s.resources.parts = Math.Max(s.resources.parts, 30);
            Vector3 gh = RoomCentre(MarsOutpostBuilder.Greenhouse), med = RoomCentre(MarsOutpostBuilder.Medical),
                q = RoomCentre(MarsOutpostBuilder.Quarters), en = RoomCentre(MarsOutpostBuilder.Energy), lab = RoomCentre(MarsOutpostBuilder.Lab);

            // Food and water only at the galley.
            Place(Floor(0, -2), Floor(0, 2)); yield return Frames(3);
            s.astronaut.satiety = 50; s.astronaut.hydration = 50; s.resources.suitOxygenGrams = 400;
            double sat = s.astronaut.satiety, hyd = s.astronaut.hydration, suit = s.resources.suitOxygenGrams;
            mission.Execute("eat"); Check(s.astronaut.satiety <= sat, "Eating away from the galley is refused", mission.Notice);
            Place(Floor(-1.4f, 1.5f), MarsWorldTasks.World(new Vector3(-2.7f, 1.34f, 3.12f))); yield return Frames(3);
            Check(mission.Context.station == "galley", "Galley detected in the command hub", mission.Context.station);
            MarsTaskTarget locker = mission.Tools.FindUse(player.AimTransform, s, out string useLabel);
            Check(locker != null && (locker.kind == MarsTaskTarget.Kind.Food || locker.kind == MarsTaskTarget.Kind.Water), "Looking at the galley offers E to eat or drink", useLabel);
            mission.Execute("eat"); Check(s.astronaut.satiety > sat, "Eat at the galley food locker", mission.Notice);
            mission.Execute("drink"); Check(s.astronaut.hydration > hyd, "Drink at the galley water dispenser", mission.Notice);
            Check(s.day.ateMorning && s.day.drankMorning, "Breakfast ticks off the daily plan");
            mission.Execute("refill"); Check(s.resources.suitOxygenGrams > suit, "Refill suit", mission.Notice);
            s.resources.canisterOxygenGrams = 0;
            if (UseTool("oxygen_canister", "Oxygen Canister (5) inside"))
                Check(s.resources.canisterOxygenGrams > 0, "Oxygen Canister (5) refills from habitat oxygen", mission.Notice);
            mission.Execute("radio", "listen"); Check(s.radioMode == RadioMode.Listen, "Radio listen (comm radio)", mission.Notice);
            int reports = s.reports.Count;
            mission.Execute("report"); Check(s.reports.Count > reports, "Queue Houston report", mission.Notice);

            s.astronaut.fatigue = 60; double fat = 60;
            yield return RunTask(TaskKind.Rest, "", q, q + Vector3.forward, null, () => s.astronaut.fatigue < fat, "Rest");
            double fit = s.astronaut.fitness;
            yield return RunTask(TaskKind.Exercise, "", q, q + Vector3.forward, null, () => s.astronaut.fitness > fit, "Exercise");
            s.astronaut.health = 80;
            yield return RunTask(TaskKind.Treat, "", med, med + Vector3.forward, "medical", () => s.astronaut.health > 80, "Medical treatment");

            // Greenhouse: plants stand inside their beds, bed 1 by task, bed 2 sown spot by spot with the shovel.
            Place(gh, gh + Vector3.forward); yield return Frames(3);
            bool inside = true;
            foreach (MarsTaskTarget t in FindObjectsOfType<MarsTaskTarget>())
            {
                if (t.kind != MarsTaskTarget.Kind.Plant) continue;
                Vector3 p = outpost.transform.InverseTransformPoint(t.transform.position);
                float x0 = t.bed == 0 ? -3.3f : 1.3f;
                if (p.x < x0 + 0.15f || p.x > x0 + 1.85f || p.z < -16.1f || p.z > -8.6f) inside = false;
            }
            Check(inside, "Every greenhouse plant spot sits inside its bed");
            yield return RunTask(TaskKind.Plant, "0:leafy", gh, gh + Vector3.forward, "greenhouse", () => s.crops[0].planted, "Plant bed 1");
            Place(gh, gh + Vector3.forward); yield return Frames(3);
            double seeds = s.resources.seeds;
            for (int slot = 0; slot < CropState.Slots; slot++)
            {
                int sl = slot;
                MarsTaskTarget spot = FindTarget(t => t.kind == MarsTaskTarget.Kind.Plant && t.bed == 1 && t.slot == sl);
                if (spot == null || !UseToolOn("regolith_shovel", spot, "Shovel (9) sows spot " + (slot + 1))) break;
            }
            Check(s.crops[1].planted && s.crops[1].sownMask == CropState.AllSlots, "Shovel (9) sows all 12 spots of bed 2", CropState.Count(s.crops[1].sownMask) + "/12");
            Check(Math.Abs(seeds - 1 - s.resources.seeds) < 0.01, "Sowing a bed uses one seed", seeds + " -> " + s.resources.seeds);
            s.crops[1].moisture = 0;
            MarsTaskTarget wet = FindTarget(t => t.kind == MarsTaskTarget.Kind.Plant && t.bed == 1 && t.slot == 4);
            if (UseToolOn("hydro_sprayer", wet, "Sprayer (0) waters bed 2"))
                Check(s.crops[1].moisture >= 0.7 && s.crops[1].moisture <= 1.15, "Sprayer (0) waters bed 2 into the green band", s.crops[1].moisture.ToString("0.00"));
            // A dry bed does not grow.
            s.crops[0].moisture = 0; double g0 = s.crops[0].growth;
            PlayerContext idle = mission.Context; idle.moving = false;
            mission.Simulation.Step(3600, mission.Environment, idle);
            Check(s.crops[0].growth <= g0 + 1e-6, "A dry bed stops growing", g0.ToString("0.000") + " -> " + s.crops[0].growth.ToString("0.000"));
            yield return Frames(2);
            Place(Floor(0f, -9.2f), Floor(-2.3f, -13.5f)); player.AimAt(MarsWorldTasks.World(new Vector3(-1.2f, 0.6f, -13f)));
            yield return Frames(3);
            yield return ScreenShot("screen_greenhouse_growing");
            // Ripe plants are picked by hand, one at a time.
            s.crops[0].ready = true; s.crops[0].growth = 1; s.resources.food = Math.Min(s.resources.food, 5);
            Place(Floor(0f, -9.2f), Floor(-2.3f, -13.5f)); player.AimAt(MarsWorldTasks.World(new Vector3(-1.2f, 0.6f, -13f)));
            yield return Frames(3);
            yield return ScreenShot("screen_greenhouse_ripe");
            Place(gh, gh + Vector3.forward); yield return Frames(3);
            double food = s.resources.food; int harvests = s.crops[0].harvests;
            for (int slot = 0; slot < CropState.Slots; slot++) mission.Execute("pick", "0:" + slot);
            Check(s.resources.food > food + 10 && !s.crops[0].planted && s.crops[0].harvests == harvests + 1,
                "Picking all 12 ripe plants with E fills the food store and empties the bed", food.ToString("0") + " -> " + s.resources.food.ToString("0"));

            // Maintenance: the fault box, fixed with the wrench's three-bolt timing.
            s.faultActive = true; s.faultSpot = 2; s.equipment.baseCondition = 0.7; int repairs = s.repairsCompleted;
            Place(en, en + Vector3.forward); yield return Frames(3);
            MarsTaskTarget fault = FindTarget(t => t.kind == MarsTaskTarget.Kind.Fault && t.gameObject.activeInHierarchy);
            Check(fault != null, "The sparking fault box appears in the base");
            yield return ScreenShot("screen_fault");
            if (fault != null && UseToolOn("power_wrench", fault, "Field Wrench (8) fixes the fault"))
                Check(!s.faultActive && s.repairsCompleted > repairs, "Field Wrench (8) fixes the fault", mission.Notice);

            ResearchNode first = null;
            foreach (ResearchNode n in ResearchCatalog.Nodes) if (n.prerequisites.Length == 0) { first = n; break; }
            s.resources.researchPoints = Math.Max(s.resources.researchPoints, first.points + 10);
            mission.Research(first.id);
            Check(s.researched.Contains(first.id), "Research " + first.id, mission.Notice);
            yield return RunTask(TaskKind.Install, first.id, en, en + Vector3.forward, "energy", () => s.installed.Contains(first.id), "Install " + first.id);

            // Solar array: sweep each dusty panel.
            for (int i = 0; i < s.panelDustEach.Length; i++) s.panelDustEach[i] = 0.3;
            int cleaned = s.panelsCleaned;
            Vector3 array = Ground(outpost.transform.TransformPoint(new Vector3(14, 0, 10)));
            Place(Ground(outpost.transform.TransformPoint(new Vector3(12f, 0, 4.6f))), MarsWorldTasks.World(new Vector3(12.1f, 0.9f, 7.5f)));
            yield return Frames(3);
            yield return ScreenShot("screen_dusty_panels");
            Place(array, array + Vector3.forward); yield return Frames(3);
            for (int i = 0; i < 6; i++)
            {
                int index = i;
                MarsTaskTarget panel = FindTarget(t => t.kind == MarsTaskTarget.Kind.Panel && t.index == index);
                if (panel == null || !UseToolOn("panel_brush", panel, "Panel Duster (7) sweeps panel " + (i + 1))) break;
            }
            Check(s.panelsCleaned > cleaned && s.equipment.panelDust < 0.01, "Panel Duster (7) sweeps all six panels clean", s.equipment.panelDust.ToString("0.00"));

            s.equipment.roverCondition = 0.7; int rRepairs = s.repairsCompleted;
            Vector3 byRover = Ground(rover.transform.position + rover.transform.right * 3f);
            Place(byRover, rover.transform.position); yield return Frames(3);
            if (UseToolOn("power_wrench", null, "Field Wrench (8) repairs the rover"))
                Check(s.repairsCompleted > rRepairs, "Field Wrench (8) repairs the rover", mission.Notice);

            // Sleep ends the day: E on a bunk after 17:00 jumps to 07:00 and shows the morning card.
            Place(q, Floor(-17f, -2.5f)); yield return Frames(3);
            s.astronaut.fatigue = 70;
            int dayBefore = s.day.number; double satBefore = s.astronaut.satiety;
            mission.Sleep();
            Check(s.task.kind == TaskKind.Rest, "Bunk: sleep starts", mission.Notice);
            float until = Time.realtimeSinceStartup + 40;
            while (s.task.kind == TaskKind.Rest && Time.realtimeSinceStartup < until) yield return null;
            double wake = mission.Simulation.LocalHour;
            Check(s.day.number == dayBefore + 1 && wake > 6.8 && wake < 7.6, "Sleeping wakes you at 07:00 on a new sol", "sol " + s.day.number + " at " + wake.ToString("0.00") + " | " + mission.Notice);
            Check(s.astronaut.satiety < satBefore, "You wake up hungrier than you went to bed");
            yield return new WaitForSeconds(0.6f);
            var hud = FindObjectOfType<MarsMissionHud>();
            var card = typeof(MarsMissionHud).GetField("summaryPlate", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(hud) as RectTransform;
            Check(card != null && card.gameObject.activeSelf, "The morning summary card shows after sleeping");
            yield return ScreenShot("screen_morning");
            typeof(MarsMissionHud).GetMethod("CloseSummary", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(hud, null);
            yield return null;
            Check(!mission.Paused, "Starting the day resumes the clock");
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
            for (int i = 0; i < 3; i++)
            {
                int index = i;
                MarsTaskTarget rock = FindTarget(t => t.kind == MarsTaskTarget.Kind.SurveyRock && t.siteId == site.id && t.index == index);
                if (rock == null) { Check(false, "Glowing survey rock " + (i + 1) + " at the site"); break; }
                if (i == 0) yield return ScreenShot("screen_survey_rocks");
                UseToolOn("rock_hammer", rock, "Rock Hammer (1) strikes rock " + (i + 1));
            }
            Check(site.surveyed, "Striking the 3 glowing rocks surveys the site", mission.Notice);
            mission.Execute("measure", site.id); Check(site.measured, "Measure site", mission.Notice);
            Place(stand, sp); yield return Frames(3);
            if (UseToolOn("core_drill", null, "Core Drill (2) drills a core")) Check(site.sampled, "Core Drill (2) drills a core", mission.Notice);
            if (UseToolOn("rock_hammer", null, "Rock Hammer (1) places the survey marker")) Check(site.markerPlaced, "Rock Hammer (1) places the survey marker", mission.Notice);
            Place(stand, sp);
            yield return Frames(2);
            s.resources.suitOxygenGrams = 300;
            if (UseTool("oxygen_canister", "Oxygen Canister (5) on EVA"))
                Check(s.resources.suitOxygenGrams > 300, "Oxygen Canister (5) tops up the suit on EVA", mission.Notice);
            Place(stand, sp + Vector3.up * 1f);
            yield return Frames(3);
            mission.CapturePhoto(site.id);
            float until = Time.realtimeSinceStartup + 20;
            while (s.task.kind == TaskKind.Photograph && Time.realtimeSinceStartup < until) { for (int k = 0; k < 20; k++) mission.Simulation.Step(1, mission.Environment, mission.Context); yield return null; }
            Check(site.photographed, "Photograph marker (survey camera)", mission.Notice + " / " + mission.Simulation.LastResult);
            Vector3 lab = RoomCentre(MarsOutpostBuilder.Lab);
            Place(lab, lab + Vector3.forward); yield return Frames(3);
            if (UseToolOn("core_drill", null, "Core Drill (2) analyzes the core at the lab"))
                Check(site.analyzed && s.day.fieldDone, "Core Drill (2) analyzes the core at the lab and ticks off the field trip", mission.Notice);

            // Houston: queue a report, transmit, and wait (inside) for a radio window and acknowledgement.
            var radioBelt = MarsSurvival.Tools.MarsExpedition.Instance.Belt;
            radioBelt.Select(Array.IndexOf(MarsSurvival.Tools.MarsTool.Order, "comm_radio"));
            bool waiting = s.reports.Exists(r => !r.acknowledged);
            var radioPlan = radioBelt.Current.Plan(new MarsSurvival.Tools.ToolTarget());
            bool radioOk = radioPlan != null && (!waiting ? !radioPlan.Blocked && radioPlan.Label.Contains("queue")
                : mission.Environment.signal > 0.02 ? !radioPlan.Blocked && radioPlan.Label.Contains("transmit") : radioPlan.Blocked && radioPlan.Label.Contains("signal"));
            Check(radioOk, "Radio (4) queues or transmits Houston reports", radioPlan == null ? "no action" : radioPlan.Label);
            if (radioOk && !radioPlan.Blocked && radioPlan.Complete != null) radioPlan.Complete();
            if (!s.reports.Exists(r => !r.acknowledged)) mission.Execute("report");
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
            var text = field != null ? field.GetValue(hud) as TMPro.TMP_Text : null;
            Check(text != null && text.text.StartsWith("Next: "), "HUD shows the next-step guide", text != null ? text.text : "missing");
            Check(text != null && text.font != null && text.font.name.Contains("Barlow Condensed"), "HUD uses the Barlow Condensed font", text != null && text.font != null ? text.font.name : "no font");
            var vitals = typeof(MarsMissionHud).GetField("vitalLabels", BindingFlags.NonPublic | BindingFlags.Instance);
            var bars = vitals != null ? vitals.GetValue(hud) as TMPro.TMP_Text[] : null;
            Check(bars != null && bars.Length == 5 && bars[0].text.StartsWith("Oxygen"), "HUD shows the five suit vitals (oxygen first)");
            Check(FindObjectOfType<MarsTrainer.UI.PauseMenu>(true) != null && FindObjectOfType<MarsTrainer.UI.EndScreen>(true) != null, "Pause menu and mission report screens exist");
            Check(FindObjectOfType<MarsToolHud>() != null, "Tool hotbar HUD exists");
            Check(mission.Simulation.Settings.leafyWaterLitersPerSol > 0.5 && mission.Simulation.Settings.leafyWaterLitersPerSol < 1.5,
                "KSC Veggie crop data sets the leafy bed water use", mission.Simulation.Settings.leafyWaterLitersPerSol.ToString("0.00"));
            Check(mission.Environment.radiationSource != null && mission.Environment.radiationSource.Contains("RAD") &&
                  mission.Environment.eventSource.Contains("DONKI"), "Environment labels its RAD and DONKI schedule sources", mission.Environment.radiationSource);
            Shot("hud_final");
        }

        /// <summary>Screens of the merged HUD: gameplay with the hotbar, console tabs, pause menu.</summary>
        private IEnumerator HudScreens()
        {
            var hud = FindObjectOfType<MarsMissionHud>();
            Vector3 gh = RoomCentre(MarsOutpostBuilder.Greenhouse);
            Place(gh, gh + Vector3.forward * 3f);
            var hotbar = MarsSurvival.Tools.MarsExpedition.Instance.Belt;
            hotbar.Select(Array.IndexOf(MarsSurvival.Tools.MarsTool.Order, "hydro_sprayer"));
            yield return new WaitForSeconds(1f);
            yield return ScreenShot("screen_hud");
            // A mini-game in progress (the wrench dial), as the player sees it.
            var gameProp = typeof(MarsSurvival.Tools.MarsToolBelt).GetProperty("Game");
            var workProp = typeof(MarsSurvival.Tools.MarsToolBelt).GetProperty("Working");
            var dial = new MarsSurvival.Tools.TimingGame(3, 1.3f, 0.17f, MarsSurvival.Tools.MiniGameView.Dial, "bolt");
            dial.Tick(0.3f, false, false, false);
            workProp.SetValue(hotbar, MarsSurvival.Tools.ToolAction.Play("fix the fault", "qa", dial, null));
            gameProp.SetValue(hotbar, dial);
            yield return Frames(2);
            yield return ScreenShot("screen_minigame");
            gameProp.SetValue(hotbar, null);
            workProp.SetValue(hotbar, null);
            var tabField = typeof(MarsMissionHud).GetField("tab", BindingFlags.NonPublic | BindingFlags.Instance);
            var rebuild = typeof(MarsMissionHud).GetMethod("RebuildPanel", BindingFlags.NonPublic | BindingFlags.Instance);
            foreach (int t in new[] { 0, 1, 2, 3, 5, 6 })
            {
                mission.SetPanelOpen(true);
                tabField.SetValue(hud, t); rebuild.Invoke(hud, null);
                yield return new WaitForSeconds(0.5f);
                yield return ScreenShot("screen_console_" + t);
            }
            mission.SetPanelOpen(false);
            var open = typeof(MarsMissionHud).GetMethod("OpenPause", BindingFlags.NonPublic | BindingFlags.Instance);
            open.Invoke(hud, null);
            yield return new WaitForSeconds(0.5f);
            yield return ScreenShot("screen_pause");
            var mapField = typeof(MarsMissionHud).GetField("map", BindingFlags.NonPublic | BindingFlags.Instance);
            var mm = mapField.GetValue(hud) as MarsMiniMap;
            Check(hud.ScreenOpen && mission.Paused, "Pause menu opens and pauses the mission");
            FindObjectOfType<MarsTrainer.UI.PauseMenu>().Close();
            yield return null;
            Check(!hud.ScreenOpen && !mission.Paused, "Closing the pause menu resumes the mission");

            // Minimap (outside, near the outpost so the rooms show) and the large map.
            Place(Ground(outpost.transform.TransformPoint(new Vector3(6f, 0f, 22f))), outpost.transform.position);
            yield return new WaitForSeconds(0.5f);
            Check(mm != null, "Minimap exists above the daily plan");
            yield return ScreenShot("screen_minimap");
            mm.SetExpanded(true);
            yield return new WaitForSeconds(0.3f);
            Check(mm.Expanded && mission.InputBlocked, "Clicking the minimap opens the large map and stops walking");
            yield return ScreenShot("screen_map");
            mm.SetExpanded(false);
            yield return null;
            Check(!mm.Expanded && !mission.InputBlocked, "Closing the large map returns to walking");
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
