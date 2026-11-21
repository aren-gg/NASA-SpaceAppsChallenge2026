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
using MarsSurvival.World;
using UnityEngine;

namespace MarsSurvival.Gameplay
{
    /// <summary>
    /// End-to-end play check, run only with the command-line flag --qa (optionally
    /// --qa-out=&lt;folder&gt;). It drives the real scene: walks the astronaut through every
    /// door, picks up every tool, then plays the garden loop (buy seeds, sow, water, grow,
    /// pick, eat, sell, expand the greenhouse) and the vitals, then writes qa-result.txt and
    /// screenshots and quits.
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
            Check(mission != null && mission.Ready, "Scene ready (terrain, outpost, astronaut)");
            if (mission == null || !mission.Ready) { Finish(); yield break; }
            player = FindObjectOfType<MarsAstronautPlayer>();
            outpost = FindObjectOfType<MarsOutpost>();
            terrain = FindObjectOfType<MarsTerrain>();
            resolveWalls = typeof(MarsAstronautPlayer).GetMethod("ResolveWalls", BindingFlags.NonPublic | BindingFlags.Instance);
            HitboxChecks();

            // Closing the home screen starts the game straight away.
            float wait = Time.realtimeSinceStartup + 10;
            while (mission.Simulation == null && Time.realtimeSinceStartup < wait) yield return null;
            Check(mission.Simulation != null && mission.IsRunning && !mission.InputBlocked, "Closing the home screen starts the game, walking");
            if (mission.Simulation == null) { Finish(); yield break; }
            double startHour = mission.Environment.localHour;
            Check(startHour > 6.5 && startHour < 7.5, "Sol 1 starts at 07:00", startHour.ToString("0.00"));
            // The clock runs about 98x real time (one sol in about 15 minutes).
            double t0 = mission.Simulation.State.elapsedSeconds;
            yield return new WaitForSecondsRealtime(2f);
            double gained = mission.Simulation.State.elapsedSeconds - t0;
            Check(gained > 120 && gained < 260, "The clock runs about 98x real time", gained.ToString("0") + " game s in 2 real s");
            mission.ClockScale = 1; // the rest of the run checks behaviour, not pacing

