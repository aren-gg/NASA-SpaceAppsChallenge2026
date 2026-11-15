# Mars survival game: complete implementation plan

Prepared on 14 November 2026 from the current repository and the requested feature list. This is an implementation handoff, not a claim that the game has been implemented or tested. Copy this whole file into a coding-enabled ChatGPT/Codex session with access to the repository.

## 1. Instructions for the implementing agent

Implement the missing work in this plan in the existing Unity project. Deliver a complete, playable Mars survival game; do not stop after another plan, a UI mockup, or isolated systems. Reuse completed work and finish partial systems. Inspect the current code before changing it, because it may have advanced since this audit.

Follow repository instructions and Cary's writing conventions. Keep the implementation straightforward, comments short, and documentation specific. Preserve user changes, Unity `.meta` files, GUIDs, and existing assets. Do not reset the repository, upgrade packages, switch engines, or replace the existing terrain, outpost, rover, or weather module to implement this plan.

This document takes precedence over the older feature-status table and execution waves in `AGENT_IMPLEMENTATION_GUIDE.md`. Use that guide for compatible module details. The user's latest mission choices are **one Martian month, half a Martian year, and one full Martian year**.

The user has confirmed a paired hardware/software design: a physical dashboard with **three adjustable knobs for Life control, Temperature and Radiation shielding**. Managing the energy spent on these systems is the main gameplay loop. The hardware requirement and these controls are confirmed project scope; board-specific details still need to be recorded.

Work autonomously on ordinary implementation decisions. If an external dependency prevents a task, record the exact blocker and continue independent work. Do not describe an untested scene, unavailable hardware, or an unverified challenge requirement as complete. Keep the requested scope intact if work spans multiple sessions.

Before coding, create a short checked/unchecked implementation ledger at `docs/implementation-status.md`. Link each requirement to its implementation and verification evidence. Update it at meaningful milestones, not after every small edit.

## 2. What already exists

The following is based on source inspection, not a new Unity playthrough or build. Paths in this table are relative to `My project/` unless stated otherwise.

| System | Current evidence | Required treatment |
| --- | --- | --- |
| Gale Crater world | `Assets/Scripts/World/`, `Assets/Data/Terrain/`; streamed terrain, elevation, coordinates, slopes, procedural rocks | Reuse. Verify in Unity; add exploration content rather than rebuilding map generation. |
| Mars time and daylight | `MarsTime.cs`, `MarsDayNightCycle.cs`; Mars24 calculations and accelerated day/night | Reuse astronomical calculations; move authoritative time advancement into the simulation. |
| Weather scheduling | `MarsWeatherSchedule.cs`, `MarsWeatherArchive.cs`; seeded dust/heat/night and bundled DONKI context | Extend existing scheduler with physical effects and complete campaign coverage. |
| Weather visuals | `Assets/MarsWeather/`; Clear, Solar Flare, Dust Storm, Heat Storm, Cold Night | Reuse API, assemblies, prefab and visual effects. Visual strength is not scientific measurement. |
| Combined environment | `Assets/Scenes/Mars.unity`, `Assets/Scripts/Integration/` | Finish explicit scene composition and standalone build verification. |
| Astronaut | `MarsAstronautPlayer.cs`, `MarsAstronautBody.cs`; walking, sprinting, jumping, collision, camera | Connect activity and traversal to survival and task systems. |
| Outpost | `Assets/Scripts/Base/`; enterable rooms, airlock doors, medical bay, greenhouse, shelter, energy room, life support, laboratory | Existing rooms and equipment are interaction locations and art. Their survival mechanics are missing. |
| Rover | `Assets/Scripts/Vehicles/`; boarding, movement, battery use by distance, pad charging, HUD | Preserve vehicle. Replace free charging and integrate battery, supplies, persistence and pause. |
| Menus and information | `MarsHomeScreen.cs`, `MarsClockHud.cs`, terrain F3 panel | Extend with mission selection, survival HUD, actions, research, objectives, save/load and debrief. |
| Survival and production | No authoritative health, food, water, fatigue, habitat power, oxygen, dose, farming or medical simulation found | Build. Decorative equipment does not count as completion. |
| Research and exploration rewards | No tree, research transactions, sample rewards, science tasks or persistent POI completion found | Build all four branches and the research acquisition loop. |
| Mission lifecycle | No campaign victory/failure, save/load or retry state found | Build. Current Start Mission only dismisses the home screen. |
| Physical controller | The user confirms a physical dashboard with three adjustable knobs; the earlier repository audit found no adapter/firmware | Integrate the confirmed Life control, Temperature and Radiation shielding controls; verify the actual device/firmware and follow section 10. |

Existing visual references are in `docs/game-design-references.md` and `docs/art/references/`. Use those references for new equipment and upgrade silhouettes. Existing outpost/rover colors differ from that guide; preserve usable work and make small, deliberate consistency fixes after gameplay works. No new art search or wholesale asset replacement is required.

Important baseline findings:

- `ProjectSettings/EditorBuildSettings.asset` currently includes only `Assets/MarsWeather/Scenes/WeatherPreview.unity`. The release build must start the gameplay scene.
- `MarsRover.Recharge()` adds battery charge without spending habitat energy. Replace this accounting before balancing the economy.
- `MarsOutpost.CurrentRoom` is calculated from the camera and is null in connecting tunnels. It cannot define the astronaut's atmosphere or radiation protection.
- Multiple components bootstrap themselves after scene load. New mission, load and retry must not create duplicate players, bases, cameras or clocks.
- F2 inspection flight can relocate the astronaut; bracket keys alter time; number keys override weather. Gate these behind development mode. Keep source information accessible in normal play.
- Some weather-prefab installation occurs only inside the editor. Serialize all required gameplay references for a built player.
- Existing terrain docs report checks using an engine stand-in, with streaming/scale still needing Unity verification. Do not turn those reports into a claim of current build validation.
- The working tree already contains modified rendering, package and project settings, a deleted `placeholder.py`, and untracked `Assets/MarsBase/` metadata/directories. Preserve them. The inspected MarsBase directory did not contain implemented gameplay.
- The old guide names Unity 2022.3.24f1; the working `ProjectVersion.txt` says 2022.3.62f3, and the manifest uses URP 14.0.12. Record the available editor and baseline without forcing an upgrade/downgrade or reverting user changes.

## 3. Product definition and mission lengths

The player maintains one astronaut and one outpost in Gale Crater using the game and its paired physical dashboard. The main loop is to adjust three knobs so Life control, Temperature and Radiation shielding each receive enough energy to keep the astronaut alive, while avoiding excessive spending on any one system. Conditions change over time, so a setting that is sufficient now may be wasteful or insufficient later.

Solar panels generate electricity; research and installed solar upgrades improve generation. Repairs, soil sampling, farming, survey-marker photography, radio operation, panel dusting and updates to Houston all consume energy from the same overall economy. Players must leave enough energy for those tasks and future survival. Farming produces food rather than electrical energy.

### Mission selection

Reuse `MarsTime.SolSeconds = 88775.244` and the project's `MarsTime.SolsPerYear = 668.5921` convention. Keep exact durations internally; round only the player-facing display.

| Preset | Duration | Display | Intended role |
| --- | --- | --- | --- |
| One Martian month | `SolsPerYear / 12`, approximately 55.716 sols | About 56 sols | Short campaign with the full core loop and meaningful research choices |
| Half a Martian year | `SolsPerYear / 2`, approximately 334.296 sols | About 334 sols | Longer resource planning and several environmental cycles |
| One Martian year | `SolsPerYear`, approximately 668.592 sols | About 669 sols | Full endurance campaign |

