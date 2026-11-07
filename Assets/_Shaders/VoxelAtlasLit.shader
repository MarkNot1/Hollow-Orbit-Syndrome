Shader "HollowOrbit/Voxel Atlas Lit"
{
    Properties
    {
        [MainTexture] _BaseMap("Voxel Atlas", 2D) = "white" {}
        [MainColor] _BaseColor("Base Color", Color) = (1,1,1,1)
        _VoxelAtlasTileSize("Atlas Tile Size", Vector) = (0.0625,0.0625,0,0)
        _VoxelAtlasInset("Atlas Inset", Float) = 0.001
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float4 _VoxelAtlasTileSize;
            float _VoxelAtlasInset;
        CBUFFER_END

        half4 SampleAtlas(float2 uv, float2 tiledUv)
        {
            if (tiledUv.x > -0.5)
            {
                float2 repeated = frac(tiledUv + 0.00001);
                float2 inset = _VoxelAtlasInset.xx;
                uv += inset + repeated * max(_VoxelAtlasTileSize.xy - inset * 2.0, 0.0001);
            }
            return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 tiledUv : TEXCOORD1;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float2 uv : TEXCOORD1;
                float2 tiledUv : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half4 color : COLOR;
                nointerpolation half farMode : TEXCOORD4;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positions = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = positions.positionCS;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv;
                output.tiledUv = input.tiledUv;
                output.fogFactor = ComputeFogFactor(positions.positionCS.z);
                output.color = input.color;
                output.farMode = input.tiledUv.x < -1.5 ? 1.0h : 0.0h;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 albedo;
                UNITY_BRANCH if (input.farMode > 0.5h)
                {
                    // Mesh colors are authored as ordinary #RRGGBB/sRGB values.
                    // Vertex colors arrive as raw 0..1 values, so decode them
                    // before the linear-space render pipeline displays them.
                    // Without this, dark palette colors appear bright and pastel.
                    albedo = half4(FastSRGBToLinear(input.color.rgb), input.color.a) * _BaseColor;
                }
                else
                {
                    albedo = SampleAtlas(input.uv, input.tiledUv);
                    clip(albedo.a - 0.5h);
                }
                half3 normalWS = normalize(input.normalWS);
                // Do not sample the camera-bounded main-light shadow atlas here.
                // Its 50-unit cutoff formed a moving dark circle in both Game and
                // Scene view. Chunks still cast shadows onto characters/props via
                // the ShadowCaster pass, but terrain lighting is distance-stable.
                Light mainLight = GetMainLight();
                half diffuse = saturate(dot(normalWS, mainLight.direction));
                half3 indirect = SampleSH(normalWS);
                half3 lighting = indirect + mainLight.color *
                    (diffuse * mainLight.distanceAttenuation);
                albedo.rgb *= lighting;
                albedo.rgb = MixFog(albedo.rgb, input.fogFactor);
                return albedo;
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                float2 tiledUv : TEXCOORD1;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float2 tiledUv : TEXCOORD1;
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                positionWS = ApplyShadowBias(positionWS, normalWS, lightDirectionWS);
                float4 positionCS = TransformWorldToHClip(positionWS);
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                ShadowVaryings output;
                output.positionCS = positionCS;
                output.uv = input.uv;
                output.tiledUv = input.tiledUv;
                return output;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target
            {
                if (input.tiledUv.x >= -1.5)
                    clip(SampleAtlas(input.uv, input.tiledUv).a - 0.5h);
                return 0;
            }
            ENDHLSL
        }
    }
}