            yield return Doors();
            yield return GatingAndTools();
            yield return Garden();
            Vitals();
            yield return HudScreens();
            Finish();
        }

        // ------------------------------------------------------------------ helpers

        private Vector3 Floor(float x, float z) { return outpost.transform.TransformPoint(new Vector3(x, 0.02f, z)); }

        private Vector3 Ground(Vector3 p) { p.y = terrain.GetGroundY(p) + 0.02f; return p; }

        private void Place(Vector3 position, Vector3 lookAt)
        {
            mission.SetShopOpen(false);
            Vector3 flat = lookAt - position; flat.y = 0;
            player.RestoreMissionPose(position, flat.sqrMagnitude > 1e-4f ? Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg : 0f);
            player.AimAt(lookAt);
        }

        private IEnumerator Frames(int n) { for (int i = 0; i < n; i++) yield return null; }

        /// <summary>Full-screen capture including the UI overlays.</summary>
        private IEnumerator ScreenShot(string name)
        {
            if (Application.isBatchMode) { ShotWithUi(name); yield break; } // WaitForEndOfFrame never fires in batchmode
            yield return new WaitForEndOfFrame();
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null) yield break;
            Texture2D shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(outDir, name + ".png"), shot.EncodeToPNG());
            Destroy(shot);
        }

        /// <summary>Batchmode capture with the overlay UI: render the canvases through the view camera for one frame.</summary>
        private void ShotWithUi(string name)
        {
            var moved = new List<Canvas>();
            foreach (Canvas c in FindObjectsOfType<Canvas>())
                if (c.isRootCanvas && c.renderMode == RenderMode.ScreenSpaceOverlay && c.enabled)
                { c.renderMode = RenderMode.ScreenSpaceCamera; c.worldCamera = View; c.planeDistance = 0.5f; moved.Add(c); }
            Canvas.ForceUpdateCanvases();
            Shot(name);
            foreach (Canvas c in moved) c.renderMode = RenderMode.ScreenSpaceOverlay;
        }

        private Camera View { get { return player != null && player.ViewCamera != null ? player.ViewCamera : Camera.main; } }

        private void Shot(string name)
        {
            Camera cam = View;
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
            // 4 hub doorways, storage, utilities, greenhouse, and the airlock's inner and outer doors.
            Check(doors.Length == 9, "Sliding doors in every doorway", doors.Length + " doors");
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
            int seeds = mission.Simulation.State.seeds[0];
            mission.Execute("plant", "lettuce:0");
            Check(mission.Notice.Contains("You need") && mission.Simulation.State.seeds[0] == seeds,
                "Sowing refuses without the shovel", mission.Notice);

            var ids = new List<string>();
            foreach (MarsBaseTool t in MarsBaseTool.All) if (MarsToolPickup.IsHandTool(t.toolId) && !ids.Contains(t.toolId)) ids.Add(t.toolId);
            Check(ids.Count == MarsSurvival.Tools.MarsTool.Order.Length, "Every hand tool is on a rack in the base", ids.Count + "");
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
                if (id == "hydro_sprayer") { Shot("held_" + id); yield return ScreenShot("screen_held_" + id); }
            }
            if (hotbar != null) hotbar.Select(0);
            Check(hotbar != null && hotbar.Current.Id == "regolith_shovel" && hotbar.CurrentCarried, "Hotbar slot 1 selects the Shovel",
                hotbar == null ? "no hotbar" : hotbar.Current.Id);
            if (hotbar == null) yield break;
            hotbar.Select(0);
            yield return Frames(2);
            Check(!hotbar.CurrentCarried && hotbar.HeldView == null, "Pressing 1 again puts the Shovel away (empty hands)");
            hotbar.Select(0);
            Check(hotbar.CurrentCarried, "Pressing 1 once more takes it back in hand");

            // The tool shed: E puts every carried tool back, and the hotbar empties.
            Place(Floor(0f, -16.4f), MarsWorldTasks.World(MarsWorldTasks.ShedPoint + new Vector3(0f, 1.7f, 0f))); yield return Frames(3);
            MarsTaskTarget shedUse = belt.FindUse(player.AimTransform, mission.Simulation.State, out string shedLabel);
            Check(shedUse != null && shedUse.kind == MarsTaskTarget.Kind.Shed, "Looking at the tool shed offers E to put tools back", shedLabel ?? "nothing");
            mission.Notice = belt.PutAllBack();
            yield return Frames(2);
            Check(mission.Simulation.State.carriedTools.Count == 0 && !hotbar.CurrentCarried && hotbar.HeldView == null,
                "Tools go back on the shed; hands and hotbar empty", mission.Notice);
            Place(Floor(1.6f, -15.6f), MarsWorldTasks.World(MarsWorldTasks.ShedPoint + new Vector3(-0.3f, 1f, 0f))); yield return Frames(3);
            Shot("tool_shed");
            foreach (MarsBaseTool t in MarsBaseTool.All)
                if (t != null && !t.Carried && Array.IndexOf(MarsSurvival.Tools.MarsTool.Order, t.toolId) >= 0) belt.TryPickUp(t);
            Check(mission.Simulation.State.carriedTools.Count == 3, "All three tools can be taken from the shed again", mission.Simulation.State.carriedTools.Count + " carried");
        }

        // ------------------------------------------------------------------ garden

        private Vector3 RoomCentre(FloorRect r) { return Floor((r.x0 + r.x1) * 0.5f, (r.z0 + r.z1) * 0.5f); }

        /// <summary>Uses a tool on a task object as a player would (aimed at it, click).</summary>
        private bool UseToolOn(string toolId, MarsTaskTarget on, string what)
        {
            MarsSurvival.Tools.MarsToolBelt hotbar = MarsSurvival.Tools.MarsExpedition.Instance == null ? null : MarsSurvival.Tools.MarsExpedition.Instance.Belt;
            if (hotbar == null) { Check(false, what + ": hotbar exists"); return false; }
            mission.Tools.Hold(toolId);   // in hand (Select would put it away if it already were)
            var target = new MarsSurvival.Tools.ToolTarget();
            if (on != null) { target.Hit = true; target.Collider = on.GetComponent<Collider>(); target.Point = on.transform.position; }
            MarsSurvival.Tools.ToolAction plan = hotbar.Current.Plan(target);
            bool usable = hotbar.CurrentCarried && plan != null && !plan.Blocked;
            if (!usable) { Check(false, what + ": " + MarsBaseTool.NameOf(toolId) + " can be used", plan == null ? "no action" : plan.Label); return false; }
            if (plan.Complete != null) plan.Complete();
            return true;
        }

        private void HitboxChecks()
        {
            float RoofAt(float x, float z)
            {
                Vector3 from = outpost.transform.TransformPoint(new Vector3(x, 12f, z));
                RaycastHit hit;
                if (!Physics.Raycast(from, -outpost.transform.up, out hit, 20f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return -99f;
                return outpost.transform.InverseTransformPoint(hit.point).y;
            }
            // Heights of the printed modules (tools/blender/outpost_modules.py): vaults run along the
            // tunnels, so the eave is sampled across the vault; the hub's skylight glass is solid.
            float vault = RoofAt(-9.5f, 0f);
            Check(vault > 3.75f, "Vaulted roof: solid up to its ridge", vault.ToString("0.00") + " m");
            float dome = RoofAt(0f, 0f);
            Check(dome > 5f, "Hub dome: solid up to its top (the skylight too)", dome.ToString("0.00") + " m");
            float eave = RoofAt(-9.5f, -3.6f);
            Check(eave < vault - 0.3f && eave > 2.7f, "Vaulted roof: the collider follows the curve down to the eaves", eave.ToString("0.00") + " m");
            float tube = RoofAt(-6f, 0f);
            Check(tube > 2.7f, "Tunnel tube: solid up to its drawn top", tube.ToString("0.00") + " m");
            Vector3 side = outpost.transform.TransformPoint(new Vector3(-6f, 1.2f, 1.65f));
            Check(Physics.CheckSphere(side, 0.25f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore), "Tunnel tube: its round sides block you");
        }

        private static MarsTaskTarget FindTarget(Func<MarsTaskTarget, bool> match)
        {
            foreach (MarsTaskTarget t in FindObjectsOfType<MarsTaskTarget>()) if (match(t)) return t;
            return null;
        }

        private MarsTaskTarget Plot(int index) { return FindTarget(t => t.kind == MarsTaskTarget.Kind.Plant && t.index == index); }

        private IEnumerator Garden()
        {
            MissionSimulation sim = mission.Simulation;
            MissionState s = sim.State;
            Vector3 gh = RoomCentre(MarsOutpostBuilder.Greenhouse);
            Place(gh, gh + Vector3.forward); yield return Frames(3);
            Check(mission.Context.station == "greenhouse", "Greenhouse detected", mission.Context.station);

            // Every plot sits inside its bed; only the first six are open at the start.
            bool inside = true; int plots = 0;
            foreach (MarsTaskTarget t in FindObjectsOfType<MarsTaskTarget>())
            {
                if (t.kind != MarsTaskTarget.Kind.Plant) continue;
                plots++;
                Vector3 p = outpost.transform.InverseTransformPoint(t.transform.position);
                float x0 = t.index < MissionSettings.SpotsPerBed ? -3.3f : 1.3f;
                if (p.x < x0 + 0.15f || p.x > x0 + 1.85f || p.z < -16.1f || p.z > -8.6f) inside = false;
            }
            Check(plots == MissionSettings.MaxPlots && inside, "24 greenhouse plots, each inside its bed", plots + "");
            Check(s.unlockedPlots == MissionSettings.StartPlots && s.credits == MissionSettings.StartCredits && s.seeds[0] == 3,
                "Start: 6 open plots, 30 credits, 3 lettuce seeds", s.unlockedPlots + " plots, " + s.credits + " cr");

            // The shop: E on the terminal opens it; buying takes credits and gives seeds.
            MarsTaskTarget terminal = FindTarget(t => t.kind == MarsTaskTarget.Kind.Shop);
            Check(terminal != null, "Seed shop kiosk in storage");
            if (terminal != null)
            {
                Place(Floor(-10.6f, 1.2f), terminal.transform.position + Vector3.up * 1.1f); yield return Frames(3);
                MarsTaskTarget aimed = mission.Tools.FindUse(player.AimTransform, s, out string label);
                Vector3 tc = terminal.GetComponent<Collider>().bounds.center - player.AimTransform.position;
                Check(aimed == terminal, "Looking at the kiosk offers E to open the shop", (label ?? "nothing") + " d=" + tc.magnitude.ToString("0.00") +
                    " angle=" + Vector3.Angle(player.AimTransform.forward, tc).ToString("0") + " usable=" + MarsTaskTarget.Usables.Contains(terminal) + " local=" + outpost.transform.InverseTransformPoint(player.transform.position));
            }
            mission.SetShopOpen(true);
            yield return Frames(2);
            Check(mission.ShopOpen && mission.InputBlocked && Cursor.visible, "The shop opens, frees the cursor and stops walking");
            yield return ScreenShot("screen_shop");
            int credits = s.credits;
            mission.Execute("buy", "tomato");
            Check(s.credits == credits - 25 && s.seeds[1] == 1, "Buy tomato seeds for 25 credits", mission.Notice);
            mission.Execute("buy", "watermelon");
            Check(s.credits == credits - 25 && s.seeds[2] == 0 && mission.Notice.Contains("Not enough"), "Can't buy what you can't afford", mission.Notice);
            mission.SetShopOpen(false);
            Check(!mission.ShopOpen && !mission.InputBlocked, "Closing the shop returns to walking");

            // Sow, refuse locked plots, water, grow, pick.
            Place(gh, gh + Vector3.forward); yield return Frames(3);
            for (int i = 0; i < 3; i++) UseToolOn("regolith_shovel", Plot(i), "Shovel (1) sows plot " + (i + 1));
            Check(s.plots[0].seed == "lettuce" && s.plots[2].seed == "lettuce" && s.seeds[0] == 0, "Shovel sows the three lettuce seeds", s.seeds[0] + " left");
            var hotbar = MarsSurvival.Tools.MarsExpedition.Instance.Belt;
            mission.Tools.Hold("regolith_shovel");
            hotbar.Current.Secondary(null); // R: next seed (tomato)
            UseToolOn("regolith_shovel", Plot(3), "Shovel sows tomato after R");
            Check(s.plots[3].seed == "tomato", "R switches the shovel to tomato seeds", s.plots[3].seed);
            var locked = hotbar.Current.Plan(new MarsSurvival.Tools.ToolTarget { Hit = true, Collider = Plot(10).GetComponent<Collider>() });
            Check(locked != null && locked.Blocked && locked.Label.Contains("Locked"), "Locked plots refuse sowing", locked == null ? "no plan" : locked.Label);
            PlayerContext idle = mission.Context; idle.moving = false;
            sim.Step(MissionSettings.SolSeconds * 0.5, mission.Environment, idle);
            Check(s.plots[0].growth == 0, "A dry plot does not grow", s.plots[0].growth.ToString("0.00"));
            for (int i = 0; i < 4; i++) UseToolOn("hydro_sprayer", Plot(i), "Sprayer (2) waters plot " + (i + 1));
            Check(s.plots[0].wetUntil > s.elapsedSeconds, "Sprayer waters a plot", mission.Notice);
            yield return Frames(2);
            TMPro.TextMeshPro timer = Plot(0).transform.Find("Timer").GetComponent<TMPro.TextMeshPro>();
            Check(timer.gameObject.activeInHierarchy && timer.text.StartsWith("0:"), "A watered lettuce shows its ripening timer", timer.text);
            Place(Floor(0.3f, -13.5f), Floor(-1.6f, -9f)); yield return Frames(3);   // look back at the first plots by the door
            Shot("garden_timers");
            double lettuceTime = SeedKind.Find("lettuce").GameSeconds;
            for (double t = 0; t < lettuceTime - 1; t += 10) sim.Step(Math.Min(10, lettuceTime - t), mission.Environment, idle);
            s.astronaut.food = s.astronaut.water = 100;
            Check(s.plots[0].Ripe && !s.plots[3].Ripe && Math.Abs(s.plots[3].growth - 0.5) < 0.02, "One watering: lettuce ripe in 30 s, tomato half grown",
                s.plots[0].growth.ToString("0.00") + " / " + s.plots[3].growth.ToString("0.00"));
            yield return Frames(2);
            Place(Floor(0.3f, -13.5f), Floor(-1.6f, -9f)); yield return Frames(3);   // look back at the first plots by the door
            Shot("garden_ripe");
            Place(Plot(0).transform.position + new Vector3(1.9f, 0, 0), Plot(0).transform.position + Vector3.up * 0.2f); yield return Frames(3);   // from the aisle
            MarsTaskTarget ripe = mission.Tools.FindUse(player.AimTransform, s, out string pickLabel);
            Check(ripe != null && ripe.kind == MarsTaskTarget.Kind.Plant && pickLabel.StartsWith("pick"), "Looking at a ripe plant offers E to pick it", pickLabel ?? "nothing");
            int basket = s.basket[0];
            for (int i = 0; i < 3; i++) mission.Execute("pick", i.ToString());
            Check(s.basket[0] == basket + 3 && s.plots[0].seed == "", "Picking moves crops to the basket and frees the plot", s.basket[0] + " lettuce");

            // Eat one, sell the rest, expand the greenhouse.
            s.astronaut.food = 40;
            Place(Floor(-13f, 1.5f), MarsWorldTasks.World(new Vector3(-14.6f, 0.9f, 3.25f))); yield return Frames(3);
            MarsTaskTarget locker = mission.Tools.FindUse(player.AimTransform, s, out string lockerLabel);
            Check(locker != null && locker.kind == MarsTaskTarget.Kind.Food, "Storage: E on the food locker offers to eat", lockerLabel ?? "nothing");
            mission.Execute("eat");
            Check(s.astronaut.food > 60 && s.basket[0] == basket + 2, "Eating a crop from the food locker refills food", mission.Notice);
            s.astronaut.water = 40; mission.Execute("drink");
            Check(s.astronaut.water == 100, "Drinking at the water dispenser refills water", mission.Notice);
            credits = s.credits;
            mission.Execute("sell", "lettuce");
            Check(s.basket[0] == 0 && s.credits == credits + (basket + 2) * 15, "Sell lettuce for 15 credits each", mission.Notice);
            s.credits += 200;
            credits = s.credits;
            mission.Execute("buy", "plots");
            Check(s.unlockedPlots == 8 && s.credits == credits - 150 && sim.ExpansionPrice == 200, "Add 2 plots for 150 credits (next 200)", mission.Notice);
            Place(gh, gh + Vector3.forward); yield return Frames(3);
            mission.Execute("buy", "lettuce");
            UseToolOn("regolith_shovel", Plot(7), "Shovel sows a new plot");
            Check(s.plots[7].seed != "", "A bought plot can be sown", s.plots[7].seed);

            // Sleep runs time to the morning.
            Place(RoomCentre(MarsOutpostBuilder.Storage), RoomCentre(MarsOutpostBuilder.Storage) + Vector3.forward); yield return Frames(3);
            s.astronaut.food = s.astronaut.water = 100;
            yield return new WaitForSecondsRealtime(1.5f); // let the clock take a live step after the scripted ones
            mission.Sleep();
            Check(sim.Asleep && mission.InputBlocked, "The bunk puts the astronaut to sleep", mission.Notice);
            float limit = Time.realtimeSinceStartup + 20;
            while (sim.Asleep && Time.realtimeSinceStartup < limit) yield return null;
            double hour = mission.Environment.localHour;
            Check(!sim.Asleep && hour > 6.8 && hour < 7.5, "Sleep ends at 07:00", hour.ToString("0.00"));
            Check(s.phase == MissionPhase.Running, "Astronaut still alive after the garden loop", s.endingReason);
        }

        private void Vitals()
        {
            MissionSimulation sim = mission.Simulation;
            MissionState s = sim.State;
            PlayerContext outside = mission.Context; outside.zone = PlayerZone.Outdoors;
            PlayerContext inside = mission.Context; inside.zone = PlayerZone.Habitat;
            s.astronaut.oxygen = 100;
            sim.Step(MissionSettings.HourSeconds, mission.Environment, outside);
            Check(Math.Abs(s.astronaut.oxygen - 75) < 1, "Oxygen drains outside (25 per hour)", s.astronaut.oxygen.ToString("0.0"));
            sim.Step(MissionSettings.HourSeconds, mission.Environment, inside);
            Check(s.astronaut.oxygen == 100, "Oxygen refills inside", s.astronaut.oxygen.ToString("0.0"));
            double food = s.astronaut.food;
            sim.Step(MissionSettings.SolSeconds * 0.25, mission.Environment, inside);
            Check(s.astronaut.food < food && s.astronaut.water < 100, "Food and water drain over time");
            var copy = new MissionSimulation(MissionSimulation.NewMission(1));
            copy.Step(MissionSettings.HourSeconds * 5, mission.Environment, outside);
            Check(copy.State.phase == MissionPhase.Lost && copy.State.endingReason.Contains("oxygen"), "Running out of oxygen ends the game", copy.State.endingReason);
        }

        /// <summary>Screens of the HUD: walking with the hotbar, the pause menu.</summary>
        private IEnumerator HudScreens()
        {
            Vector3 gh = RoomCentre(MarsOutpostBuilder.Greenhouse);
            Place(gh, gh + Vector3.forward * 3f);
            MarsSurvival.Tools.MarsExpedition.Instance.Belt.Select(1);
            yield return new WaitForSeconds(1f);
            yield return ScreenShot("screen_hud");
            Check(FindObjectOfType<MarsTrainer.UI.PauseMenu>(true) != null && FindObjectOfType<MarsTrainer.UI.EndScreen>(true) != null, "Pause menu and end screens exist");
            Check(FindObjectOfType<MarsToolHud>() != null, "Tool hotbar HUD exists");
            MarsTrainer.UI.PauseMenu pause = FindObjectOfType<MarsTrainer.UI.PauseMenu>(true);
            mission.SetPaused(true); pause.Open();
            yield return new WaitForSeconds(0.5f);
            yield return ScreenShot("screen_pause");
            Check(mission.Paused && mission.InputBlocked, "Pause menu pauses the mission");
            pause.Close();
            yield return null;
            Check(!mission.Paused && !mission.InputBlocked, "Closing the pause menu resumes the mission");
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