For this game, a **Martian month means one-twelfth of a Martian year**. State this in the selector; it is a game calendar convention, not an official NASA month. Show mission-relative Sol 1, progress and sols remaining separately from astronomical Mars date/local time.

All presets include farming, solar investment, healthcare, exercise, radiation, oxygen, weather, exploration and all four research paths. Adjust research reward budgets, objective spacing and starting support to make each preset viable. Do not change scientific units or silently multiply physiological rates to fit a shorter campaign. Document any deliberately compressed crop growth or other game rules.

Use the short campaign as the default selection. Keep campaign length separate from difficulty. A short guided tutorial may precede any preset, but it must not replace the selected campaign.

### Play cycle

1. Read solar generation, stored energy, forecast, machine condition and astronaut needs.
2. Adjust the three hardware knobs to spend just enough on Life control, Temperature and Radiation shielding. Compare requested power, delivered power and current useful need.
3. Budget the remaining energy for repairs, farming, sampling, survey photographs, communications, panel dusting and the return journey from any trip.
4. Carry out tasks, collect samples and photographic evidence, maintain the rover/base, and return to analyze findings.
5. Manage radio signal and transmission windows to send status and science updates to the in-game Houston mission control.
6. Spend research points and installation resources to improve solar generation and other systems; dust panels to recover output lost to deposits.
7. Run the daily routine or advance time while safe, then rebalance the knobs as weather, reserves and equipment needs change. Repeat until the selected mission duration is complete.

Success requires reaching the exact campaign duration with the astronaut alive. Health reaching zero ends the mission once; present a readable cause and retry/load options. If lethal damage and mission completion coincide, resolve damage first and document that rule. Stop all resource transactions after a terminal state.

The debrief explains survival duration, major decisions, research paths, science completed and causes of any failure. Use constructive language suitable for the existing intended audience of ages 7–12. Challenge-specific audience requirements must be verified separately in section 9.

## 4. Architecture and contracts

Create one authoritative simulation; UI, world objects, tasks and hardware issue validated commands. They must not directly mutate shared resources. Follow the current namespace and assembly conventions, adding only the structure required by the implementation.

Suggested ownership and files:

| Area | Responsibility | Suggested location |
| --- | --- | --- |
| Core | Clock, mission state, commands, configuration, tick order, resource accounting | `Assets/Scripts/Simulation/` |
| Environment | Physical temperature, solar availability, radiation profile and event effects | Extend `Assets/Scripts/Environment/` |
| Progression | Research definitions, prerequisites, transactions and derived upgrade effects | `Assets/Scripts/Research/` |
| Exploration | POIs, tasks, samples, route budgets and map objectives | `Assets/Scripts/Exploration/` |
| Operations | Machine condition/repairs, field equipment, survey records, radio signal and Houston report queue | `Assets/Scripts/Operations/`, or existing task modules where appropriate |
| Hardware | Three-knob input, calibration, connection state and feedback | `Assets/Scripts/Hardware/` and device firmware |
| Persistence | Versioned save, validation, restore and backup | `Assets/Scripts/Persistence/` |
| Interaction/UI | Stations, menus, HUD, notifications and command bindings | Existing `Interaction/` and `UI/` |
| Integration | Scene composition, existing component adapters and startup readiness | Existing `Integration/` |
| Data preparation | Offline downloads, extraction, small prepared datasets and provenance | Existing `tools/data/`, `Assets/Data/`, `docs/data/` |

These are suggested paths, not a reason to duplicate an existing abstraction. Use ScriptableObjects for authored configuration and fresh serializable state for each mission. Do not store mutable mission values in shared configuration assets.

Minimum contracts:

- `MissionConfig`: preset, exact duration, seed, start date, difficulty, resource capacities/rates, three channel ratings/useful-output curves, task costs, machine wear, signal model, research definitions and data profile IDs.
- `MissionState`: lifecycle, elapsed simulated seconds, seed/random state, astronaut, habitat, suit, rover, field battery, three knob setpoints, crops, installed equipment/condition, panel deposits, tasks, research, POI/sample/marker/photo records, radio mode, report queue/acknowledgments, event state and learning progress.
- `PowerAllocationState`: independent Life control, Temperature and Radiation shielding requests, actual delivered kW, estimated useful need and shortfall/waste indicators. The hardware adapter changes requests through commands; Unity calculates consequences.
- `EnvironmentSample`: local temperature, solar model inputs/output, physical radiation rate with units, simultaneous active hazards, and source/model IDs. It is independent of weather visual transition weights.
- `GameCommand`: stable action ID, parameters and validation. Return a success/failure reason; never apply half a purchase or half a resource transfer.
- `SimulationSnapshot`: read-only values for UI, stations, history and hardware feedback.

Units: simulated seconds, real metres, degrees Celsius, kW, kWh, litres of water, a documented oxygen mass unit such as grams, and absorbed radiation dose/rate with explicit units such as microgray and microgray/hour when supported by the source. Food stock is portions or mass; satiety, hydration, fatigue and HP may be clearly labeled game scales. Keep food energy and electrical energy separate.

Tick order:

1. Advance to the next bounded time step or earlier event/action boundary.
2. Evaluate all environmental conditions at that simulation time.
3. Gather requested electrical loads and production inputs.
4. Allocate available generation/storage to the three knob requests, then other loads, using the shortage rule below and equipment limits.
5. Apply actual production, consumption, charging, transfers and losses exactly once.
6. Advance crops/tasks; apply astronaut exposure, needs, treatment and recovery.
7. Resolve rewards/upgrades, warnings and terminal state; publish one snapshot.

Split steps at important discontinuities: storm onset/end, reserve depletion, crop/task completion, exposure threshold and campaign end. Avoid a large catch-up step that skips a damaging event. Record cause codes for HP changes and an aggregated resource ledger for debugging.

### Time, pause and campaign pacing

The existing 20-real-minute sol would make a full year take about 223 real hours before pauses. Add safe time advancement so all three presets are practical to finish.

- Use one simulation clock for survival, crops, tasks, research installation, weather and mission progress. `MarsDayNightCycle` becomes a consumer of that clock for astronomical/visual state.
- Keep Unity physics, UI animations and rendering stable; do not accelerate the entire engine with a huge `Time.timeScale`.
- Default free exploration to one simulated second per real second so metre-based movement and resource use agree. Higher management speeds are available while the astronaut is stationary in the base. Clearly show the current speed.
- Add explicit **Rest**, **Run daily routine**, and **Advance until next decision** actions. During time advancement, simulate stocked meals/drinking, scheduled sleep/exercise and assigned care tasks with their real resource costs. Do not require hundreds of identical manual meal clicks.
- Give the astronaut one active personal task at a time. Sleeping, exercising, installing equipment, staffed analysis and EVA cannot overlap. Unattended devices/crops may run concurrently. Automatic routines use the same command validation, reservations and costs as manual actions; interruptions cannot duplicate progress or supplies.
- Offer bounded increments such as one sol and several sols. Stop on an actionable warning, new hazard requiring a decision, selected task completion, invalid routine, resource risk or mission end. Make repeated low-priority notices nonblocking after acknowledgment; critical changes still stop advancement.
- Compute accelerated periods through bounded simulation steps with a work budget per frame. Do not discard accumulated time to catch up. Keep cancellation and progress feedback responsive.
- Pause freezes resource values, movement, tasks, event progression and mission time. Menus and harmless presentation animations may continue. Resume cannot reapply production or rewards.
- Use a provisional playtime target of 30–45 active minutes for the short campaign, 60–90 for half-year and 90–150 for full-year, excluding pauses. These are design targets to validate, not measured outcomes. Tune decisions and advancement controls rather than changing units.

