# Rover

A drivable battery rover, built in code (`Assets/Scripts/Vehicles/`). It parks on the front left of the outpost pad, facing the way off the pad.

## Driving

| Key | Action |
| --- | --- |
| E | Board when within 7 m; step out when below about 7 km/h |
| W | Accelerate |
| S | Brake, then reverse |
| A / D | Steer |
| Space | Handbrake |
| Mouse | Orbit the chase camera |

| Setting (`MarsRover`) | Default | Meaning |
| --- | --- | --- |
| `maxSpeedKmh` | 50 | Top speed on level ground |
| `reverseSpeedKmh` | 20 | Top speed in reverse |
| `acceleration` | 2.4 m/s² | Pull from rest; full up to 30 km/h, then tailing off to nothing at top speed |
| `braking`, `handbrake` | 3.2, 4.5 m/s² | Slowing with S and with Space |
| `lateralGrip` | 3 m/s² | Sideways grip; sets how tightly it turns at speed |
| `batteryCapacityKwh` | 60 kWh | Battery size; the rover starts full |
| `consumptionKwhPerKm` | 0.5 kWh/km | Energy per kilometre driven |
| `rechargePercentPerSecond` | 1.5 | Recharge rate while parked on the outpost pad |

- **Speed:** about 3.5 s to 30 km/h and about 12 s until the display reads 50 km/h on level ground. Slopes add or remove speed under Mars gravity (3.71 m/s²), so it can run a little over 50 downhill.
- **Battery:** range is capacity divided by consumption: 60 / 0.5 = 120 km on a full battery, however it is driven. The display shows percent and kilometres left. At 0% the motors give nothing and the rover rolls to a stop.
- **Recharging:** stopped anywhere on the outpost pad, it regains 1.5% per second (about 67 s from empty). This is a game convenience, not a modelled charger.
- **Ground:** the rover follows the terrain under its four wheels, pitching and rolling with it, and drives onto the pad and ramps. Buildings, solar panels and any step higher than 0.7 m stop it. It does not leave the ground over crests.
- **Turning:** the front wheels turn up to 32° at walking pace and 5° at top speed, and the turn rate is capped by grip, so the turning circle at 50 km/h is about 130 m across.
- **Headlights** come on in twilight and at night while it is driven.

The acceleration and grip figures are chosen to be plausible for tyres under Mars gravity; they are not taken from a real vehicle.

## Model

A long white hull with a tapered tail and rounded nose, a dark wrap-around windscreen, two strips of blue solar cells on the roof, a dark chassis and four large treaded wheels 2 m across. It is 10.1 m long, 5.5 m wide and 3.9 m tall, with a cabin 2.16 m from floor to roof, so a 1.8 m astronaut can stand inside. The shape is drawn small and enlarged 1.6 times (`MarsRoverModel.Scale`). It is an original low-poly model in the spirit of the cargo-rover concept image, using the same materials as the outpost. `MarsRoverModel.cs` builds it; `MarsRover.cs` drives it.

For other systems: `rover.SpeedKmh`, `rover.BatteryPercent`, `rover.RangeLeftKm`, `rover.OdometerKm`, `rover.IsDriven`, `rover.IsCharging`.
