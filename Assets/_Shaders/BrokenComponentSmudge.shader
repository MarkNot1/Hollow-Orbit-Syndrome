Shader "Hollow Orbit/Broken Component Smudge"
{
    Properties
    {
        [MainTexture] _BaseMap("Base Map", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1, 1, 1, 1)
        _SmudgeScale("Smudge Scale", Range(4, 64)) = 20
        _SmudgeAmount("Smudge Amount", Range(0, 1)) = 0.58
        _SmudgeDarkness("Smudge Darkness", Range(0, 1)) = 0.14
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionOS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _SmudgeScale;
                float _SmudgeAmount;
                float _SmudgeDarkness;
            CBUFFER_END

            float Hash31(float3 value)
            {
                value = frac(value * 0.1031);
                value += dot(value, value.yzx + 33.33);
                return frac((value.x + value.y) * value.z);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.positionOS = input.positionOS.xyz;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 baseSample = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 normalColor = baseSample * _BaseColor;

                float3 pixelCell = floor(input.positionOS * max(_SmudgeScale, 0.001));
                float fineNoise = Hash31(pixelCell);
                float clusterNoise = Hash31(floor(pixelCell * 0.28) + 19.7);
                float smudgeNoise = fineNoise * 0.38 + clusterNoise * 0.62;
                float smudgeMask = smoothstep(
                    _SmudgeAmount,
                    min(_SmudgeAmount + 0.08, 1.0),
                    smudgeNoise);
                half3 damagedColor = normalColor.rgb * max(_SmudgeDarkness, 0.02);
                half3 color = lerp(normalColor.rgb, damagedColor, smudgeMask);

                half3 normalWS = normalize(input.normalWS);
                Light mainLight = GetMainLight();
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 lighting = SampleSH(normalWS) +
                    mainLight.color * diffuse * mainLight.distanceAttenuation;
                color *= max(lighting, 0.18);
                color = MixFog(color, input.fogFactor);
                return half4(color, normalColor.a);
            }
            ENDHLSL
        }
    }
}
