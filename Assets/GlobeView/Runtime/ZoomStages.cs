namespace Tycoon.GlobeView
{
    /// <summary>
    /// Five zoom stages by camera height: Z0 space, Z1 globe, Z2 continent, Z3 region, Z4 close.
    /// <paramref name="edges"/> are the four heights where Z0|Z1, Z1|Z2, Z2|Z3 and Z3|Z4 meet.
    /// </summary>
    public static class ZoomStages
    {
        public const int Count = 5;

        /// <summary>Next stage for the given height; a stage only changes after passing its edge by the hysteresis share (no flicker).</summary>
        public static int Next(int current, float height, float[] edges, float hysteresis)
        {
            int s = current;
            while (s < Count - 1 && height < edges[s] * (1f - hysteresis)) s++;
            while (s > 0 && height > edges[s - 1] * (1f + hysteresis)) s--;
            return s;
        }
    }
}
