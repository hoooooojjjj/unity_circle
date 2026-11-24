Shader "Custom/VolumetricPinBeam"
{
    Properties
    {
        _Color ("Beam Color", Color) = (1.0, 0.85, 0.3, 1.0)
        _Intensity ("Intensity", Range(0, 5)) = 0.85
        _EdgePower ("Edge Softness (Fresnel)", Range(0.5, 6.0)) = 2.4
        _VerticalFade ("Vertical Fade", Range(0.1, 4.0)) = 1.3
        _TopBoost ("Top Brightness", Range(0.5, 3.0)) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha One
        ZWrite Off
        Cull Back

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half _Intensity;
                half _EdgePower;
                half _VerticalFade;
                half _TopBoost;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normInputs = GetVertexNormalInputs(input.normalOS);

                output.positionCS = posInputs.positionCS;
                output.positionWS = posInputs.positionWS;
                output.normalWS = normInputs.normalWS;
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 viewDirWS = normalize(GetWorldSpaceViewDir(input.positionWS));
                float3 normalWS = normalize(input.normalWS);

                // Smoothly fade out silhouette edges using Fresnel
                float ndotv = saturate(abs(dot(normalWS, viewDirWS)));
                float edgeFade = pow(ndotv, _EdgePower);

                // uv.y = 1 at top (fixture), 0 at bottom (floor)
                float v = saturate(input.uv.y);
                float verticalFade = pow(v, _VerticalFade) * lerp(1.0, _TopBoost, v);
                // Soften the bottom contact edge slightly so there is no harsh cut
                float bottomSoft = smoothstep(0.0, 0.12, v);

                half alpha = _Color.a * _Intensity * edgeFade * verticalFade * bottomSoft;
                return half4(_Color.rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
