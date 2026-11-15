# Mars outpost

An enterable habitat with one room for each survival system in `AGENT_IMPLEMENTATION_GUIDE.md`. It is built in code when the terrain scene starts (`Assets/Scripts/Base/`): flat-shaded, no textures. The shells are white with dark red accents and charcoal trim; the furniture and tools keep the eight palette colours of `Task_assets`.

## Plan

```
                         Greenhouse
                             |
  Storm shelter - Quarters - Medical - HUB - Energy - Life support
                             |
                      EVA prep and lab
                             |
                          Airlock - landing - ramp
```

The outpost stands on a levelled regolith pad (56 m × 51 m). It is placed 40 m downhill of the player's start with the airlock facing uphill, so the pad meets the ground at the entrance, and the player's view is turned to face it. If the front edge of the pad still stands above the ground, an apron ramp is added.

## Shapes

- **Command hub:** a twelve-sided room 10 m across under a stepped dome, with a dark red cap, four dark red bands and the comms mast. A round command table stands in the middle.
- **Other modules:** capsules with vaulted roofs and a dark red stripe along the ridge (olive on the greenhouse, for the grow lights). The greenhouse and life support have rounded ends with a dark window band. The rooms inside keep flat walls.
- **Tunnels:** eight-sided white tubes with dark red collars around a flat-floored corridor.
- **Storm shelter:** buried under piled regolith, as before.
- **Outside:** a blue solar array, a white water tank, pairs of small dark red tanks, and the rover's parking spot on the front left of the pad.

## Rooms

| Room | Systems it stands for | What is inside | Tools from `Task_assets` |
| --- | --- | --- | --- |
| Airlock | Oxygen (suit versus habitat), Movement | Two sliding doors that never open together, hazard stripes | — |
| EVA prep and sample lab | Movement and exploration, Oxygen (suit refill) | Suit lockers, suit oxygen refill station, sample bench | Rock Hammer, Core Sample Drill, Sample Scoop, Sample Canister Rack, Survey Camera, 2 Survey Markers, Comm Radio, Portable O2 Canister |
| Command hub and galley | Solar event and Dust storm alerts, Hardware dashboard, HUD; Hunger and Water | Command desk and screens, crater map, comms racks, dashboard pedestal, galley counter, food lockers, table, supply shelving, comms mast on the roof | Comm Radio, Radiation Dosimeter |
| Medical bay | Health | Treatment bed, vital-signs monitor, medical cabinet, supply counter | Portable O2 Canister, Radiation Dosimeter |
| Crew quarters | Tiredness | Two double bunks, lockers, desk | Habitat Work Lamp |
| Storm shelter | Radiation, Solar event | Water-wall tanks on three sides, regolith piled over the roof and walls, benches, ration crate, hatch door | Radiation Dosimeter, Habitat Work Lamp, Comm Radio |
| Energy room | Energy reserves | Four battery cabinets, power distribution board, workbench; solar array of six panels outside | Energy Meter, Field Wrench, Solar Panel Duster |
| Life support | Oxygen, Water, Temperature | Three oxygen tanks, two water tanks, oxygen generator, water recycler, thermal control panel; water tank outside | 3 Portable O2 Canisters |
| Greenhouse | Hunger (farming) | Two raised crop beds under grow lights, water tank, potting bench | Regolith Shovel, Hydroponic Sprayer |

All 15 tool models are used, 25 placements in total. A label at the top of the screen names the room the astronaut is in, what it is for, and the tools kept there.

The rooms are places only. They do not yet change health, oxygen, power or any other value; that belongs to the simulation (`Assets/Scripts/Simulation/`), which can read `MarsOutpost.CurrentRoom`.

## How it works

| Path under `My project/Assets/` | Role |
| --- | --- |
| `Scripts/Base/MarsOutpost.cs` | Places the outpost, levels the pad, clears rocks, shows the room label |
| `Scripts/Base/MarsOutpostBuilder.cs` | The plan: rooms, walls, tunnels, furniture, tool placement |
| `Scripts/Base/OutpostGeometry.cs` | Flat-shaded boxes, tapered boxes and prisms, one submesh per palette colour |
| `Scripts/Base/MarsOutpostDoor.cs` | Sliding doors that open as the astronaut approaches; airlock interlock |
| `Scripts/Base/Editor/MarsToolModelImporter.cs` | Import settings for the tool models |
| `MarsTools/Resources/MarsTools/` | The 15 tool models, copied from `Task_assets/models/` |

- Walls, floors, roofs and furniture have box colliders. The astronaut collides with any collider that is not part of the terrain, steps onto ledges up to 0.45 m, and stops rising under a ceiling.
- Each room is its own mesh with a ceiling light, so rooms stay lit at night.
- Windows are see-through; the storm shelter has none.
- Run outside Unity against a stand-in for the engine, the builder produces about 10,900 triangles and every room is reachable on foot from the entry landing. The shapes were checked in an offline render; how it looks in Play mode has not been checked.

## Relation to the `assets` branch

The `assets` branch has an earlier `MarsOutpost` that places the solid `base_outpost` model from `Base_assets`. That model cannot be entered. This version replaces it at the same path and keeps its public members (`terrain`, `placeInFrontOfPlayer`, `distanceFromPlayerMeters`, `rockClearanceMeters`, `DoorPosition`, `IsPlaced`). The collision and rock-clearing changes from that branch (`MarsAstronautPlayer.cs`, `MarsRockBuilder.cs`, `MarsTerrain.cs`) are taken over unchanged, plus a ceiling check in the player. Merging `assets` later conflicts only in `MarsOutpost.cs`; keep this version.
