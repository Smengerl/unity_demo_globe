using System.Collections.Generic;
using Tycoon.GlobeView.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tycoon.GlobeView
{
    /// <summary>Airport markers on the globe: pooled, colour and size from the model, thinned out by importance and zoom stage.</summary>
    internal sealed class AirportLayer
    {
        class Marker
        {
            public Transform T;
            public MeshRenderer R;
            public bool Visible = true;
            public bool Selected;
        }

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly Color SelectedColor = new Color(1f, 0.95f, 0.2f, 1f);
        static readonly float[] SizeFactor = { 0.8f, 1f, 1.3f };

        readonly Transform root;
        readonly Mesh sphere;
        readonly Material markerMat, outlineMat;
        readonly List<Marker> markers = new List<Marker>();
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();

        public AirportLayer(Transform root, Mesh sphere, Material flat)
        {
            this.root = root;
            this.sphere = sphere;
            // dark halo behind every marker keeps it readable on bright ground; drawn first (lower queue)
            outlineMat = new Material(flat) { color = new Color(0.04f, 0.06f, 0.12f, 0.75f), renderQueue = 3000 };
            markerMat = new Material(flat) { renderQueue = 3001 };
        }

        public void OnDataChanged(GlobeData d)
        {
            while (markers.Count < d.AirportCount) markers.Add(CreateMarker());
            for (int i = 0; i < markers.Count; i++)
            {
                bool used = i < d.AirportCount;
                markers[i].T.gameObject.SetActive(used);
                if (!used) continue;
                markers[i].T.position = d.AirportDir[i] * RouteMath.SurfaceHeight;
                markers[i].Visible = true;
                markers[i].Selected = false;        // colour is re-applied in Tick
                SetColor(markers[i], BaseColor(d.Airports[i]));
            }
        }

        Marker CreateMarker()
        {
            var go = new GameObject("Airport");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = sphere;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = markerMat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            var ring = new GameObject("Outline");
            ring.transform.SetParent(go.transform, false);
            ring.transform.localScale = Vector3.one * 1.5f;
            ring.AddComponent<MeshFilter>().sharedMesh = sphere;
            var rr = ring.AddComponent<MeshRenderer>();
            rr.sharedMaterial = outlineMat;
            rr.shadowCastingMode = ShadowCastingMode.Off;
            rr.receiveShadows = false;
            return new Marker { T = go.transform, R = r };
        }

        static Color BaseColor(in AirportView a)
        {
            Color c = a.Color;
            switch (a.Kind)
            {
                case AirportKind.Competitor: c.a = 0.8f; break;
                case AirportKind.Available: c.a = 0.6f; break;
                case AirportKind.Closed: c.a = 0.35f; break;
            }
            return c;
        }

        void SetColor(Marker m, Color c)
        {
            block.SetColor(ColorId, c);
            m.R.SetPropertyBlock(block);
        }

        public void Tick(GlobeData d, float height, int stage, int selectedAirportId)
        {
            float scale = Mathf.Clamp(height * 0.006f, 0.0007f, 0.012f);
            int minImportance = stage == 0 ? 2 : stage == 1 ? 1 : 0;     // thin out when the globe is small
            for (int i = 0; i < d.AirportCount; i++)
            {
                var m = markers[i];
                bool selected = d.Airports[i].Id == selectedAirportId;
                bool visible = selected || d.Airports[i].Importance >= minImportance;
                if (visible != m.Visible) { m.Visible = visible; m.T.gameObject.SetActive(visible); }
                if (!visible) continue;
                if (selected != m.Selected) { m.Selected = selected; SetColor(m, selected ? SelectedColor : BaseColor(d.Airports[i])); }
                float size = SizeFactor[Mathf.Min(d.Airports[i].Size, (byte)2)];
                m.T.localScale = Vector3.one * (scale * size * (selected ? 1.6f : 1f));
            }
        }

        /// <summary>The nearest visible airport within <paramref name="radiusPx"/> of a screen position (near side of the globe only).</summary>
        public bool TryPick(GlobeData d, Camera cam, Vector2 screenPx, float radiusPx, float height, out int airportId)
        {
            airportId = -1;
            float best = radiusPx;
            Vector3 camDir = cam.transform.position.normalized;
            float horizon = GeoMath.HorizonDot(height) + 0.02f;
            for (int i = 0; i < d.AirportCount; i++)
            {
                if (!markers[i].Visible || Vector3.Dot(d.AirportDir[i], camDir) < horizon) continue;
                Vector3 sp = cam.WorldToScreenPoint(d.AirportDir[i] * RouteMath.SurfaceHeight);
                if (sp.z <= 0f) continue;
                float dist = Vector2.Distance(new Vector2(sp.x, sp.y), screenPx);
                if (dist < best) { best = dist; airportId = d.Airports[i].Id; }
            }
            return airportId >= 0;
        }

        public bool TryScreenPoint(GlobeData d, Camera cam, float height, int airportId, out Vector2 screenPx, out bool visible)
        {
            screenPx = default;
            visible = false;
            if (!d.TryAirportIndex(airportId, out int i)) return false;
            Vector3 sp = cam.WorldToScreenPoint(d.AirportDir[i] * RouteMath.SurfaceHeight);
            screenPx = new Vector2(sp.x, sp.y);
            bool nearSide = Vector3.Dot(d.AirportDir[i], cam.transform.position.normalized) >= GeoMath.HorizonDot(height) + 0.02f;
            visible = sp.z > 0f && nearSide && sp.x >= 0f && sp.x <= Screen.width && sp.y >= 0f && sp.y <= Screen.height;
            return true;
        }
    }
}
