Shader "Tycoon/Stars"
{
    SubShader
    {
        Tags { "RenderType" = "Background" "Queue" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "TycoonCommon.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 dir : TEXCOORD0;
            };

            Varyings vert(Attributes i)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(i.positionOS.xyz);
                o.dir = i.positionOS.xyz;
                return o;
            }

            float3 StarLayer(float3 d, float scale, float density, float size)
            {
                float3 p = d * scale;
                float3 cell = floor(p);
                float h = TyHash(cell);
                float3 jitter = float3(TyHash(cell + 1.7), TyHash(cell + 3.1), TyHash(cell + 5.3)) - 0.5;
                float dist = length(p - (cell + 0.5 + jitter * 0.7));
                float on = step(density, h);
                float bright = 0.35 + 0.65 * TyHash(cell + 9.2);
                float3 tint = lerp(float3(1.0, 0.85, 0.7), float3(0.7, 0.8, 1.0), TyHash(cell + 11.9));
                return tint * on * bright * smoothstep(size, 0.0, dist);
            }

            half4 frag(Varyings i) : SV_Target
            {
                // stars are fixed in space; rotate into the Earth-fixed frame by sidereal time
                float3 d = mul((float3x3)_TycoonStarRot, normalize(i.dir));
                float3 c = StarLayer(d, 220.0, 0.93, 0.14)
                         + StarLayer(d, 520.0, 0.96, 0.18) * 0.8;
                return half4(c, 1);
            }
            ENDHLSL
        }
    }
}
