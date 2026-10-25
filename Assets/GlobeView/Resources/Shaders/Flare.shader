Shader "Tycoon/Flare"
{
    // Lens-flare sprite: additive, drawn on top of everything (the script hides it when the Earth covers the sun).
    Properties
    {
        _Color ("Colour (rgb) and strength (a)", Color) = (1, 1, 1, 1)
        _Shape ("Shape: 0 glow, 1 ring, 2 streak", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+20" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "TycoonCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Shape;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 p : TEXCOORD0; };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.p = i.uv * 2.0 - 1.0;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float r2 = dot(i.p, i.p);
                float r = sqrt(r2);
                float v;
                if (_Shape < 0.5)        // soft glow: bright core, long tail
                    v = exp(-r2 * 6.0) + 0.12 / (1.0 + r2 * 30.0);
                else if (_Shape < 1.5)   // ghost: faint disc with a bright ring
                    v = 0.18 * (1.0 - smoothstep(0.0, 1.0, r)) + 0.9 * exp(-pow((r - 0.72) / 0.13, 2.0));
                else                     // anamorphic streak
                    v = exp(-abs(i.p.y) * 28.0) * exp(-abs(i.p.x) * 2.2);
                v *= 1.0 - smoothstep(0.85, 1.0, r);           // fade out at the quad border (no visible edge)
                return half4(_Color.rgb * (v * _Color.a), 1);
            }
            ENDHLSL
        }
    }
}