## 5. Survival, production and investment

### Electrical generation, storage and spending

Solar arrays are the ongoing electrical energy source. Solar research improves installed panels' output, capacity or collection efficiency; it does not award a lump of electricity when research completes. Batteries store previously generated energy. Farming supplies food/body energy and consumes electrical energy; it does not charge batteries. Samples, photographs, repairs and Houston reports may enable progress or research rewards, but do not create electricity.

### Three-knob energy allocation

Use these exact dashboard labels, with a short explanation of Life control in the UI:

| Knob | Controlled equipment | Insufficient power | Excess spending |
| --- | --- | --- | --- |
| **Life control** | Oxygen production, cabin air processing and water recycling | Lower life-support output depletes available oxygen/water and eventually harms the astronaut | Equipment still draws its selected power; useful production is limited by need, available inputs and storage |
| **Temperature** | Habitat heating/cooling and thermal-control equipment | Internal temperature drifts toward unsafe conditions, with gradual health consequences | Additional power above useful thermal demand gives little or no extra safety |
| **Radiation shielding** | The game's powered radiation-protection subsystem | Additional protection falls below that needed for the current event | Protection has a capped useful effect, so high settings during low exposure waste stored energy |

- Each knob independently maps 0–100% to 0–rated kW for its channel. These are equipment settings, not percentages of one shared total; do not normalize them to sum to 100%.
- The setting requests actual power consumption, not merely a ceiling under which the game automatically selects the optimum. When supply exists, the selected power is spent. Useful output saturates; show avoidable draw or low efficiency above useful demand. Three maximum settings must not be the universally best strategy.
- Estimate the current power needed for safe operation using conditions, reserves, installed upgrades and machine condition. Present this estimate with a warning/target band, while leaving the decision to the player. Research can reduce required draw or improve output, but cannot silently reposition the knobs.
- Available power is current solar generation plus permitted battery discharge. Supply the three requested channels first. If their sum exceeds available power, scale all three deliveries by the same `available / requested_total` factor, leaving saved knob positions unchanged. Zero total request yields zero channel delivery. Show every shortfall; never silently choose a different optimal allocation.
- Supply greenhouse operations, charging, repairs, tools and communications from the remaining available power, using one documented queue/priority rule. If their draw cannot be supported, reduce progress or pause according to the task's rules. High knob settings can therefore drain reserves or delay essential work even when the astronaut's immediate needs are met.
- Bill each device once. Oxygen production, air processing and recycling are inside the Life control channel; they must not appear again as additional independent charges. Task-specific medical devices, greenhouse systems and tools use separate task loads.
- Treat the extra shielding effectiveness-versus-power relationship as an explicit gameplay abstraction. Keep permanent habitat/shelter shielding as a separate passive baseline, and never imply that losing power removes physical shielding material or that extra power erases accumulated dose. This abstraction is not a measured NASA shielding technology.

The hardware sends settings; the software owns the clock, power ledger, environment and resulting health. Keyboard/mouse controls mirror all three knobs for accessibility, testing and disconnected operation.

### Generation, storage and other loads

- Compute solar output from panel area, efficiency, Sun geometry/distance, an explicit atmospheric/dust model and panel condition. Use geometric sunrise/sunset; a bright sky or twilight shader cannot generate energy.
- Track habitat, rover and portable field-equipment storage separately. Respect capacity, maximum charge/discharge power, losses and charger limits. Outdoor tools/cameras/radios use their connected portable or rover battery; they cannot remotely spend habitat energy without a modeled connection. Recharging them debits the habitat through the same load allocator.
- Integrate `energy_kWh = power_kW * elapsed_seconds / 3600`. Available supply is generation plus permitted battery discharge, not two copies of the same energy.
- Include the three dashboard channels plus greenhouse lighting/pumps, medical devices, analysis, installation, rover/field-battery charging and every task in the operations table below. Expose requested versus delivered loads and estimated battery endurance.
- Apply the three-channel shortage rule before the other-load queue. A partially powered device produces only the supported fraction of its output; no safety routine may secretly supply energy or override a deliberately low knob setting.
- Provide install/upgrade/service interactions at the current array and energy room. Use research unlocks, a small parts inventory, installation time and consumed energy. Show cost, benefit, prerequisites and available placement slots before purchase.
- Supply parts through starting stock and clearly fictional mission supply caches/rewards. Make the acquisition route explicit; do not create a circular requirement where the player needs the upgrade to obtain its only construction material.
- Replace `MarsRover.Recharge()` with a real charger load. An empty habitat cannot charge the rover. Charger input energy must equal battery gain plus modeled loss.

Acceptance: each physical knob changes its own requested kW and actual spending; excess settings drain more energy without unlimited benefit; low settings cause visible shortages; simultaneous demands follow the documented rule. Nighttime/dust reduce output, solar upgrades measurably improve it, storage remains within bounds, costs are paid once and a known balance scenario conserves energy after declared losses.

### Food, hunger and farming

- Separate food inventory, satiety and crop state. Eating uses food and improves satiety; low satiety reduces work efficiency before sustained starvation damages health.
- Use the existing greenhouse beds. Provide plant, care, harvest and replant actions with crop identity, growth stage, time remaining, water/power needs and stress warnings.
- Growth requires viable greenhouse temperature, water, available inputs and delivered power. Underpowered or dry plants stall or become stressed according to visible rules; they must not produce free harvests.
- Choose a small initial crop set, such as a fast leafy crop and a slower higher-yield staple. Source biological context, but label tuned growth duration/yields as game values unless directly supported by the selected dataset.
- Model nutrient/seed availability simply and document it. Ensure a viable replenishment/reuse path throughout the longest campaign. Do not assume untreated Martian soil is ready for ordinary outdoor farming.
- Limit food storage; show spoilage only if implemented as a clear, useful rule. Farming automation may be unlocked, but basic farming must be available early without a research soft lock.
- Make plants and upgrade capacity visible on existing beds using the local asset/art conventions.

Acceptance: one crop cycle consumes its inputs, survives save/load and gives one harvest; no food appears during a complete power/water outage; starting reserves last until a reasonably managed first harvest on every preset.

### Water, oxygen and atmosphere context

- Keep clean water, wastewater and astronaut hydration separate. Drinking consumes clean water; recycling requires wastewater and delivered power and has a configured recovery fraction. Model plant and oxygen-production water use explicitly.
- Keep habitat and suit oxygen separate. Breathing consumes the currently connected supply; production requires its documented inputs and power. Suits refill only at a valid connected station and debit habitat stock.
- Implement astronaut-position-based habitat, tunnel, airlock, shelter and outdoor zones. Cameras must not change exposure context. Include connecting tunnels so moving between rooms does not count as an EVA.
- Extend the existing airlock interlock with a simple documented transition model, suit readiness checks, time/power cost and any modeled gas loss. Do not require a full fluid simulation.
- Initially treat rover travel as using the astronaut's suit supplies unless a separate pressurized cabin system is actually implemented. State that rule in the boarding and trip UI; a closed-looking model is not proof of life support.
- Show remaining suit time as an estimate based on current/planned activity and return travel, with a reserve margin. Warnings must arrive before the player has no feasible response.
- Calculate the net consumable budget for every mission preset before tuning starting stock. Account for unrecovered water, oxygen-production water, crop use, airlock losses and medical/nutrient consumption. Specify which outputs enter wastewater and which are unrecoverable. A recycler below 100% recovery needs enough initial stock or a defined, finite replenishment route to cover net losses through 669 sols; do not assume recycling makes the outpost self-sufficient.

