using UnityEngine;

namespace Tycoon.GlobeView.Contracts
{
    // Everything the game tells the view. IDs are the game's own and stay stable; the view keeps no game state.

    /// <summary>Game time. The game owns the clock; the view only displays it.</summary>
    public readonly struct GameTimeState
    {
        /// <summary>Game time as Unix seconds (UTC). Decides sun, moon, stars, day and night.</summary>
        public readonly double UtcSeconds;
        /// <summary>Game seconds per real second, 0 = paused. The view uses it to calm down day/night and to turn single planes into traffic flow at high speed.</summary>
        public readonly double Rate;

        public GameTimeState(double utcSeconds, double rate) { UtcSeconds = utcSeconds; Rate = rate; }
    }

    public enum AirportKind : byte { Own, Competitor, Available, Closed }
    public enum RouteStatus : byte { Active, Planned, Paused }

    public readonly struct AirportView
    {
        public readonly int Id;
        public readonly string Label;
        public readonly float Lat, Lon;
        /// <summary>0..3. Decides from which zoom stage the marker is shown (3 = always, 0 = only when close).</summary>
        public readonly byte Importance;
        public readonly AirportKind Kind;
        public readonly Color32 Color;
        /// <summary>Marker size class 0..2.</summary>
        public readonly byte Size;

        public AirportView(int id, string label, float lat, float lon, byte importance, AirportKind kind, Color32 color, byte size = 1)
        {
            Id = id; Label = label; Lat = lat; Lon = lon; Importance = importance; Kind = kind; Color = color; Size = size;
        }
    }

    public readonly struct RouteView
    {
        public readonly int Id, FromId, ToId;
        /// <summary>0 normal, 1 main route (also shown when zoomed far out).</summary>
        public readonly byte Importance;
        public readonly Color32 Color;
        /// <summary>Line width factor, 1 = standard.</summary>
        public readonly float Width;
        public readonly RouteStatus Status;

        public RouteView(int id, int fromId, int toId, byte importance, Color32 color, float width = 1f, RouteStatus status = RouteStatus.Active)
        {
            Id = id; FromId = fromId; ToId = toId; Importance = importance; Color = color; Width = width; Status = status;
        }
    }

    /// <summary>
    /// One flight as a timetable entry. The view computes the position from the game time, so pause, speed changes, time jumps
    /// and loading a saved game need no special handling. A delay is a new entry with the same Id and changed times
    /// (the game raises <see cref="IGlobeModel.FlightsVersion"/>).
    /// </summary>
    public readonly struct FlightView
    {
        public readonly int Id, RouteId;
        public readonly double DepartUtc, ArriveUtc;
        /// <summary>Flies from the route's second airport to its first.</summary>
        public readonly bool Backwards;
        /// <summary>Aircraft class 0..2 (size).</summary>
        public readonly byte Kind;
        public readonly Color32 Color;

        public FlightView(int id, int routeId, double departUtc, double arriveUtc, bool backwards, byte kind, Color32 color)
        {
            Id = id; RouteId = routeId; DepartUtc = departUtc; ArriveUtc = arriveUtc; Backwards = backwards; Kind = kind; Color = color;
        }
    }

    /// <summary>What the player has selected; -1 = nothing. The view highlights it and dims the rest.</summary>
    public readonly struct SelectionState
    {
        public readonly int AirportId, RouteId;

        public SelectionState(int airportId, int routeId) { AirportId = airportId; RouteId = routeId; }
        public static SelectionState None => new SelectionState(-1, -1);
    }

    public readonly struct CameraState
    {
        public readonly float Lat, Lon;
        /// <summary>Distance above the surface in Earth radii.</summary>
        public readonly float Height;

        public CameraState(float lat, float lon, float height) { Lat = lat; Lon = lon; Height = height; }
    }
}
