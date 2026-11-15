using System;

namespace MarsSurvival.TimeAndWeather
{
    /// <summary>
    /// Mars timekeeping and Sun position from the Mars24 algorithm:
    /// Allison and McEwen (2000), "A post-Pathfinder evaluation of areocentric solar
    /// coordinates with improved timing recipes for Mars seasonal/diurnal climate
    /// studies", Planet. Space Sci. 48, 215-235, as published by NASA GISS
    /// (https://www.giss.nasa.gov/tools/mars24/help/algorithm.html).
    ///
    /// Pure arithmetic with no Unity types, so it can be checked outside the editor.
    /// Angles are degrees, longitudes are east-positive, times are UTC.
    /// </summary>
    public static class MarsTime
    {
        /// <summary>Length of a mean solar day on Mars (a sol) in SI seconds: 24 h 39 m 35.244 s.</summary>
        public const double SolSeconds = 88775.244;
        public const double SolInEarthDays = 1.0274912517;
        /// <summary>Sols in a Mars tropical year.</summary>
        public const double SolsPerYear = 668.5921;

        /// <summary>Curiosity's landing, 2012-08-06 05:17:57 UTC, and its site (Bradbury Landing).</summary>
        public const double CuriosityLandingUnix = 1344230277.0;
        public const double CuriosityLongitudeEast = 137.4417;
        public const double CuriosityLatitude = -4.5895;

        private const double Deg2Rad = Math.PI / 180.0;
        private const double Rad2Deg = 180.0 / Math.PI;

        /// <summary>Everything the game needs to know about one moment at one place on Mars.</summary>
        public struct State
        {
            /// <summary>Mars Sol Date: sols counted continuously at Mars's prime meridian.</summary>
            public double marsSolDate;
            /// <summary>Mars Sol Date shifted to the local meridian: whole part = local sol, fraction = time of day.</summary>
            public double localSolDate;
            /// <summary>Whole local sol number: the Mars Sol Date day count on the local mean-time calendar.</summary>
            public long localSol;
            /// <summary>Coordinated Mars Time (mean solar time at the prime meridian), hours 0-24.</summary>
            public double coordinatedMarsTimeHours;
            /// <summary>Local Mean Solar Time, hours 0-24. This is the clock time.</summary>
            public double localMeanSolarTimeHours;
            /// <summary>Local True Solar Time, hours 0-24. The Sun is highest at 12.</summary>
            public double localTrueSolarTimeHours;
            /// <summary>Areocentric solar longitude Ls, 0-360: 0 = northern spring equinox.</summary>
            public double solarLongitude;
            /// <summary>Mars Year on the Clancy et al. calendar (MY 1 began 11 April 1955).</summary>
            public int marsYear;
            public double equationOfTimeDegrees;
            public double sunDeclination;
            /// <summary>Sun's height above the horizon; negative at night.</summary>
            public double sunElevation;
            /// <summary>Unit vector toward the Sun: x = east, y = up, z = north.</summary>
            public double sunEast, sunUp, sunNorth;
            /// <summary>Sun-Mars distance in astronomical units.</summary>
            public double sunDistanceAu;
        }

        /// <summary>TAI - UTC. Constant since 2017; earlier steps kept so historical dates check out.</summary>
        private static double LeapSeconds(double unixSeconds)
        {
            if (unixSeconds >= 1483228800.0) return 37; // 2017-01-01
            if (unixSeconds >= 1435708800.0) return 36; // 2015-07-01
            if (unixSeconds >= 1341100800.0) return 35; // 2012-07-01
            if (unixSeconds >= 1230768000.0) return 34; // 2009-01-01
            if (unixSeconds >= 1136073600.0) return 33; // 2006-01-01
            return 32;                                  // 1999-01-01
        }

