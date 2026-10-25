using System;
using System.Collections.Generic;
using Tycoon.GlobeView;
using Tycoon.GlobeView.Contracts;
using Tycoon.Hud;
using UnityEngine;

namespace Tycoon.Demo
{
    /// <summary>
    /// Stands in for the game: owns the clock, makes up airports, routes and a flight timetable, and reacts to taps.
    /// The real game's controller will implement <see cref="IGlobeModel"/> the same way.
    /// </summary>
    public sealed class FakeController : IGlobeModel, IHudModel
    {
        const double CruiseKmPerHour = 850.0;
        static readonly Color32 OwnColor = HudTokens.MapOwn;
        static readonly Color32 RouteColor = HudTokens.MapRouteActive;
        static readonly Color32 PlaneColor = new Color32(255, 255, 255, 255);

        // IATA, name, lat, lon, hub
        static readonly (string iata, string name, float lat, float lon, bool hub)[] Table =
        {
            ("FRA", "Frankfurt", 50.04f, 8.56f, true), ("MUC", "Munich", 48.35f, 11.79f, true),
            ("BER", "Berlin Brandenburg", 52.37f, 13.50f, false), ("HAM", "Hamburg", 53.63f, 9.99f, false),
            ("DUS", "Duesseldorf", 51.29f, 6.77f, false), ("CGN", "Cologne/Bonn", 50.87f, 7.14f, false),
            ("STR", "Stuttgart", 48.69f, 9.22f, false), ("NUE", "Nuremberg", 49.50f, 11.07f, false),
            ("LEJ", "Leipzig/Halle", 51.42f, 12.24f, false), ("HAJ", "Hanover", 52.46f, 9.69f, false),
            ("ZRH", "Zurich", 47.46f, 8.55f, false), ("BSL", "Basel-Mulhouse", 47.59f, 7.53f, false),
            ("GVA", "Geneva", 46.24f, 6.11f, false), ("VIE", "Vienna", 48.11f, 16.57f, true),
            ("PRG", "Prague", 50.10f, 14.26f, false), ("BUD", "Budapest", 47.44f, 19.26f, false),
            ("WAW", "Warsaw", 52.17f, 20.97f, false), ("AMS", "Amsterdam", 52.31f, 4.77f, true),
            ("BRU", "Brussels", 50.90f, 4.48f, false), ("CDG", "Paris Charles de Gaulle", 49.01f, 2.55f, true),
            ("LHR", "London Heathrow", 51.47f, -0.45f, true), ("DUB", "Dublin", 53.43f, -6.25f, false),
            ("CPH", "Copenhagen", 55.62f, 12.65f, false), ("ARN", "Stockholm Arlanda", 59.65f, 17.92f, false),
            ("OSL", "Oslo", 60.20f, 11.10f, false), ("MXP", "Milan Malpensa", 45.63f, 8.73f, false),
            ("FCO", "Rome Fiumicino", 41.80f, 12.24f, true), ("MAD", "Madrid", 40.49f, -3.57f, true),
            ("LIS", "Lisbon", 38.77f, -9.13f, false), ("ATH", "Athens", 37.94f, 23.94f, false),
            ("IST", "Istanbul", 41.28f, 28.75f, true), ("JFK", "New York JFK", 40.64f, -73.78f, true),
        };

        static readonly (string from, string to)[] RoutePairs =
        {
            ("FRA", "JFK"), ("LHR", "JFK"), ("FRA", "LHR"), ("MUC", "BER"), ("FRA", "VIE"),
            ("AMS", "CDG"), ("ZRH", "MAD"), ("BER", "ARN"), ("MUC", "FCO"), ("FRA", "IST"),
            ("HAM", "CPH"), ("DUS", "WAW"), ("CDG", "LIS"), ("VIE", "ATH"), ("BRU", "FRA"),
            ("PRG", "MUC"), ("DUB", "AMS"), ("OSL", "FRA"), ("STR", "BUD"), ("GVA", "LHR"),
        };

        class Timetable
        {
            public int RouteId;
            public double IntervalSeconds, DurationSeconds, NextDeparture;
            public bool NextBackwards;
        }

