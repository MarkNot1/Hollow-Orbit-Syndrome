Shader "Hollow Orbit/Circuit Placement Plus Grid"
{
    Properties
    {
        _Color ("Color", Color) = (0.094, 0.094, 0.094, 0.5)
        _Size ("Grid Size", Vector) = (16, 16, 0, 0)
        _GridOffset ("Grid Offset", Vector) = (-0.5, 0.5, 0, 0)
        _Thickness ("Marker Half Thickness", Range(0.001, 0.2)) = 0.015
        _MarkerHalfLength ("Marker Half Length", Range(0.01, 0.49)) = 0.12
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Unlit"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float4 _Size;
                float4 _GridOffset;
                float _Thickness;
                float _MarkerHalfLength;
            CBUFFER_END

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 gridPosition = input.uv * _Size.xy + _GridOffset.xy;
                float2 distanceToIntersection = abs(frac(gridPosition + 0.5) - 0.5);
                float2 antialiasWidth = max(fwidth(distanceToIntersection), 0.0001);

                float verticalArm =
                    (1.0 - smoothstep(_Thickness, _Thickness + antialiasWidth.x, distanceToIntersection.x)) *
                    (1.0 - smoothstep(_MarkerHalfLength, _MarkerHalfLength + antialiasWidth.y, distanceToIntersection.y));

                float horizontalArm =
                    (1.0 - smoothstep(_MarkerHalfLength, _MarkerHalfLength + antialiasWidth.x, distanceToIntersection.x)) *
                    (1.0 - smoothstep(_Thickness, _Thickness + antialiasWidth.y, distanceToIntersection.y));

                half marker = saturate(max(verticalArm, horizontalArm));
                return half4(_Color.rgb, _Color.a * marker);
            }
            ENDHLSL
        }
    }
}