Acceptance: transfers conserve mass within declared losses; water recycling cannot create input water; habitat reserves cannot remotely refill an outdoor suit; boarding/camera changes do not reset oxygen or exposure.

### Fatigue, exercise, healthcare and HP

- Track fatigue from activity and time awake. Sleep/rest reduces fatigue. Work efficiency affects task duration and associated supply use; it must not secretly multiply every unrelated production system.
- Add exercise equipment/action in the base. Exercise costs time, oxygen, water and any powered-device load; temporarily increases fatigue and supports long-term fitness/recovery. It must not instantly heal starvation or radiation injury.
- Provide a sustainable daily routine with rest and exercise. Let players see why neglected rest or fitness makes future tasks harder.
- Medical treatment requires an appropriate station, finite supplies and delivered power where relevant. Basic care is available from the start; research improves capacity/efficiency. Stable conditions and rest support recovery.
- HP ranges from 0 to 100. Damage sources: prolonged dehydration/starvation, sustained oxygen shortage, dangerous internal/suit temperature and explicitly simplified severe radiation scenarios. Attribute every change to a cause.
- Distinguish outdoor temperature from protected astronaut temperature. Thermal response changes gradually with insulation, equipment and delivered heat/cooling.

Acceptance: warnings precede avoidable damage; tired astronauts take longer to work; rest does not refund consumed supplies; exercise has both costs and delayed benefit; treatment cannot consume unavailable supplies or trigger duplicate healing.

### Radiation and weather

- Track current absorbed dose rate and cumulative absorbed dose using compatible source units. Shelter reduces future exposure by a documented model factor; it never deletes accumulated dose.
- Provide a configurable gameplay exposure threshold above which severe scenarios apply HP loss. Label it as a teaching/game rule, not a medical threshold or an estimate of a child's real-world risk. Ordinary background radiation must not cause instant collapse.
- Keep source observations, derived profiles and invented event intensity separate. Existing DONKI visual strength cannot be converted directly into Mars surface dose.
- A solar-event sequence needs forecast/warning, onset, peak and recovery. The player can return, shelter and manage life support. Describe particle-event danger accurately; surface exposure is not limited to when a decorative Sun is visible.
- Dust reduces modeled solar input and visibility, increases travel/task difficulty, and leaves persistent panel deposits. Dusting panels is a required energy-consuming maintenance task that reduces deposits and restores potential output. Distinguish lasting deposits from airborne dust that clears with the storm; cleaning cannot remove atmospheric dust or generate power at night.
- Heat events and cold nights change the thermal environment and power demand. Retain the requested heat-storm visual as a stylized thermal-stress scenario; do not claim it is an observed Martian storm category.
- Support overlapping effects. Dust attenuation, cold-night heating and solar-particle exposure remain active together even if the weather controller displays only one dominant visual.
- Reuse `WeatherController.Activate(...)` and `StopWeather()`. Preserve one owner for atmosphere settings and the current terrain/weather integration. UI and weather transition weights remain presentation outputs.
- The bundled DONKI archive ends on 15 November 2026 and contains no Mars SEP detections. For dates outside coverage, use a seeded, clearly labeled simulated continuation, or deliberately select a supported historical scenario. Never silently repeat archived events as new observations or imply future forecasts.

Acceptance: identical seed/start/actions reproduce event boundaries; physical effects persist through visual transitions and save/load; shelter changes the future dose slope; storms end without resetting unrelated hazards; all mission presets remain playable beyond archive coverage.

### Energy-consuming operations

Every task below is required. Define duration, electrical draw or per-action energy cost, energy source, additional supplies, useful output and interruption behavior in configuration. Preview the expected cost and resulting reserves before starting. Apply actual cost through the shared ledger; use either a per-action charge or integrated power for the same work, never both. Powered equipment can run unattended where specified, but the astronaut cannot perform two staffed tasks at once.

| Task | Action and energy use | Result / completion check |
| --- | --- | --- |
| **Fix machines: rover and base** | Inspect condition, select a fault and use a powered tool with parts and time. Base repairs use connected habitat power; field repairs use the portable/rover battery. | Repairs restore a defined amount of condition/output. Wear or faults reduce performance in an explained way. Repairing a healthy machine spends no accidental cost and gives no repeat reward. |
| **Sample soil** | Use the sample scoop/drill at a valid site; consume field-tool energy, time and a container where modeled. Analysis back at the lab has its own energy cost. | Create one persistent sample with site ID, coordinates and collection time. Analysis produces the defined once-only research reward. |
| **Farming** | Plant, water, tend and harvest using the greenhouse's powered equipment. Account for lighting/pumping while crops grow and tools used for staffed work. | Inputs and delivered power affect growth/yield; the completed crop yields food. Continuous greenhouse draw is counted once, including while a care task is active. |
| **Picture documentation with survey markers** | Place an identified marker, frame it and the target with the survey camera, and capture a photo using field-battery energy. | Store the image/thumbnail with marker ID, site, coordinates and mission time. Validate required framing/subject evidence; a blank photo or repeated photo cannot farm rewards. |
| **Manage communication radio** | Switch radio off/listen/transmit, inspect signal, choose a position or wait for a better window, and operate it using habitat or field power as connected. | Signal affects throughput, transmission duration and energy spent. Poor/no signal is visible; more transmitter power does not guarantee a blocked link becomes usable. |
| **Dust the solar panel** | Reach the array and use the powered cleaning tool, spending energy and time while managing EVA needs. | Reduce persistent deposits and restore the achievable solar output under current sunlight/airborne dust. A clean panel cannot be repeatedly dusted for free energy or points. |
| **Give updates to Houston** | Compose a status/science report and transmit it over the radio. Include energy/reserve status, hazards, repairs, sample results and marker photographs when available. | A queued report becomes delivered/acknowledged only after its transmission completes. Stable report/objective IDs prevent repeat acknowledgment or reward. |

Use a small machine-condition model for rover propulsion, solar arrays and key base equipment. Wear from use and configured event damage must be deterministic, bounded and warned about before critical failure where avoidable. Show the fault, affected function, repair cost and estimated duration. Provide sufficient starting repair capability and attainable parts so the first repair does not require an already broken machine to manufacture its only repair tool. These wear/damage rules are game tuning unless separately sourced.

Radio signal varies with **location and simulation time**. Begin with a reproducible model using authored coverage regions or terrain-derived visibility to fictional local relays, plus scheduled relay windows. Label coverage and timing as modeled gameplay conditions, not measured NASA communications coverage. Show a signal indicator, usable-window estimate and route suggestions where supported by the model.