        readonly List<AirportView> airports = new List<AirportView>();
        readonly List<RouteView> routes = new List<RouteView>();
        readonly List<FlightView> flights = new List<FlightView>();
        readonly List<Timetable> timetables = new List<Timetable>();
        readonly Dictionary<int, string> cities = new Dictionary<int, string>();
        readonly System.Random rng = new System.Random(7);

        double utc, rate = 600.0;
        double cash = 12_400_000.0;
        double generatedUntil;
        int nextFlightId = 1;
        float delayTimer;
        bool stress;

        public int AirportsVersion { get; private set; }
        public int RoutesVersion { get; private set; }
        public int FlightsVersion { get; private set; }
        public SelectionState Selection { get; private set; } = SelectionState.None;
        public GameTimeState Time => new GameTimeState(utc, rate);
        public IReadOnlyList<AirportView> Airports => airports;
        public IReadOnlyList<RouteView> Routes => routes;
        public IReadOnlyList<FlightView> Flights => flights;

        // ---- money and texts for the HUD (IHudModel) ----

        public double Cash => cash;
        public double CashPerDay => 38_200.0;
        public event Action<HudToastKind, string> Notice;
        public void SetRate(double newRate) => Rate = newRate;
        public string AirportName(int airportId) => cities.TryGetValue(airportId, out var n) ? n : "?";

        public void AirportStats(int airportId, out int routeCount, out int flightsPerDay, out float load)
        {
            routeCount = 0;
            double perDay = 0;
            foreach (var t in timetables)
            {
                foreach (var r in routes)
                    if (r.Id == t.RouteId && (r.FromId == airportId || r.ToId == airportId)) { routeCount++; perDay += 86400.0 / t.IntervalSeconds; }
            }
            flightsPerDay = (int)Math.Round(perDay);
            load = 0.55f + (airportId * 37 % 40) / 100f;        // made up: the demo has no passengers
        }

        public FakeController()
        {
            utc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();   // start with the real time, so the terminator matches reality
            Build();
        }

        // ---- the game's own state, driven by the demo UI and the tests ----

        public double Rate { get => rate; set => rate = Math.Max(0.0, value); }
        public bool Stress => stress;

        public void SetTime(double unixSeconds, double newRate)
        {
            utc = unixSeconds;
            rate = Math.Max(0.0, newRate);
            flights.Clear();
            generatedUntil = utc - 86400.0;
            foreach (var t in timetables) t.NextDeparture = utc - 86400.0 + rng.NextDouble() * t.IntervalSeconds;
            TopUp();
        }

        public void SetStress(bool on)
        {
            if (on == stress) return;
            stress = on;
            Build();
        }

        public void Update(float realDeltaTime)
        {
            utc += rate * realDeltaTime;
            cash += CashPerDay * rate * realDeltaTime / 86400.0;
            TopUp();

            delayTimer += realDeltaTime;
            if (delayTimer > 15f) { delayTimer = 0f; DelayOneFlight(); }
        }

        // ---- selection (answers to the view's taps) ----

        public void SelectAirport(int id) => Selection = Selection.AirportId == id ? SelectionState.None : new SelectionState(id, -1);
        public void SelectRoute(int id) => Selection = new SelectionState(Selection.AirportId, id);
        public void ClearSelection() => Selection = SelectionState.None;

        // ---- building the world ----

