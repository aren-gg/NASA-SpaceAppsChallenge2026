using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MarsSurvival.Simulation;
using MarsSurvival.TimeAndWeather;

public static class MissionEnvironmentChecks
{
    public static int Main(string[] args)
    {
        try
        {
            int count = Run();
            if (args.Length == 1) count += Run(ReadPreparedCsv(args[0]));
            Console.WriteLine("Environment checks passed: " + count);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    public static int Run(MissionEnvironment.EnvironmentProfile profile = null)
    {
        MissionEnvironment model = new MissionEnvironment(profile);
        int checks = 0;
        Action<bool, string> assert = (valid, name) => { if (!valid) throw new Exception(name); checks++; };
        const double start = 1763985600;
        bool foundDust = false, foundSolar = false, foundWarning = false, foundNight = false, foundOverlap = false;
        for (int i = 0; i <= 13372; i++)
        {
            double elapsed = i * 0.05 * MarsTime.SolSeconds;
            EnvironmentSample a = model.Sample(elapsed, start, 2026, -4.5895, 137.4417);
            EnvironmentSample b = model.Sample(elapsed, start, 2026, -4.5895, 137.4417);
            assert(a.temperatureC == b.temperatureC && a.solarFraction == b.solarFraction && a.dust == b.dust && a.solarEvent == b.solarEvent, "Deterministic sampling");
            assert(!double.IsNaN(a.temperatureC) && a.temperatureC > -150 && a.temperatureC < 50, "Temperature range");
            assert(a.radiationMicrograyPerHour >= 8.75 && a.radiationMicrograyPerHour <= 508.75, "Dose units and bounds");
            assert(a.signal >= 0 && a.signal <= 1 && a.nextWindowSeconds >= 0, "Radio bounds");
            if (MarsTime.Compute(start + elapsed, -4.5895, 137.4417).sunElevation <= 0)
            {
                assert(a.solarFraction == 0, "No night generation");
                foundNight = true;
            }
            if (elapsed < MissionSettings.Duration(MissionLength.Month))
            {
                foundDust |= a.dust > 0;
                foundSolar |= a.solarEvent > 0;
                foundWarning |= a.warning;
            }
            foundOverlap |= a.dust > 0 && a.cold > 0;
        }
        assert(foundDust && foundSolar && foundWarning && foundNight && foundOverlap, "Short mission hazards and simultaneous effects");
        assert(model.TemperatureAt(0) == model.TemperatureAt(24), "Cyclic temperature continuity");
        EnvironmentSample later = model.Sample(650 * MarsTime.SolSeconds, start, 2026, -4.5895, 137.4417);
        assert(later.eventSource.Contains("Seeded"), "Honest long-duration source label");
        return checks;
    }

    private static MissionEnvironment.EnvironmentProfile ReadPreparedCsv(string path)
    {
        List<MissionEnvironment.TemperaturePoint> points = new List<MissionEnvironment.TemperaturePoint>();
        string[] lines = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            string[] fields = lines[i].Split(',');
            points.Add(new MissionEnvironment.TemperaturePoint
            {
                hour = double.Parse(fields[0].Trim('"'), CultureInfo.InvariantCulture),
                temperatureC = double.Parse(fields[2].Trim('"'), CultureInfo.InvariantCulture)
            });
        }
        if (points.Count != 24) throw new Exception("Expected 24 prepared hourly observations");
        MissionEnvironment.TemperaturePoint first = points[0], last = points[points.Count - 1];
        double boundary = last.temperatureC + (first.temperatureC - last.temperatureC) * (24 - last.hour) / (first.hour + 24 - last.hour);
        points.Insert(0, new MissionEnvironment.TemperaturePoint { hour = 0, temperatureC = boundary });
        points.Add(new MissionEnvironment.TemperaturePoint { hour = 24, temperatureC = boundary });
        return new MissionEnvironment.EnvironmentProfile
        {
            temperatureSource = "REMS sol224 quality-filtered hourly profile",
            temperatures = points.ToArray()
        };
    }
}
