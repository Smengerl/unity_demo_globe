using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// Compiling a shader variant takes a moment (the editor does it in the background and draws magenta meanwhile). So a keyword set is
    /// only switched on when its variant has been warmed up; until then the old set stays active. Sets of the next stage are warmed
    /// ahead of time. Without graphics (batch mode, tests) and in tools nothing is held back.
    /// </summary>
    internal sealed class ShaderWarmup
    {
        sealed class Entry
        {
            public ShaderVariantCollection Collection;
            public float Started;
            public bool Done;
        }

        const float GiveUpSeconds = 5f;

        readonly Shader shader;
        readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>();
        readonly Queue<Entry> pending = new Queue<Entry>();
        readonly bool enabled;

        public ShaderWarmup(Shader shader)
        {
            this.shader = shader;
            enabled = shader != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
        }

        /// <summary>True when this keyword set can be used without a compile hitch; starts the warm-up if it has not begun.</summary>
        public bool IsWarm(IReadOnlyList<string> keywords, float now)
        {
            if (!enabled) return true;
            var e = Get(keywords, now);
            return e.Done || now - e.Started > GiveUpSeconds;
        }

        /// <summary>Starts warming a set that will be needed soon.</summary>
        public void Prefetch(IReadOnlyList<string> keywords, float now)
        {
            if (enabled) Get(keywords, now);
        }

        /// <summary>One step of the warm-up per frame.</summary>
        public void Tick()
        {
            while (pending.Count > 0 && pending.Peek().Done) pending.Dequeue();
            if (pending.Count == 0) return;
            var e = pending.Peek();
            if (e.Collection.WarmUpProgressively(1) || e.Collection.isWarmedUp) e.Done = true;
        }

        Entry Get(IReadOnlyList<string> keywords, float now)
        {
            var sorted = new List<string>(keywords);
            sorted.Sort();
            string key = string.Join(" ", sorted);
            if (entries.TryGetValue(key, out var e)) return e;

            e = new Entry { Started = now };
            try
            {
                var set = new ShaderVariantCollection();
                var variant = new ShaderVariantCollection.ShaderVariant(shader, PassType.ScriptableRenderPipeline, sorted.ToArray());
                if (set.Add(variant)) { e.Collection = set; pending.Enqueue(e); }
                else e.Done = true;                           // not a variant the shader knows: nothing to wait for
            }
            catch (System.ArgumentException) { e.Done = true; }
            entries[key] = e;
            return e;
        }
    }
}
