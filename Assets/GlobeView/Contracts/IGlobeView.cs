using System;
using UnityEngine;

namespace Tycoon.GlobeView.Contracts
{
    /// <summary>Quality ceiling of the device. The zoom stages then decide what is on at the current zoom.</summary>
    public enum QualityTier { Auto, Low, Medium, High }

    /// <summary>What the game (controller, UI) can ask of the view and hear from it.</summary>
    public interface IGlobeView
    {
        // ---- events ----
        /// <summary>Basic textures are loaded, the first frame is rendered.</summary>
        event Action Ready;
        /// <summary>The player tapped an airport (picked with a generous radius, nearest wins).</summary>
        event Action<int> AirportTapped;
        /// <summary>The player tapped the globe where no airport is; lat, lon in degrees.</summary>
        event Action<float, float> EmptyTapped;
        /// <summary>The camera moved; raised at most ~15 times per second.</summary>
        event Action<CameraState> CameraChanged;
        /// <summary>The effective detail stage changed (0 far .. 4 close).</summary>
        event Action<int> ZoomStageChanged;

        // ---- state ----
        int ZoomStage { get; }
        CameraState GetCameraState();
        void SetCameraState(CameraState state);

        // ---- commands ----
        void FocusOn(float lat, float lon, float height, float seconds);
        void FocusAirport(int airportId, float height = 0.3f, float seconds = 1.2f);
        /// <summary>Turns and zooms so that both ends of the route are in view.</summary>
        void FrameRoute(int routeId, float seconds = 1.2f);
        void SetZoomLimits(float minHeight, float maxHeight);
        void SetInputEnabled(bool rotate, bool zoom, bool tap);
        /// <summary>Moves the globe's centre away from the screen centre when panels cover part of the screen (pixels).</summary>
        void SetViewportInsets(float top, float bottom, float left, float right);
        void SetQualityTier(QualityTier tier);
        /// <summary>Stops updating and rendering (menus on top, app in the background).</summary>
        void SetActive(bool active);
        /// <summary>The UI says whether a screen position belongs to it; the view then ignores gestures that start there.</summary>
        void SetPointerBlocker(Func<Vector2, bool> isOverUi);

        // ---- queries ----
        /// <summary>Screen position (pixels) of an airport, for labels and tooltips of the game UI. Visible = on the near side of the globe and on screen.</summary>
        bool TryGetAirportScreenPoint(int airportId, out Vector2 screenPx, out bool visible);
    }
}
