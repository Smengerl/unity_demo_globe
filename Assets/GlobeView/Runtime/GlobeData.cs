using System;
using System.Collections.Generic;
using Tycoon.GlobeView.Contracts;
using UnityEngine;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// The view's own copy of the model lists plus what the layers derive from them (directions, arc geometry, route index of a flight).
    /// Refreshed only when a version number of the model changed; all layers read from here.
    /// </summary>
    internal sealed class GlobeData
    {
        public struct RouteGeom
        {
            public Vector3 A, B, Mid;
            public float AngleRad;
            public int FromIndex;               // airport index of the start, -1 if an end of the route is unknown
        }

        public AirportView[] Airports = new AirportView[0];
        public Vector3[] AirportDir = new Vector3[0];
        public int AirportCount;

        public RouteView[] Routes = new RouteView[0];
        public RouteGeom[] RouteGeoms = new RouteGeom[0];
        public int RouteCount;

        public FlightView[] Flights = new FlightView[0];
        public int[] FlightRoute = new int[0];  // route index of each flight, -1 if the route is unknown
        public int FlightCount;

        public bool AirportsChanged, RoutesChanged, FlightsChanged;

        readonly Dictionary<int, int> airportIndex = new Dictionary<int, int>();
        readonly Dictionary<int, int> routeIndex = new Dictionary<int, int>();
        int airportsVersion = int.MinValue, routesVersion = int.MinValue, flightsVersion = int.MinValue;

        public bool TryAirportIndex(int id, out int index) => airportIndex.TryGetValue(id, out index);
        public bool TryRouteIndex(int id, out int index) => routeIndex.TryGetValue(id, out index);

        public void Refresh(IGlobeModel model)
        {
            AirportsChanged = model.AirportsVersion != airportsVersion;
            RoutesChanged = model.RoutesVersion != routesVersion;
            FlightsChanged = model.FlightsVersion != flightsVersion;

            if (AirportsChanged)
            {
                airportsVersion = model.AirportsVersion;
                Copy(model.Airports, ref Airports, out AirportCount);
                if (AirportDir.Length < AirportCount) AirportDir = new Vector3[Airports.Length];
                airportIndex.Clear();
                for (int i = 0; i < AirportCount; i++)
                {
                    AirportDir[i] = GeoMath.Dir(Airports[i].Lat, Airports[i].Lon);
                    airportIndex[Airports[i].Id] = i;
                }
            }

            if (RoutesChanged || AirportsChanged)   // arcs depend on airport positions
            {
                routesVersion = model.RoutesVersion;
                Copy(model.Routes, ref Routes, out RouteCount);
                if (RouteGeoms.Length < RouteCount) RouteGeoms = new RouteGeom[Routes.Length];
                routeIndex.Clear();
                for (int i = 0; i < RouteCount; i++)
                {
                    routeIndex[Routes[i].Id] = i;
                    var g = new RouteGeom { FromIndex = -1 };
                    if (airportIndex.TryGetValue(Routes[i].FromId, out int from) && airportIndex.TryGetValue(Routes[i].ToId, out int to))
                    {
                        g.FromIndex = from;
                        g.A = AirportDir[from];
                        g.B = AirportDir[to];
                        g.Mid = Vector3.Slerp(g.A, g.B, 0.5f);
                        g.AngleRad = RouteMath.AngleRad(g.A, g.B);
                    }
                    RouteGeoms[i] = g;
                }
                RoutesChanged = true;   // also true when only the airports moved
            }

            if (FlightsChanged || RoutesChanged)    // route indices depend on the route list
            {
                flightsVersion = model.FlightsVersion;
                Copy(model.Flights, ref Flights, out FlightCount);
                if (FlightRoute.Length < FlightCount) FlightRoute = new int[Flights.Length];
                for (int i = 0; i < FlightCount; i++)
                    FlightRoute[i] = routeIndex.TryGetValue(Flights[i].RouteId, out int r) && RouteGeoms[r].FromIndex >= 0 ? r : -1;
                FlightsChanged = true;
            }
        }

        static void Copy<T>(IReadOnlyList<T> src, ref T[] dst, out int count)
        {
            count = src.Count;
            if (dst.Length < count) dst = new T[Math.Max(count, dst.Length * 2)];
            for (int i = 0; i < count; i++) dst[i] = src[i];
        }
    }
}
