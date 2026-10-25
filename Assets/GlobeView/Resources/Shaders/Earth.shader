Shader "Tycoon/Earth"
{
    Properties
    {
        [NoScaleOffset] _DayTex ("Day (equirectangular)", 2D) = "gray" {}
        [NoScaleOffset] _NightTex ("Night lights", 2D) = "black" {}
        [NoScaleOffset] _WaterMask ("Water mask (white = water)", 2D) = "black" {}
        [NoScaleOffset] _DetailTex ("Regional detail (day)", 2D) = "black" {}
        [NoScaleOffset] _DetailNightTex ("Regional detail (night lights)", 2D) = "black" {}
        [NoScaleOffset] _NormalTex ("Relief (tangent-space east/north/up, linear)", 2D) = "bump" {}
        [NoScaleOffset] _DetailNormalTex ("Regional relief", 2D) = "bump" {}
        [NoScaleOffset] _WavesTex ("Wave normals, tileable", 2D) = "bump" {}
        [NoScaleOffset] _CoastTex ("Distance from land over water (0..1 of _CoastKm)", 2D) = "white" {}
        [NoScaleOffset] _DetailCoastTex ("Regional coast distance", 2D) = "white" {}
        _DetailRect ("Detail rect: lonMin latMin lonMax latMax (deg)", Vector) = (0, 0, 0, 0)
        _UseWaterMask ("Add the deep-ocean mask to the colour-based water guess", Float) = 0
        _UseDetailNight ("Use regional night texture", Float) = 0
        _NightDay ("Day map visibility on the night side (moonlit)", Range(0, 3)) = 0.7
        _NightMapAlpha ("Share of the night map when zoomed in (the light dots carry the rest)", Range(0, 1)) = 0.12
        _LightDensity ("How many dots per night-map brightness (more = denser lights)", Range(0.5, 8)) = 3.5
        _UseCoast ("Use coast textures", Float) = 0
        _CoastKm ("Distance range of the global coast map (km)", Float) = 39
        _DetailCoastKm ("Distance range of the tile coast maps (km)", Float) = 39
        _Foam ("Surf and foam at the coast", Range(0, 2)) = 1
        _LandGlint ("Sun reflection on land", Range(0, 3)) = 0.8
        _ZMipBias ("Zoom stage: mip bias (far = coarser textures)", Float) = 0
        _ZWaveLayers ("Zoom stage: number of wave layers (1..3)", Float) = 3
        _ZFoamDots ("Zoom stage: foam pixels on/off", Float) = 1
        _ZLightGrids ("Zoom stage: number of light grids (1..3)", Float) = 3
        _ZLightWide ("Zoom stage: lights glow into the neighbours (3x3) on/off", Float) = 1
        _ZFlares ("Zoom stage: bright flares of the twinkling lights on/off", Float) = 1
        _Sparkle ("Twinkling city light dots", Range(0, 2)) = 1
        _Relief ("Relief shading strength", Range(0, 1)) = 1
        _AtmoColor ("Atmosphere colour", Color) = (0.35, 0.6, 1, 1)
        _CityGain ("City light gain", Float) = 3
        _Shininess ("Sun glint sharpness", Float) = 400
        _GlintStrength ("Sun glint strength", Float) = 1.8
        _WaveStrength ("Wave strength", Float) = 2.5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "Forward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // detail features, switched at runtime by DetailController
            #pragma multi_compile_local _ TY_WAVES
            #pragma multi_compile_local _ TY_RELIEF
            #pragma multi_compile_local _ TY_DETAIL
            #pragma multi_compile_local _ TY_LIGHTS
            #pragma multi_compile_local _ TY_NIGHTDETAIL
            #pragma multi_compile_local _ TY_FOAM
            #include "TycoonCommon.hlsl"
            #if defined(TY_DETAIL) || defined(TY_NIGHTDETAIL)
              #define TY_REGION   // the regional rectangle is needed (day detail on High, sharp night map on Medium and High)
            #endif

            TEXTURE2D(_DayTex);    SAMPLER(sampler_DayTex);
            TEXTURE2D(_NightTex);  SAMPLER(sampler_NightTex);
            TEXTURE2D(_WaterMask); SAMPLER(sampler_WaterMask);
            TEXTURE2D(_DetailTex); SAMPLER(sampler_DetailTex);
            TEXTURE2D(_DetailNightTex); SAMPLER(sampler_DetailNightTex);
            TEXTURE2D(_NormalTex); SAMPLER(sampler_NormalTex);
            TEXTURE2D(_DetailNormalTex); SAMPLER(sampler_DetailNormalTex);
            TEXTURE2D(_WavesTex);  SAMPLER(sampler_WavesTex);
            TEXTURE2D(_CoastTex);  SAMPLER(sampler_CoastTex);
            TEXTURE2D(_DetailCoastTex); SAMPLER(sampler_DetailCoastTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _DetailRect;
                float _UseWaterMask;
                float _UseDetailNight;
                float _UseCoast;
                float _CoastKm;
                float _DetailCoastKm;
                float _Foam;
                float _LandGlint;
                float _ZMipBias;
                float _ZWaveLayers;
                float _ZFoamDots;
                float _ZLightGrids;
                float _ZLightWide;
                float _ZFlares;
                float _Sparkle;
                float _NightDay;
                float _NightMapAlpha;
                float _LightDensity;
                float4 _NightTex_TexelSize;   // set by Unity: 1/w, 1/h, w, h
                float _Relief;
                float4 _AtmoColor;
                float _CityGain;
                float _Shininess;
                float _GlintStrength;
                float _WaveStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;   // u: longitude (continuous across the date line, wraps), v: latitude
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                o.uv = i.uv;
                return o;
            }

            // One layer of the tileable wave map: sin/cos = rotation of the tiling, drift in tiles per second.
            float2 WaveLayer(float2 uv, float2 dx, float2 dy, float2 tile, float2 drift, float sn, float cs, float t)
            {
                float2x2 R = float2x2(cs, -sn, sn, cs);
                float2 p = mul(R, uv * tile + drift * t);
                float2 g = SAMPLE_TEXTURE2D_GRAD(_WavesTex, sampler_WavesTex, p, mul(R, dx * tile), mul(R, dy * tile)).xy * 2.0 - 1.0;
                return mul(g, R);   // rotate the normal back (transpose of R)
            }

            // Night map value at a uv position (global map, plus the sharp regional map where it exists), for the light dots.
            float NightLumAt(float2 uvC)
            {
                float3 n = SAMPLE_TEXTURE2D_LOD(_NightTex, sampler_NightTex, uvC, 0).rgb;
            #if defined(TY_REGION)
                if (_UseDetailNight > 0.5)
                {
                    float2 span = max(_DetailRect.zw - _DetailRect.xy, 1e-3);
                    float2 deg = float2(frac(uvC.x) * 360.0 - 180.0, (uvC.y - 0.5) * 180.0);
                    float2 uvD = (deg - _DetailRect.xy) / span;
                    float2 edge = min(uvD, 1.0 - uvD);
                    float w = smoothstep(0.0, 0.04, min(edge.x, edge.y)) * step(0.5, _DetailRect.z - _DetailRect.x);
                    float4 nd = SAMPLE_TEXTURE2D_LOD(_DetailNightTex, sampler_DetailNightTex, uvD, 0);
                    n = lerp(n, nd.rgb, w * nd.a);       // alpha: where the tile window has data
                }
            #endif
                return dot(n, float3(0.3, 0.5, 0.2));
            }

            // One light of the grid: lit or not by the night map at the cell's own position, soft round glow that reaches into the
            // neighbouring cells, so neighbours melt into each other instead of looking like a regular row of dots.
            float3 CellLight(float colU, float row, float nRow, float cellsV, float uPix, float vPix, float lightMask, float density)
            {
                float colI = colU - nRow * floor(colU / nRow);               // wrapped around the date line, for ids and map lookup
                float3 id = float3(colI, row, cellsV);
                float h = TyHash(id);
                float lum = sqrt(NightLumAt(float2((colI + 0.5) / nRow, (row + 0.5) / cellsV)));   // the map is linear: sqrt lifts dim suburbs
                float p = saturate(smoothstep(0.06, 0.80, lum) * density);
                float on = step(1.0 - p, h);
                if (on <= 0.0) return 0.0;

                float h2 = TyHash(id + 21.9);                                // own threshold: the light is on above it, off below it
                on *= step(h2, lightMask);                                  // hard threshold: a light is either fully on or fully off
                float2 jitter = float2(TyHash(id + 1.7), TyHash(id + 3.1)) - 0.5;
                float2 d = float2(uPix, vPix) - (float2(colU, row) + 0.5 + jitter * 0.6);
                float size = 0.7 + 0.35 * TyHash(id + 5.3);                  // glow radius in cells (reaches the neighbours), differs per light
                float shape = exp(-dot(d, d) / (size * size) * 3.0);        // ~0 beyond 1.5 cells: nothing is cut off at the 3x3 border
                float rate = 1.5 + 4.5 * TyHash(id + 9.1);
                float twinkle = (0.12 + 0.88 * (0.5 + 0.5 * sin(_TycoonTime * rate + h * 40.0)))
                              * (0.7 + 0.3 * sin(_TycoonTime * rate * 0.37 + h * 91.0));
                // short bright flares at random moments, so the sparkle is noticeable even when nothing moves on screen
                twinkle += 1.6 * _ZFlares * pow(max(0.0, sin(_TycoonTime * (0.9 + 1.7 * TyHash(id + 17.3)) + h * 77.0)), 14.0);
                float3 tint = lerp(float3(1.0, 0.78, 0.5), float3(1.0, 0.95, 0.85), TyHash(id + 13.7));
                return tint * (on * shape * twinkle * (0.5 + 0.8 * p));
            }

            // Twinkling lights on a grid of square cells in uv space (rows of equal height, cells per row ~ cos(lat)).
            // The night map decides per cell whether it holds a light, so the map is reproduced as a field of individual lights.
            // Each pixel adds up the glow of the 3x3 cells around it, so lights melt into each other. `fade` is the grid's share (finer grids take over from coarser
            // ones as you zoom in).
            float3 UvLights(float2 uv, float cellsV, float lightMask, float pixelSize, float density, float span, out float fade)
            {
                float cellRad = PI / cellsV;                                 // cell height on the unit sphere (PI is from URP's Core.hlsl)
                float px = cellRad / max(pixelSize, 1e-7);                          // cell height in pixels
                fade = saturate((px - 1.0) / 1.2);                           // 0 below 1.0 px, 1 above 2.2 px (lights come in early)
                if (fade <= 0.0) return 0.0;

                float vPix = uv.y * cellsV;
                float row0 = floor(vPix);
                float3 sum = 0.0;
                for (int r = -1; r <= 1; r++)
                {
                    if (abs(r) > span) continue;
                    float row = row0 + r;
                    float latRow = ((row + 0.5) / cellsV - 0.5) * PI;
                    float nRow = max(1.0, floor(2.0 * cellsV * cos(latRow)));
                    float uPix = frac(uv.x) * nRow;
                    float c0 = floor(uPix);
                    for (int c = -1; c <= 1; c++)
                        if (abs(c) <= span) sum += CellLight(c0 + c, row, nRow, cellsV, uPix, vPix, lightMask, density);
                }
                return sum;
            }

            // Surf: white pixels along the coast that re-roll over time (slow, fixed speed, independent of game speed).
            // Each grid cell gets a new random draw every few seconds, cross-faded; density follows the shore band.
            float FoamDots(float3 N, float cellRad, float band, float t)
            {
                float3 p = N / cellRad;
                float3 cell = floor(p);
                float phase = t * 1.4 + TyHash(cell + 4.1) * 3.0;
                float slot = floor(phase);
                float blend = smoothstep(0.0, 1.0, frac(phase));
                float a = step(TyHash(cell + slot * 7.31), band);
                float b = step(TyHash(cell + (slot + 1.0) * 7.31), band);
                float3 jitter = float3(TyHash(cell + 1.7), TyHash(cell + 3.1), TyHash(cell + 5.3)) - 0.5;
                float dist = length(p - (cell + 0.5 + jitter * 0.6));
                return lerp(a, b, blend) * (1.0 - smoothstep(0.0, 0.5, dist));
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = normalize(_WorldSpaceCameraPos - i.positionWS);
                float3 L = normalize(_TycoonSunDir.xyz);
                // high game speed: day and night would blink, so everything that depends on the sun blends toward one calm, constant light
                float steady = 1.0 - _TycoonCalm;
                float ndl = lerp(dot(N, L), 0.25, _TycoonCalm);

                float2 uv = i.uv;
                float2 dx = ddx(uv), dy = ddy(uv);
                float mipScale = exp2(_ZMipBias);   // far zoom: coarser mips, fewer cache misses
                dx *= mipScale; dy *= mipScale;
                float psPix = max(length(ddx(N)), length(ddy(N)));   // size of one pixel on the unit sphere (computed outside branches: it needs derivatives)

                float3 day = SAMPLE_TEXTURE2D_GRAD(_DayTex, sampler_DayTex, uv, dx, dy).rgb;

                // local east / north directions (needed for relief and waves)
                float2 eh = float2(-N.z, N.x);
                float3 E = float3(eh.x, 0.0, eh.y) / max(length(eh), 1e-4);
                float3 Nn = cross(E, N);

                float ndlTerrain = ndl;
                float3 Nsurf = N;   // surface normal incl. relief, for the moonlight on the night side
            #if defined(TY_RELIEF)
                float3 tn = SAMPLE_TEXTURE2D_GRAD(_NormalTex, sampler_NormalTex, uv, dx, dy).xyz * 2.0 - 1.0;
            #endif

            #if defined(TY_REGION)
                // regional rectangle (lon/lat in degrees) for the high-res overlays
                float2 span = max(_DetailRect.zw - _DetailRect.xy, 1e-3);
                float2 deg = float2(frac(uv.x) * 360.0 - 180.0, (uv.y - 0.5) * 180.0);
                float2 uvD = (deg - _DetailRect.xy) / span;
                float2 scaleD = float2(360.0, 180.0) / span;
                float2 edge = min(uvD, 1.0 - uvD);
                float wD = smoothstep(0.0, 0.04, min(edge.x, edge.y)) * step(0.5, _DetailRect.z - _DetailRect.x);
            #endif
            #if defined(TY_DETAIL)
                // regional day colour and relief
                float4 dd = SAMPLE_TEXTURE2D_GRAD(_DetailTex, sampler_DetailTex, uvD, dx * scaleD, dy * scaleD);
                day = lerp(day, dd.rgb, wD * dd.a);       // alpha: where the tile window has data
              #if defined(TY_RELIEF)
                float4 tnS = SAMPLE_TEXTURE2D_GRAD(_DetailNormalTex, sampler_DetailNormalTex, uvD, dx * scaleD, dy * scaleD);
                tn = lerp(tn, tnS.xyz * 2.0 - 1.0, wD * tnS.a);
              #endif
            #endif

            #if defined(TY_RELIEF)
                float3 Nterrain = normalize(E * tn.x + Nn * tn.y + N * tn.z);
                Nsurf = Nterrain;
                ndlTerrain = lerp(ndl, lerp(dot(Nterrain, L), 0.25, _TycoonCalm), _Relief);
            #endif

                // ratio-based so very dark deep-ocean pixels still count as water; white (ice) has ratio ~1
                float waterGuess = smoothstep(1.08, 1.45, day.b / (max(day.r, day.g) + 0.003));
                // the mask only adds deep ocean the colour guess misses (pale ridges); coastlines stay as sharp as the colour map
                float water = max(waterGuess, _UseWaterMask > 0.5 ? SAMPLE_TEXTURE2D_GRAD(_WaterMask, sampler_WaterMask, uv, dx, dy).r : 0.0);

                // day side: soft falloff from noon to dusk instead of a hard Lambert edge
                float lit = TySoft(-0.35, 1.1, ndlTerrain);
                float3 warm = lerp(float3(1.0, 0.45, 0.25), float3(1, 1, 1), TySoft(-0.05, 0.55, ndl));
                float3 col = day * (0.012 + lit * 1.35) * warm;
                // twilight: sun between +5 deg and -18 deg elevation. Orange at the terminator, blue further into the night.
                float dusk = TySoft(-0.50, -0.02, ndl) * (1.0 - TySoft(0.02, 0.42, ndl));
                float3 duskTint = lerp(float3(0.25, 0.28, 0.65), float3(1.0, 0.42, 0.18), TySoft(-0.40, 0.12, ndl));
                col += (day * 0.6 + 0.04) * duskTint * dusk * 0.5;

                // night side: the day map (with relief) stays faintly visible in bluish moonlight; night map and light dots come on top
                float nightMask = 1.0 - TySoft(-0.42, 0.16, ndl);
                float moonLit = smoothstep(-0.2, 1.1, dot(Nsurf, normalize(_TycoonMoonDir.xyz)));
                col += day * float3(0.55, 0.72, 1.0) * (0.04 + 0.09 * moonLit) * _NightDay * nightMask;

                // city lights come on from dusk, full at nautical twilight
                // quintic texel interpolation instead of bilinear: city lights stay round blobs instead of visible squares when magnified
                float2 nTexel = uv * _NightTex_TexelSize.zw + 0.5;
                float2 nI = floor(nTexel), nF = nTexel - nI;
                nF = nF * nF * nF * (nF * (nF * 6.0 - 15.0) + 10.0);
                float2 uvNight = (nI + nF - 0.5) * _NightTex_TexelSize.xy;
                float3 night = SAMPLE_TEXTURE2D_GRAD(_NightTex, sampler_NightTex, uvNight, dx, dy).rgb;
            #if defined(TY_REGION)
                if (_UseDetailNight > 0.5)   // sharp night map from the tile window
                {
                    float4 ns = SAMPLE_TEXTURE2D_GRAD(_DetailNightTex, sampler_DetailNightTex, uvD, dx * scaleD, dy * scaleD);
                    night = lerp(night, ns.rgb, wD * ns.a);
                }
            #endif
                float3 glow = night * float3(1.0, 0.78, 0.5) * _CityGain * nightMask;

            #if defined(TY_LIGHTS)
                // Zoomed in, the night map turns into a field of single, twinkling lights (sharp at any zoom, anywhere on Earth).
                // Three grids of different fineness; the finest one that is still at least ~2-3 px per cell takes over from the coarser ones.
                // The night map itself stays underneath at only a small share (_NightMapAlpha), so its pixels hardly show.
                float3 dots = 0.0;
                float glowK = 1.0;
                // Evening: lights stay on a bit longer into the twilight than the night map's glow (sun up to ~11 degrees above the horizon).
                // After local midnight they go out one by one until the end of the morning twilight. Local time = hour angle,
                // the longitude difference to the subsolar point (+ afternoon / evening, - morning); the night's progress after midnight
                // is the sun's climb from its midnight elevation to the end of dawn.
                float lonHere = (frac(uv.x) * 2.0 - 1.0) * 3.14159265;
                float hourAngle = lonHere - atan2(L.x, -L.z);
                hourAngle -= 6.2831853 * floor((hourAngle + 3.14159265) / 6.2831853);   // wrap to [-pi, pi)
                float ndlMidnight = L.y * N.y - sqrt(saturate(1.0 - L.y * L.y)) * sqrt(saturate(1.0 - N.y * N.y));
                float morning = hourAngle < 0.0 ? TySoft(ndlMidnight, max(0.2, ndlMidnight + 1e-3), ndl) : 0.0;
                float lightMask = hourAngle < 0.0 ? 1.0 - morning : 1.0 - TySoft(-0.42, 0.26, ndl);
                lightMask = lerp(lightMask, 0.35, _TycoonCalm);              // calm: a steady share of the lights stays on
                glow *= 1.0 - morning;
                if (lightMask > 0.01)
                {
                    float f1 = 0.0, f2 = 0.0, f4 = 0.0;
                    // zoom stage decides how many grids exist and whether the lights glow into their neighbours
                    float span = _ZLightWide > 0.5 ? 1.0 : 0.0;
                    float3 d4 = _ZLightGrids > 2.5 ? UvLights(uv, 16384.0, lightMask, psPix, _LightDensity, span, f4) : 0.0;
                    float3 d2 = _ZLightGrids > 1.5 ? UvLights(uv, 8192.0, lightMask, psPix, _LightDensity, span, f2) : 0.0;
                    float3 d1 = UvLights(uv, 4096.0, lightMask, psPix, _LightDensity, span, f1);
                    float w4 = f4, w2 = f2 * (1.0 - f4), w1 = f1 * (1.0 - f2);
                    dots = d4 * w4 * 0.22 + d2 * w2 * 0.3 + d1 * w1 * 0.4;
                    glowK = lerp(1.0, _NightMapAlpha, saturate(f4 + f2 + f1) * saturate(_Sparkle));
                }
                col += glow * glowK + dots * _CityGain * 0.5 * _Sparkle;
            #else
                col += glow;
            #endif

                // ocean waves: tiling normal maps at unrelated scales and angles (fade out with distance)
                float3 Nw = N;
            #if defined(TY_WAVES)
                if (water > 0.01)
                {
                    float fade = saturate(1.0 - psPix * 330.0);
                    float t = _Time.y;
                    // the same tiny map at up to three unrelated scales and angles (the zoom stage decides how many), so no repeat pattern is visible ...
                    float2 g = WaveLayer(uv, dx, dy, float2(80.0, 40.0), float2(0.06, 0.04), 0.0, 1.0, t);
                    if (_ZWaveLayers > 1.0)
                        g += WaveLayer(uv, dx, dy, float2(210.0, 105.0), float2(-0.15, 0.10), 0.6, 0.8, t) * 0.8 * saturate(_ZWaveLayers - 1.0);
                    if (_ZWaveLayers > 2.0)
                        g += WaveLayer(uv, dx, dy, float2(520.0, 260.0), float2(0.30, 0.25), 0.945, 0.326, t) * 0.6 * saturate(_ZWaveLayers - 2.0);
                    // ... and calm and rough patches from a very large-scale pass over the same map
                    float calmRough = _ZWaveLayers < 2.5 ? 1.0 : clamp(1.0 + 3.0 * (SAMPLE_TEXTURE2D_GRAD(_WavesTex, sampler_WavesTex, uv * float2(9.0, 4.5) + float2(t * 0.004, 0), dx * float2(9.0, 4.5), dy * float2(9.0, 4.5)).x * 2.0 - 1.0), 0.35, 1.8);
                    g *= _WaveStrength * fade * calmRough;
                    Nw = normalize(E * g.x + Nn * g.y + N);

                    // brighter and darker wave facets (sun side and sky side)
                    float facet = 1.0 + 5.0 * (dot(Nw, L) - dot(N, L)) + 2.0 * (dot(Nw, V) - dot(N, V));
                    col = lerp(col, col * clamp(facet, 0.45, 1.7), water * steady);
                }
            #endif

                // sun glint and sky reflection on water
                float3 H = normalize(L + V);
                float nhW = saturate(dot(Nw, H)), nhG = saturate(dot(N, H));
                // narrow sparkle on the waves + a broad glare lobe + a very wide soft sheen: three scales of reflection
                float glint = (pow(nhW, _Shininess) + 0.10 * pow(nhG, 60.0) + 0.035 * pow(nhG, 9.0)) * _GlintStrength;
                col += float3(1.0, 0.92, 0.8) * glint * water * TySoft(0.0, 0.3, ndl) * steady;
                float fres = pow(1.0 - saturate(dot(N, V)), 4.0);
                col += _AtmoColor.rgb * fres * 0.25 * water * lit;

                // sun reflection on land: broad and weak, follows the relief; bright ground (sand, snow) reflects more than forest
                float lumDay = dot(day, float3(0.3, 0.55, 0.15));
                float nhL = saturate(dot(Nsurf, H));
                float landSpec = pow(nhL, 24.0) + 0.35 * pow(nhL, 6.0) + 0.12 * pow(nhL, 2.5);
                // grazing sheen: land and sea turn silvery where you look across them toward the sun (big "glare" at low sun)
                float graze = pow(1.0 - saturate(dot(N, V)), 3.0) * pow(saturate(dot(-V, L) * 0.5 + 0.5), 3.0);
                col += float3(1.0, 0.92, 0.8) * graze * lit * 0.2 * _LandGlint * steady;
                col += float3(1.0, 0.95, 0.85) * landSpec * (1.0 - water) * _LandGlint * (0.3 + 1.2 * smoothstep(0.12, 0.55, lumDay))
                       * TySoft(0.0, 0.3, ndl) * steady;

            #if defined(TY_FOAM)
                // Surf and foam along the coast, from the coast distance maps. Three parts, all re-rolling over time (slow, fixed speed,
                // independent of game speed): a bright shore line, a wider band of white pixels (three grid sizes so it also reads
                // at the zoom of Central Europe) and a faint continuous foam edge.
                if (_UseCoast > 0.5 && water > 0.5)
                {
                    float coastKm = SAMPLE_TEXTURE2D_GRAD(_CoastTex, sampler_CoastTex, uv, dx, dy).r * _CoastKm;
                  #if defined(TY_DETAIL)
                    float4 cs = SAMPLE_TEXTURE2D_GRAD(_DetailCoastTex, sampler_DetailCoastTex, uvD, dx * scaleD, dy * scaleD);
                    coastKm = lerp(coastKm, cs.r * _DetailCoastKm, wD * cs.a);
                  #endif
                    // A ripple pattern that keeps changing (three noise layers drifting in different directions, like the waves on the
                    // water) is made stronger and whiter the closer it gets to the coast. No line along the coast, no regular motion.
                    float wCoast = 1.0 - smoothstep(0.0, 34.0, coastKm);                          // reaches ~34 km out (the range of the coast maps)
                    float wNear = wCoast * wCoast;                                                // how strong the pattern is: grows toward the coast
                    float t = _TycoonTime;
                    // finer and faster than the waves: two fine layers do the work, the large slow one only leaves busy and calm stretches of coast
                    float a1 = saturate(1.0 - psPix * 800.0 * 0.9), a2 = saturate(1.0 - psPix * 2200.0 * 0.9);
                    float n1 = TyNoise(N * 800.0 + float3(t * 2.2, t * 1.2, 0.0));
                    float n2 = TyNoise(N * 2200.0 - float3(0.0, t * 3.0, t * 1.8));
                    float n3 = TyNoise(N * 200.0 + float3(t * 0.6, 0.0, t * 0.7));
                    float ripple = lerp(0.5, n1, a1) * 0.5 + lerp(0.5, n2, a2) * 0.42 + n3 * 0.3;   // 0 .. 1.22, mean ~0.61
                    float thr = lerp(1.02, 0.62, wNear);                                          // lower threshold = more foam near the coast
                    float foamField = smoothstep(thr, thr + 0.09, ripple);                        // sharp flecks instead of soft fields
                    float soft = foamField * (0.08 + 0.92 * wNear) * 0.8;                         // far from the coast: thin and transparent

                    // white pixels: the finest grid that is at least ~2-3 px per cell takes over from the coarser ones
                    float dens = wNear * (0.2 + 0.9 * foamField);
                    float fA = saturate((0.0003 / max(psPix, 1e-7) - 1.0) / 1.2);
                    float fB = saturate((0.0008 / max(psPix, 1e-7) - 1.0) / 1.2);
                    float fC = saturate((0.0020 / max(psPix, 1e-7) - 1.0) / 1.2);
                    float pxA = (_ZFoamDots > 0.5 && fA > 0.0) ? FoamDots(N, 0.0003, dens, _TycoonTime) : 0.0;
                    float pxB = (_ZFoamDots > 0.5 && fB > 0.0) ? FoamDots(N, 0.0008, dens, _TycoonTime) : 0.0;
                    float pxC = (_ZFoamDots > 0.5 && fC > 0.0) ? FoamDots(N, 0.0020, dens * 0.8, _TycoonTime) : 0.0;
                    float whitePixels = pxA * fA + pxB * fB * (1.0 - fA) + pxC * fC * (1.0 - fB);
                    float foamLight = saturate(lit + 0.10 * nightMask);
                    col += float3(1.0, 1.0, 1.0) * (whitePixels * 3.2 + soft) * _Foam * foamLight;
                }
            #endif

                // atmosphere rim on the sunlit limb
                // (thicker: a wide inner haze plus the bright limb; orange at the terminator, blue in daylight, and extra glare
                // when the sun is behind the globe as seen from the camera)
                float limb = 1.0 - saturate(dot(N, V));
                float3 rimTint = lerp(float3(0.85, 0.50, 0.24), _AtmoColor.rgb, smoothstep(-0.35, 1.0, ndl) * 0.75);   // same continuous gold-to-blue gradient as the halo
                float fwd = pow(saturate(dot(-V, L)), 4.0);
                col += rimTint * (pow(limb, 3.0) * 1.3 + pow(limb, 1.6) * 0.22) * TySoft(-0.2, 0.5, ndl) * (1.0 + 1.5 * fwd);
                col += rimTint * pow(limb, 4.0) * exp(-pow(ndl / 0.4, 2.0)) * 0.22;       // terminator glow, weak

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