        void Build()
        {
            airports.Clear(); routes.Clear(); timetables.Clear(); flights.Clear(); cities.Clear();
            var idOf = new Dictionary<string, int>();
            for (int i = 0; i < Table.Length; i++)
            {
                int id = i + 1;
                idOf[Table[i].iata] = id;
                cities[id] = Table[i].name;
                airports.Add(new AirportView(id, Table[i].iata, Table[i].lat, Table[i].lon, (byte)(Table[i].hub ? 3 : 1), AirportKind.Own, OwnColor, (byte)(Table[i].hub ? 2 : 1)));
            }
            var hubs = new HashSet<string>();
            foreach (var t in Table) if (t.hub) hubs.Add(t.iata);
            for (int i = 0; i < RoutePairs.Length; i++)
            {
                bool main = hubs.Contains(RoutePairs[i].from) && hubs.Contains(RoutePairs[i].to);
                routes.Add(new RouteView(100 + i, idOf[RoutePairs[i].from], idOf[RoutePairs[i].to], (byte)(main ? 1 : 0), RouteColor));
            }

            if (stress)   // many random airports and routes, to see how the view copes
            {
                for (int i = 0; i < 200; i++)
                {
                    int id = 1000 + i;
                    cities[id] = $"Stress test {i}";
                    airports.Add(new AirportView(id, "S" + i, (float)(rng.NextDouble() * 120 - 50), (float)(rng.NextDouble() * 340 - 170), (byte)rng.Next(0, 3), AirportKind.Competitor, HudTokens.MapCompetitor));
                }
                for (int i = 0; i < 800; i++)
                    routes.Add(new RouteView(2000 + i, 1000 + rng.Next(200), 1000 + rng.Next(200), 0, HudTokens.MapRouteActive));
            }

            var byId = new Dictionary<int, AirportView>();
            foreach (var a in airports) byId[a.Id] = a;
            foreach (var r in routes)
            {
                if (r.FromId == r.ToId) continue;
                float km = Vector3.Angle(GeoMath.Dir(byId[r.FromId].Lat, byId[r.FromId].Lon), GeoMath.Dir(byId[r.ToId].Lat, byId[r.ToId].Lon)) * Mathf.Deg2Rad * GeoMath.RadiusKm;
                int perDay = Mathf.Clamp(Mathf.RoundToInt(km / 700f) + 2, 2, 10);
                timetables.Add(new Timetable
                {
                    RouteId = r.Id,
                    IntervalSeconds = 86400.0 / perDay,
                    DurationSeconds = km / CruiseKmPerHour * 3600.0 + 1200.0,
                    NextDeparture = utc - 86400.0 + rng.NextDouble() * 86400.0 / perDay,
                    NextBackwards = rng.Next(2) == 1,
                });
            }

            generatedUntil = utc - 86400.0;
            AirportsVersion++;
            RoutesVersion++;
            TopUp();
            Selection = SelectionState.None;
        }

        // Flights are generated ahead of the game time; the further ahead the faster the game runs.
        void TopUp()
        {
            double horizon = Math.Max(6.0 * 3600.0, rate * 8.0);
            if (generatedUntil >= utc + horizon * 0.5) return;

            double until = utc + horizon;
            flights.RemoveAll(f => f.ArriveUtc < utc - 120.0);
            foreach (var t in timetables)
            {
                while (t.NextDeparture < until)
                {
                    flights.Add(new FlightView(nextFlightId++, t.RouteId, t.NextDeparture, t.NextDeparture + t.DurationSeconds, t.NextBackwards, (byte)rng.Next(0, 3), PlaneColor));
                    t.NextBackwards = !t.NextBackwards;
                    t.NextDeparture += t.IntervalSeconds;
                }
            }
            generatedUntil = until;
            FlightsVersion++;
        }

        string AirportLabel(int id)
        {
            foreach (var a in airports) if (a.Id == id) return a.Label;
            return "?";
        }

        // Demo of a change: a flight that has not left yet leaves 15 minutes late (the view just gets new times).
        void DelayOneFlight()
        {
            for (int tries = 0; tries < 50 && flights.Count > 0; tries++)
            {
                int i = rng.Next(flights.Count);
                var f = flights[i];
                if (f.DepartUtc > utc && f.DepartUtc < utc + 3.0 * 3600.0)
                {
                    flights[i] = new FlightView(f.Id, f.RouteId, f.DepartUtc + 900.0, f.ArriveUtc + 900.0, f.Backwards, f.Kind, f.Color);
                    FlightsVersion++;
                    foreach (var r in routes)
                        if (r.Id == f.RouteId) { Notice?.Invoke(HudToastKind.Warning, $"Delay {AirportLabel(r.FromId)} – {AirportLabel(r.ToId)}: +15 min"); break; }
                    return;
                }
            }
        }
    }
}
