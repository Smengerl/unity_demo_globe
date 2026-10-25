using UnityEngine;

namespace Tycoon.GlobeView
{
    /// <summary>Route arcs and flight progress. Lines and planes use the same function, so a plane always sits on its line.</summary>
    public static class RouteMath
    {
        /// <summary>Radius at which markers and the ends of routes sit (just above the globe surface).</summary>
        public const float SurfaceHeight = 1.0005f;

        /// <summary>Position on the arc at t (0..1): starts and ends on the surface, peaks mid-route; longer routes climb higher (up to ~900 km).</summary>
        public static Vector3 Position(Vector3 a, Vector3 b, float angleRad, float t)
        {
            float peak = Mathf.Clamp(0.02f + 0.10f * angleRad, 0.025f, 0.14f);
            return Vector3.Slerp(a, b, t) * (SurfaceHeight + peak * Mathf.Sin(Mathf.PI * t));
        }

        /// <summary>Progress 0..1 of a flight at game time <paramref name="now"/>; outside 0..1 the flight is not in the air.</summary>
        public static float Progress(double now, double depart, double arrive)
        {
            double span = arrive - depart;
            return span <= 0.0 ? 1f : (float)((now - depart) / span);
        }

        public static float AngleRad(Vector3 a, Vector3 b) => Vector3.Angle(a, b) * Mathf.Deg2Rad;
    }
}
