using System.IO;
using System.Threading.Tasks;
using Tycoon.GlobeView.Contracts;
using UnityEngine;
using UnityEngine.Networking;

namespace Tycoon.GlobeView
{
    /// <summary>A tile file being read; <see cref="Data"/> is null if the tile does not exist.</summary>
    internal abstract class TileRequest
    {
        public abstract bool IsDone { get; }
        public abstract byte[] Data { get; }
        public virtual void Dispose() { }
    }

    /// <summary>
    /// Reads tile files: from a folder (read on a worker thread) or from an http(s) / jar address (UnityWebRequest).
    /// Where they are comes from <see cref="TilesConfig.BaseUrl"/>; empty means EarthTiles next to the project (editor, desktop)
    /// or StreamingAssets/earth-tiles.
    /// </summary>
    internal sealed class TileSource
    {
        readonly string baseUrl;
        readonly bool isUrl;
        readonly TilesConfig config;

        public string Base => baseUrl;

        public TileSource(TilesConfig config)
        {
            this.config = config;
            baseUrl = Resolve(config);
            isUrl = baseUrl.Contains("://");
        }

        static string Resolve(TilesConfig t)
        {
            if (!string.IsNullOrEmpty(t.BaseUrl)) return t.BaseUrl.TrimEnd('/', '\\');
#if UNITY_EDITOR || UNITY_STANDALONE
            var dev = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "EarthTiles"));
            if (Directory.Exists(dev)) return dev;
#endif
            return Path.Combine(Application.streamingAssetsPath, "earth-tiles");
        }

        public string PathOf(string layer, int level, int x, int y)
        {
            var format = "jpg";
            foreach (var l in config.Layers) if (l.Name == layer) format = l.Format;
            return $"{baseUrl}/{layer}/{level}/{x}_{y}.{format}";
        }

        /// <summary>True if the folder with the tiles exists (always true for addresses; they are only found out by asking).</summary>
        public bool Available => isUrl || Directory.Exists(baseUrl);

        public TileRequest Request(string layer, int level, int x, int y)
        {
            string path = PathOf(layer, level, x, y);
            if (isUrl) return new WebTileRequest(path);
            return new FileTileRequest(path);
        }

        /// <summary>Reads a tile at once (screenshot tool, tests); only from folders.</summary>
        public byte[] ReadSync(string layer, int level, int x, int y)
        {
            if (isUrl) return null;
            string path = PathOf(layer, level, x, y);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        sealed class FileTileRequest : TileRequest
        {
            readonly Task<byte[]> task;
            public FileTileRequest(string path) { task = Task.Run(() => File.Exists(path) ? File.ReadAllBytes(path) : null); }
            public override bool IsDone => task.IsCompleted;
            public override byte[] Data => task.IsFaulted ? null : task.Result;
        }

        sealed class WebTileRequest : TileRequest
        {
            readonly UnityWebRequest request;
            readonly UnityWebRequestAsyncOperation op;
            public WebTileRequest(string url)
            {
                request = UnityWebRequest.Get(url);
                op = request.SendWebRequest();
            }
            public override bool IsDone => op.isDone;
            public override byte[] Data => request.result == UnityWebRequest.Result.Success ? request.downloadHandler.data : null;
            public override void Dispose() => request.Dispose();
        }
    }
}
