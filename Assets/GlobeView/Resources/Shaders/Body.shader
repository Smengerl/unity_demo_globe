Shader "Tycoon/Body"
{
    Properties
    {
        _Color ("Colour", Color) = (0.7, 0.7, 0.7, 1)
        _Emissive ("Emissive (sun)", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "TycoonCommon.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Emissive;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(i.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float lit = saturate(dot(normalize(i.normalWS), normalize(_TycoonSunDir.xyz)));
                return half4(_Color.rgb * lerp(lit * 1.2 + 0.01, 4.0, _Emissive), 1);
            }
            ENDHLSL
        }
    }
}
