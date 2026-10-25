using System.Collections.Generic;

namespace Tycoon.GlobeView.Contracts
{
    /// <summary>
    /// Implemented by the game (controller). The view reads it every frame: time and selection directly, the lists only when
    /// their version number changed. Do not hand out lists that are modified while the view reads them (swap in a new list or
    /// copy at the end of the controller's tick).
    /// </summary>
    public interface IGlobeModel
    {
        GameTimeState Time { get; }

        int AirportsVersion { get; }
        IReadOnlyList<AirportView> Airports { get; }

        int RoutesVersion { get; }
        IReadOnlyList<RouteView> Routes { get; }

        int FlightsVersion { get; }
        IReadOnlyList<FlightView> Flights { get; }

        SelectionState Selection { get; }
    }
}
