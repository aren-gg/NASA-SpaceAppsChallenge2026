# Mars terrain and weather prototype

Two modules in one Unity project (`My project/`, Unity 2022.3, URP 14):

- **Terrain:** Gale Crater at life size (1:1) from real NASA/USGS elevation data, streamed in chunks around the camera. See [terrain data and setup](docs/data/terrain-data.md).
- **Weather:** Clear, Solar Flare, Dust Storm, Heat Storm and Cold Night visuals. See the [MarsWeather integration guide](My%20project/Assets/MarsWeather/README.md).

## Run both together

Open `My project/Assets/Scenes/Mars.unity`. If the scene has no weather yet, choose **Mars → Add Weather To Terrain Scene**. Press Play.

| Key | Action |
| --- | --- |
| W A S D | Walk (3.4 km/h) |
| Mouse | Look (Esc frees the cursor, click captures it) |
| Left Shift + W | Sprint (29 km/h) |
| Space | Jump (Mars gravity) |
| F2 | Switch between the astronaut and the free inspection camera (W A S D, Q / E, right mouse to look, Shift faster, scroll wheel changes speed) |
| 1–5 | Clear, Solar Flare, Dust Storm, Heat Storm, Cold Night |
| F3 | Terrain data panel |

## Weather on its own

Open `My project/Assets/MarsWeather/Scenes/WeatherPreview.unity` and press Play. Use the five buttons or keys 1–5 to switch weather.
