# Mars terrain and weather prototype

Two modules in one Unity project (`My project/`, Unity 2022.3, URP 14):

- **Terrain:** Gale Crater at life size (1:1) from real NASA/USGS elevation data, streamed in chunks around the camera. See [terrain data and setup](docs/data/terrain-data.md).
- **Time and events:** real Mars time with a 20-minute day-night cycle, and solar events replayed from NASA's DONKI catalogue. The sol, local time and current event show in the top-right info tab. See [Mars clock and weather events](docs/data/weather-events.md).
- **Outpost:** an enterable habitat 40 m from the start, with a room for each survival system (airlock, EVA prep and lab, command hub and galley, medical bay, crew quarters, storm shelter, energy room, life support, greenhouse) and the tool models placed in the rooms that use them. See [Mars outpost](docs/outpost.md).
- **Rover:** a drivable battery rover parked on the outpost pad: 50 km/h top speed, 120 km on a full battery, recharges on the pad. See [Rover](docs/rover.md).
- **Weather:** Clear, Solar Flare, Dust Storm, Heat Storm and Cold Night visuals. See the [MarsWeather integration guide](My%20project/Assets/MarsWeather/README.md).

## Run both together

Open `My project/Assets/Scenes/Mars.unity`. If the scene has no weather yet, choose **Mars → Add Weather To Terrain Scene**. Press Play.

Play opens on the home screen: the camera slowly circles the starting area (or the outpost, once it is in the scene) while the menu offers **Start mission** (or press Enter), **How to play** and **Quit**. Starting fades into the astronaut's view. The screen is built in code (`Scripts/UI/MarsHomeScreen.cs`); to change its text or skip it, add a **MarsHomeScreen** component to a scene object and edit it or untick *Show On Start*.

| Key | Action |
| --- | --- |
| W A S D | Walk (3.4 km/h) |
| Mouse | Look (Esc frees the cursor, click captures it) |
| Left Shift + W | Sprint (29 km/h) |
| Space | Jump (Mars gravity) |
| E | Board the rover when standing next to it; step out when nearly stopped |
| In the rover | W accelerate, S brake then reverse, A / D steer, Space handbrake, mouse orbits the camera |
| F2 | Switch between the astronaut and the free inspection camera (W A S D, Q / E, right mouse to look, Shift faster, scroll wheel changes speed) |
| 1–5 | Clear, Solar Flare, Dust Storm, Heat Storm, Cold Night (the schedule takes over again after 45 s) |
| ] / [ | Step the Mars clock one hour forward / back |
| F3 | Terrain data panel |

## Weather on its own

Open `My project/Assets/MarsWeather/Scenes/WeatherPreview.unity` and press Play. Use the five buttons or keys 1–5 to switch weather.
