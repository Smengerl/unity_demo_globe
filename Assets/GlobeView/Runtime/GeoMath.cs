using UnityEngine;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// Convention: Earth is a unit sphere at the origin, +Y = north pole, lat 0 / lon 0 = -Z, east = +X.
    /// Seen from outside (camera at -Z looking +Z), east is to the right. The Earth shader uses the same mapping.
    /// </summary>
    public static class GeoMath
    {
        public const float RadiusKm = 6371f;

        public static Vector3 Dir(double latDeg, double lonDeg)
        {
            double la = latDeg * Mathf.Deg2Rad, lo = lonDeg * Mathf.Deg2Rad;
            double c = System.Math.Cos(la);
            return new Vector3(
                (float)(c * System.Math.Sin(lo)),
                (float)System.Math.Sin(la),
                (float)(-c * System.Math.Cos(lo)));
        }

        /// <summary>The dot product (direction · camera direction) above which a point on the surface is on the near side of the globe for a camera at this height.</summary>
        public static float HorizonDot(float cameraHeight) => 1f / (1f + cameraHeight);

        public static void LatLon(Vector3 dir, out float latDeg, out float lonDeg)
        {
            latDeg = Mathf.Asin(Mathf.Clamp(dir.y, -1f, 1f)) * Mathf.Rad2Deg;
            lonDeg = Mathf.Atan2(dir.x, -dir.z) * Mathf.Rad2Deg;
        }
    }
}
