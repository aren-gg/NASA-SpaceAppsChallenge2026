# Mars terrain and weather prototype

Two modules in one Unity project (`My project/`, Unity 2022.3, URP 14):

- **Terrain:** Gale Crater at life size (1:1) from real NASA/USGS elevation data, streamed in chunks around the camera. See [terrain data and setup](docs/data/terrain-data.md).
- **Weather:** Clear, Solar Flare, Dust Storm, Heat Storm and Cold Night visuals. See the [MarsWeather integration guide](My%20project/Assets/MarsWeather/README.md).

## Run both together

Open `My project/Assets/Scenes/Mars.unity`. If the scene has no weather yet, choose **Mars → Add Weather To Terrain Scene**. Press Play.

| Key | Action |
| --- | --- |
| W A S D, Q / E | Move, down / up |
| Right mouse | Look |
| Shift, scroll wheel | Faster, change speed |
| 1–5 | Clear, Solar Flare, Dust Storm, Heat Storm, Cold Night |
| F3 | Terrain data panel |

## Weather on its own

Open `My project/Assets/MarsWeather/Scenes/WeatherPreview.unity` and press Play. Use the five buttons or keys 1–5 to switch weather.
