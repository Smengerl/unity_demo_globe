using System;
using System.Collections.Generic;
using Tycoon.GlobeView.Contracts;
using Unity.Collections;
using UnityEngine;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// The detail the globe shows from the tile pyramid: a block of tiles around the camera put together into one texture per layer
    /// (day, night, relief, coast), which the Earth shader lays over the global base textures inside the block's rectangle.
    /// Where a tile is missing the next coarser one is stretched in; where nothing exists the pixel stays transparent and the base
    /// shows through (the shader weighs by alpha). A new block is built next to the old one and swapped in when it is complete.
    /// </summary>
    internal sealed class TileWindow
    {
        sealed class Layer
        {
            public string Name, Property;
            public int Min, Max;
            public bool Linear;
            public uint Neutral;            // colour of pixels without data (alpha 0)
            public Texture2D Shown, Building;
            public int Loaded;
        }

        sealed class Job
        {
            public Layer Layer;
            public int I, J;                // cell in the block
            public int Level, X, Y;         // tile asked for at the moment (the target, or a coarser ancestor)
            public TileRequest Request;
        }

        static readonly Dictionary<string, (string property, bool linear, uint neutral)> Known = new Dictionary<string, (string, bool, uint)>
        {
            { "day", ("_DetailTex", false, 0x00000000u) },
            { "night", ("_DetailNightTex", false, 0x00000000u) },
            { "relief", ("_DetailNormalTex", true, 0x00FF8080u) },        // flat normal, no data
            { "coast", ("_DetailCoastTex", true, 0x00FFFFFFu) },
        };

        readonly TilesConfig config;
        readonly TileSource source;
        readonly int tileSize;
        readonly List<Layer> layers = new List<Layer>();
        readonly Dictionary<string, uint[]> cache = new Dictionary<string, uint[]>();
        readonly Queue<string> cacheOrder = new Queue<string>();
        readonly uint[] row;
        Texture2D scratch;

        // shown block
        int shownLevel = -1, shownX0, shownY0, shownN;
        // block being built
        bool building;
        int bLevel, bX0, bY0, bN;
        List<Job> jobs;
        readonly List<Job> inflight = new List<Job>();
        int nextJob, finished;
        float buildStart;

        /// <summary>Rectangle of the shown block (lonMin, latMin, lonMax, latMax).</summary>
        public Vector4 Rect { get; private set; }
        /// <summary>Changes whenever a new block was swapped in or the block was released.</summary>
        public int Version { get; private set; }
        public bool Available => source.Available;
        public bool HasData(string layer) { var l = Find(layer); return l != null && l.Shown != null; }
        public Texture2D Texture(string layer) => Find(layer)?.Shown;
        public bool Building => building;
        /// <summary>Tests only: keep the textures readable so pixels can be checked.</summary>
        internal bool KeepReadable;

        public TileWindow(TilesConfig config)
        {
            this.config = config;
            source = new TileSource(config);
            tileSize = config.TileSize;
            row = new uint[tileSize];
        }

        Layer Find(string name)
        {
            foreach (var l in layers) if (l.Name == name) return l;
            return null;
        }

        /// <summary>Which layers to keep (by name); layers that are no longer wanted are released.</summary>
        public void SetLayers(IReadOnlyList<string> names)
        {
            bool same = names.Count == layers.Count;
            for (int i = 0; same && i < names.Count; i++) same = layers[i].Name == names[i];
            if (same) return;
            AbortBuild();
            foreach (var l in layers) DestroyTex(ref l.Shown);
            layers.Clear();
            foreach (var n in names)
            {
                if (!Known.TryGetValue(n, out var k)) continue;
                var cfg = Array.Find(config.Layers, x => x.Name == n);
                if (cfg == null) continue;
                layers.Add(new Layer { Name = n, Property = k.property, Linear = k.linear, Neutral = k.neutral, Min = cfg.MinLevel, Max = cfg.MaxLevel });
            }
            shownLevel = -1;
            Version++;
        }

        public string PropertyOf(string layer) => Find(layer)?.Property;

        /// <summary>Makes sure a block for this level and position is shown or on its way.</summary>
        public void Want(int level, float lat, float lon, int tilesPerSide, bool sync)
        {
            if (layers.Count == 0) return;
            var (x0, y0, n) = TileMath.Place(level, lat, lon, tilesPerSide);
            bool shownOk = shownLevel == level && shownN == n && TileMath.IsCentred(level, lat, lon, shownX0, shownY0, shownN);
            if (building)
            {
                bool buildOk = bLevel == level && bN == n && TileMath.IsCentred(level, lat, lon, bX0, bY0, bN);
                if (buildOk) { if (sync) FinishSync(); return; }
            }
            else if (shownOk) return;
            StartBuild(level, x0, y0, n);
            if (sync) FinishSync();
        }

        public void Tick(float now)
        {
            if (!building) return;
            Pump(config.TilesPerFrame, false);
            if (finished >= jobs.Count || now - buildStart > 8f) Complete();
        }

        public void Release()
        {
            AbortBuild();
            foreach (var l in layers) DestroyTex(ref l.Shown);
            shownLevel = -1;
            cache.Clear(); cacheOrder.Clear();
            if (scratch != null) { Discard(scratch); scratch = null; }
            Version++;
        }

        // ---- building ----

        void StartBuild(int level, int x0, int y0, int n)
        {
            AbortBuild();
            building = true;
            bLevel = level; bX0 = x0; bY0 = y0; bN = n;
            buildStart = Time.unscaledTime;
            int w = n * tileSize;
            foreach (var l in layers)
            {
                var tex = new Texture2D(w, w, TextureFormat.RGBA32, true, l.Linear)
                {
                    wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Trilinear, anisoLevel = 4, hideFlags = HideFlags.HideAndDontSave,
                };
                var raw = tex.GetRawTextureData<uint>();
                for (int i = 0; i < w * w; i++) raw[i] = l.Neutral;
                l.Building = tex;
                l.Loaded = 0;
            }
            // cells nearest to the middle first
            jobs = new List<Job>();
            float mid = (n - 1) * 0.5f;
            var cells = new List<Vector2Int>();
            for (int j = 0; j < n; j++) for (int i = 0; i < n; i++) cells.Add(new Vector2Int(i, j));
            cells.Sort((a, b) => ((a.x - mid) * (a.x - mid) + (a.y - mid) * (a.y - mid)).CompareTo((b.x - mid) * (b.x - mid) + (b.y - mid) * (b.y - mid)));
            foreach (var c in cells)
                foreach (var l in layers)
                {
                    int lvl = Mathf.Min(level, l.Max);
                    if (lvl < l.Min) continue;                      // nothing in this layer at such a coarse level
                    int s = level - lvl;
                    jobs.Add(new Job { Layer = l, I = c.x, J = c.y, Level = lvl, X = (x0 + c.x) >> s, Y = (y0 + c.y) >> s });
                }
            nextJob = finished = 0;
        }

        void AbortBuild()
        {
            if (!building) return;
            foreach (var j in inflight) j.Request?.Dispose();
            inflight.Clear();
            foreach (var l in layers) DestroyTex(ref l.Building);
            building = false;
        }

        void FinishSync()
        {
            Pump(int.MaxValue, true);
            Complete();
        }

        // issue requests, then decode what has arrived (at most `budget` tiles)
        void Pump(int budget, bool sync)
        {
            if (sync)
            {
                foreach (var job in jobs)
                {
                    while (!TryFromCache(job))
                    {
                        var data = source.ReadSync(job.Layer.Name, job.Level, job.X, job.Y);
                        if (data != null && Place(job, data)) break;
                        if (!Coarser(job)) break;
                    }
                    finished++;
                }
                nextJob = jobs.Count;
                return;
            }

            while (inflight.Count < 12 && nextJob < jobs.Count)
            {
                var job = jobs[nextJob++];
                if (TryFromCache(job)) finished++; else Issue(job);
            }
            for (int k = 0; k < inflight.Count && budget > 0; k++)
            {
                var job = inflight[k];
                if (!job.Request.IsDone) continue;
                byte[] data = job.Request.Data;
                job.Request.Dispose();
                job.Request = null;
                inflight.RemoveAt(k--);
                if (data != null)
                {
                    budget--;
                    if (Place(job, data)) { finished++; continue; }
                }
                if (Coarser(job)) { if (TryFromCache(job)) finished++; else Issue(job); }
                else finished++;                                    // nothing there: the pixels stay transparent
            }
        }

        void Issue(Job job)
        {
            job.Request = source.Request(job.Layer.Name, job.Level, job.X, job.Y);
            inflight.Add(job);
        }

        // the tile was missing: ask for its parent; false when there is none
        bool Coarser(Job job)
        {
            if (job.Level - 1 < job.Layer.Min) return false;
            job.Level--;
            job.X >>= 1;
            job.Y >>= 1;
            return true;
        }

        bool TryFromCache(Job job)
        {
            if (!cache.TryGetValue(Key(job.Layer, job.Level, job.X, job.Y), out var px)) return false;
            Paste(job, px);
            return true;
        }

        static string Key(Layer l, int level, int x, int y) => l.Name + "/" + level + "/" + x + "_" + y;

        bool Place(Job job, byte[] data)
        {
            var px = Decode(data);
            if (px == null) return false;
            string key = Key(job.Layer, job.Level, job.X, job.Y);
            if (!cache.ContainsKey(key))
            {
                cache[key] = px;
                cacheOrder.Enqueue(key);
                while (cacheOrder.Count > 24) cache.Remove(cacheOrder.Dequeue());
            }
            Paste(job, px);
            return true;
        }

        uint[] Decode(byte[] data)
        {
            if (scratch == null) scratch = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };
            if (!ImageConversion.LoadImage(scratch, data, false) || scratch.width != tileSize || scratch.height != tileSize) return null;
            var px = new uint[tileSize * tileSize];
            if (scratch.format == TextureFormat.RGBA32)
            {
                scratch.GetRawTextureData<uint>().CopyTo(px);
            }
            else if (scratch.format == TextureFormat.RGB24)
            {
                var raw = scratch.GetRawTextureData<byte>();
                for (int i = 0; i < px.Length; i++) px[i] = raw[i * 3] | ((uint)raw[i * 3 + 1] << 8) | ((uint)raw[i * 3 + 2] << 16) | 0xFF000000u;
            }
            else
            {
                var c = scratch.GetPixels32();
                for (int i = 0; i < px.Length; i++) px[i] = c[i].r | ((uint)c[i].g << 8) | ((uint)c[i].b << 16) | ((uint)c[i].a << 24);
            }
            return px;
        }

        // copies a tile (or the part of a coarser tile that covers the cell) into the block; rows run bottom to top in both
        void Paste(Job job, uint[] px)
        {
            var l = job.Layer;
            int w = bN * tileSize, S = tileSize;
            var win = l.Building.GetRawTextureData<uint>();
            int dstX = job.I * S, dstYBottom = (bN - 1 - job.J) * S;
            int shift = bLevel - job.Level;
            if (shift == 0)
            {
                for (int r = 0; r < S; r++) NativeArray<uint>.Copy(px, r * S, win, (dstYBottom + r) * w + dstX, S);
            }
            else
            {
                int scale = 1 << shift;
                if (scale > S) return;
                int sub = S / scale;
                int cellX = bX0 + job.I, cellY = bY0 + job.J;
                int sx = (cellX % scale) * sub, syTop = (cellY % scale) * sub;
                for (int r = 0; r < S; r++)
                {
                    int tSrc = syTop + (S - 1 - r) * sub / S;
                    int srcRow = S - 1 - tSrc;
                    for (int c = 0; c < S; c++) row[c] = px[srcRow * S + sx + c * sub / S];
                    NativeArray<uint>.Copy(row, 0, win, (dstYBottom + r) * w + dstX, S);
                }
            }
            l.Loaded++;
        }

        void Complete()
        {
            foreach (var j in inflight) j.Request?.Dispose();
            inflight.Clear();
            foreach (var l in layers)
            {
                DestroyTex(ref l.Shown);
                if (l.Loaded > 0) { l.Building.Apply(true, !KeepReadable); l.Shown = l.Building; }
                else Discard(l.Building);
                l.Building = null;
            }
            building = false;
            shownLevel = bLevel; shownX0 = bX0; shownY0 = bY0; shownN = bN;
            Rect = TileMath.Bounds(bLevel, bX0, bY0, bN, bN);
            Version++;
        }

        static void DestroyTex(ref Texture2D t)
        {
            if (t != null) Discard(t);
            t = null;
        }

        static void Discard(UnityEngine.Object o) => GlobeViewHost.Discard(o);
    }
}
