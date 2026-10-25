using System;
using Tycoon.GlobeView.Contracts;

namespace Tycoon.Hud
{
    public enum HudToastKind { Info, Success, Warning, Error }

    /// <summary>
    /// What the HUD needs from the game besides the globe's <see cref="IGlobeModel"/>: clock and money, the speed control, texts for an
    /// airport and the few commands the HUD gives. The game's controller implements it next to <see cref="IGlobeModel"/>.
    /// </summary>
    public interface IHudModel
    {
        GameTimeState Time { get; }
        double Cash { get; }
        double CashPerDay { get; }

        /// <summary>Game seconds per real second; 0 pauses.</summary>
        void SetRate(double rate);

        string AirportName(int airportId);
        void AirportStats(int airportId, out int routes, out int flightsPerDay, out float load);

        void ClearSelection();
        void SelectRoute(int routeId);

        event Action<HudToastKind, string> Notice;
    }
}