Houston is an **in-game mission-control destination**. Simulate outgoing reports and acknowledgments locally so judging remains offline. The feature does not send real messages to NASA or require an email/chat integration. Keep unsent/partial reports in a persistent queue; specify resumable progress, bounded retry attempts, listening/transmitting draw and cancellation costs. At zero signal, pause transmission rather than burn unlimited retry energy or mark the report delivered. An acknowledgment may complete an objective or grant one configured research bonus, never electrical energy.

Acceptance: demonstrate every listed task with a measured energy debit, useful result and interrupted/save-loaded continuation. The rover/base can be repaired; soil and marker photos survive reload; weak signal costs more time/energy for the same successful report; no-signal reports remain queued; cleaning panels improves subsequent generation; none of these operations creates battery charge directly.

## 6. Research tree

Implement four visible branches with a useful starting node, a choice between two middle investments, and a capstone. All basic survival actions exist before research. Research improves the choices available; it must not be the only way to perform the first meal, refill, harvest or emergency shelter action.

Use these 16 nodes as the initial complete tree. Values and costs belong in authored configuration and require balancing; the effects below are design requirements, not measured NASA specifications.

| Branch | Foundation | Middle choices, each requiring foundation | Capstone, requiring both middle nodes |
| --- | --- | --- | --- |
| Health and farming | **H0: Controlled grow beds** — better crop stability/input efficiency | **H1: Nutrition program** — better harvest/meal benefit; **H2: Medical devices** — more effective powered treatment | **H3: Recovery suite** — improved exercise/rest recovery and coordinated care |
| Solar generation | **S0: Improved solar cells** — greater conversion efficiency | **S1: Array expansion** — unlock additional panel slots; **S2: Dust servicing** — more effective/automated panel maintenance with costs | **S3: Tracking array** — improved angle collection with a small actuator load |
| Protection | **P0: Environmental monitoring** — earlier useful warnings and clearer forecasts | **P1: Shielded shelter** — lower modeled exposure while inside; **P2: Thermal insulation** — reduced heat loss and cooling/heating demand | **P3: Resilient habitat** — improved protection/reliability under overlapping hazards |
| Storage and efficiency | **E0: Battery modules** — unlock increased storage | **E1: Deep storage** — further capacity and/or discharge capability; **E2: Efficient life support** — lower power per unit of actual production | **E3: Smart distribution** — better demand forecasts and charging/standby efficiency, while preserving the player's three knob settings |

Requirements:

- Display point cost, prerequisites, actual effect, installation cost and state: locked, available, researched, installed.
- Treat research and installation as separate concepts. Unlocking an array slot does not create a free panel; an efficiency upgrade applies only when its equipment is installed/active.
- Make transactions atomic at their separate stages: research spends points after checking prerequisites; installation reserves/spends parts and schedules its time/power cost after checking the unlocked equipment and capacity. Lack of parts must not prevent an otherwise valid research unlock. Define what is reserved, consumed and refundable when installation is cancelled; activate the effect only when installation completes. Stable IDs prevent double purchases and double application on reload.
- Derive final equipment stats from base values plus installed upgrades; do not repeatedly multiply mutable values when loading.
- Keep all four branches accessible. Early choices are constrained by budget, without permanent mutually exclusive locks unless deliberately documented.
- Reward exploration and analysis, not waiting in place or repeatedly clicking the same object. Balance point supply so all branches are usable on all presets and each preset can reach at least one capstone without exhausting every POI.
- Show improvements in the world: additional panels/battery cabinets, crop trays, medical equipment, shelter shell/berm or insulation details. Reuse existing geometry and asset families.

Acceptance: invalid purchases spend nothing; prerequisites and each node's actual effect are testable; save/load preserves unlocks and installations without stacking effects; at least two distinct investment strategies can finish each preset with competent play.

## 7. Open-world exploration and science

The data-driven world already exists. Populate the existing Gale terrain with a small, complete science loop that uses real coordinates/elevation and clearly distinguishes fictional mission objects from observations.

- Use `MarsTerrain.IsReady`, `GetGroundY`, `GetGroundElevationMeters`, `GetSlopeDegrees`, `GetLatLon`, `LatLonToWorld` and `IsInsideTile`. Terrain queries must work when render chunks are unloaded.
- Preserve the 1:1 scale and 200 m source-grid metadata. Interpolated walking surfaces and procedural rocks are not metre-resolution scientific measurements.
- Generate deterministic POIs from the mission seed and stable site IDs. Start with at least 12 distinct sites: near-base training sites, medium walking routes and rover routes. Add enough optional sites/reward value for each preset's research budget.
- Validate tile bounds, terrain slope, collisions, access and a feasible route to/from the base. Use a modest terrain-sampled navigation graph or another tested reachability method; a safe endpoint alone does not prove a safe path.
- Keep initial tasks close enough for starter equipment. Place advanced sites farther away with higher rewards and preparation costs. Do not require upgrades obtainable only from those same inaccessible sites.
- Place the first walking sites roughly 40–100 m from the base along tested paths, with a first rewarded round trip targeted at 3–5 real minutes including interactions. A 1 km walking round trip already takes about 18 minutes at the current walk speed. Cluster early tasks, allow several objectives on one outing and use the rover for longer routes. Check route time against the preset's active-playtime target before placing mandatory objectives.
- Provide a map with habitat/rover position, discovered sites, selected waypoint, distance, altitude, route difficulty and approximate round-trip resource cost. Display estimated reserve on return.
- Implement terrain survey (coordinates/elevation/slope), environmental measurement (temperature/radiation/solar context), soil collection followed by base analysis, and survey-marker photo documentation. Include communication coverage/windows when planning routes and reporting findings to Houston.
- All operations listed in section 5 consume electrical energy as well as their appropriate time, oxygen, water, fatigue and supplies. Show the connected energy source and allow cancellation with clearly defined partial progress and cost behavior.
- Give finite first-completion research rewards, with a small understanding/analysis step tied to the observation. Allow retries for learning without duplicate points. Log site completion and sample IDs in saves.
- Make all measurement displays identify the origin: observed dataset, derived terrain/model result or simulated scenario. Procedural sample caches are fictional; do not claim the DEM identifies minerals, water deposits or individual rocks.
- Integrate the existing rover into route planning and the energy economy. Validate safe exit locations, slope/boundary handling and a practical response to running low on charge. Never offer free rescue that bypasses survival without labeling it an assisted-mode rule.

Acceptance: the same seed yields the same sites; no mandatory site is outside the DEM or unreachable; a complete trip consumes supplies, returns a sample, grants research once and enables an upgrade; reload cannot duplicate samples/rewards; map and altitude agree with terrain metadata.

## 8. UI, onboarding, persistence and accessibility

Extend the existing home screen with **New mission**, **Continue**, **Mission length**, **How to play**, **Settings** and **Quit**. Starting a campaign initializes one complete state only after terrain, outpost, player and environmental data are ready.

HUD requirements:

- Main strip: HP, hunger/satiety, hydration, fatigue, suit/connected oxygen, current exposure warning.
- Dashboard panel: three controls matching Life control, Temperature and Radiation shielding; show knob percentage, requested/delivered kW, useful-need band, shortfall/avoidable draw and hardware connection/takeover state.
- Habitat panel: food/water reserves, battery kWh and capacity, generation/load/net kW, separate task loads, crop state, machine condition and medical supplies. Show field/rover charge when relevant.
- Mission panel: selected duration, relative sol, progress, objective, forecast and time controls.
- Research panel: four branches, costs, prerequisites, effects and install action.
- Operations panel: repair/cleaning jobs, sample/photo/marker log, radio mode/signal/window and Houston report queue with sent/acknowledged status.
- Context actions: clear station/POI prompts and disabled-action reasons. One key press must not both board a rover and activate a nearby station.
- Actionable warnings: describe cause, consequence and response, for example “Low power: greenhouse lights are off. Reduce a lower-priority load or add generation.”
- Science/source panel: origin, location/date coverage, units and simplified-model explanation. Keep detailed citations out of the main survival flow but easy to find.

