using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// Cheap lens flare for the sun: a few additive sprites in front of the camera, one glow on the sun and ghosts along the
    /// line through the screen centre, all in gold. Faded out when the Earth covers the sun (smoothly at the limb, so the sun can
    /// shine through the atmosphere). Eight sprites, no post-processing.
    /// </summary>
    internal sealed class SunFlare
    {
        class Sprite
        {
            public Transform T;
            public MeshRenderer R;
            public float K, Size, Shape, Alpha, Stretch;
            public Color Colour;
            public bool Ghost;
        }

        const float Depth = 0.5f;   // distance of the sprites in front of the camera (beyond the near plane)
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int ShapeId = Shader.PropertyToID("_Shape");

        readonly List<Sprite> sprites = new List<Sprite>();
        readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        readonly Camera cam;
        readonly Transform sun;
        readonly Transform root;

        public SunFlare(Camera camera, Transform sunTransform, Material material)
        {
            cam = camera;
            sun = sunTransform;
            root = new GameObject("Sun flare").transform;
            root.SetParent(cam.transform, false);

            var mesh = new Mesh { name = "FlareQuad" };
            mesh.vertices = new[] { new Vector3(-.5f, -.5f, 0), new Vector3(.5f, -.5f, 0), new Vector3(-.5f, .5f, 0), new Vector3(.5f, .5f, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            mesh.triangles = new[] { 0, 2, 1, 2, 3, 1 };
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100f);

            //            k      size  shape alpha stretch ghost colour
            Add(mesh, material, 1.00f, 1.60f, 0, 0.18f, 1, false, new Color(1.0f, 0.78f, 0.5f));    // wide warm halo around the sun
            Add(mesh, material, 1.00f, 0.55f, 0, 0.55f, 1, false, new Color(1.0f, 0.92f, 0.75f));   // core glow
            Add(mesh, material, 1.00f, 0.50f, 2, 0.45f, 7, false, new Color(1.0f, 0.85f, 0.6f));    // horizontal streak
            Add(mesh, material, 0.55f, 0.10f, 1, 0.16f, 1, true, new Color(1.0f, 0.72f, 0.38f));    // ghosts, along the line through the centre
            Add(mesh, material, -0.40f, 0.20f, 1, 0.16f, 1, true, new Color(1.0f, 0.8f, 0.5f));
            Add(mesh, material, -0.90f, 0.32f, 1, 0.11f, 1, true, new Color(1.0f, 0.7f, 0.35f));
            Add(mesh, material, -1.55f, 0.26f, 0, 0.12f, 1, true, new Color(0.95f, 0.65f, 0.4f));
            Add(mesh, material, 0.25f, 0.07f, 0, 0.35f, 1, true, new Color(1.0f, 0.88f, 0.62f));
        }

        void Add(Mesh mesh, Material mat, float k, float size, float shape, float alpha, float stretch, bool ghost, Color colour)
        {
            var go = new GameObject("Flare");
            go.transform.SetParent(root, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            sprites.Add(new Sprite { T = go.transform, R = r, K = k, Size = size, Shape = shape, Alpha = alpha, Stretch = stretch, Colour = colour, Ghost = ghost });
        }

        public void Destroy()
        {
            if (root != null) GlobeViewHost.Discard(root.gameObject);
        }

        /// <param name="stage">Effective zoom stage: everything up to Z2, only glow and core at Z3, nothing at Z4 (the sun is never in view that close).</param>
        /// <param name="ghostsAllowed">Ghosts and streak are skipped on weak devices.</param>
        public void Tick(int stage, bool ghostsAllowed)
        {
            Vector3 vp = cam.WorldToViewportPoint(sun.position);
            bool front = vp.z > 0f;

            // Earth in the way? Closest approach of the camera-sun line to the Earth's centre; the atmosphere lets some light through.
            float occlusion = 1f;
            if (front)
            {
                Vector3 c = cam.transform.position;
                Vector3 d = (sun.position - c).normalized;
                float along = -Vector3.Dot(c, d);
                if (along > 0f) occlusion = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.98f, 1.07f, (c + d * along).magnitude));
            }

            var centre = new Vector2(0.5f, 0.5f);
            Vector2 offset = new Vector2(vp.x, vp.y) - centre;
            float edge = Mathf.Clamp01(1.25f - offset.magnitude * 1.1f);         // fades toward the screen edge
            float strength = front ? occlusion * edge : 0f;
            bool ghosts = ghostsAllowed && stage <= 2;
            bool streak = stage <= 2;

            float halfH = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * Depth;
            float halfW = halfH * cam.aspect;
            foreach (var s in sprites)
            {
                bool show = strength > 0.002f && stage < 4 && (!s.Ghost || ghosts) && (s.Shape != 2f || streak);
                s.R.enabled = show;
                if (!show) continue;
                Vector2 v = centre + offset * s.K;
                s.T.localPosition = new Vector3((v.x - 0.5f) * 2f * halfW, (v.y - 0.5f) * 2f * halfH, Depth);
                float h = s.Size * 2f * halfH;
                s.T.localScale = new Vector3(h * s.Stretch, h, 1f);
                Color col = s.Colour;
                col.a = s.Alpha * strength * (s.Ghost ? Mathf.Clamp01(offset.magnitude * 2.5f + 0.2f) : 1f);
                block.SetColor(ColorId, col);
                block.SetFloat(ShapeId, s.Shape);
                s.R.SetPropertyBlock(block);
            }
        }
    }
}
