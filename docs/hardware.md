# Three-knob dashboard

The dashboard uses an **Arduino Uno over USB serial**. Cary already has the Arduino sketch and will connect it later. Physical pairing is intentionally deferred while the game is completed. No sketch, pin assignment, baud rate or serial format has been replaced or assumed.

The game exposes the three required channels: **Life control**, **Temperature**, and **Radiation shielding**. Physical input and software controls issue the same power-request commands. Hardware supplies settings; the simulation owns energy accounting.

## Available software interface

`MarsSurvival.Hardware.MarsDashboardAdapter` provides:

- `Bind(Func<double[]> settings, Action<PowerChannel, double> command, Action<string> disconnect)` connects the adapter to the mission controller. Call this on Unity's main thread.
- `BeginEmulator()` explicitly selects software input. It never sets `Connected` to true.
- `FeedNormalized(life, temperature, shielding)` queues three finite values from 0 to 1. Values already normalized by a bridge bypass raw ADC calibration.
- `FeedRawReadings(life, temperature, shielding, maximum)` queues ADC readings. Uno's usual 0–1023 range is the default, but the bridge must use the actual sketch's range. An overload accepts a sequence number when the existing protocol provides one.
- `AttachPhysicalSource(connectionName)` is called by the future USB reader after it has opened a real connection. `Connected` becomes true only after valid readings arrive.
- `Disconnect()` retains logical settings and requests that fast-forward stop. Missing readings trigger the same behavior after the configured timeout.
- `NotifySoftwareOverride(channel)` and `RequireTakeover()` prevent incoming physical positions from overwriting a mouse adjustment or loaded mission state.
- `SetCalibration(channel, minimum, maximum, inverted)` stores endpoint calibration in device preferences, separate from mission saves.
- `SetFeedback(deliveredKw, missionPhase)` and the `FeedbackReady` event expose logical settings and actual delivery for any feedback supported by the existing hardware. Encoding/writing the feedback is the future USB bridge's responsibility.

`FeedNormalized` and `FeedRawReadings` may run on a reader thread. They add bounded, immutable readings to a queue. Unity invokes the bound game commands on its main thread. Other lifecycle/configuration methods run on the main thread.

Each input source must select either the raw-reading or normalized-reading API and keep its sequencing consistent. Stop an old reader before attaching a replacement. The queue rejects stale readings and duplicate/out-of-order supplied sequence numbers. Source changes reset takeover and invalidate queued readings.

## Calibration and ownership

Incoming positions are smoothed with an 80 ms time constant and a 0.4% command deadband. After reconnect/load/software override, a channel waits until its physical position reaches or crosses the logical setting, within a 2.5% tolerance. The takeover sample retains the existing setting; later samples follow the knob. The UI can display `AwaitingTakeover`, `RawValues` and `NormalizedValues`.

On-screen adjustments must call `NotifySoftwareOverride` before applying their command. Physical callbacks must not call it, or the knob will continually lose control. Rebinding a loaded mission should call `RequireTakeover`.

## Software emulator and checks

The easiest emulator calls `BeginEmulator()` followed by `FeedNormalized(...)` as its sliders change or at a regular sample rate. It exercises the same calibration-independent smoothing, ownership and command path as a normalized USB bridge.

An **optional test-only** line format is also implemented by `DashboardProtocol` and `FeedEmulatorLine`. The existing Arduino sketch does not have to use it:

```text
MARS,1,HELLO,test-boot,1023
MARS,1,KNOBS,test-boot,1,100,430,460,50
MARS,1,HEARTBEAT,test-boot,2,200
```

The parser bounds lines, validates ranges and versions, and rejects duplicate/old packets. `DashboardProtocolChecks.Run()` returns failed check descriptions; `RunOrThrow()` is available to the project's Unity validation runner. The checks cover malformed packets, boot sessions, sequence rollover, bounded framing, calibration and safe takeover.

## Pairing the existing Uno later

1. Read the existing sketch's baud rate, message framing, field order, range and update frequency. Confirm which field is each knob.
2. Add a USB reader/decoder for that format. Read off the main thread, bound line/frame length, and feed the parsed values through the adapter. Do not change the sketch merely to match the optional emulator format.
3. Call `AttachPhysicalSource` only after opening the connection. Feed readings periodically even when knobs are stationary, or explicitly translate the sketch's heartbeat into current readings. Call `Disconnect` when the reader fails/closes.
4. Capture actual endpoints and calibrate each channel. Verify the physical knobs independently change the corresponding game request and spend real simulated energy.
5. Check noisy input, unplugging, reconnecting and loading a save with different physical knob positions. Confirm settings do not jump and fast-forward stops on loss of input.

Software checks do not establish successful physical pairing. The actual USB reader/decoder and connected-device checks remain deferred at Cary's request.