Tutorial sequence: connect/calibrate the three knobs or select their on-screen equivalents → compare balanced and excessive power settings → eat/drink/refill → plant and assign care → learn the daily routine and safe time advancement → harvest → sample soil and photograph a survey marker nearby → return/analyze → repair equipment and dust a panel → find a radio window and update Houston → research/install a solar improvement → rebalance during a guided weather event. Teach advancement before waiting for a crop, and use short guided tasks for the first demonstrations. Keep instructions skippable and replayable; show consequences through values and equipment behavior.

Support mouse and keyboard throughout, remappable or documented controls, readable scaling/contrast, icons plus text, and warnings that do not rely on color or audio alone. Fix current home-screen selection clearing/navigation behavior so keyboard focus persists. Prevent gameplay input while menus/text fields own focus. Include a reduced-motion/weather-intensity option that does not change physical hazard values.

Persistence:

- Use versioned local saves with atomic replacement and a recoverable backup. Validate ranges, IDs and schema version on load; show a useful error for corrupt/incompatible saves.
- Save exact simulated time, preset/start/seed/random state, resources, astronaut context/position, rover/field-battery state, all three logical knob settings, crops, machine condition/deposits, ongoing tasks, installed equipment, research, POI/sample/marker/photo records, radio/report state and events. Keep hardware calibration in device settings and use safe takeover when physical positions differ from loaded values. Record configuration/data versions so incompatible updates are detected.
- Autosave at safe milestones and provide manual save/load. Do not overwrite the only recoverable save solely because the player reached failure.
- Restoring state must rebuild derived values without awarding rewards, applying upgrades again or rerunning the last tick. Starting a new mission must not inherit shared ScriptableObject state.

Acceptance: keyboard-only menu use works; warnings remain readable in dust/night; save/load during a crop task and overlapping storm resumes matching state; repeated new/retry/load actions leave one player, base, rover, clock and menu.

## 9. NASA challenge and scientific-data alignment

The user supplied these exact pages:

