using UnityEngine;

namespace Tycoon.GlobeView
{
    internal static class MeshFactory
    {
        /// <summary>Unit UV-sphere, same lat/lon layout as <see cref="GeoMath.Dir"/>. Normals = positions; uv = (longitude, latitude).</summary>
        public static Mesh Sphere(int segments, int rings)
        {
            var verts = new Vector3[(segments + 1) * (rings + 1)];
            var uvs = new Vector2[verts.Length];
            for (int r = 0; r <= rings; r++)
            {
                float phi = Mathf.PI * r / rings;
                float y = Mathf.Cos(phi), s = Mathf.Sin(phi);
                for (int k = 0; k <= segments; k++)
                {
                    float th = 2f * Mathf.PI * k / segments;
                    verts[r * (segments + 1) + k] = new Vector3(s * Mathf.Sin(th), y, -s * Mathf.Cos(th));
                    // u = longitude / 360 + 0.5, running on past 1 so it stays continuous (textures wrap); v = latitude
                    uvs[r * (segments + 1) + k] = new Vector2(k / (float)segments + 0.5f, 1f - r / (float)rings);
                }
            }

            var tris = new int[segments * rings * 6];
            int t = 0;
            for (int r = 0; r < rings; r++)
            {
                for (int k = 0; k < segments; k++)
                {
                    int a = r * (segments + 1) + k;
                    int b = a + segments + 1;
                    tris[t++] = a; tris[t++] = a + 1; tris[t++] = b;
                    tris[t++] = a + 1; tris[t++] = b + 1; tris[t++] = b;
                }
            }

            var mesh = new Mesh { name = "Sphere" };
            mesh.vertices = verts;
            mesh.normals = verts;
            mesh.uv = uvs;
            mesh.colors32 = White(verts.Length);
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Flat airplane silhouette in the XZ plane, nose at +Z, length ~1.8, wingspan ~1.9.</summary>
        public static Mesh Airplane()
        {
            var outline = new[]
            {
                new Vector2(0f, 1f), new Vector2(0.10f, 0.30f),
                new Vector2(0.95f, -0.15f), new Vector2(0.95f, -0.30f),
                new Vector2(0.10f, -0.12f), new Vector2(0.38f, -0.80f),
                new Vector2(0f, -0.68f), new Vector2(-0.38f, -0.80f),
                new Vector2(-0.10f, -0.12f), new Vector2(-0.95f, -0.30f),
                new Vector2(-0.95f, -0.15f), new Vector2(-0.10f, 0.30f),
            };
            int n = outline.Length;
            var verts = new Vector3[n + 1];
            var normals = new Vector3[n + 1];
            var tris = new int[n * 3];
            for (int i = 0; i < n; i++)
            {
                verts[i + 1] = new Vector3(outline[i].x, 0f, outline[i].y);
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = (i + 1) % n + 1;
            }
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;

            var mesh = new Mesh { name = "Airplane" };
            mesh.vertices = verts;
            mesh.normals = normals;
            mesh.colors32 = White(verts.Length);
            mesh.triangles = tris;
            mesh.RecalculateBounds();
            return mesh;
        }

        static Color32[] White(int n)
        {
            var c = new Color32[n];
            for (int i = 0; i < n; i++) c[i] = new Color32(255, 255, 255, 255);
            return c;
        }
    }
}
