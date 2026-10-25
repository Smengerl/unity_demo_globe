using UnityEngine;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// Geometry of the tile pyramid (plate carree): level L has 2^(L+1) x 2^L tiles, tile (0, 0) lies at the north-west corner.
    /// </summary>
    public static class TileMath
    {
        public static int Cols(int level) => 2 << level;
        public static int Rows(int level) => 1 << level;

        /// <summary>Pixels per degree of longitude at a level.</summary>
        public static float PixelsPerDegree(int level, int tileSize) => tileSize * Cols(level) / 360f;

        public static Vector2Int TileOf(float lat, float lon, int level)
        {
            int cols = Cols(level), rows = Rows(level);
            int x = Mathf.Clamp(Mathf.FloorToInt((lon + 180f) / 360f * cols), 0, cols - 1);
            int y = Mathf.Clamp(Mathf.FloorToInt((90f - lat) / 180f * rows), 0, rows - 1);
            return new Vector2Int(x, y);
        }

        /// <summary>Bounds of a block of tiles as (lonMin, latMin, lonMax, latMax) in degrees.</summary>
        public static Vector4 Bounds(int level, int x, int y, int countX, int countY)
        {
            float w = 360f / Cols(level), h = 180f / Rows(level);
            return new Vector4(-180f + x * w, 90f - (y + countY) * h, -180f + (x + countX) * w, 90f - y * h);
        }

        /// <summary>
        /// The block of <paramref name="tilesPerSide"/> tiles (fewer if the world is smaller at that level) centred on a position,
        /// shifted to stay inside the world: x0, y0 of its first tile and its side length in tiles.
        /// </summary>
        public static (int x0, int y0, int n) Place(int level, float lat, float lon, int tilesPerSide)
        {
            int n = Mathf.Clamp(tilesPerSide, 1, Rows(level));
            var c = TileOf(lat, lon, level);
            int x0 = Mathf.Clamp(c.x - n / 2, 0, Cols(level) - n);
            int y0 = Mathf.Clamp(c.y - n / 2, 0, Rows(level) - n);
            return (x0, y0, n);
        }

        /// <summary>True while the position is still in the middle half of the block, so the block need not move yet.</summary>
        public static bool IsCentred(int level, float lat, float lon, int x0, int y0, int n)
        {
            var c = TileOf(lat, lon, level);
            float mid = (n - 1) * 0.5f;
            bool atEdgeX = x0 == 0 || x0 + n == Cols(level), atEdgeY = y0 == 0 || y0 + n == Rows(level);
            bool okX = Mathf.Abs(c.x - (x0 + mid)) <= n * 0.25f + 0.5f || atEdgeX && c.x >= x0 && c.x < x0 + n;
            bool okY = Mathf.Abs(c.y - (y0 + mid)) <= n * 0.25f + 0.5f || atEdgeY && c.y >= y0 && c.y < y0 + n;
            return okX && okY;
        }
    }
}
