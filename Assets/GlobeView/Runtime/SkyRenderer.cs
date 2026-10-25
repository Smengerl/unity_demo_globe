using Tycoon.GlobeView.Contracts;
using UnityEngine;

namespace Tycoon.GlobeView
{
    /// <summary>
    /// Feeds sun, moon and star rotation to the shaders and places sun and moon. All of it follows the game time of the model.
    /// Also keeps the real-time clock for the twinkling lights and waves and the "calm" factor that tames day and night at high speed.
    /// </summary>
    internal sealed class SkyRenderer
    {
        const float SunDistance = 150f;     // not to scale
        const float MoonDistance = 60.3f;   // to scale (Earth radius = 1)
        const float TwinkleSpeed = 0.45f;   // twinkling runs at a fixed pace in real time, independent of the game speed

        readonly GlobeViewConfig config;
        readonly Transform sun, moon;
        float twinkle;

        public SkyRenderer(GlobeViewConfig config, Transform sun, Transform moon)
        {
            this.config = config;
            this.sun = sun;
            this.moon = moon;
            Shader.SetGlobalVector("_TycoonSunDir", new Vector4(1f, 0f, 0f, 0f));   // sane values before the first tick
            Shader.SetGlobalVector("_TycoonMoonDir", new Vector4(0f, 1f, 0f, 0f));
            Shader.SetGlobalMatrix("_TycoonStarRot", Matrix4x4.identity);
            Shader.SetGlobalFloat("_TycoonTime", 0f);
            Shader.SetGlobalFloat("_TycoonCalm", 0f);
        }

        public void Tick(GameTimeState time, float realDeltaTime)
        {
            var sky = Astronomy.Compute(time.UtcSeconds);
            Vector3 sunDir = sky.SunDir, moonDir = sky.MoonDir;
            if (config.Sun == SunMode.Fixed)
            {
                sunDir = GeoMath.Dir(config.FixedSunLat, config.FixedSunLon);
                moonDir = GeoMath.Dir(-config.FixedSunLat, config.FixedSunLon + 150.0);
            }

            Shader.SetGlobalVector("_TycoonSunDir", sunDir);
            Shader.SetGlobalVector("_TycoonMoonDir", moonDir);
            Shader.SetGlobalMatrix("_TycoonStarRot", Matrix4x4.Rotate(Quaternion.AngleAxis(sky.GmstDeg, Vector3.up)));
            sun.position = sunDir * SunDistance;
            moon.position = moonDir * MoonDistance;

            twinkle += realDeltaTime * TwinkleSpeed;
            Shader.SetGlobalFloat("_TycoonTime", twinkle % 3600f);

            // day and night at high game speed would blink: the shader blends toward a constant soft light
            float lo = config.CalmRange.x, hi = config.CalmRange.y;
            if (config.ReduceMotion) { lo *= 0.25f; hi *= 0.25f; }
            float calm = config.Sun == SunMode.Fixed ? 0f : Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(lo, hi, (float)time.Rate));
            Shader.SetGlobalFloat("_TycoonCalm", calm);
        }
    }
}
