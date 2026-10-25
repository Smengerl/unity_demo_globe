using System.Collections.Generic;
using Tycoon.GlobeView.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tycoon.GlobeView
{
    /// <summary>Route arcs as lines (pooled). Main routes show from far away, all routes from Z2; routes behind the horizon are switched off.</summary>
    internal sealed class RouteLayer
    {
        const int Steps = 48;
        const float BaseAlpha = 0.55f;

        class Line
        {
            public LineRenderer Main, Outline;
            public bool On;
        }

        readonly Transform root;
        readonly Material lineMat, outlineMat;
        readonly List<Line> lines = new List<Line>();

        /// <summary>Per route index: drawn this frame (the flight layer only shows planes on drawn routes).</summary>
        public bool[] Visible = new bool[0];

        public RouteLayer(Transform root, Material flat)
        {
            this.root = root;
            // dark outline under the lines keeps them readable on bright ground; drawn first (lower queue)
            outlineMat = new Material(flat) { color = new Color(0.04f, 0.06f, 0.12f, 0.45f), renderQueue = 3000 };
            lineMat = new Material(flat) { renderQueue = 3001 };
        }

        public void OnDataChanged(GlobeData d)
        {
            while (lines.Count < d.RouteCount) lines.Add(CreateLine());
            if (Visible.Length < d.RouteCount) Visible = new bool[lines.Count];
            for (int i = 0; i < lines.Count; i++)
            {
                bool used = i < d.RouteCount && d.RouteGeoms[i].FromIndex >= 0;
                lines[i].On = false;
                lines[i].Main.enabled = lines[i].Outline.enabled = false;
                if (!used) continue;
                var g = d.RouteGeoms[i];
                for (int s = 0; s <= Steps; s++)
                {
                    Vector3 p = RouteMath.Position(g.A, g.B, g.AngleRad, s / (float)Steps);
                    lines[i].Main.SetPosition(s, p);
                    lines[i].Outline.SetPosition(s, p);
                }
            }
        }

        Line CreateLine()
        {
            return new Line { Main = CreateRenderer("Route", lineMat), Outline = CreateRenderer("Route outline", outlineMat) };
        }

        LineRenderer CreateRenderer(string name, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            var lr = go.AddComponent<LineRenderer>();
            lr.sharedMaterial = mat;
            lr.useWorldSpace = true;
            lr.positionCount = Steps + 1;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.enabled = false;
            return lr;
        }

        public void Tick(GlobeData d, Vector3 camDir, float height, int stage, SelectionState sel)
        {
            float width = Mathf.Clamp(height * 0.004f, 0.0003f, 0.006f);
            float horizon = GeoMath.HorizonDot(height) - 0.25f;     // arcs are high, so be generous
            for (int i = 0; i < d.RouteCount; i++)
            {
                var g = d.RouteGeoms[i];
                bool show = g.FromIndex >= 0
                    && (stage >= 2 || d.Routes[i].Importance >= 1)
                    && Mathf.Max(Vector3.Dot(g.A, camDir), Mathf.Max(Vector3.Dot(g.B, camDir), Vector3.Dot(g.Mid, camDir))) > horizon;
                Visible[i] = show;
                var line = lines[i];
                if (show != line.On) { line.On = show; line.Main.enabled = line.Outline.enabled = show; }
                if (!show) continue;

                var r = d.Routes[i];
                float alpha = r.Status == RouteStatus.Active ? BaseAlpha : r.Status == RouteStatus.Planned ? 0.3f : 0.18f;
                if (sel.RouteId >= 0) alpha *= r.Id == sel.RouteId ? 1.4f : 0.35f;
                else if (sel.AirportId >= 0) alpha *= (r.FromId == sel.AirportId || r.ToId == sel.AirportId) ? 1.4f : 0.35f;
                Color c = r.Color;
                c.a = Mathf.Min(alpha, 1f);
                line.Main.startColor = line.Main.endColor = c;
                line.Main.widthMultiplier = width * r.Width;
                line.Outline.widthMultiplier = width * r.Width * 2.4f;
                line.Outline.startColor = line.Outline.endColor = new Color(0.04f, 0.06f, 0.12f, 0.45f * c.a / BaseAlpha);
            }
        }
    }
}
