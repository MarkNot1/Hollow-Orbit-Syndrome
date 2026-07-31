Shader "Hollow Orbit/Skybox/Seam Blended Panoramic"
{
    Properties
    {
        _Tint("Tint Color", Color) = (0.5, 0.5, 0.5, 0.5)
        [Gamma] _Exposure("Exposure", Range(0, 8)) = 1
        _Rotation("Rotation", Range(0, 360)) = 0
        [NoScaleOffset] _MainTex("Spherical (HDR)", 2D) = "grey" {}
        _SeamBlendDegrees("Seam Repair Width", Range(0, 30)) = 6
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Background"
            "RenderType" = "Background"
            "PreviewType" = "Skybox"
        }

        Cull Off
        ZWrite Off

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 2.0

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            half4 _MainTex_HDR;
            half4 _Tint;
            half _Exposure;
            float _Rotation;
            float _SeamBlendDegrees;

            float2 ToRadialCoords(float3 coords)
            {
                float3 normalizedCoords = normalize(coords);
                float latitude = acos(normalizedCoords.y);
                float longitude = atan2(normalizedCoords.z, normalizedCoords.x);
                float2 sphereCoords =
                    float2(longitude, latitude) *
                    float2(0.5 / UNITY_PI, 1.0 / UNITY_PI);

                return float2(0.5, 1.0) - sphereCoords;
            }

            float3 RotateAroundYInDegrees(float3 vertex, float degrees)
            {
                float alpha = degrees * UNITY_PI / 180.0;
                float sine;
                float cosine;
                sincos(alpha, sine, cosine);
                float2x2 rotation = float2x2(cosine, -sine, sine, cosine);
                return float3(mul(rotation, vertex.xz), vertex.y).xzy;
            }

            struct Attributes
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 vertex : SV_POSITION;
                float3 texcoord : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                float3 rotated = RotateAroundYInDegrees(input.vertex, _Rotation);
                output.vertex = UnityObjectToClipPos(rotated);
                output.texcoord = input.vertex.xyz;
                return output;
            }

            half3 SampleAndDecode(float2 uv)
            {
                return DecodeHDR(tex2D(_MainTex, uv), _MainTex_HDR);
            }

            half3 RepairSeam(float2 uv)
            {
                half3 original = SampleAndDecode(uv);
                float repairWidth = _SeamBlendDegrees / 360.0;

                if (repairWidth <= 0.0)
                    return original;

                // Express U as a signed coordinate around the wrap boundary:
                // the right edge approaches 0 from below and the left edge
                // leaves 0 above it.
                float signedDistance =
                    uv.x > 0.5 ? uv.x - 1.0 : uv.x;

                if (abs(signedDistance) >= repairWidth)
                    return original;

                // Sample clean columns immediately outside the damaged region,
                // then build a smooth bridge between them. The bridge meets the
                // untouched panorama exactly at both ends.
                half3 rightAnchor =
                    SampleAndDecode(float2(1.0 - repairWidth, uv.y));
                half3 leftAnchor =
                    SampleAndDecode(float2(repairWidth, uv.y));

                float bridgePosition =
                    saturate((signedDistance + repairWidth) /
                             (2.0 * repairWidth));
                bridgePosition =
                    bridgePosition * bridgePosition *
                    (3.0 - 2.0 * bridgePosition);

                return lerp(rightAnchor, leftAnchor, bridgePosition);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = ToRadialCoords(input.texcoord);
                half3 panorama = RepairSeam(uv);

                panorama *= _Tint.rgb * unity_ColorSpaceDouble.rgb;
                panorama *= _Exposure;
                return half4(panorama, 1.0h);
            }
            ENDCG
        }
    }

    Fallback Off
}
