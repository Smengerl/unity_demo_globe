using System;
using UnityEngine;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// Low-precision sun/moon positions (Meeus-style, ~0.3-1 deg) in the Earth-fixed frame of <see cref="GeoMath"/>.
    /// Good enough for visuals; not for navigation.
    /// </summary>
    public static class Astronomy
    {
        public struct Sky
        {
            public Vector3 SunDir;
            public Vector3 MoonDir;
            public float GmstDeg;
        }

        const double D2R = Math.PI / 180.0, R2D = 180.0 / Math.PI;

        static double Norm360(double a)
        {
            a %= 360.0;
            return a < 0 ? a + 360.0 : a;
        }

        /// <param name="unixSeconds">Time as Unix seconds (UTC).</param>
        public static Sky Compute(double unixSeconds)
        {
            double d = unixSeconds / 86400.0 - 10957.5;   // days since J2000.0 (2000-01-01 12:00 UTC)
            double eps = (23.4393 - 3.563e-7 * d) * D2R;
            double gmst = Norm360(280.46061837 + 360.98564736629 * d);

            double meanLon = 280.460 + 0.9856474 * d;
            double g = (357.528 + 0.9856003 * d) * D2R;
            double sunLon = (meanLon + 1.915 * Math.Sin(g) + 0.020 * Math.Sin(2 * g)) * D2R;
            EclipticToEquatorial(sunLon, 0, eps, out double sunRa, out double sunDec);

            double moonMeanLon = 218.316 + 13.176396 * d;
            double m = (134.963 + 13.064993 * d) * D2R;
            double f = (93.272 + 13.229350 * d) * D2R;
            double moonLon = (moonMeanLon + 6.289 * Math.Sin(m)) * D2R;
            double moonLat = 5.128 * Math.Sin(f) * D2R;
            EclipticToEquatorial(moonLon, moonLat, eps, out double moonRa, out double moonDec);

            return new Sky
            {
                SunDir = GeoMath.Dir(sunDec * R2D, sunRa * R2D - gmst),
                MoonDir = GeoMath.Dir(moonDec * R2D, moonRa * R2D - gmst),
                GmstDeg = (float)gmst,
            };
        }

        static void EclipticToEquatorial(double lon, double lat, double eps, out double ra, out double dec)
        {
            ra = Math.Atan2(Math.Sin(lon) * Math.Cos(eps) - Math.Tan(lat) * Math.Sin(eps), Math.Cos(lon));
            dec = Math.Asin(Math.Sin(lat) * Math.Cos(eps) + Math.Cos(lat) * Math.Sin(eps) * Math.Sin(lon));
        }
    }
}
