using System.Collections.Generic;
using Tycoon.GlobeView.Contracts;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// Planes, pooled. Each flight is a timetable entry: its position follows from the game time, so nothing is sent per frame.
    /// At high game speed single planes would only flicker, so above <see cref="GlobeViewConfig.FlowRate"/> the layer shows a
    /// traffic flow instead: as many planes per route as flights are in the air, gliding along at a calm fixed pace.
    /// </summary>
    internal sealed class FlightLayer
    {
        const float FlowPace = 0.28f;                   // route lengths per real second in flow mode
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly float[] KindScale = { 0.8f, 1f, 1.3f };

        class Plane
        {
            public Transform T;
            public MeshRenderer R;
            public int ColorKey;
        }

        readonly Transform root;
        readonly Mesh planeMesh;
        readonly Material planeMat, outlineMat;
        readonly List<Plane> pool = new List<Plane>();
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly GlobeViewConfig config;

        int[] shownOnRoute = new int[0];
        int[] activeOnRoute = new int[0];
        int used;                                       // planes switched on last frame

        public FlightLayer(Transform root, Mesh planeMesh, Material flat, GlobeViewConfig config)
        {
            this.root = root;
            this.planeMesh = planeMesh;
            this.config = config;
            outlineMat = new Material(flat) { color = new Color(0.04f, 0.06f, 0.12f, 0.45f), renderQueue = 3000 };
            planeMat = new Material(flat) { renderQueue = 3002 };
        }

        Plane Acquire(int index)
        {
            while (pool.Count <= index)
            {
                var go = new GameObject("Plane");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = planeMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = planeMat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                var outline = new GameObject("Outline");
                outline.transform.SetParent(go.transform, false);
                outline.transform.localScale = Vector3.one * 1.4f;
                outline.AddComponent<MeshFilter>().sharedMesh = planeMesh;
                var or = outline.AddComponent<MeshRenderer>();
                or.sharedMaterial = outlineMat;
                or.shadowCastingMode = ShadowCastingMode.Off;
                or.receiveShadows = false;
                go.SetActive(false);
                pool.Add(new Plane { T = go.transform, R = r, ColorKey = -1 });
            }
            var p = pool[index];
            if (!p.T.gameObject.activeSelf) p.T.gameObject.SetActive(true);
            return p;
        }

        public void Tick(GlobeData d, bool[] routeVisible, GameTimeState time, float realTime, float height, int stage)
        {
            if (shownOnRoute.Length < d.RouteCount) { shownOnRoute = new int[d.RouteCount]; activeOnRoute = new int[d.RouteCount]; }
            System.Array.Clear(shownOnRoute, 0, d.RouteCount);
            System.Array.Clear(activeOnRoute, 0, d.RouteCount);

            int maxPerRoute = stage <= 1 ? 1 : stage == 2 ? 2 : 4;
            float scale = Mathf.Clamp(height * 0.012f, 0.0015f, 0.025f);
            bool flow = time.Rate >= config.FlowRate;
            int count = 0;

            if (!flow)
            {
                for (int i = 0; i < d.FlightCount; i++)
                {
                    int r = d.FlightRoute[i];
                    if (r < 0 || !routeVisible[r] || shownOnRoute[r] >= maxPerRoute) continue;
                    var f = d.Flights[i];
                    float t = RouteMath.Progress(time.UtcSeconds, f.DepartUtc, f.ArriveUtc);
                    if (t < 0f || t >= 1f) continue;
                    shownOnRoute[r]++;
                    Place(Acquire(count++), d.RouteGeoms[r], f.Backwards ? 1f - t : t, f.Backwards, scale * KindScale[Mathf.Min(f.Kind, (byte)2)], f.Color);
                }
            }
            else
            {
                for (int i = 0; i < d.FlightCount; i++)
                {
                    int r = d.FlightRoute[i];
                    if (r < 0 || !routeVisible[r]) continue;
                    var f = d.Flights[i];
                    if (time.UtcSeconds >= f.DepartUtc && time.UtcSeconds < f.ArriveUtc) activeOnRoute[r]++;
                }
                for (int r = 0; r < d.RouteCount; r++)
                {
                    int n = Mathf.Min(activeOnRoute[r], maxPerRoute);
                    for (int k = 0; k < n; k++)
                    {
                        float phase = Mathf.Repeat(realTime * FlowPace + k / (float)n + r * 0.137f, 1f);
                        bool back = (k & 1) == 1;
                        Place(Acquire(count++), d.RouteGeoms[r], back ? 1f - phase : phase, back, scale, d.Routes[r].Color);
                    }
                }
            }

            for (int k = count; k < used; k++) pool[k].T.gameObject.SetActive(false);
            used = count;
        }

        void Place(Plane p, in GlobeData.RouteGeom g, float t, bool backwards, float scale, Color32 color)
        {
            Vector3 pos = RouteMath.Position(g.A, g.B, g.AngleRad, t);
            Vector3 ahead = RouteMath.Position(g.A, g.B, g.AngleRad, Mathf.Min(t + 0.01f, 1f)) - RouteMath.Position(g.A, g.B, g.AngleRad, Mathf.Max(t - 0.01f, 0f));
            if (backwards) ahead = -ahead;
            p.T.position = pos;
            if (ahead.sqrMagnitude > 1e-14f) p.T.rotation = Quaternion.LookRotation(ahead, pos.normalized);
            p.T.localScale = Vector3.one * scale;

            int key = color.r | color.g << 8 | color.b << 16;
            if (key != p.ColorKey)
            {
                p.ColorKey = key;
                block.SetColor(ColorId, new Color32(color.r, color.g, color.b, 255));
                p.R.SetPropertyBlock(block);
            }
        }
    }
}
