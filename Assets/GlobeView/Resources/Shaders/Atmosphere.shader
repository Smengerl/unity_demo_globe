Shader "Tycoon/Atmosphere"
{
    Properties
    {
        _AtmoColor ("Colour", Color) = (0.3, 0.55, 1, 1)
        _Intensity ("Intensity", Float) = 1.9
        _RadiusOut ("Outer radius (must match mesh scale)", Float) = 1.07
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Front   // back faces only: halo behind the globe edge, hidden by depth where Earth covers it

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "TycoonCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _AtmoColor;
                float _Intensity;
                float _RadiusOut;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.positionWS = TransformObjectToWorld(i.positionOS.xyz);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 cp = _WorldSpaceCameraPos;
                float3 rd = normalize(i.positionWS - cp);
                float b = length(cross(rd, cp));                       // ray distance from Earth centre
                float t = saturate((_RadiusOut - b) / (_RadiusOut - 1.0));
                float3 closest = cp - rd * dot(cp, rd);
                float3 L = normalize(_TycoonSunDir.xyz);
                float sunSide = lerp(dot(normalize(closest), L), 0.3, _TycoonCalm);   // +1 sun-lit limb, -1 night limb (calm at high game speed)
                float day = TySoft(-0.19, 0.45, sunSide);
                // thick, slightly softer falloff: the glow reaches farther out than a plain power curve
                float falloff = pow(t, 2.4) + 0.35 * pow(t, 6.0);
                // sunset colours: orange to red near the terminator, blue on the day side
                // one continuous gradient: weak gold at the terminator -> blue by day, but ~25 % gold stays even at noon
                float dayMix = smoothstep(-0.35, 1.0, sunSide) * 0.75;
                float3 colour = lerp(float3(0.85, 0.50, 0.24), _AtmoColor.rgb, dayMix);
                // glare: looking toward the sun the thin air lights up (forward scattering), strongest when the Earth is backlit
                float forward = pow(saturate(dot(rd, L)), 5.0) * (1.0 - _TycoonCalm);
                float intensity = _Intensity * (1.0 + 3.0 * forward);
                float twilight = exp(-pow(sunSide / 0.5, 2.0)) * 0.55;          // soft extra glow around the terminator, kept weak
                float3 haze = colour * falloff * (day + 0.35 * twilight) * intensity;
                return half4(haze, 1);
            }
            ENDHLSL
        }
    }
}