- [2026 Junior Astronaut Mission Trainer challenge](https://www.spaceappschallenge.org/2026/challenges/junior-astronaut-mission-trainer/)
- [2026 challenge resources](https://www.spaceappschallenge.org/2026/challenges/junior-astronaut-mission-trainer/?tab=resources)

Challenge verification and the researched source manifest are specified below. Do not call project design proposals official requirements. Do not silently substitute another year's challenge. Where the exact page is unavailable, record that limitation and continue the game/data work that is already defined.

### Verified challenge scope and remaining verification

The indexed official **2026** challenge summary was retrieved. It asks for an interactive game/app in which students operate a lunar or Martian outpost and experience consequential tradeoffs among life support, radiation shielding, power and food production. It lists Intermediate and Beginner/Youth difficulty. This project uses the Martian option. [Official challenge summary](https://www.spaceappschallenge.org/2026/challenges/junior-astronaut-mission-trainer/)

The full page and its resources tab returned inaccessible/502 responses during this planning pass. Therefore, the exact resource list, a specific age range, any official physical-hardware requirement and additional submission requirements are **not verified**. Ages 7–12 come from the existing project guide. The physical three-knob dashboard is now explicitly required by the user regardless of the challenge's hardware rules. The sources below are supplemental primary sources, not a claimed transcription of the resources tab.

At implementation start, retry the exact resources URL and reconcile its list with the manifest below. Record each confirmed requirement, source URL, implementation location and evidence in `docs/challenge-alignment.md`. If access still fails, keep the limitation explicit and continue the defined game work. Before claiming full challenge compliance, obtain the complete official brief/resource list and close any actual gaps. Do not borrow requirements from another year or a different challenge.

### Features that demonstrate the verified challenge goal

These are concrete design responses to implement, not extra rules quoted from NASA.

| Learning goal | Implementation | Demonstration evidence |
| --- | --- | --- |
| Operate a Martian outpost | The existing habitat becomes the place for life support, growing, care and energy decisions | A new player completes the base routine through actual stations/UI |
| Understand competing demands | Three physical knobs show requested/delivered power, useful need and total spending | Excess spending drains reserves or blocks tasks; underallocation harms the relevant survival system |
| Understand radiation protection | Forecast, recall/shelter command, current exposure and cumulative-dose history | An otherwise comparable sheltered period accumulates less modeled dose; existing dose remains |
| Connect food and life support | Crops share water/power resources with the astronaut's needs | A crop investment helps later food supply but consumes resources now |
| Learn from success/failure | Cause-coded warnings, decision history and a short debrief | The player can identify which decision improved or harmed survival |
| Investigate a real place | Georeferenced terrain survey and clearly labeled environmental readings | A science card shows location, units, source and what is simulated |

Add short **predict → act → observe → explain** moments to the tutorial and selected science tasks. For example: predict whether the battery can support the next night, adjust the Temperature knob, advance safely, then compare expected and actual reserves. Include at least six concise learning cards covering electricity versus food, oxygen/water recycling, controlled crops, radiation shelter, dust/solar power, and rest/exercise. Tie each to an action and a source, allow replay, and provide helpful feedback without punishing reading speed.

For the demonstration, prepare one reproducible seed and starting snapshot with three-knob balancing, a power shortage, a crop decision, a shelter decision, energy-consuming fieldwork and a radio update to Houston. The debrief should connect at least two player decisions to their observed consequences. A screenshot of several meters is not sufficient evidence that the tradeoffs work.

### Source manifest and concrete use

The sources marked **checked** below were consulted during planning. Existing-source rows retain repository provenance and identify verification still needed. All numerical balancing coefficients must identify whether they are measured, derived or chosen for gameplay.

| ID / status | Primary source | Relevant information and implementation |
| --- | --- | --- |
| T1 — existing bundled source | [USGS MOLA–HRSC DEM](https://planetarymaps.usgs.gov/mosaic/Mars/HRSC_MOLA_Blend/Mars_HRSC_MOLA_BlendDEM_Global_200mp_v2.tif), contextualized by [NASA Mars Trek](https://trek.nasa.gov/mars/) | Reuse the current Gale tile, metadata and preparation script. Numerical heights come from the DEM, not shaded-relief image brightness. Preserve the areoid reference, 200 m grid and coordinate conversions. Verify the product citation/accuracy and local HRSC coverage before submission; a blended grid can include interpolated MOLA data. |
| T2 — existing implementation source | [NASA GISS Mars24 algorithm](https://www.giss.nasa.gov/tools/mars24/help/algorithm.html) | Reuse `MarsTime` for local time, Sun direction/distance and season. Keep its existing worked-example checks reproducible. Mission elapsed duration is separate from local-date changes as the astronaut moves. |
| W1 — checked archive entry point; numeric extraction still needed | [Current PDS Mars atmosphere archive](https://atmos.nmsu.edu/data_and_services/atmospheres_data/MARS/data_archive.html) | Follow Curiosity to REMS calibrated products; select a small Gale air-temperature series covering local times. Inspect product units, calibration, missing values and quality flags, then export a representative sol/profile. Keep air and ground temperature distinct. The older PDS link in the original guide says it is no longer actively maintained. |
| R1 — checked historical reference; numeric series still needed for a replay | [NASA Curiosity RAD measurements](https://science.nasa.gov/resource/radiation-measurements-on-mars/) | The August 2012–June 2013 surface example describes a background absorbed-dose rate of about 210 microgray/day, largely from galactic cosmic rays. This is historical context, not a live global value. Obtain documented RAD numeric products for a measured profile; otherwise a constant/profile derived from this summary must be labeled representative. Verify the source's time-unit definition before conversion. |
| E1 — checked mechanism reference | [NASA Environmental Control and Life Support Systems](https://www.nasa.gov/reference/environmental-control-and-life-support-systems-eclss/) | Water recovery, cabin-air cleaning/CO₂ removal and oxygen production from water supply the relationships for the simulation. Model gas processing as part of life-support demand. Use explicit inputs/outputs; game capacities and efficiencies are not automatically ISS specifications. |
| F1 — checked mechanism reference | [NASA Growing Plants in Space](https://www.nasa.gov/exploration-research-and-technology/growing-plants-in-space/) | Veggie and Advanced Plant Habitat support the need for light, water, nutrients, root air and controlled conditions. Use that context for greenhouse care and research. These ISS experiments do not establish a Mars staple-crop yield or an entire astronaut diet; label selected crop durations and yields appropriately. |
| P1 — checked engineering reference | [NASA Mars surface power study](https://www.nasa.gov/wp-content/uploads/2024/01/mars-surface-power-generation-challenges-and-considerations.pdf) | Dust affects available solar power, and storm extent/duration varies. Use this to justify storage, efficiency and dust-response decisions. An invented solar attenuation percentage or storm interval remains a model assumption. If extracting quantitative values, cite the exact page/table and conditions. |
| H1 — checked rationale reference | [NASA Exercise Overview](https://www.nasa.gov/ochmo-tb-031-exercise-overview-2/) | Exercise is a countermeasure supporting astronaut fitness under altered gravity. Use for conditioning/recovery explanations. It does not provide a validated Mars fatigue-to-HP formula or justify instantaneous healing. |
| S1 — existing archive/context | [NASA CCMC DONKI](https://ccmc.gsfc.nasa.gov/tools/DONKI/) | Reuse archived event IDs, timestamps and links. Preserve the distinction between flare records, predicted CME arrivals and detections. The current bundle's display intensity and invented duration are not Mars dose measurements. |

Minimum implementation data deliverables: retain T1/T2 and S1 with corrected provenance where necessary; prepare W1 temperature inputs; provide a documented R1-derived background model or validated numeric RAD profile; connect E1/F1/P1/H1 to specific mechanisms and learning cards. Cite actual products and retrieval dates in the resulting manifest. A numeric measured storm profile is preferable when obtainable, but a clearly labeled simulated severe event must not be presented as RAD observations.

Do not add unrelated datasets merely to increase the number of NASA links. Do not substitute ISS radiation records for Mars surface exposure, Earth weather for Gale conditions, or solar images for electrical-generation measurements. Real observations support the environment and learning; game rules define achievable mission balance.

### Data preparation contract

Bundle small curated datasets for offline play. Reuse existing terrain and DONKI preparation; do not download an entire planetary archive. Add reproducible scripts only for missing inputs, with source/licensing checks and explicit retrieval accounting.

For every dataset, record: stable ID; source/product URL; instrument/provider; observation vs prediction vs simulation; location; observation dates and retrieval UTC; units; resolution/cadence; quality flags/missing values; processing/resampling/interpolation; coverage; license/attribution; hash; downloaded and bundled bytes; relevant limitations. Preserve original detector units for radiation. Never silently convert absorbed dose to effective dose.

Use compact prepared CSV/JSON/binary assets with a manifest. Suggested weather rows contain time/sol, local hour, temperature, quality and source ID; radiation rows contain time, dose rate, unit, detector and source ID. Keep simulated multipliers in a separate tuning asset. Validate numeric ranges, monotonic timestamps, duplicate IDs and missing-data rules before import.

Choose a documented gap policy: interpolate only within an allowed interval; otherwise use a labeled representative model or report unavailable data. Reusing a profile across seasons/sites must be visible as a model assumption. Data from different dates/instruments must not be presented as one simultaneous measured scenario.

If a numeric source cannot be obtained, use a documented fallback for development and keep the data requirement open. Do not invent measurements, silently scrape values from chart images, or claim the fallback fulfills a missing scientific dataset.

## 10. Physical dashboard integration

The hardware dashboard and software are paired parts of the game. Its **three adjustable knobs are confirmed**: Life control, Temperature and Radiation shielding. They control the energy requests defined in section 5. Additional buttons, displays or LEDs are optional device details, not substitutes for these knobs. The actual board, wiring, supported transport and calibration ranges still need to be recorded before device-specific work.

Implement one `SetPowerRequest(channel, normalizedValue)` command path for physical input and the three on-screen controls. Give the channels stable IDs (`life_control`, `temperature`, `radiation_shielding`). Calibrate physical minimum/maximum values, clamp the normalized range, and smooth readings with a small deadband so knob noise does not flood commands. Do not let smoothing make deliberate adjustments feel delayed. Expose requested and delivered power separately in software and any available hardware feedback.

Use the actual device's supported connection, with USB serial behind an adapter if that is the chosen transport. Implement a small versioned protocol with sequence IDs, bounded values, message framing and a heartbeat. Reject malformed messages and stale/out-of-order packets; repeated input must not duplicate energy use. IO stays off the Unity main thread, and validated input is queued for main-thread command processing. Firmware reports knob readings; it does not maintain a second game-resource ledger.

Keep an emulator and on-screen fallback for development, accessibility and disconnects. A disconnect preserves the last valid settings, stops fast-forward and presents a connection warning; it must not zero life support. On reconnect or save load, use soft takeover: retain the logical setting until its physical knob crosses that value within a documented tolerance, with an on-screen indicator. This avoids suddenly applying a mismatched physical position. Define input ownership explicitly so hardware readings and mouse input do not fight each frame.

Obtain only the missing board/transport/wiring details before writing firmware or assigning pins; do not ask whether the three-knob hardware is wanted. Continue the emulator and independent software work if the device is unavailable, but keep physical integration marked incomplete. Deliver wiring/calibration instructions, firmware where needed, and a repeatable connected-device check for every knob, noisy readings, disconnect, reconnect and saved-state takeover. The complete project includes verified hardware/software pairing as well as a usable software fallback.

## 11. Execution order and delegation

Use one lead and up to three workers concurrently. Define shared contracts and file ownership before parallel work. Keep resource accounting with one owner. Workers do not independently edit the same scene, prefab, config, assembly definition or project setting. Reuse agents for follow-up work; do not duplicate browsing or run overlapping whole-project reviews.

| Phase | Work | Exit evidence |
| --- | --- | --- |
| 0. Verify baseline | Read instructions, preserve dirty files, check actual editor/packages, inspect scene composition and existing APIs, verify challenge/resources, record ledger and source manifest | Reproducible baseline, explicit status/limitations and no unnecessary rewrites |
| 1. Shared core | Define state/config/commands and the three knob channels; implement authoritative clock, lifecycle, all three duration presets, pause and save schema; add zones/readiness; gate debug controls | One deterministic mission starts/pauses/restarts; room/tunnel/camera and knob-command mapping checks pass |
| 2. First complete loop | Three-knob allocation/emulator, solar/storage, water/oxygen, HP/needs, first crop cycle, station/HUD controls, field batteries and real rover charging | Balanced settings sustain the astronaut; overspending drains reserves; shortages change actual system output |
| 3. Environment and care | Physical weather/data adapters, dose/thermal effects, overlapping hazards, shelter, healthcare, exercise, daily routine and safe time advancement | Weather forces resource decisions; care/actions work; advancement stops safely and resumes correctly |
| 4. Exploration and progression | Reachable science sites/map, soil sampling, marker photography, rover/base repair, panel dusting, radio coverage/windows, Houston reports, research/installation | Every listed task spends energy and provides its intended result; solar research improves generation; report/reward IDs prevent duplication |
| 5. Full campaigns and hardware | Preset-specific balance, long-duration coverage, persistence, tutorial, debrief, accessibility, source cards, calibrated physical three-knob integration and fallback | Each preset supports viable strategies; actual knob changes drive the shared simulation, with disconnect/takeover behavior verified |
| 6. Release verification | Focused tests, actual Unity playthrough, offline desktop build, performance/data checks, challenge evidence and user-facing docs | Runnable build plus truthful acceptance ledger and specific remaining external blockers, if any |

Parallel ownership after the lead establishes contracts:

- **Simulation worker:** resources, health, crops, medical/exercise, three-channel allocation, task power/repair-condition accounting and focused simulation tests.
- **Environment/world worker:** scientific preparation/manifest, physical weather, coverage, terrain-based site placement and route feasibility; coordinate exploration reward interfaces with the lead.
- **Interaction/progression worker:** station actions, sample/photo/report interactions, research UI/definitions, map/tutorial/accessibility, three-knob dashboard adapter and firmware; consume agreed state/commands. Assign radio model ownership explicitly before parallel work.
- **Lead:** shared contracts, research transaction/persistence integration as needed, scene/build composition, ownership handoffs, integration tests and release evidence. If these tasks are reassigned, make exact ownership explicit.

Each worker receives outcome, owned files, inputs/contracts, acceptance cases and a required handoff: changed files, checks performed, assumptions and integration needs. Use one bounded independent review after the integrated loop exists, focusing on resource conservation, time, source claims, persistence and build failures. Resolve findings and rerun affected checks.

## 12. Verification and completion criteria

Use reproducible Edit Mode checks for pure simulation/data logic and Play Mode/build checks for actual integration. Add a Unity Test Framework setup compatible with the installed version only if needed; first inspect package resolution. Do not count external engine stubs as a substitute for Unity compilation or a player build.

| Test group | Required scenarios |
| --- | --- |
| Clock and mission presets | Exact month/half/full durations, fractional final sol, render-rate independence, acceleration/pause, final-tick failure precedence, no dropped catch-up time |
| Resource conservation | Known solar/load/battery balance, charge/discharge limits, partial power, zero-input production, water/oxygen transfers, real rover charging, capacity bounds and net consumable sufficiency for each full preset |
| Dashboard allocation | Independent 0–100% mappings, requested/delivered/needed power, excessive-setting cost, proportional shortfall, task headroom, no hidden normalization/auto-optimization and no double-billed Life control loads |
| Survival and care | Starvation/dehydration/hypoxia causes, thermal lag, fatigue/task efficiency, exercise costs/benefit, treatment limits, warning lead time and single failure transition |
| Farming | Full cycle, input shortages, stress/recovery, harvest once, replant/seed sustainability and save/load mid-growth |
| Weather and radiation | Deterministic seed, onset/expiry, overlapping dust/night/solar, no dose from visual weights, protection affecting future dose only, archived/simulated coverage labels |
| Exploration | Seeded stable IDs, tile/slope/path checks, starter round trips, boundary behavior, rover exit, once-only sample/research rewards and exhausted site behavior |
| Operations | Energy debit and useful output for every required task; repair faults/parts, portable battery charging, soil records, marker/photo validation, persistent deposits/dusting and partial-task cancellation/load |
| Radio and Houston | Signal varies by place/time; weak/no signal, listening/transmit costs, bounded retries, resumable reports, once-only acknowledgments, persistent evidence and offline operation |
| Research | Every node, prerequisites, insufficient-cost rollback, install/unlock distinction, installation cancellation/reservations, actual effect, no load-time stacking and non-softlocked progression |
| Persistence | Round-trip representative states, active tasks/events, random state, atomic save recovery, corrupt/version-mismatched files, clean new mission and repeated retry/load |
| Interface and hardware | Actual three-knob pairing/calibration, channel mapping, noisy/stale/duplicate readings, logical/physical takeover, disconnect retaining settings/stopping fast-forward, keyboard focus and screen fallback |
| Scene/build | Gameplay entry scene, required serialized weather references, no editor-only dependencies or preview controls, assets/shaders/data present offline |
| Terrain/data | Known coordinate/elevation checks with stated tolerance, round trips, tile bounds, truthful DEM resolution, missing-data policy, source hashes and attribution |
| Performance | Profiler evidence on stated hardware, bounded terrain/rock pools and UI history, responsive fast-forward, normal walking/rover traversal and extended campaign simulation |

Run deterministic automated campaigns through **all three durations**, including deliberate scarcity/failure cases. Use scripted decision policies that obey the same commands, time costs and resource rules as players; do not secretly refill stock to make a test pass. Demonstrate at least two viable strategies per preset, such as solar/storage emphasis and efficiency/protection emphasis, while keeping all basic survival systems active.

Then manually play the core loop in Unity and the standalone build: connect dashboard → balance three knobs → grow food → repair rover/base → sample soil and photograph a marker → clean panels → transmit an update to Houston → research/install improved solar → rebalance for a storm → save/load → complete or fail/retry. Test the connected hardware and its software fallback. Include a short repeatable demonstration route for judging; a separate demo configuration may accelerate presentation, but its settings must be visible and must not be presented as normal campaign balance.

Completion means:

- Every user-requested mechanic exists and connects to the shared game state.
- The physical dashboard's three knobs control Life control, Temperature and Radiation shielding spending, and balancing sufficient power against excess draw is the main tested gameplay loop.
- Repairs, soil sampling, farming, survey-marker photographs, radio management, panel dusting and Houston reports each have working energy costs and outcomes. Solar generation and installed solar research provide the specified energy-growth path.
- All three Martian mission presets work, including safe acceleration and exact terminal conditions.
- Exploration earns research; all four branches produce real, persistent gameplay effects.
- NASA/scientific data actually drives relevant systems, with sources, units, scope and model assumptions visible.
- The release starts in the correct scene, works offline, pairs with the required physical dashboard and remains usable through on-screen controls if hardware disconnects.
- Verified challenge requirements have linked evidence; unavailable pages/hardware are explicitly unresolved rather than assumed satisfied.
- Relevant tests and actual build checks have results, with unavailable checks reported honestly.

Deliver the source changes, a runnable desktop build when the environment supports it, updated run/setup instructions, controls, data manifest/citations, balance configuration notes, test/build results, and the final implementation ledger. End with a concise account of what changed, how it was verified and any concrete remaining blocker. Do not call the work finished while required, unblocked implementation remains.