        public static State Compute(double unixSeconds, double latitude, double longitudeEast)
        {
            // A-1..A-6: Julian dates and days since J2000 in Terrestrial Time.
            double julianUtc = 2440587.5 + unixSeconds / 86400.0;
            double julianTt = julianUtc + (LeapSeconds(unixSeconds) + 32.184) / 86400.0;
            double days = julianTt - 2451545.0;

            // B-1..B-4: mean anomaly, fictitious mean sun, perturbers, equation of centre.
            double meanAnomaly = 19.3871 + 0.52402073 * days;
            double fictitiousMeanSun = 270.3871 + 0.524038496 * days;
            double perturbers = 0.0;
            for (int i = 0; i < PerturberAmplitude.Length; i++)
            {
                perturbers += PerturberAmplitude[i] *
                              Math.Cos((0.985626 * days / PerturberPeriod[i] + PerturberPhase[i]) * Deg2Rad);
            }
            double m = meanAnomaly * Deg2Rad;
            double equationOfCentre = (10.691 + 3.0e-7 * days) * Math.Sin(m) + 0.623 * Math.Sin(2 * m) +
                                      0.050 * Math.Sin(3 * m) + 0.005 * Math.Sin(4 * m) +
                                      0.0005 * Math.Sin(5 * m) + perturbers;

            // B-5: areocentric solar longitude.
            double lsUnwrapped = fictitiousMeanSun + equationOfCentre;
            double ls = Wrap(lsUnwrapped, 360.0);
            double lsRad = ls * Deg2Rad;

            // C-1..C-4: equation of time, Mars Sol Date, clock times.
            double equationOfTime = 2.861 * Math.Sin(2 * lsRad) - 0.071 * Math.Sin(4 * lsRad) +
                                    0.002 * Math.Sin(6 * lsRad) - equationOfCentre;
            double msd = (julianTt - 2451549.5) / SolInEarthDays + 44796.0 - 0.0009626;
            double mtc = Wrap(24.0 * msd, 24.0);
            double localDate = msd + longitudeEast / 360.0;
            double lmst = Wrap(24.0 * localDate, 24.0);
            double ltst = Wrap(lmst + equationOfTime / 15.0, 24.0);

            // D-1..D-2 and the horizon triangle: where the Sun is in the local sky.
            double declination = Math.Asin(0.42565 * Math.Sin(lsRad)) * Rad2Deg + 0.25 * Math.Sin(lsRad);
            double hourAngle = (ltst - 12.0) * 15.0 * Deg2Rad;
            double lat = latitude * Deg2Rad, dec = declination * Deg2Rad;

            State s;
            s.marsSolDate = msd;
            s.localSolDate = localDate;
            s.localSol = (long)Math.Floor(localDate);
            s.coordinatedMarsTimeHours = mtc;
            s.localMeanSolarTimeHours = lmst;
            s.localTrueSolarTimeHours = ltst;
            s.solarLongitude = ls;
            // At J2000 the unwrapped longitude is 277 degrees and the year is MY 24.
            s.marsYear = 24 + (int)Math.Floor(lsUnwrapped / 360.0);
            s.equationOfTimeDegrees = equationOfTime;
            s.sunDeclination = declination;
            s.sunEast = -Math.Cos(dec) * Math.Sin(hourAngle);
            s.sunNorth = Math.Cos(lat) * Math.Sin(dec) - Math.Sin(lat) * Math.Cos(dec) * Math.Cos(hourAngle);
            s.sunUp = Math.Sin(lat) * Math.Sin(dec) + Math.Cos(lat) * Math.Cos(dec) * Math.Cos(hourAngle);
            s.sunElevation = Math.Asin(Math.Max(-1.0, Math.Min(1.0, s.sunUp))) * Rad2Deg;
            // Distance on the orbital ellipse (a = 1.523679 au, e = 0.0934) at the true anomaly.
            double trueAnomaly = (meanAnomaly + equationOfCentre) * Deg2Rad;
            s.sunDistanceAu = 1.523679 * (1.0 - 0.0934 * 0.0934) / (1.0 + 0.0934 * Math.Cos(trueAnomaly));
            return s;
        }

        /// <summary>Sols since Curiosity landed, on the mission's own calendar (landing day is sol 0).</summary>
        public static long CuriositySol(double unixSeconds)
        {
            State now = Compute(unixSeconds, CuriosityLatitude, CuriosityLongitudeEast);
            State landing = Compute(CuriosityLandingUnix, CuriosityLatitude, CuriosityLongitudeEast);
            return now.localSol - landing.localSol;
        }

        /// <summary>Season at a latitude from Ls. Southern seasons are the reverse of northern ones.</summary>
        public static string Season(double solarLongitude, double latitude)
        {
            int quarter = (int)Math.Floor(Wrap(solarLongitude, 360.0) / 90.0) % 4;
            if (latitude < 0) quarter = (quarter + 2) % 4;
            return SeasonNames[quarter];
        }

        /// <summary>"14:07" from hours 0-24 (Mars hours and minutes: 1/24 and 1/1440 of a sol).</summary>
        public static string FormatClock(double hours)
        {
            int minutes = (int)Math.Floor(Wrap(hours, 24.0) * 60.0);
            return (minutes / 60).ToString("00") + ":" + (minutes % 60).ToString("00");
        }

        public static double Wrap(double value, double period)
        {
            double r = value % period;
            return r < 0 ? r + period : r;
        }

        private static readonly string[] SeasonNames = { "spring", "summer", "autumn", "winter" };
        private static readonly double[] PerturberAmplitude = { 0.0071, 0.0057, 0.0039, 0.0037, 0.0021, 0.0020, 0.0018 };
        private static readonly double[] PerturberPeriod = { 2.2353, 2.7543, 1.1177, 15.7866, 2.1354, 2.4694, 32.8493 };
        private static readonly double[] PerturberPhase = { 49.409, 168.173, 191.837, 21.736, 15.704, 95.528, 49.095 };
    }
}
